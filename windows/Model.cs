using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Atajos que no dependen de un timer concreto (los valores son los ids de RegisterHotKey).
    enum GlobalAction { Reset = 1, Overlay = 2 }

    static class Directions
    {
        public static readonly string[] Arrows = { "↑", "→", "↓", "←" };
        public static readonly string[] Names = { "Norte", "Derecha", "Sur", "Izquierda" };
    }

    /// Configuración de un timer (se guarda en %APPDATA%\AlfaHub\timers.json).
    [DataContract]
    sealed class TimerConfig
    {
        [DataMember] public string Id = Guid.NewGuid().ToString();
        [DataMember] public string Name = "Timer";
        /// "cycle": cuenta atrás repetible (Pez, Bakragore); "rotation": direcciones (Pilar).
        [DataMember] public string Kind = "cycle";
        [DataMember] public int Cycle = 120;          // segundos de cada ciclo
        [DataMember] public bool Repeats = true;      // al llegar a 0 vuelve a empezar
        [DataMember] public int Total;                // duración total en segundos (0 = sin límite)
        [DataMember] public int Warning = 25;         // rojo en los últimos N s
        [DataMember] public bool CycleBeep = true;    // sonido de aviso al empezar cada ciclo
        [DataMember] public int Step = 6;             // segundos por dirección
        [DataMember] public int StartDirection;
        [DataMember] public int BeepSeconds = 5;      // beeps en los últimos N s
        [DataMember] public string SoundPath;         // sonido de aviso propio (null = beep)
        [DataMember] public int Shortcut;             // iniciar / pausar (Keys; 0 = sin atajo)

        public bool IsRotation => Kind == "rotation";
        public TimerConfig Clone() => (TimerConfig)MemberwiseClone();
    }

    enum TimerPreset { Pez, Bakragore, Pilar, Custom }

    /// Un espejo: una región de la ventana de Tibia mostrada en otra parte de la pantalla
    /// (se guarda en %APPDATA%\AlfaHub\espejos.json).
    [DataContract]
    sealed class MirrorConfig
    {
        [DataMember] public string Id = Guid.NewGuid().ToString();
        [DataMember] public string Name = "Espejo";
        // Región, en píxeles del área de cliente de la ventana de Tibia
        [DataMember] public int X, Y, Width = 120, Height = 60;
        // Posición en pantalla y aspecto
        [DataMember] public int Left = 100, Top = 100;
        [DataMember] public int Zoom = 100;      // %
        [DataMember] public int Opacity = 100;   // %
        [DataMember] public bool Visible = true;
        [DataMember] public bool Border = true;

        public Size DisplaySize => new Size(Math.Max(8, Width * Zoom / 100), Math.Max(8, Height * Zoom / 100));
    }

    static class Presets
    {
        public static string Title(TimerPreset p) =>
            p == TimerPreset.Pez ? "Pez" : p == TimerPreset.Bakragore ? "Bakragore" : p == TimerPreset.Pilar ? "Pilar" : "Personalizado";

        public static string Detail(TimerPreset p) =>
            p == TimerPreset.Pez ? "Cada 2:00, alerta en los últimos 25 s"
            : p == TimerPreset.Bakragore ? "Mecánica cada 1:30 durante 25 min"
            : p == TimerPreset.Pilar ? "Rotación de los Totens cada 6 s"
            : "Cuenta atrás de 1:00 que no se repite";

        public static TimerConfig Config(TimerPreset p)
        {
            switch (p)
            {
                case TimerPreset.Pez:
                    return new TimerConfig { Name = "Pez", Cycle = 120, Repeats = true, Warning = 25, CycleBeep = true, BeepSeconds = 5 };
                case TimerPreset.Bakragore:
                    return new TimerConfig { Name = "Bakragore", Cycle = 90, Repeats = true, Total = 25 * 60, Warning = 10, CycleBeep = false, BeepSeconds = 5 };
                case TimerPreset.Pilar:
                    return new TimerConfig { Name = "Pilar", Kind = "rotation", Step = 6, BeepSeconds = 2 };
                default:
                    return new TimerConfig { Name = "Timer", Cycle = 60, Repeats = false, Warning = 10, CycleBeep = true, BeepSeconds = 3 };
            }
        }
    }

    /// Lo que se muestra de un timer en un instante.
    struct TimerDisplay
    {
        public string Text;      // "01:30" o "↑ 6"
        public string Detail;    // "Ciclo 2 · Total 03:10", "Norte · luego Derecha"…
        public double Progress;
        public bool Critical;
        public bool Finished;

        public string Signature => string.Join("|", Text, Detail, Math.Round(Progress, 3), Critical, Finished);
    }

    /// Un timer en marcha: su configuración, su cronómetro y lo que muestra.
    sealed class TimerRun
    {
        readonly Model model;
        readonly Stopwatch watch = new Stopwatch();
        int lastSecond = -1, lastSlot = -1, lastCount = -1;

        public TimerConfig Config;
        public TimerDisplay Display { get; private set; }

        public TimerRun(Model model, TimerConfig config)
        {
            this.model = model;
            Config = config;
            Refresh(false);
        }

        public string Id => Config.Id;
        public bool Running => watch.IsRunning;
        /// En marcha o pausado a medias: se muestra en el overlay.
        public bool Started => watch.IsRunning || watch.ElapsedTicks > 0;

        public void Toggle()
        {
            if (watch.IsRunning) watch.Stop();
            else
            {
                if (Display.Finished) Reset();
                watch.Start();
            }
            Refresh(true);
        }

        public void Reset()
        {
            watch.Reset();
            lastSecond = lastSlot = lastCount = -1;
            Refresh(false);
        }

        public void Tick() { if (watch.IsRunning) Refresh(true); }

        /// Vuelve a calcular lo que se muestra (p. ej. tras cambiar la configuración).
        public void Changed() => Refresh(false);

        /// Sonido de aviso del timer: el archivo elegido o el beep por defecto.
        public void PlayAlert(bool longBeep = false)
        {
            if (!string.IsNullOrEmpty(Config.SoundPath) && Beeper.PlayFile(Config.SoundPath, model)) return;
            Beeper.Beep(880, longBeep ? 0.6 : 0.25, model);
        }

        void Refresh(bool sound) => Display = Config.IsRotation ? RotationDisplay(sound) : CycleDisplay(sound);

        TimerDisplay CycleDisplay(bool sound)
        {
            int cycle = Math.Max(5, Config.Cycle);
            int limit = Config.Repeats ? (Config.Total > 0 ? Config.Total : int.MaxValue) : cycle;
            int e = (int)Math.Min(watch.Elapsed.TotalSeconds, limit);
            bool finished = e >= limit;
            int remaining = finished ? 0 : cycle - e % cycle;

            if (sound && e != lastSecond)
            {
                lastSecond = e;
                if (finished)
                {
                    watch.Stop();
                    PlayAlert(true);
                }
                else
                {
                    if (Config.CycleBeep && e > 0 && e % cycle == 0) PlayAlert();
                    if (Config.BeepSeconds > 0 && remaining >= 1 && remaining <= Config.BeepSeconds) Beeper.Beep(1200, 0.15, model);
                }
            }

            int cycleNumber = Math.Min(e, limit - 1) / cycle + 1;
            string detail = finished ? $"Terminado · {Model.Mmss(e)}"
                : !Config.Repeats ? $"Cuenta atrás · {Model.Mmss(cycle)}"
                : Config.Total > 0 ? $"Ciclo {cycleNumber} · Quedan {Model.Mmss(limit - e)}"
                : $"Ciclo {cycleNumber} · Total {Model.Mmss(e)}";
            return new TimerDisplay
            {
                Text = Model.Mmss(remaining),
                Detail = detail,
                Finished = finished,
                Critical = !finished && remaining <= Config.Warning,
                Progress = finished ? 1 : (double)(e % cycle) / cycle,
            };
        }

        TimerDisplay RotationDisplay(bool sound)
        {
            double step = Math.Max(1, Config.Step);
            double t = watch.Elapsed.TotalSeconds;
            int slot = (int)(t / step);
            int dir = (Config.StartDirection + slot) % 4;
            int count = Math.Max(1, (int)Math.Ceiling(step - (t - slot * step)));

            if (sound && watch.IsRunning)
            {
                if (slot != lastSlot)
                {
                    lastSlot = slot; lastCount = count;
                    PlayAlert();
                }
                else if (count != lastCount)
                {
                    lastCount = count;
                    if (Config.BeepSeconds > 0 && count >= 1 && count <= Config.BeepSeconds) Beeper.Beep(1200, 0.15, model);
                }
            }

            return new TimerDisplay
            {
                Text = $"{Directions.Arrows[dir]} {count}",
                Detail = $"{Directions.Names[dir]} · luego {Directions.Names[(dir + 1) % 4]}",
                Critical = watch.IsRunning && count <= Config.BeepSeconds,
                Progress = (t - slot * step) / step,
            };
        }

        /// "Cada 02:00 · Ctrl+Alt+P · Beep" (para la lista de timers).
        public string Summary
        {
            get
            {
                var c = Config;
                string what = c.IsRotation ? $"Rotación cada {c.Step} s"
                    : !c.Repeats ? $"Cuenta atrás {Model.Mmss(c.Cycle)}"
                    : c.Total > 0 ? $"Cada {Model.Mmss(c.Cycle)} durante {c.Total / 60} min"
                    : $"Cada {Model.Mmss(c.Cycle)}";
                string sound = string.IsNullOrEmpty(c.SoundPath) ? "Beep" : Path.GetFileNameWithoutExtension(c.SoundPath);
                return $"{what} · {Model.Describe((Keys)c.Shortcut)} · {sound}";
            }
        }
    }

    /// Timers y ajustes. Ajustes en %APPDATA%\AlfaHub\ajustes.ini y timers en timers.json.
    sealed class Model
    {
        public const string DefaultCounterColor = "A873FA", DefaultAlertColor = "F54D4D", DefaultBackgroundColor = "111827D9";
        public const int MaxTimers = 8;
        public static readonly Dictionary<GlobalAction, Keys> DefaultGlobalShortcuts = new Dictionary<GlobalAction, Keys>
        {
            [GlobalAction.Reset] = Keys.Control | Keys.Alt | Keys.R,
            [GlobalAction.Overlay] = Keys.Control | Keys.Alt | Keys.O,
        };

        public readonly List<TimerRun> Runs = new List<TimerRun>();
        public const int MaxMirrors = 10;
        public readonly List<MirrorConfig> Mirrors = new List<MirrorConfig>();
        public bool MirrorsVisible = true;
        /// Espejos desbloqueados: se ven siempre y se pueden arrastrar. No se guarda.
        public bool MirrorsUnlocked;
        public string SelectedId;
        string lastUsedId;

        // Ajustes generales
        public double Volume = 0.5;
        public bool Muted;
        public bool OverlayVisible = true;
        public double OverlayScale = 1;
        public double OverlayOpacity = 1;
        public Color CounterColor = Hex(DefaultCounterColor);
        public Color AlertColor = Hex(DefaultAlertColor);
        public Color BackgroundColor = Hex(DefaultBackgroundColor);
        public Point? OverlayPosition;
        public Dictionary<GlobalAction, Keys> GlobalShortcuts = new Dictionary<GlobalAction, Keys>(DefaultGlobalShortcuts);

        // Estado (no se guarda)
        public bool OverlayUnlocked;
        public bool TibiaRunning;

        public TimerRun Selected => Runs.FirstOrDefault(r => r.Id == SelectedId) ?? Runs[0];
        public TimerRun Find(string id) => Runs.FirstOrDefault(r => r.Id == id);
        public int RunningCount => Runs.Count(r => r.Running);

        /// Timers que se ven en el overlay: los que están en marcha o a medias; si no hay, el elegido.
        public List<TimerRun> OverlayRuns
        {
            get
            {
                var started = Runs.Where(r => r.Started).ToList();
                return started.Count > 0 ? started : new List<TimerRun> { Selected };
            }
        }

        public void Tick() { foreach (var run in Runs) run.Tick(); }

        public void Toggle(TimerRun run) { run.Toggle(); lastUsedId = run.Id; }
        public void Reset(TimerRun run) { run.Reset(); lastUsedId = run.Id; }

        public void Perform(GlobalAction action)
        {
            if (action == GlobalAction.Reset) (Find(lastUsedId) ?? Selected).Reset();
            else OverlayVisible = !OverlayVisible;
        }

        public TimerRun Add(TimerPreset preset)
        {
            if (Runs.Count >= MaxTimers) return null;
            var config = Presets.Config(preset);
            var names = new HashSet<string>(Runs.Select(r => r.Config.Name));
            if (names.Contains(config.Name))
            {
                int n = 2;
                while (names.Contains($"{config.Name} {n}")) n++;
                config.Name = $"{config.Name} {n}";
            }
            var run = new TimerRun(this, config);
            Runs.Add(run);
            SelectedId = run.Id;
            return run;
        }

        public void Duplicate(TimerRun run)
        {
            if (Runs.Count >= MaxTimers) return;
            var config = run.Config.Clone();
            config.Id = Guid.NewGuid().ToString();
            config.Shortcut = 0;  // una combinación solo puede estar en un timer
            config.Name += " (copia)";
            Runs.Insert(Runs.IndexOf(run) + 1, new TimerRun(this, config));
        }

        public void Remove(TimerRun run)
        {
            if (Runs.Count <= 1) return;
            int index = Runs.IndexOf(run);
            Runs.Remove(run);
            if (SelectedId == run.Id) SelectedId = Runs[Math.Min(index, Runs.Count - 1)].Id;
        }

        /// Carga un preset en un timer existente, conservando su id, sonido y atajo.
        public static void ApplyPreset(TimerRun run, TimerPreset preset)
        {
            var config = Presets.Config(preset);
            config.Id = run.Config.Id;
            config.SoundPath = run.Config.SoundPath;
            config.Shortcut = run.Config.Shortcut;
            run.Config = config;
            run.Changed();
        }

        public void RestoreColors()
        {
            CounterColor = Hex(DefaultCounterColor);
            AlertColor = Hex(DefaultAlertColor);
            BackgroundColor = Hex(DefaultBackgroundColor);
        }

        public void RestoreGlobalShortcuts() { GlobalShortcuts = new Dictionary<GlobalAction, Keys>(DefaultGlobalShortcuts); }

        /// Quita una combinación de todos los sitios (antes de asignarla a otro).
        void Release(Keys keys)
        {
            if (keys == Keys.None) return;
            foreach (var action in GlobalShortcuts.Keys.ToList()) if (GlobalShortcuts[action] == keys) GlobalShortcuts[action] = Keys.None;
            foreach (var run in Runs) if ((Keys)run.Config.Shortcut == keys) run.Config.Shortcut = 0;
        }

        public void SetShortcut(GlobalAction action, Keys keys) { Release(keys); GlobalShortcuts[action] = keys; }
        public void SetShortcut(TimerRun run, Keys keys) { Release(keys); run.Config.Shortcut = (int)keys; }

        public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
        public static string Mmss(int s) => $"{s / 60:00}:{s % 60:00}";

        // ---- Colores "RRGGBB" / "RRGGBBAA" ----

        public static Color Hex(string hex)
        {
            hex = (hex ?? "").Trim().TrimStart('#');
            if (hex.Length == 6) hex += "FF";
            if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
                return Color.White;
            return Color.FromArgb((int)(v & 0xFF), (int)(v >> 24), (int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF));
        }

        public static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

        // ---- Atajos ----

        public static string Describe(Keys keys)
        {
            if (keys == Keys.None) return "Sin atajo";
            var sb = new StringBuilder();
            if ((keys & Keys.Control) != 0) sb.Append("Ctrl+");
            if ((keys & Keys.Alt) != 0) sb.Append("Alt+");
            if ((keys & Keys.Shift) != 0) sb.Append("Mayús+");
            Keys code = keys & Keys.KeyCode;
            if (code >= Keys.D0 && code <= Keys.D9) sb.Append((char)('0' + (code - Keys.D0)));
            else if (code >= Keys.NumPad0 && code <= Keys.NumPad9) sb.Append("Num" + (code - Keys.NumPad0));
            else sb.Append(new KeysConverter().ConvertToInvariantString(code));
            return sb.ToString();
        }

        // ---- Guardado ----

        static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlfaHub");
        static string SettingsPath => Path.Combine(Folder, "ajustes.ini");
        static string TimersPath => Path.Combine(Folder, "timers.json");
        static string MirrorsPath => Path.Combine(Folder, "espejos.json");

        public void Save()
        {
            var inv = CultureInfo.InvariantCulture;
            var lines = new List<string>
            {
                "volume=" + Volume.ToString(inv), "muted=" + Muted, "overlayVisible=" + OverlayVisible,
                "overlayScale=" + OverlayScale.ToString(inv), "overlayOpacity=" + OverlayOpacity.ToString(inv),
                "counterColor=" + Hex(CounterColor), "alertColor=" + Hex(AlertColor), "backgroundColor=" + Hex(BackgroundColor),
                "selectedTimer=" + SelectedId, "mirrorsVisible=" + MirrorsVisible,
            };
            if (OverlayPosition.HasValue) lines.Add($"overlayPosition={OverlayPosition.Value.X},{OverlayPosition.Value.Y}");
            foreach (var pair in GlobalShortcuts) lines.Add("global." + pair.Key + "=" + (int)pair.Value);
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(SettingsPath, lines);
                using (var stream = File.Create(TimersPath))
                    new DataContractJsonSerializer(typeof(List<TimerConfig>)).WriteObject(stream, Runs.Select(r => r.Config).ToList());
                using (var stream = File.Create(MirrorsPath))
                    new DataContractJsonSerializer(typeof(List<MirrorConfig>)).WriteObject(stream, Mirrors);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Load()
        {
            var inv = CultureInfo.InvariantCulture;
            // Valores de la versión anterior (un solo timer), para migrarlos al timer Pez.
            var legacy = new Dictionary<string, string>();
            if (File.Exists(SettingsPath))
            {
                foreach (var line in File.ReadAllLines(SettingsPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                    legacy[key] = value;
                    try
                    {
                        switch (key)
                        {
                            case "volume": Volume = double.Parse(value, inv); break;
                            case "muted": Muted = bool.Parse(value); break;
                            case "overlayVisible": OverlayVisible = bool.Parse(value); break;
                            case "overlayScale": OverlayScale = double.Parse(value, inv); break;
                            case "overlayOpacity": OverlayOpacity = double.Parse(value, inv); break;
                            case "counterColor": CounterColor = Hex(value); break;
                            case "alertColor": AlertColor = Hex(value); break;
                            case "backgroundColor": BackgroundColor = Hex(value); break;
                            case "selectedTimer": SelectedId = value; break;
                            case "mirrorsVisible": MirrorsVisible = bool.Parse(value); break;
                            case "overlayPosition":
                                var xy = value.Split(',');
                                OverlayPosition = new Point(int.Parse(xy[0], inv), int.Parse(xy[1], inv));
                                break;
                            default:
                                if (key.StartsWith("global.", StringComparison.Ordinal) && Enum.TryParse(key.Substring(7), out GlobalAction action))
                                    GlobalShortcuts[action] = (Keys)int.Parse(value, inv);
                                else if (key == "shortcut.Reset") GlobalShortcuts[GlobalAction.Reset] = (Keys)int.Parse(value, inv);
                                else if (key == "shortcut.Overlay") GlobalShortcuts[GlobalAction.Overlay] = (Keys)int.Parse(value, inv);
                                break;
                        }
                    }
                    catch (FormatException) { }
                    catch (IndexOutOfRangeException) { }
                }
            }

            List<TimerConfig> configs = null;
            if (File.Exists(TimersPath))
            {
                try
                {
                    using (var stream = File.OpenRead(TimersPath))
                        configs = (List<TimerConfig>)new DataContractJsonSerializer(typeof(List<TimerConfig>)).ReadObject(stream);
                }
                catch (SerializationException) { configs = null; }
            }
            if (configs == null || configs.Count == 0)
            {
                // Primera vez (o migración): Pez con los ajustes de antes, Bakragore y Pilar.
                var pez = Presets.Config(TimerPreset.Pez);
                int n;
                bool b;
                if (legacy.TryGetValue("cycle", out string v) && int.TryParse(v, out n)) pez.Cycle = Math.Max(5, n);
                if (legacy.TryGetValue("warning", out v) && int.TryParse(v, out n)) pez.Warning = n;
                if (legacy.TryGetValue("beepSeconds", out v) && int.TryParse(v, out n)) pez.BeepSeconds = n;
                if (legacy.TryGetValue("cycleBeep", out v) && bool.TryParse(v, out b)) pez.CycleBeep = b;
                pez.Shortcut = legacy.TryGetValue("shortcut.Toggle", out v) && int.TryParse(v, out n) ? n : (int)(Keys.Control | Keys.Alt | Keys.P);
                configs = new List<TimerConfig> { pez, Presets.Config(TimerPreset.Bakragore), Presets.Config(TimerPreset.Pilar) };
            }
            Runs.Clear();
            foreach (var config in configs) Runs.Add(new TimerRun(this, config));

            Mirrors.Clear();
            if (File.Exists(MirrorsPath))
            {
                try
                {
                    using (var stream = File.OpenRead(MirrorsPath))
                        Mirrors.AddRange((List<MirrorConfig>)new DataContractJsonSerializer(typeof(List<MirrorConfig>)).ReadObject(stream));
                }
                catch (SerializationException) { }
            }
            if (Find(SelectedId) == null) SelectedId = Runs[0].Id;
        }
    }

    /// Beeps senoidales como los del sitio original, y sonidos propios (wav/mp3) con MCI de Windows.
    static class Beeper
    {
        static SoundPlayer player;

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder result, int length, IntPtr callback);

        public static void Beep(double freq, double duration, Model model)
        {
            if (model.Muted || model.Volume <= 0) return;
            try
            {
                player?.Stop();
                player = new SoundPlayer(new MemoryStream(Wav(freq, duration, model.Volume)));
                player.Play();
            }
            catch (InvalidOperationException) { }
        }

        /// Reproduce un archivo de audio; devuelve false si no se pudo (el llamador suena un beep).
        public static bool PlayFile(string path, Model model)
        {
            if (model.Muted || model.Volume <= 0) return true;
            if (!File.Exists(path)) return false;
            mciSendString("close alfahub", null, 0, IntPtr.Zero);
            if (mciSendString($"open \"{path}\" type mpegvideo alias alfahub", null, 0, IntPtr.Zero) != 0) return false;
            mciSendString($"setaudio alfahub volume to {(int)(model.Volume * 1000)}", null, 0, IntPtr.Zero);
            return mciSendString("play alfahub", null, 0, IntPtr.Zero) == 0;
        }

        static byte[] Wav(double freq, double duration, double volume)
        {
            const int rate = 44100;
            int count = (int)(rate * duration);
            double decay = Math.Log(0.01) / duration;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + count * 2);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(count * 2);
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / rate;
                    double attack = Math.Min(1, t / 0.004);
                    w.Write((short)(Math.Sin(2 * Math.PI * freq * t) * Math.Exp(decay * t) * attack * 32000 * volume));
                }
                return ms.ToArray();
            }
        }
    }
}

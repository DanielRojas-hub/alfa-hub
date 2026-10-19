using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text;
using System.Windows.Forms;

namespace AlfaHub
{
    enum HotKeyAction { Toggle = 1, Reset = 2, Overlay = 3 }

    /// Estado del timer y todos los ajustes. Los ajustes se guardan en %APPDATA%\AlfaHub\ajustes.ini.
    sealed class Model
    {
        public const int DefaultCycle = 120, DefaultWarning = 25, DefaultBeeps = 5;
        public const string DefaultCounterColor = "A873FA", DefaultAlertColor = "F54D4D", DefaultBackgroundColor = "111827D9";
        public static readonly Dictionary<HotKeyAction, Keys> DefaultShortcuts = new Dictionary<HotKeyAction, Keys>
        {
            [HotKeyAction.Toggle] = Keys.Control | Keys.Alt | Keys.P,
            [HotKeyAction.Reset] = Keys.Control | Keys.Alt | Keys.R,
            [HotKeyAction.Overlay] = Keys.Control | Keys.Alt | Keys.O,
        };

        // Ajustes
        public int Cycle = DefaultCycle;
        public int Warning = DefaultWarning;
        public int BeepSeconds = DefaultBeeps;
        public bool CycleBeep = true;
        public double Volume = 0.5;
        public bool Muted;
        public bool OverlayVisible = true;
        public double OverlayScale = 1;
        public double OverlayOpacity = 1;
        public Color CounterColor = Hex(DefaultCounterColor);
        public Color AlertColor = Hex(DefaultAlertColor);
        public Color BackgroundColor = Hex(DefaultBackgroundColor);
        public Point? OverlayPosition;
        public Dictionary<HotKeyAction, Keys> Shortcuts = new Dictionary<HotKeyAction, Keys>(DefaultShortcuts);

        // Estado (no se guarda)
        public bool OverlayUnlocked;
        readonly Stopwatch watch = new Stopwatch();
        public int Seconds { get; private set; }

        public bool Running => watch.IsRunning;
        public int Remaining => Cycle - Seconds % Cycle;
        public double Progress => (double)(Seconds % Cycle) / Cycle;
        public int Cycles => Seconds / Cycle;
        public bool Critical => Remaining <= Warning;

        public void Toggle() { if (watch.IsRunning) watch.Stop(); else watch.Start(); }
        public void Reset() { watch.Reset(); Seconds = 0; }

        public void Tick()
        {
            if (!watch.IsRunning) return;
            int e = (int)watch.Elapsed.TotalSeconds;
            if (e == Seconds) return;
            Seconds = e;
            if (CycleBeep && e > 0 && e % Cycle == 0) Beeper.Beep(880, 0.25, this);
            if (BeepSeconds > 0 && Remaining >= 1 && Remaining <= BeepSeconds) Beeper.Beep(1200, 0.15, this);
        }

        public void SetCycle(int seconds) { Cycle = Clamp(seconds, 5, 3600); }

        public void RestoreTimerDefaults()
        {
            Cycle = DefaultCycle; Warning = DefaultWarning; BeepSeconds = DefaultBeeps; CycleBeep = true;
        }

        public void RestoreColors()
        {
            CounterColor = Hex(DefaultCounterColor);
            AlertColor = Hex(DefaultAlertColor);
            BackgroundColor = Hex(DefaultBackgroundColor);
        }

        public void RestoreShortcuts() { Shortcuts = new Dictionary<HotKeyAction, Keys>(DefaultShortcuts); }

        /// Asigna un atajo; si otra acción tenía la misma combinación, se la quita.
        public void SetShortcut(HotKeyAction action, Keys keys)
        {
            if (keys != Keys.None)
                foreach (var other in new List<HotKeyAction>(Shortcuts.Keys))
                    if (other != action && Shortcuts[other] == keys) Shortcuts[other] = Keys.None;
            Shortcuts[action] = keys;
        }

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

        static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlfaHub", "ajustes.ini");

        public void Save()
        {
            var inv = CultureInfo.InvariantCulture;
            var lines = new List<string>
            {
                "cycle=" + Cycle, "warning=" + Warning, "beepSeconds=" + BeepSeconds, "cycleBeep=" + CycleBeep,
                "volume=" + Volume.ToString(inv), "muted=" + Muted, "overlayVisible=" + OverlayVisible,
                "overlayScale=" + OverlayScale.ToString(inv), "overlayOpacity=" + OverlayOpacity.ToString(inv),
                "counterColor=" + Hex(CounterColor), "alertColor=" + Hex(AlertColor), "backgroundColor=" + Hex(BackgroundColor),
            };
            if (OverlayPosition.HasValue) lines.Add($"overlayPosition={OverlayPosition.Value.X},{OverlayPosition.Value.Y}");
            foreach (var pair in Shortcuts) lines.Add("shortcut." + pair.Key + "=" + (int)pair.Value);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllLines(SettingsPath, lines);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Load()
        {
            if (!File.Exists(SettingsPath)) return;
            var inv = CultureInfo.InvariantCulture;
            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                try
                {
                    switch (key)
                    {
                        case "cycle": SetCycle(int.Parse(value, inv)); break;
                        case "warning": Warning = int.Parse(value, inv); break;
                        case "beepSeconds": BeepSeconds = int.Parse(value, inv); break;
                        case "cycleBeep": CycleBeep = bool.Parse(value); break;
                        case "volume": Volume = double.Parse(value, inv); break;
                        case "muted": Muted = bool.Parse(value); break;
                        case "overlayVisible": OverlayVisible = bool.Parse(value); break;
                        case "overlayScale": OverlayScale = double.Parse(value, inv); break;
                        case "overlayOpacity": OverlayOpacity = double.Parse(value, inv); break;
                        case "counterColor": CounterColor = Hex(value); break;
                        case "alertColor": AlertColor = Hex(value); break;
                        case "backgroundColor": BackgroundColor = Hex(value); break;
                        case "overlayPosition":
                            var xy = value.Split(',');
                            OverlayPosition = new Point(int.Parse(xy[0], inv), int.Parse(xy[1], inv));
                            break;
                        default:
                            if (key.StartsWith("shortcut.", StringComparison.Ordinal) &&
                                Enum.TryParse(key.Substring(9), out HotKeyAction action))
                                Shortcuts[action] = (Keys)int.Parse(value, inv);
                            break;
                    }
                }
                catch (FormatException) { }
                catch (IndexOutOfRangeException) { }
            }
        }
    }

    /// Beeps senoidales con caída exponencial, como los del sitio original.
    static class Beeper
    {
        static SoundPlayer player;

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

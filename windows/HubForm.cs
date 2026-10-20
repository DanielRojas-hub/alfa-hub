using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Colores de la app (mismos que la versión de macOS).
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(28, 28, 30);         // contenido
        public static readonly Color Sidebar = Color.FromArgb(21, 21, 23);      // barra lateral y superior
        public static readonly Color Line = Color.FromArgb(40, 40, 43);         // separadores
        public static readonly Color HeroCard = Color.FromArgb(38, 38, 41);     // tarjeta del timer
        public static readonly Color Row = Color.FromArgb(40, 40, 43);          // filas y secciones
        public static readonly Color Segment = Color.FromArgb(44, 44, 47);      // fondo de las pestañas
        public static readonly Color ControlFill = Color.FromArgb(62, 62, 66);  // botones grises
        public static readonly Color Field = Color.FromArgb(22, 22, 24);        // campos de texto
        public static readonly Color Fore = Color.FromArgb(240, 240, 242);
        public static readonly Color Muted = Color.FromArgb(150, 150, 156);
        /// Contador del hub en blanco hueso; botones y selección en azul.
        public static readonly Color Accent = Color.FromArgb(237, 237, 239);
        public static readonly Color Blue = Color.FromArgb(10, 132, 255);
        public static readonly Color Amber = Color.FromArgb(217, 120, 5);
        public static readonly Color Green = Color.FromArgb(48, 209, 88);
    }

    /// Recursos incrustados en el .exe.
    static class Resources
    {
        static Stream Open(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream("AlfaHub." + name);

        /// Sprite del personaje (Outfiter de TibiaWiki), recortado a su contorno.
        public static readonly Image Character = LoadImage("character.png");

        public static Icon AppIcon(Size size)
        {
            using (var stream = Open("AlfaHub.ico")) return stream == null ? SystemIcons.Application : new Icon(stream, size);
        }

        static Image LoadImage(string name)
        {
            using (var stream = Open(name))
            {
                if (stream == null) return null;
                using (var image = Image.FromStream(stream)) return new Bitmap(image);
            }
        }

        public static void DrawPixelArt(Graphics g, Image image, RectangleF rect)
        {
            if (image == null) return;
            var mode = g.InterpolationMode;
            var offset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, rect);
            g.InterpolationMode = mode;
            g.PixelOffsetMode = offset;
        }
    }

    enum HubPage { Timers, Audio, Mirrors, Outfit, Shortcuts, Sound }

    /// Ventana principal: barra superior, barra lateral y una página a la derecha.
    sealed partial class HubForm : Form
    {
        readonly Model model;
        readonly TopBar topBar;
        readonly Sidebar sidebar;
        readonly Panel content = new Panel { Dock = DockStyle.Fill };
        HubPage page = HubPage.Timers;
        string editingId, editingMirrorId, editingAudioId;

        // Página visible y sus controles vivos (se refrescan en cada tick)
        readonly List<TimerRow> rows = new List<TimerRow>();
        readonly List<AudioRow> audioRows = new List<AudioRow>();
        Label audioStatus;
        HeroCard hero;
        PreviewPanel preview;
        Label countLabel;

        public event Action Changed;               // cualquier ajuste cambió (guardar y redibujar)
        public event Action ShortcutsChanged;      // volver a registrar los atajos globales
        public event Action<bool> RecordingShortcut;
        public event Action<int> HotKeyPressed;
        public event Action MirrorsChanged;        // espejos añadidos, quitados o cambiados
        /// El personaje elegido en la página Outfit.
        public readonly OutfitState Outfit;

        public HubForm(Model model)
        {
            this.model = model;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);  // medidas en píxeles a 100 %; se escalan con el DPI
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Alfa Hub";
            BackColor = Theme.Back;
            ForeColor = Theme.Fore;
            Font = new Font("Segoe UI", 9.75f);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 620);
            MinimumSize = new Size(836, 560);

            topBar = new TopBar(model) { Dock = DockStyle.Top, Height = 52 };
            topBar.OverlayToggled += () => { model.OverlayVisible = !model.OverlayVisible; Changed?.Invoke(); RefreshTimer(); };
            Outfit = new OutfitState(this);
            sidebar = new Sidebar { Dock = DockStyle.Left, Width = 200, Character = () => Outfit.Display };
            Outfit.Changed += () => sidebar.Invalidate();
            sidebar.Navigate += p => { editingId = null; editingMirrorId = null; editingAudioId = null; ShowPage(p); };

            Controls.Add(content);
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Line });
            Controls.Add(sidebar);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Line });
            Controls.Add(topBar);

            ShowPage(HubPage.Timers);
            ResumeLayout(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkTitleBar(Handle);
        }

        // ---------- Navegación ----------

        void ShowPage(HubPage target)
        {
            page = target;
            sidebar.Select(target);
            rows.Clear();
            audioRows.Clear();
            audioStatus = null;
            hero = null;
            preview = null;
            countLabel = null;

            Control view;
            if (target == HubPage.Timers)
                view = editingId != null && model.Find(editingId) != null ? BuildEditor(model.Find(editingId)) : BuildTimersPage();
            else if (target == HubPage.Mirrors)
            {
                var mirror = model.Mirrors.FirstOrDefault(m => m.Id == editingMirrorId);
                view = mirror != null ? BuildMirrorEditor(mirror) : BuildMirrorsPage();
            }
            else if (target == HubPage.Audio)
            {
                var audio = model.Audios.FirstOrDefault(a => a.Id == editingAudioId);
                view = audio != null ? BuildAudioEditor(audio) : BuildAudioPage();
            }
            else if (target == HubPage.Outfit) view = BuildOutfitPage();
            else if (target == HubPage.Shortcuts) view = BuildShortcutsPage();
            else view = BuildSoundPage();

            content.SuspendLayout();
            foreach (Control old in content.Controls.Cast<Control>().ToList()) old.Dispose();
            content.Controls.Clear();
            view.Dock = DockStyle.Fill;
            content.Controls.Add(view);
            content.ResumeLayout(true);
            RefreshTimer();
        }

        void Edit(TimerRun run)
        {
            editingId = run.Id;
            model.SelectedId = run.Id;
            ShowPage(HubPage.Timers);
        }

        /// Vuelve a construir la página actual (tras añadir/borrar timers o cambiar el tipo de uno).
        void Rebuild() => ShowPage(page);

        static StackPanel Page(params Control[] items)
        {
            var stack = new StackPanel { Padding = new Padding(28, 20, 28, 24), BackColor = Theme.Back };
            foreach (var item in items) stack.Controls.Add(item);
            return stack;
        }

        // ---------- Página: Timers ----------

        Control BuildTimersPage()
        {
            var add = new RoundButton { Text = "＋  Añadir timer", FillColor = Theme.Blue, Pill = true, Height = 34, BackColor = Theme.Back };
            add.Font = new Font("Segoe UI Semibold", 10f);
            add.FitText(16);
            add.Click += (s, e) => PresetMenu(preset =>
            {
                var run = model.Add(preset);
                Changed?.Invoke();
                ShortcutsChanged?.Invoke();
                if (run != null) Edit(run);
            }).Show(add, new Point(0, add.Height + 4));

            countLabel = new Label { AutoSize = true, ForeColor = Theme.Muted, BackColor = Theme.Back };
            var pauseAll = SmallButton("❚❚  Pausar todos", () => { foreach (var r in model.Runs.Where(r => r.Running)) model.Toggle(r); RefreshTimer(); });
            var resetAll = SmallButton("↺  Reiniciar todos", () => { foreach (var r in model.Runs) model.Reset(r); RefreshTimer(); });
            var unlock = SmallButton(model.OverlayUnlocked ? "🔒  Bloquear contadores" : "🔓  Desbloquear para moverlos", () =>
            {
                model.OverlayUnlocked = !model.OverlayUnlocked;
                if (model.OverlayUnlocked) model.OverlayVisible = true;
                Changed?.Invoke();
                Rebuild();
            });
            var toolbar = new ToolbarRow(countLabel, unlock, pauseAll, resetAll) { Margin = new Padding(0, 4, 0, 8) };

            var items = new List<Control> { new PageHeader("Timers", "Cuentas atrás y rotaciones con aviso; cada uno con su contador sobre Tibia", add), toolbar };
            foreach (var run in model.Runs)
            {
                var row = new TimerRow(model, run) { Margin = new Padding(0, 0, 0, 8) };
                row.ToggleClicked += () => { model.Toggle(run); RefreshTimer(); };
                row.ResetClicked += () => { model.Reset(run); RefreshTimer(); };
                row.EditClicked += () => Edit(run);
                row.Selected += () => { model.SelectedId = run.Id; RefreshTimer(); };
                row.DuplicateClicked += () => { model.Duplicate(run); Changed?.Invoke(); Rebuild(); };
                row.DeleteClicked += () => { model.Remove(run); Changed?.Invoke(); ShortcutsChanged?.Invoke(); Rebuild(); };
                rows.Add(row);
                items.Add(row);
            }
            return Page(items.ToArray());
        }

        ContextMenuStrip PresetMenu(Action<TimerPreset> pick)
        {
            var menu = DarkMenu.Create();
            foreach (TimerPreset preset in Enum.GetValues(typeof(TimerPreset)))
            {
                var p = preset;
                menu.Items.Add($"{Presets.Title(p)} — {Presets.Detail(p)}", null, (s, e) => pick(p));
            }
            return menu;
        }

        static RoundButton SmallButton(string text, Action onClick)
        {
            var b = new RoundButton { Text = text, Height = 28, BackColor = Theme.Back, FillColor = Theme.Row, BorderColor = Theme.Line };
            b.FitText(10);
            b.Click += (s, e) => onClick();
            return b;
        }

        // ---------- Página: editor de un timer ----------

        Control BuildEditor(TimerRun run)
        {
            var c = run.Config;
            var back = new RoundButton { Text = "‹  Volver a Timers", ForeColor = Theme.Blue, FillColor = Theme.Back, BackColor = Theme.Back, Height = 26 };
            back.FitText(2);
            back.Click += (s, e) => { editingId = null; ShowPage(HubPage.Timers); };
            hero = new HeroCard(model, run) { Margin = new Padding(0, 6, 0, 4) };
            hero.ToggleClicked += () => { model.Toggle(run); RefreshTimer(); };
            hero.ResetClicked += () => { model.Reset(run); RefreshTimer(); };

            // Timer
            var name = new TextField { Value = c.Name };
            name.TextCommitted += (s, e) =>
            {
                string value = name.Value.Trim();
                if (value.Length == 0 || value == run.Config.Name) return;
                run.Config.Name = value;
                Changed?.Invoke();
                RefreshTimer();
            };
            var kind = new SegmentedControl("Cuenta atrás", "Rotación") { Width = 210, Height = 30 };
            kind.SetSelected(c.IsRotation ? 1 : 0);
            kind.SelectedIndexChanged += i => { run.Config.Kind = i == 1 ? "rotation" : "cycle"; Apply(run); Rebuild(); };
            var presetButton = ActionButton("Cargar preset  ▾", null);
            presetButton.Click += (s, e) => PresetMenu(p => { Model.ApplyPreset(run, p); Changed?.Invoke(); Rebuild(); })
                .Show(presetButton, new Point(0, presetButton.Height + 4));

            var backRow = new FormRow(null, back, RowLayout.Leading, 28) { BackColor = Theme.Back, Margin = new Padding(-12, 0, 0, 0) };
            back.BackColor = Theme.Back;
            var items = new List<Control>
            {
                backRow, hero,
                new SectionHeader("Timer"),
                new SectionCard(Row("Nombre", name), Row("Tipo", kind), new FormRow(null, presetButton, RowLayout.Leading)),
            };

            if (!c.IsRotation)
            {
                var chips = new InlineGroup { Height = 30 };
                foreach (int preset in new[] { 60, 90, 120, 150, 180 })
                {
                    int value = preset;
                    var pill = new PillButton { Text = Model.Mmss(value), Width = 62, Selected = c.Cycle == value };
                    pill.Click += (s, e) => { run.Config.Cycle = value; Apply(run); Rebuild(); };
                    chips.Controls.Add(pill);
                }
                var minutes = new NumberField { Maximum = 60, Value = c.Cycle / 60 };
                var seconds = new NumberField { Maximum = 59, Value = c.Cycle % 60 };
                var stepper = new StepperArrows();
                minutes.ValueCommitted += (s, e) => { run.Config.Cycle = Math.Max(5, minutes.Value * 60 + run.Config.Cycle % 60); Apply(run); Rebuild(); };
                seconds.ValueCommitted += (s, e) => { run.Config.Cycle = Math.Max(5, run.Config.Cycle / 60 * 60 + seconds.Value); Apply(run); Rebuild(); };
                stepper.Up += () => { run.Config.Cycle = Math.Min(3600, run.Config.Cycle + 5); Apply(run); Rebuild(); };
                stepper.Down += () => { run.Config.Cycle = Math.Max(5, run.Config.Cycle - 5); Apply(run); Rebuild(); };
                var repeats = new ToggleSwitch { Checked = c.Repeats };
                repeats.CheckedChanged += (s, e) => { run.Config.Repeats = repeats.Checked; Apply(run); Rebuild(); };

                var duration = new List<FormRow>
                {
                    new FormRow(null, chips, RowLayout.Centered, 46),
                    Row("Personalizado", new InlineGroup(minutes, new Caption("min"), seconds, new Caption("s"), stepper) { Height = 28 }),
                    Row("Repetir al llegar a cero", repeats),
                };
                if (c.Repeats)
                {
                    var total = new NumberField { Maximum = 600, Value = c.Total / 60, Width = 48 };
                    var totalStepper = new StepperArrows();
                    total.ValueCommitted += (s, e) => { run.Config.Total = total.Value * 60; Apply(run); };
                    totalStepper.Up += () => { run.Config.Total = Math.Min(600, run.Config.Total / 60 + 1) * 60; total.Value = run.Config.Total / 60; Apply(run); };
                    totalStepper.Down += () => { run.Config.Total = Math.Max(0, run.Config.Total / 60 - 1) * 60; total.Value = run.Config.Total / 60; Apply(run); };
                    duration.Add(Row("Duración total", new InlineGroup(total, new Caption("min"), totalStepper) { Height = 28 }));
                }

                FormRow warningRow = null, beepsRow = null;
                warningRow = Row($"Alerta en los últimos {c.Warning} s", Stepper(
                    () => { run.Config.Warning = Math.Min(run.Config.Cycle, run.Config.Warning + 1); },
                    () => { run.Config.Warning = Math.Max(0, run.Config.Warning - 1); },
                    () => $"Alerta en los últimos {run.Config.Warning} s", run, r => warningRow.Text = r));
                beepsRow = Row($"Beeps en los últimos {c.BeepSeconds} s", Stepper(
                    () => { run.Config.BeepSeconds = Math.Min(30, run.Config.BeepSeconds + 1); },
                    () => { run.Config.BeepSeconds = Math.Max(0, run.Config.BeepSeconds - 1); },
                    () => $"Beeps en los últimos {run.Config.BeepSeconds} s", run, r => beepsRow.Text = r));
                var cycleBeep = new ToggleSwitch { Checked = c.CycleBeep };
                cycleBeep.CheckedChanged += (s, e) => { run.Config.CycleBeep = cycleBeep.Checked; Apply(run); };

                items.Add(new SectionHeader("Duración"));
                items.Add(new SectionCard(duration.ToArray()));
                items.Add(new SectionHeader("Avisos"));
                items.Add(new SectionCard(warningRow, beepsRow, Row("Sonido de aviso al empezar cada ciclo", cycleBeep)));
                items.Add(new NoteLabel(c.Repeats && c.Total == 0
                    ? "Duración total en 0: se repite hasta que lo pares."
                    : "Al terminar suena el sonido de aviso."));
            }
            else
            {
                FormRow stepRow = null, beepsRow = null;
                stepRow = Row($"Segundos por dirección: {c.Step}", Stepper(
                    () => { run.Config.Step = Math.Min(60, run.Config.Step + 1); },
                    () => { run.Config.Step = Math.Max(1, run.Config.Step - 1); },
                    () => $"Segundos por dirección: {run.Config.Step}", run, r => stepRow.Text = r));
                beepsRow = Row($"Beeps en los últimos {c.BeepSeconds} s", Stepper(
                    () => { run.Config.BeepSeconds = Math.Min(run.Config.Step, run.Config.BeepSeconds + 1); },
                    () => { run.Config.BeepSeconds = Math.Max(0, run.Config.BeepSeconds - 1); },
                    () => $"Beeps en los últimos {run.Config.BeepSeconds} s", run, r => beepsRow.Text = r));
                var direction = new SegmentedControl("↑ N", "→ Der", "↓ S", "← Izq") { Width = 240, Height = 30 };
                direction.SetSelected(c.StartDirection);
                direction.SelectedIndexChanged += i => { run.Config.StartDirection = i; Apply(run); };
                items.Add(new SectionHeader("Rotación"));
                items.Add(new SectionCard(stepRow, Row("Dirección inicial", direction), beepsRow));
            }

            // Sonido y atajo
            var soundGroup = SoundChooser(() => run.Config.Sound, v => run.Config.Sound = v, () => run.Config.SoundPath,
                v => run.Config.SoundPath = v, "Sonido de aviso para " + run.Config.Name, () => run.PlayAlert(), () => Apply(run));
            var shortcut = ShortcutFor(run);
            items.Add(new SectionHeader("Sonido y atajo"));
            items.Add(new SectionCard(
                Row("Sonido de aviso", soundGroup),
                Row("Atajo iniciar / pausar", shortcut)));
            items.Add(new NoteLabel("El sonido de aviso suena al empezar cada ciclo (o cada dirección) y al terminar. Los beeps de los últimos segundos son aparte."));
            items.AddRange(OverlaySection(run));

            if (model.Runs.Count > 1)
            {
                var delete = ActionButton("Eliminar este timer", () =>
                {
                    model.Remove(run);
                    editingId = null;
                    Changed?.Invoke();
                    ShortcutsChanged?.Invoke();
                    ShowPage(HubPage.Timers);
                });
                delete.ForeColor = Color.FromArgb(255, 105, 97);
                items.Add(new SectionCard(new FormRow(null, delete, RowLayout.Leading)) { Margin = new Padding(0, 16, 0, 0) });
            }
            return Page(items.ToArray());
        }

        /// Flechas que cambian un valor del timer y actualizan el texto de su fila.
        StepperArrows Stepper(Action up, Action down, Func<string> label, TimerRun run, Action<string> setLabel)
        {
            var arrows = new StepperArrows();
            arrows.Up += () => { up(); setLabel(label()); Apply(run); };
            arrows.Down += () => { down(); setLabel(label()); Apply(run); };
            return arrows;
        }

        void Apply(TimerRun run)
        {
            run.Changed();
            Changed?.Invoke();
            RefreshTimer();
        }

        ShortcutButton ShortcutFor(TimerRun run)
        {
            var button = new ShortcutButton { Width = 150, Height = 26 };
            button.Display((Keys)run.Config.Shortcut);
            button.Recorded += keys =>
            {
                model.SetShortcut(run, keys);
                Changed?.Invoke();
                ShortcutsChanged?.Invoke();
                Rebuild();
            };
            button.RecordingChanged += recording => RecordingShortcut?.Invoke(recording);
            return button;
        }

        // ---------- Página: Espejos ----------

        Control BuildMirrorsPage()
        {
            var add = new RoundButton { Text = "＋  Añadir espejo", FillColor = Theme.Blue, Pill = true, Height = 34, BackColor = Theme.Back };
            add.Font = new Font("Segoe UI Semibold", 10f);
            add.FitText(16);
            add.Click += (s, e) => AddMirror();

            var count = new Label
            {
                AutoSize = true, ForeColor = Theme.Muted, BackColor = Theme.Back,
                Text = $"{model.Mirrors.Count} {(model.Mirrors.Count == 1 ? "espejo" : "espejos")} · {model.Mirrors.Count(m => m.Visible)} visibles",
            };
            var showAll = SmallButton(model.MirrorsVisible ? "Ocultar todos" : "Mostrar todos", () =>
            {
                model.MirrorsVisible = !model.MirrorsVisible;
                MirrorsChanged?.Invoke();
                Rebuild();
            });
            var lockAll = SmallButton(model.MirrorsUnlocked ? "🔒  Bloquear" : "🔓  Desbloquear para moverlos", () =>
            {
                model.MirrorsUnlocked = !model.MirrorsUnlocked;
                MirrorsChanged?.Invoke();
                Rebuild();
            });
            var items = new List<Control>
            {
                new PageHeader("Espejos", "Copia las partes de Tibia que necesitas donde quieras", add),
                new ToolbarRow(count, showAll, lockAll) { Margin = new Padding(0, 4, 0, 8) },
            };
            if (model.Mirrors.Count == 0)
                items.Add(new NoteLabel("Todavía no hay espejos. Con Tibia abierto, pulsa “Añadir espejo” y arrastra sobre la parte que quieras copiar (cooldowns, hechizos, barra de vida…)."));
            foreach (var mirror in model.Mirrors)
            {
                var m = mirror;
                var row = new MirrorRow(m) { Margin = new Padding(0, 0, 0, 8) };
                row.Changed += () => MirrorsChanged?.Invoke();
                row.EditClicked += () => { editingMirrorId = m.Id; ShowPage(HubPage.Mirrors); };
                row.ReselectClicked += () => Reselect(m);
                row.DeleteClicked += () => { model.Mirrors.Remove(m); MirrorsChanged?.Invoke(); Rebuild(); };
                items.Add(row);
            }
            items.Add(new NoteLabel("Los espejos solo se ven con Tibia al frente y dejan pasar los clics al juego. Los dibuja Windows con sus miniaturas de ventana (las mismas de la barra de tareas): Alfa Hub no captura la pantalla ni lee nada del cliente."));
            return Page(items.ToArray());
        }

        Control BuildMirrorEditor(MirrorConfig m)
        {
            var back = new RoundButton { Text = "‹  Volver a Espejos", ForeColor = Theme.Blue, FillColor = Theme.Back, BackColor = Theme.Back, Height = 26 };
            back.FitText(2);
            back.Click += (s, e) => { editingMirrorId = null; ShowPage(HubPage.Mirrors); };
            var backRow = new FormRow(null, back, RowLayout.Leading, 28) { BackColor = Theme.Back, Margin = new Padding(-12, 0, 0, 0) };
            back.BackColor = Theme.Back;

            var name = new TextField { Value = m.Name };
            name.TextCommitted += (s, e) =>
            {
                string value = name.Value.Trim();
                if (value.Length == 0 || value == m.Name) return;
                m.Name = value;
                MirrorsChanged?.Invoke();
            };
            var zoom = new FlatSlider { Minimum = 25, Maximum = 400, Value = m.Zoom, Width = 190 };
            var zoomLabel = new Caption($"{m.Zoom} %") { Width = 48 };
            zoom.ValueChanged += (s, e) => { m.Zoom = zoom.Value; zoomLabel.Text = $"{zoom.Value} %"; MirrorsChanged?.Invoke(); };
            var opacity = new FlatSlider { Minimum = 10, Maximum = 100, Value = m.Opacity, Width = 190 };
            var opacityLabel = new Caption($"{m.Opacity} %") { Width = 48 };
            opacity.ValueChanged += (s, e) => { m.Opacity = opacity.Value; opacityLabel.Text = $"{opacity.Value} %"; MirrorsChanged?.Invoke(); };
            var visible = new ToggleSwitch { Checked = m.Visible };
            visible.CheckedChanged += (s, e) => { m.Visible = visible.Checked; MirrorsChanged?.Invoke(); };
            var border = new ToggleSwitch { Checked = m.Border };
            border.CheckedChanged += (s, e) => { m.Border = border.Checked; MirrorsChanged?.Invoke(); };
            var delete = ActionButton("Eliminar este espejo", () =>
            {
                model.Mirrors.Remove(m);
                editingMirrorId = null;
                MirrorsChanged?.Invoke();
                ShowPage(HubPage.Mirrors);
            });
            delete.ForeColor = Color.FromArgb(255, 105, 97);

            return Page(
                backRow,
                new PageHeader(m.Name, $"Región de {m.Width} × {m.Height} px de la ventana de Tibia"),
                new SectionHeader("Espejo"),
                new SectionCard(
                    Row("Nombre", name),
                    Row("Visible", visible),
                    Row("Tamaño", new InlineGroup(zoom, zoomLabel) { Gap = 8 }),
                    Row("Opacidad", new InlineGroup(opacity, opacityLabel) { Gap = 8 }),
                    Row("Borde", border),
                    new FormRow(null, ActionButton("Volver a elegir la región", () => Reselect(m)), RowLayout.Leading)),
                new NoteLabel("Para moverlo, usa “Desbloquear para moverlos” en la lista de espejos y arrástralo."),
                new SectionCard(new FormRow(null, delete, RowLayout.Leading)) { Margin = new Padding(0, 16, 0, 0) });
        }

        /// Oculta el hub y deja elegir una región arrastrando sobre la ventana de Tibia.
        bool SelectRegion(out Rectangle region, out Rectangle tibiaClient)
        {
            region = tibiaClient = Rectangle.Empty;
            IntPtr tibia = TibiaWindow.Find();
            if (tibia == IntPtr.Zero)
            {
                MessageBox.Show(this, "Abre Tibia (en modo ventana o pantalla completa sin bordes) para elegir la región.",
                    "Alfa Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            tibiaClient = TibiaWindow.ClientScreenRect(tibia);
            Hide();
            Native.SetForegroundWindow(tibia);
            System.Threading.Thread.Sleep(250);
            bool ok;
            using (var selection = new SelectionForm(tibiaClient))
            {
                ok = selection.ShowDialog() == DialogResult.OK;
                region = selection.Result;
            }
            Show();
            Activate();
            return ok;
        }

        void AddMirror()
        {
            if (!SelectRegion(out Rectangle region, out Rectangle client)) return;
            var mirror = new MirrorConfig
            {
                Name = $"Espejo {model.Mirrors.Count + 1}",
                X = region.X, Y = region.Y, Width = region.Width, Height = region.Height,
            };
            // Aparece junto a la región elegida (sin tapar el hub); queda desbloqueado para moverlo a su sitio.
            Point place = PlaceNear(new Rectangle(client.X + region.X, client.Y + region.Y, region.Width, region.Height), mirror.DisplaySize);
            mirror.Left = place.X;
            mirror.Top = place.Y;
            model.Mirrors.Add(mirror);
            model.MirrorsUnlocked = true;
            MirrorsChanged?.Invoke();
            editingMirrorId = null;
            ShowPage(HubPage.Mirrors);
        }

        /// Primer sitio libre junto a la región (derecha, izquierda, debajo, encima) que no tape el hub.
        Point PlaceNear(Rectangle region, Size size)
        {
            const int gap = 16;
            Rectangle screen = Screen.FromRectangle(region).WorkingArea;
            var candidates = new[]
            {
                new Point(region.Right + gap, region.Top),
                new Point(region.Left - gap - size.Width, region.Top),
                new Point(region.Left, region.Bottom + gap),
                new Point(region.Left, region.Top - gap - size.Height),
            };
            foreach (var p in candidates)
            {
                var rect = new Rectangle(p, size);
                if (screen.Contains(rect) && !rect.IntersectsWith(Bounds)) return p;
            }
            // Si no cabe en ningún lado, en la esquina contraria al hub.
            int x = Bounds.Left - screen.Left > screen.Right - Bounds.Right ? screen.Left + 20 : screen.Right - size.Width - 20;
            return new Point(x, screen.Top + 80);
        }

        void Reselect(MirrorConfig m)
        {
            if (!SelectRegion(out Rectangle region, out _)) return;
            m.X = region.X; m.Y = region.Y; m.Width = region.Width; m.Height = region.Height;
            MirrorsChanged?.Invoke();
            Rebuild();
        }

        // ---------- Editor de un timer: su overlay ----------

        IEnumerable<Control> OverlaySection(TimerRun run)
        {
            var style = run.Config.Overlay;
            preview = new PreviewPanel(run);
            var previewAlert = new ToggleSwitch();
            previewAlert.CheckedChanged += (s, e) => preview.ShowAlert = previewAlert.Checked;

            var show = new ToggleSwitch { Checked = style.Show };
            var scale = new FlatSlider { Minimum = 60, Maximum = 300, Value = (int)Math.Round(style.Scale * 100), Width = 190 };
            var scaleLabel = new Caption($"{scale.Value} %") { Width = 48 };
            var opacity = new FlatSlider { Minimum = 20, Maximum = 100, Value = (int)Math.Round(style.Opacity * 100), Width = 190 };
            var opacityLabel = new Caption($"{opacity.Value} %") { Width = 48 };
            var counterColor = new ColorSwatch { FillColor = style.Counter };
            var alertColor = new ColorSwatch { FillColor = style.Alert };
            var backgroundColor = new ColorSwatch { FillColor = Color.FromArgb(255, style.Background) };
            var backgroundAlpha = new FlatSlider { Maximum = 100, Value = (int)Math.Round(style.Background.A / 2.55) };

            show.CheckedChanged += (s, e) => { style.Show = show.Checked; Apply(run); };
            scale.ValueChanged += (s, e) => { style.Scale = scale.Value / 100.0; scaleLabel.Text = $"{scale.Value} %"; preview.Invalidate(); Changed?.Invoke(); };
            opacity.ValueChanged += (s, e) => { style.Opacity = opacity.Value / 100.0; opacityLabel.Text = $"{opacity.Value} %"; preview.Invalidate(); Changed?.Invoke(); };
            counterColor.Click += (s, e) => PickColor(style.Counter, c => { style.CounterColor = Model.Hex(c); counterColor.FillColor = c; });
            alertColor.Click += (s, e) => PickColor(style.Alert, c => { style.AlertColor = Model.Hex(c); alertColor.FillColor = c; });
            backgroundColor.Click += (s, e) => PickColor(style.Background, c =>
            {
                style.BackgroundColor = Model.Hex(Color.FromArgb(style.Background.A, c));
                backgroundColor.FillColor = Color.FromArgb(255, c);
            });
            backgroundAlpha.ValueChanged += (s, e) =>
            {
                style.BackgroundColor = Model.Hex(Color.FromArgb((int)Math.Round(backgroundAlpha.Value * 2.55), style.Background));
                preview.Invalidate();
                Changed?.Invoke();
            };
            var restore = ActionButton("Restaurar colores", () =>
            {
                style.CounterColor = OverlayStyle.DefaultCounter;
                style.AlertColor = OverlayStyle.DefaultAlert;
                style.BackgroundColor = OverlayStyle.DefaultBackground;
                Changed?.Invoke();
                Rebuild();
            });
            var autoPlace = ActionButton("Volver a su sitio automático", () => { style.HasPosition = false; Changed?.Invoke(); Rebuild(); });
            autoPlace.Enabled = style.HasPosition;

            return new Control[]
            {
                new SectionHeader("Overlay"),
                new SectionCard(
                    Row("Mostrar en el overlay", show),
                    new FormRow(null, preview, RowLayout.Stretch, 112),
                    Row("Ver en alerta", previewAlert),
                    Row("Tamaño", new InlineGroup(scale, scaleLabel) { Gap = 8 }),
                    Row("Opacidad", new InlineGroup(opacity, opacityLabel) { Gap = 8 }),
                    Row("Contador", counterColor),
                    Row("Contador en alerta", alertColor),
                    Row("Fondo", backgroundColor),
                    Row("Opacidad del fondo", backgroundAlpha),
                    new FormRow(null, new InlineGroup(restore, autoPlace) { Gap = 8 }, RowLayout.Leading)),
                new NoteLabel("El contador de este timer sobre Tibia: solo aparece con Tibia al frente y deja pasar los clics. Para moverlo, pulsa “Desbloquear para moverlos” en la lista de timers y arrástralo. Pon la opacidad del fondo en cero para ver solo el número."),
            };
        }

        /// Botón con el sonido elegido (menú con los incluidos, que suenan al elegirlos), archivo propio y probar.
        InlineGroup SoundChooser(Func<string> getSound, Action<string> setSound, Func<string> getPath, Action<string> setPath,
            string dialogTitle, Action play, Action changed)
        {
            var current = new RoundButton { Text = Model.SoundName(getSound(), getPath()) + "  ▾", Height = 26, FillColor = Theme.ControlFill };
            current.FitText(12);
            current.Click += (s, e) =>
            {
                var menu = DarkMenu.Create();
                foreach (string id in Sounds.Ids)
                {
                    string value = id;
                    var item = (ToolStripMenuItem)menu.Items.Add(Sounds.Title(value), null, (a, b) =>
                    {
                        setPath(null);
                        setSound(value);
                        changed();
                        play();
                        Rebuild();
                    });
                    item.Checked = string.IsNullOrEmpty(getPath()) && Sounds.Normalize(getSound()) == value;
                }
                menu.Show(current, new Point(0, current.Height + 2));
            };
            var file = ActionButton("Archivo propio…", () =>
            {
                using (var dialog = new OpenFileDialog { Filter = "Audio (*.wav;*.mp3)|*.wav;*.mp3", Title = dialogTitle })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    setPath(dialog.FileName);
                    changed();
                    Rebuild();
                }
            });
            var test = ActionButton("Probar", play);
            return new InlineGroup(current, file, test) { Gap = 8 };
        }

        void PickColor(Color current, Action<Color> apply)
        {
            using (var dialog = new ColorDialog { Color = Color.FromArgb(255, current), FullOpen = true, AnyColor = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                apply(dialog.Color);
                preview?.Invalidate();
                Changed?.Invoke();
            }
        }

        // ---------- Página: Atajos ----------

        Control BuildShortcutsPage()
        {
            var timerRows = model.Runs.Select(run => Row(run.Config.Name, ShortcutFor(run))).ToArray();
            var globalRows = new List<FormRow>();
            foreach (GlobalAction action in Enum.GetValues(typeof(GlobalAction)))
            {
                var a = action;
                var button = new ShortcutButton { Width = 150, Height = 26 };
                button.Display(model.GlobalShortcuts.TryGetValue(a, out Keys k) ? k : Keys.None);
                button.Recorded += keys => { model.SetShortcut(a, keys); Changed?.Invoke(); ShortcutsChanged?.Invoke(); Rebuild(); };
                button.RecordingChanged += recording => RecordingShortcut?.Invoke(recording);
                globalRows.Add(Row(a == GlobalAction.Reset ? "Reiniciar el último timer usado" : "Mostrar / ocultar overlay", button));
            }
            globalRows.Add(new FormRow(null, ActionButton("Restaurar atajos globales", () =>
            {
                model.RestoreGlobalShortcuts();
                Changed?.Invoke();
                ShortcutsChanged?.Invoke();
                Rebuild();
            }), RowLayout.Leading));

            return Page(
                new PageHeader("Atajos", "Controla los timers sin salir del juego"),
                new SectionHeader("Iniciar / pausar cada timer"),
                new SectionCard(timerRows),
                new SectionHeader("Globales"),
                new SectionCard(globalRows.ToArray()),
                new NoteLabel("Funcionan con Tibia al frente. Haz clic en un atajo y pulsa la nueva combinación (Esc cancela); usa al menos Ctrl, Alt o Mayús, o una tecla F sola. Una combinación solo puede estar en un sitio."));
        }

        // ---------- Página: Sonido ----------

        Control BuildSoundPage()
        {
            var muted = new ToggleSwitch { Checked = model.Muted };
            var volume = new FlatSlider { Maximum = 100, Value = (int)Math.Round(model.Volume * 100), Width = 190, Enabled = !model.Muted };
            var percent = new Caption($"{volume.Value} %") { Width = 44 };
            muted.CheckedChanged += (s, e) => { model.Muted = muted.Checked; volume.Enabled = !muted.Checked; Changed?.Invoke(); };
            volume.ValueChanged += (s, e) => { model.Volume = volume.Value / 100.0; percent.Text = $"{volume.Value} %"; Changed?.Invoke(); };

            return Page(
                new PageHeader("Sonido", "Volumen de los beeps y sonidos de aviso"),
                new SectionHeader("General"),
                new SectionCard(
                    Row("Silenciar", muted),
                    Row("Volumen", new InlineGroup(volume, percent) { Gap = 8 }),
                    new FormRow(null, ActionButton("Probar beep", () => Beeper.Beep(1200, 0.15, model)), RowLayout.Leading)),
                new NoteLabel("El sonido de cada timer y de cada habilidad de Tibia Audio se elige al editarlos: hay 14 sonidos incluidos o puedes usar un archivo .wav/.mp3 propio."));
        }

        // ---------- Refresco ----------

        /// Refresca lo que cambia con el tiempo (se llama en cada tick).
        public void RefreshTimer()
        {
            topBar.UpdateState();
            sidebar.Badge = model.RunningCount;
            foreach (var row in rows) row.UpdateState();
            hero?.UpdateState();
            if (countLabel != null)
            {
                string text = $"{model.Runs.Count} {(model.Runs.Count == 1 ? "timer" : "timers")} · {model.RunningCount} en marcha";
                if (countLabel.Text != text) countLabel.Text = text;
            }
            foreach (var row in audioRows) row.UpdateState();
            if (audioStatus != null && audioStatus.Text != AudioStatusText) audioStatus.Text = AudioStatusText;
        }

        public void OnOverlayVisibilityToggled() => RefreshTimer();

        // ---------- Ventana ----------

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY) HotKeyPressed?.Invoke(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Cerrar el hub solo lo oculta: los timers y el overlay siguen (salir desde el ícono de la bandeja).
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }

        // ---------- Helpers ----------

        static FormRow Row(string text, Control accessory) => new FormRow(text, accessory);

        static RoundButton ActionButton(string text, Action onClick)
        {
            var b = new RoundButton { Text = text, Height = 26 };
            b.FitText(12);
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }
    }

    // ---------- Barra superior y barra lateral ----------

    /// Barra superior: estado de Tibia y botón del overlay.
    sealed class TopBar : PaintedControl
    {
        readonly Model model;
        readonly RoundButton overlayButton = new RoundButton { Pill = true, Height = 28, BackColor = Theme.Sidebar };
        string lastState;

        public event Action OverlayToggled;

        public TopBar(Model model)
        {
            this.model = model;
            BackColor = Theme.Sidebar;
            overlayButton.Click += (s, e) => OverlayToggled?.Invoke();
            Controls.Add(overlayButton);
        }

        public void UpdateState()
        {
            string text = model.OverlayVisible ? "👁  Overlay visible" : "⊘  Overlay oculto";
            if (overlayButton.Text != text)
            {
                overlayButton.Text = text;
                overlayButton.ForeColor = model.OverlayVisible ? Color.White : Theme.Muted;
                overlayButton.FitText(12);
                PerformLayout();
            }
            string state = model.TibiaRunning.ToString();
            if (state != lastState) { lastState = state; Invalidate(); }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = (int)(16 * K);
            overlayButton.Location = new Point(Width - pad - overlayButton.Width, (Height - overlayButton.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            // Estado de Tibia, a la izquierda del botón del overlay.
            string status = model.TibiaRunning ? "Tibia abierto" : "Tibia cerrado";
            var size = TextRenderer.MeasureText(g, status, Font, Size.Empty, TextFormatFlags.NoPadding);
            float right = overlayButton.Left - 16 * k;
            int tx = (int)(right - size.Width);
            TextRenderer.DrawText(g, status, Font, new Point(tx, (Height - size.Height) / 2), Theme.Muted, TextFormatFlags.NoPadding);
            using (var dot = new SolidBrush(model.TibiaRunning ? Theme.Green : Theme.Muted))
                g.FillEllipse(dot, tx - 13 * k, Height / 2f - 3.5f * k, 7 * k, 7 * k);
        }
    }

    /// Barra lateral con las secciones y el personaje al pie.
    sealed class Sidebar : PaintedControl
    {
        readonly Dictionary<HubPage, NavItem> items = new Dictionary<HubPage, NavItem>();
        readonly List<(Control control, int top)> layout = new List<(Control, int)>();

        public event Action<HubPage> Navigate;
        /// Personaje que se dibuja al pie (el elegido en la página Outfit).
        public Func<Image> Character = () => Resources.Character;

        public Sidebar()
        {
            BackColor = Theme.Sidebar;
            int y = 12;
            y = AddLabel("PRINCIPAL", y);
            y = AddItem(HubPage.Timers, "Timers", "", y);
            y = AddItem(HubPage.Audio, "Tibia Audio", "\uE8D6", y);
            y = AddItem(HubPage.Mirrors, "Espejos", "\uE7C4", y);
            y = AddLabel("AJUSTES", y + 14);
            y = AddItem(HubPage.Outfit, "Outfit", "\uE77B", y);
            y = AddItem(HubPage.Shortcuts, "Atajos", "", y);
            AddItem(HubPage.Sound, "Sonido", "", y);
        }

        int AddLabel(string text, int y)
        {
            var label = new Caption(text) { Font = new Font("Segoe UI Semibold", 7.5f), ForeColor = Theme.Muted, BackColor = Theme.Sidebar, Height = 22 };
            Controls.Add(label);
            layout.Add((label, y));
            return y + 24;
        }

        int AddItem(HubPage page, string text, string glyph, int y)
        {
            var item = new NavItem(text, glyph) { BackColor = Theme.Sidebar, Height = 36 };
            item.Click += (s, e) => Navigate?.Invoke(page);
            items[page] = item;
            Controls.Add(item);
            layout.Add((item, y));
            return y + 40;
        }

        public void Select(HubPage page)
        {
            foreach (var pair in items) pair.Value.Selected = pair.Key == page;
        }

        public int Badge { set => items[HubPage.Timers].Badge = value; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float k = K;
            int pad = (int)(10 * k);
            foreach (var (control, top) in layout)
            {
                if (control is Caption) control.SetBounds(pad + (int)(10 * k), (int)(top * k), control.Width, control.Height);
                else control.SetBounds(pad, (int)(top * k), Width - pad * 2, control.Height);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            // Personaje con su frase al pie de la barra.
            Image sprite = Character();
            // Como el original (×2); los sprites grandes (con montura) se reducen para caber.
            float scale = Math.Min(2f, 170f / Math.Max(sprite?.Width ?? 37, sprite?.Height ?? 35));
            float w = (sprite?.Width ?? 37) * scale * k, h = (sprite?.Height ?? 35) * scale * k;
            var spriteRect = new RectangleF((Width - w) / 2, Height - 14 * k - h, w, h);
            Resources.DrawPixelArt(g, sprite, spriteRect);

            const string phrase = "Slayer macaco e mielda";
            using (var font = new Font("Segoe UI", 12f * k, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF text = g.MeasureString(phrase, font);
                float padX = 11 * k, padY = 6 * k, tail = 8 * k;
                float bw = text.Width + padX * 2, bh = text.Height + padY * 2;
                var body = new RectangleF((Width - bw) / 2, spriteRect.Y - 4 * k - tail - bh, bw, bh);
                using (var bubble = CounterRenderer.RoundedRect(body, Math.Min(12 * k, bh / 2)))
                {
                    float mid = body.X + body.Width / 2;
                    bubble.AddPolygon(new[]
                    {
                        new PointF(mid - 7 * k, body.Bottom - 1),
                        new PointF(mid + 2 * k, body.Bottom + tail),
                        new PointF(mid + 6 * k, body.Bottom - 1),
                    });
                    using (var shadowPath = (GraphicsPath)bubble.Clone())
                    using (var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                    {
                        shadowPath.Transform(new Matrix(1, 0, 0, 1, 0, 1.5f * k));
                        g.FillPath(shadowBrush, shadowPath);
                    }
                    using (var fill = new SolidBrush(Color.FromArgb(245, 245, 247))) g.FillPath(fill, bubble);
                }
                using (var ink = new SolidBrush(Color.FromArgb(26, 26, 26)))
                    g.DrawString(phrase, font, ink, body.X + padX, body.Y + padY);
            }
        }
    }

    /// Entrada de la barra lateral: ícono, texto, marca azul si está elegida y contador opcional.
    sealed class NavItem : RoundButton
    {
        readonly string glyph;
        bool selected;
        int badge;

        public NavItem(string text, string glyph)
        {
            Text = text;
            this.glyph = glyph;
            FillColor = Theme.Sidebar;
        }

        public bool Selected
        {
            get => selected;
            set { selected = value; FillColor = value ? Color.FromArgb(38, 38, 42) : Theme.Sidebar; }
        }

        public int Badge { set { if (badge != value) { badge = value; Invalidate(); } } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, r, 8 * k, FillColor);
            if (selected) Fill(g, new RectangleF(0, Height / 2f - 9 * k, 3 * k, 18 * k), 1.5f * k, Theme.Blue);
            Color fore = selected ? Color.White : Color.FromArgb(200, 200, 205);
            using (var icons = new Font("Segoe MDL2 Assets", 11f))
                TextRenderer.DrawText(g, glyph, icons, new Rectangle((int)(12 * k), 0, (int)(20 * k), Height), fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            using (var font = new Font("Segoe UI", 9.75f, selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Text, font, new Rectangle((int)(42 * k), 0, Width, Height), fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (badge > 0)
            {
                string b = badge.ToString();
                using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                {
                    var size = TextRenderer.MeasureText(b, font);
                    var pill = new RectangleF(Width - 12 * k - size.Width - 8 * k, Height / 2f - 9 * k, size.Width + 8 * k, 18 * k);
                    Fill(g, pill, 9 * k, Color.FromArgb(60, Theme.Green));
                    TextRenderer.DrawText(g, b, font, Rectangle.Round(pill), Theme.Green,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                }
            }
        }
    }

    // ---------- Piezas de las páginas ----------

    /// Título y subtítulo de una página, con un botón opcional a la derecha.
    sealed class PageHeader : PaintedControl, IStackItem
    {
        readonly string subtitle;
        readonly Control action;

        public PageHeader(string title, string subtitle, Control action = null)
        {
            Text = title;
            this.subtitle = subtitle;
            this.action = action;
            BackColor = Theme.Back;
            Margin = new Padding(0, 0, 0, 10);
            if (action != null) Controls.Add(action);
        }

        public int HeightFor(int width) => (int)(56 * K);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (action != null) action.Location = new Point(Width - action.Width, (Height - action.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            using (var title = new Font("Segoe UI", 17f, FontStyle.Bold))
                TextRenderer.DrawText(g, Text, title, new Point(0, 0), Color.White, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, subtitle, Font, new Point(0, (int)(34 * K)), Theme.Muted, TextFormatFlags.NoPadding);
        }
    }

    /// Línea de herramientas: texto a la izquierda y botones a la derecha.
    sealed class ToolbarRow : PaintedControl, IStackItem
    {
        readonly Label label;
        readonly Control[] buttons;

        public ToolbarRow(Label label, params Control[] buttons)
        {
            this.label = label;
            this.buttons = buttons;
            BackColor = Theme.Back;
            Controls.Add(label);
            foreach (var b in buttons) Controls.Add(b);
        }

        public int HeightFor(int width) => (int)(30 * K);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            label.Location = new Point(0, (Height - label.Height) / 2);
            int x = Width;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                x -= buttons[i].Width;
                buttons[i].Location = new Point(x, (Height - buttons[i].Height) / 2);
                x -= (int)(8 * K);
            }
        }

        protected override void OnPaint(PaintEventArgs e) => Begin(e);
    }

    /// Fila de la lista de timers: barra de estado, nombre, resumen, contador y acciones.
    sealed class TimerRow : PaintedControl, IStackItem
    {
        readonly Model model;
        readonly TimerRun run;
        readonly RoundButton toggle, reset, edit, more;
        string lastState;

        public event Action ToggleClicked, ResetClicked, EditClicked, DuplicateClicked, DeleteClicked, Selected;

        public TimerRow(Model model, TimerRun run)
        {
            this.model = model;
            this.run = run;
            BackColor = Theme.Back;
            toggle = new RoundButton { Pill = true, Size = new Size(30, 30), BackColor = Theme.Row };
            reset = new RoundButton { Caption = "↺", Text = "Reiniciar " + run.Config.Name, Pill = true, Size = new Size(30, 30), FillColor = Theme.ControlFill, BackColor = Theme.Row };
            edit = new RoundButton { Caption = "✎  Editar", Text = "Editar " + run.Config.Name, Height = 28, FillColor = Theme.ControlFill, BackColor = Theme.Row };
            edit.FitText(11);
            more = new RoundButton { Caption = "⋮", Text = "Más opciones " + run.Config.Name, Pill = true, Size = new Size(30, 30), FillColor = Theme.Row, BackColor = Theme.Row };
            toggle.Click += (s, e) => ToggleClicked?.Invoke();
            reset.Click += (s, e) => ResetClicked?.Invoke();
            edit.Click += (s, e) => EditClicked?.Invoke();
            more.Click += (s, e) =>
            {
                var menu = DarkMenu.Create();
                menu.Items.Add("Duplicar", null, (a, b) => DuplicateClicked?.Invoke());
                if (model.Runs.Count > 1) menu.Items.Add("Eliminar", null, (a, b) => DeleteClicked?.Invoke());
                menu.Show(more, new Point(0, more.Height + 2));
            };
            Controls.AddRange(new Control[] { toggle, reset, edit, more });
            Cursor = Cursors.Hand;
            UpdateState();
        }

        public int HeightFor(int width) => (int)(64 * K);

        protected override void OnClick(EventArgs e) { base.OnClick(e); Selected?.Invoke(); }
        protected override void OnDoubleClick(EventArgs e) { base.OnDoubleClick(e); EditClicked?.Invoke(); }

        public void UpdateState()
        {
            toggle.Caption = run.Running ? "❚❚" : "▶";
            toggle.Text = (run.Running ? "Pausar " : "Iniciar ") + run.Config.Name;
            toggle.FillColor = run.Running ? Theme.Amber : Theme.Blue;
            string state = run.Display.Signature + run.Running + run.Started + (model.SelectedId == run.Id) + run.Config.Name + run.Summary;
            if (state != lastState || (run.Display.Critical && run.Running))
            {
                lastState = state;
                Invalidate();
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float k = K;
            int x = Width - (int)(10 * k);
            foreach (var c in new Control[] { more, edit, reset, toggle })
            {
                x -= c.Width;
                c.Location = new Point(x, (Height - c.Height) / 2);
                x -= (int)((c == edit ? 12 : 6) * k);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            var d = run.Display;
            var card = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, card, 10 * k, Theme.Row);
            Stroke(g, card, 10 * k, model.SelectedId == run.Id ? Color.FromArgb(130, Theme.Blue) : Color.FromArgb(48, 48, 52));

            Color state = d.Finished ? run.Config.Overlay.Alert : !run.Started ? Color.FromArgb(70, 70, 75)
                : !run.Running ? Theme.Amber : d.Critical ? run.Config.Overlay.Alert : Theme.Green;
            Fill(g, new RectangleF(10 * k, 12 * k, 4 * k, Height - 24 * k), 2 * k, state);

            using (var name = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, run.Config.Name, name, new Rectangle((int)(26 * k), (int)(12 * k), (int)(260 * k), (int)(22 * k)),
                    Color.White, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            using (var small = new Font("Segoe UI", 8.25f))
                TextRenderer.DrawText(g, run.Summary, small, new Rectangle((int)(26 * k), (int)(36 * k), (int)(300 * k), (int)(18 * k)),
                    Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            // Contador y barra a la izquierda de los botones.
            float right = toggle.Left - 16 * k;
            Color accent = d.Critical || d.Finished ? run.Config.Overlay.Alert : Theme.Accent;
            int alpha = (int)(255 * CounterRenderer.Pulse(d.Critical && run.Running));
            using (var font = new Font("Consolas", 22f * k, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.FromArgb(alpha, accent)))
            using (var format = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString(d.Text, font, brush, new RectangleF(right - 160 * k, 10 * k, 160 * k, 30 * k), format);
            var track = new RectangleF(right - 90 * k, 44 * k, 90 * k, 4 * k);
            Fill(g, track, 2 * k, Color.FromArgb(40, 255, 255, 255));
            if (run.Started) Fill(g, new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * (float)d.Progress), track.Height), 2 * k, accent);
        }
    }

    /// Fila de la lista de espejos: nombre, región, visible, opacidad y acciones.
    sealed class MirrorRow : PaintedControl, IStackItem
    {
        readonly MirrorConfig mirror;
        readonly ToggleSwitch visible;
        readonly FlatSlider opacity;
        readonly Caption opacityLabel;
        readonly RoundButton edit, more;

        public event Action Changed, EditClicked, ReselectClicked, DeleteClicked;

        public MirrorRow(MirrorConfig mirror)
        {
            this.mirror = mirror;
            BackColor = Theme.Back;
            Cursor = Cursors.Hand;
            visible = new ToggleSwitch { Checked = mirror.Visible, Text = "Visible " + mirror.Name, BackColor = Theme.Row };
            opacity = new FlatSlider { Minimum = 10, Maximum = 100, Value = mirror.Opacity, Width = 110, BackColor = Theme.Row };
            opacityLabel = new Caption($"{mirror.Opacity} %") { Width = 44, BackColor = Theme.Row };
            edit = new RoundButton { Caption = "✎  Editar", Text = "Editar " + mirror.Name, Height = 28, FillColor = Theme.ControlFill, BackColor = Theme.Row };
            edit.FitText(11);
            more = new RoundButton { Caption = "⋮", Text = "Más opciones " + mirror.Name, Pill = true, Size = new Size(30, 30), FillColor = Theme.Row, BackColor = Theme.Row };
            visible.CheckedChanged += (s, e) => { mirror.Visible = visible.Checked; Invalidate(); Changed?.Invoke(); };
            opacity.ValueChanged += (s, e) => { mirror.Opacity = opacity.Value; opacityLabel.Text = $"{opacity.Value} %"; Changed?.Invoke(); };
            edit.Click += (s, e) => EditClicked?.Invoke();
            more.Click += (s, e) =>
            {
                var menu = DarkMenu.Create();
                menu.Items.Add("Volver a elegir la región", null, (a, b) => ReselectClicked?.Invoke());
                menu.Items.Add("Eliminar", null, (a, b) => DeleteClicked?.Invoke());
                menu.Show(more, new Point(0, more.Height + 2));
            };
            Controls.AddRange(new Control[] { visible, opacity, opacityLabel, edit, more });
        }

        public int HeightFor(int width) => (int)(64 * K);

        protected override void OnDoubleClick(EventArgs e) { base.OnDoubleClick(e); EditClicked?.Invoke(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float k = K;
            int x = Width - (int)(10 * k);
            foreach (var c in new Control[] { more, edit, opacityLabel, opacity, visible })
            {
                x -= c.Width;
                c.Location = new Point(x, (Height - c.Height) / 2);
                x -= (int)((c == edit ? 14 : c == opacity ? 18 : 6) * k);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            var card = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, card, 10 * k, Theme.Row);
            Stroke(g, card, 10 * k, Color.FromArgb(48, 48, 52));
            Fill(g, new RectangleF(10 * k, 12 * k, 4 * k, Height - 24 * k), 2 * k, mirror.Visible ? Theme.Green : Color.FromArgb(70, 70, 75));
            using (var name = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, mirror.Name, name, new Rectangle((int)(26 * k), (int)(12 * k), (int)(220 * k), (int)(22 * k)),
                    Color.White, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            string summary = $"{mirror.Width} × {mirror.Height} px · {mirror.Zoom} % · {(mirror.Visible ? "Visible" : "Oculto")}";
            using (var small = new Font("Segoe UI", 8.25f))
                TextRenderer.DrawText(g, summary, small, new Rectangle((int)(26 * k), (int)(36 * k), (int)(240 * k), (int)(18 * k)),
                    Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// Tarjeta del editor: nombre, estado, controles y contador grande.
    sealed class HeroCard : PaintedControl, IStackItem
    {
        readonly Model model;
        readonly TimerRun run;
        readonly RoundButton toggle = new RoundButton(), reset = new RoundButton { Text = "↺  Reiniciar" };
        string lastState;

        public event Action ToggleClicked, ResetClicked;

        public HeroCard(Model model, TimerRun run)
        {
            this.model = model;
            this.run = run;
            BackColor = Theme.Back;
            foreach (var b in new[] { toggle, reset })
            {
                b.Font = new Font("Segoe UI Semibold", 10f);
                b.Radius = 9;
                b.BackColor = Theme.HeroCard;
                b.Height = 34;
                Controls.Add(b);
            }
            toggle.Width = 120;
            reset.FitText(14);
            reset.FillColor = Color.FromArgb(52, 52, 56);
            reset.BorderColor = Color.FromArgb(66, 66, 70);
            toggle.Click += (s, e) => ToggleClicked?.Invoke();
            reset.Click += (s, e) => ResetClicked?.Invoke();
            UpdateState();
        }

        public int HeightFor(int width) => (int)(136 * K);

        public void UpdateState()
        {
            toggle.Text = run.Running ? "❚❚  Pausar" : "▶  Iniciar";
            toggle.FillColor = run.Running ? Theme.Amber : Theme.Blue;
            string state = run.Display.Signature + run.Running + run.Config.Name;
            if (state != lastState || (run.Display.Critical && run.Running))
            {
                lastState = state;
                Invalidate();
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float k = K;
            int y = Height - (int)(18 * k) - toggle.Height;
            toggle.Location = new Point((int)(18 * k), y);
            reset.Location = new Point(toggle.Right + (int)(10 * k), y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            var d = run.Display;
            var card = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, card, 14 * k, Theme.HeroCard);
            Stroke(g, card, 14 * k, Color.FromArgb(54, 54, 58));

            using (var name = new Font("Segoe UI", 15f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, run.Config.Name, name, new Point((int)(18 * k), (int)(16 * k)), Color.White, TextFormatFlags.NoPadding);
                int nameWidth = TextRenderer.MeasureText(g, run.Config.Name, name, Size.Empty, TextFormatFlags.NoPadding).Width;
                DrawChip(g, 18 * k + nameWidth + 10 * k, 20 * k, k, d);
            }
            using (var mono = new Font("Consolas", 9f))
                TextRenderer.DrawText(g, d.Detail, mono, new Point((int)(18 * k), (int)(50 * k)), Theme.Muted, TextFormatFlags.NoPadding);

            Color accent = d.Critical ? run.Config.Overlay.Alert : Theme.Accent;
            int alpha = (int)(255 * CounterRenderer.Pulse(d.Critical && run.Running));
            using (var font = new Font("Consolas", 50f * k, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.FromArgb(alpha, accent)))
            using (var format = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString(d.Text, font, brush, new RectangleF(Width - 330 * k, 18 * k, 310 * k, 70 * k), format);
            var track = new RectangleF(Width - 220 * k, 96 * k, 200 * k, 5 * k);
            Fill(g, track, 2.5f * k, Color.FromArgb(40, 255, 255, 255));
            Fill(g, new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * (float)d.Progress), track.Height), 2.5f * k, accent);
        }

        void DrawChip(Graphics g, float left, float top, float k, TimerDisplay d)
        {
            string label;
            Color color;
            if (d.Finished) { label = "Terminado"; color = run.Config.Overlay.Alert; }
            else if (!run.Running) { label = "En pausa"; color = Theme.Muted; }
            else if (d.Critical) { label = "¡Alerta!"; color = run.Config.Overlay.Alert; }
            else { label = "En marcha"; color = Theme.Green; }
            using (var font = new Font("Segoe UI Semibold", 11f * k, GraphicsUnit.Pixel))
            {
                SizeF text = g.MeasureString(label, font);
                float w = text.Width + 22 * k, h = 20 * k;
                var rect = new RectangleF(left, top, w, h);
                Fill(g, rect, h / 2, Color.FromArgb(40, color));
                using (var dot = new SolidBrush(color))
                {
                    g.FillEllipse(dot, rect.X + 8 * k, rect.Y + h / 2 - 3.5f * k, 7 * k, 7 * k);
                    g.DrawString(label, font, dot, rect.X + 17 * k, rect.Y + (h - text.Height) / 2);
                }
            }
        }
    }

    /// Vista previa del overlay sobre un fondo que imita el mapa de Tibia.
    sealed class PreviewPanel : Control
    {
        readonly TimerRun run;
        bool showAlert;

        public PreviewPanel(TimerRun run)
        {
            this.run = run;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
        }

        public bool ShowAlert { get => showAlert; set { showAlert = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            float k = DeviceDpi / 96f;
            var rect = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var shape = CounterRenderer.RoundedRect(rect, 8 * k))
            using (var fill = new LinearGradientBrush(rect, Color.FromArgb(74, 92, 51), Color.FromArgb(51, 41, 31), 35f))
                g.FillPath(fill, shape);

            var style = run.Config.Overlay;
            string text = showAlert ? "00:05" : run.Display.Text;
            using (var counter = CounterRenderer.Draw(style, (float)style.Scale * k, text, showAlert, true, false))
            {
                float w = counter.Width, h = counter.Height;
                float fit = Math.Min(1f, Math.Min((Width - 16 * k) / w, (Height - 12 * k) / h));
                w *= fit; h *= fit;
                var dest = new Rectangle((int)((Width - w) / 2), (int)((Height - h) / 2), (int)w, (int)h);
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)style.Opacity });
                    g.DrawImage(counter, dest, 0, 0, counter.Width, counter.Height, GraphicsUnit.Pixel, attributes);
                }
            }
        }
    }

    /// Menús contextuales oscuros.
    static class DarkMenu
    {
        public static ContextMenuStrip Create()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new Colors()),
                BackColor = Theme.Row,
                ForeColor = Color.White,
                ShowImageMargin = false,
                Font = new Font("Segoe UI", 9.75f),
            };
            return menu;
        }

        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Row;
            public override Color MenuItemSelected => Color.FromArgb(60, 60, 66);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 66);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 66);
            public override Color MenuItemBorder => Color.FromArgb(60, 60, 66);
            public override Color MenuBorder => Color.FromArgb(70, 70, 76);
            public override Color ImageMarginGradientBegin => Theme.Row;
            public override Color ImageMarginGradientMiddle => Theme.Row;
            public override Color ImageMarginGradientEnd => Theme.Row;
            public override Color SeparatorDark => Color.FromArgb(70, 70, 76);
        }
    }

    /// Botón que graba una combinación de teclas al pulsarlo. Esc cancela.
    sealed class ShortcutButton : RoundButton
    {
        Keys value;
        bool recording;
        /// Vale cualquier tecla sola (las de Tibia Audio no se registran como atajo).
        public bool AllowPlain;
        public string EmptyText = "Sin atajo";

        public event Action<Keys> Recorded;
        public event Action<bool> RecordingChanged;

        public ShortcutButton()
        {
            FillColor = Theme.ControlFill;
            Font = new Font("Segoe UI Semibold", 9.5f);
        }

        public void Display(Keys keys) { value = keys; UpdateLabel(); }
        public void Assign(Keys keys) { value = keys; UpdateLabel(); Recorded?.Invoke(keys); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            SetRecording(!recording);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            if (recording) SetRecording(false);
        }

        void SetRecording(bool on)
        {
            recording = on;
            RecordingChanged?.Invoke(on);
            UpdateLabel();
        }

        void UpdateLabel()
        {
            Text = recording ? (AllowPlain ? "Pulsa la tecla…" : "Pulsa la combinación…") : value == Keys.None ? EmptyText : Model.Describe(value);
            ForeColor = recording ? Theme.Amber : value == Keys.None ? Theme.Muted : Color.White;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!recording) return base.ProcessCmdKey(ref msg, keyData);
            Keys key = keyData & Keys.KeyCode, mods = keyData & Keys.Modifiers;
            if (key == Keys.Escape && mods == Keys.None) { SetRecording(false); return true; }
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                return true;  // esperando la tecla principal
            bool functionKey = key >= Keys.F1 && key <= Keys.F24;
            if (mods == Keys.None && !functionKey && !AllowPlain) { SystemSounds.Beep.Play(); return true; }
            recording = false;
            RecordingChanged?.Invoke(false);
            Assign(keyData);
            return true;
        }
    }
}

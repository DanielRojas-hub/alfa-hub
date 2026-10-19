using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Colores tomados de la versión de macOS (docs/DISENO-REFERENCIA.md).
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(50, 48, 48);         // fondo de la ventana
        public static readonly Color HeroCard = Color.FromArgb(65, 62, 62);     // tarjeta del timer
        public static readonly Color Row = Color.FromArgb(59, 58, 57);          // filas de las secciones
        public static readonly Color Segment = Color.FromArgb(63, 61, 61);      // fondo de las pestañas
        public static readonly Color ControlFill = Color.FromArgb(82, 80, 79);  // botones grises
        public static readonly Color Field = Color.FromArgb(34, 33, 33);        // campos numéricos
        public static readonly Color Fore = Color.FromArgb(240, 240, 242);
        public static readonly Color Muted = Color.FromArgb(160, 158, 158);
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
    }

    /// Ventana principal: personaje, tarjeta del timer y los ajustes en tres pestañas.
    sealed class HubForm : Form
    {
        readonly Model model;
        bool syncing;

        readonly HeroPanel hero;
        readonly StackPanel[] pages = new StackPanel[3];
        readonly PreviewPanel preview;

        // Ciclo
        readonly NumberField minutes = new NumberField { Maximum = 60 }, seconds = new NumberField { Maximum = 59 };
        readonly StepperArrows cycleStepper = new StepperArrows(), warningStepper = new StepperArrows(), beepsStepper = new StepperArrows();
        FormRow warningRow, beepsRow;
        readonly ToggleSwitch cycleBeep = new ToggleSwitch();
        readonly List<PillButton> presetButtons = new List<PillButton>();
        static readonly int[] Presets = { 60, 90, 120, 150, 180 };
        // Sonido
        readonly ToggleSwitch muted = new ToggleSwitch();
        readonly FlatSlider volume = new FlatSlider { Maximum = 100, Width = 150 };
        // Overlay
        readonly ToggleSwitch overlayVisible = new ToggleSwitch(), unlocked = new ToggleSwitch(), previewAlert = new ToggleSwitch();
        readonly FlatSlider scale = new FlatSlider { Minimum = 60, Maximum = 300 }, opacity = new FlatSlider { Minimum = 20, Maximum = 100 };
        readonly FlatSlider backgroundAlpha = new FlatSlider { Maximum = 100 };
        // Colores
        readonly ColorSwatch counterColor = new ColorSwatch(), alertColor = new ColorSwatch(), backgroundColor = new ColorSwatch();
        // Atajos
        readonly Dictionary<HotKeyAction, ShortcutButton> shortcutButtons = new Dictionary<HotKeyAction, ShortcutButton>();

        public event Action Changed;               // cualquier ajuste cambió (guardar y redibujar)
        public event Action ShortcutsChanged;      // volver a registrar los atajos globales
        public event Action<bool> RecordingShortcut;
        public event Action ResetOverlayPosition;
        public event Action<int> HotKeyPressed;

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
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 800);
            MinimumSize = new Size(456, 560);
            Padding = new Padding(16, 10, 16, 0);

            var mascot = new MascotPanel { Dock = DockStyle.Top, Height = 78 };
            hero = new HeroPanel(model) { Dock = DockStyle.Top, Height = 184 };
            hero.ToggleClicked += () => { model.Toggle(); RefreshTimer(); };
            hero.ResetClicked += () => { model.Reset(); RefreshTimer(); };

            var tabs = new SegmentedControl("Ciclo y sonido", "Overlay", "Atajos") { Dock = DockStyle.Top, Height = 32 };
            tabs.SelectedIndexChanged += SelectTab;

            preview = new PreviewPanel(model);
            pages[0] = BuildTimerPage();
            pages[1] = BuildOverlayPage();
            pages[2] = BuildShortcutsPage();

            // Orden de acoplado: lo último añadido con Dock.Top queda arriba.
            foreach (var page in pages) Controls.Add(page);
            Controls.Add(Spacer(4));
            Controls.Add(tabs);
            Controls.Add(Spacer(12));
            Controls.Add(hero);
            Controls.Add(mascot);

            SyncFromModel();
            SelectTab(0);
            RefreshTimer();
            ResumeLayout(true);
        }

        static Panel Spacer(int height) => new Panel { Dock = DockStyle.Top, Height = height };

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkTitleBar(Handle);
        }

        void SelectTab(int index)
        {
            for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == index;
        }

        // ---------- Páginas ----------

        static StackPanel Page(params Control[] items)
        {
            var page = new StackPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 2, 16) };
            foreach (var item in items) page.Controls.Add(item);
            return page;
        }

        static FormRow Row(string text, Control accessory) => new FormRow(text, accessory);

        static FormRow ButtonRow(RoundButton button) => new FormRow(null, button, RowLayout.Leading);

        static RoundButton ActionButton(string text, Action onClick)
        {
            var b = new RoundButton { Text = text, Height = 26 };
            b.FitText(12);
            b.Click += (s, e) => onClick();
            return b;
        }

        StackPanel BuildTimerPage()
        {
            var chips = new InlineGroup { Height = 30 };
            foreach (int preset in Presets)
            {
                int value = preset;
                var pill = new PillButton { Text = Model.Mmss(value), Width = 62 };
                pill.Click += (s, e) => { model.SetCycle(value); SyncFromModel(); Changed?.Invoke(); };
                presetButtons.Add(pill);
                chips.Controls.Add(pill);
            }
            var custom = new InlineGroup(minutes, new Caption("min"), seconds, new Caption("s"), cycleStepper) { Height = 28 };
            warningRow = Row("", warningStepper);
            beepsRow = Row("", beepsStepper);
            var volumeRow = new InlineGroup(volume, ActionButton("Probar", () => Beeper.Beep(1200, 0.15, model))) { Gap = 10 };

            minutes.ValueCommitted += (s, e) => SetCycle(minutes.Value * 60 + model.Cycle % 60);
            seconds.ValueCommitted += (s, e) => SetCycle(model.Cycle / 60 * 60 + seconds.Value);
            cycleStepper.Up += () => SetCycle(model.Cycle + 5);
            cycleStepper.Down += () => SetCycle(model.Cycle - 5);
            warningStepper.Up += () => Apply(() => model.Warning = Math.Min(model.Cycle, model.Warning + 1));
            warningStepper.Down += () => Apply(() => model.Warning = Math.Max(0, model.Warning - 1));
            beepsStepper.Up += () => Apply(() => model.BeepSeconds = Math.Min(30, model.BeepSeconds + 1));
            beepsStepper.Down += () => Apply(() => model.BeepSeconds = Math.Max(0, model.BeepSeconds - 1));
            cycleBeep.CheckedChanged += (s, e) => Set(() => model.CycleBeep = cycleBeep.Checked);
            muted.CheckedChanged += (s, e) => Set(() => { model.Muted = muted.Checked; volume.Enabled = !muted.Checked; });
            volume.ValueChanged += (s, e) => Set(() => model.Volume = volume.Value / 100.0);

            return Page(
                new SectionHeader("Tiempo entre ciclos"),
                new SectionCard(
                    new FormRow(null, chips, RowLayout.Centered, 46),
                    Row("Personalizado", custom)),
                new SectionHeader("Avisos"),
                new SectionCard(
                    warningRow,
                    beepsRow,
                    Row("Tono al empezar cada ciclo", cycleBeep),
                    ButtonRow(ActionButton("Restaurar valores del Pez (2:00 · 25 s · 5 s)", () => Apply(model.RestoreTimerDefaults)))),
                new SectionHeader("Sonido"),
                new SectionCard(
                    Row("Silenciar", muted),
                    Row("Volumen", volumeRow)));
        }

        StackPanel BuildOverlayPage()
        {
            previewAlert.CheckedChanged += (s, e) => preview.ShowAlert = previewAlert.Checked;
            overlayVisible.CheckedChanged += (s, e) => Set(() => model.OverlayVisible = overlayVisible.Checked);
            unlocked.CheckedChanged += (s, e) => Set(() =>
            {
                model.OverlayUnlocked = unlocked.Checked;
                if (unlocked.Checked) { model.OverlayVisible = true; overlayVisible.Checked = true; }
            });
            scale.ValueChanged += (s, e) => Set(() => model.OverlayScale = scale.Value / 100.0);
            opacity.ValueChanged += (s, e) => Set(() => model.OverlayOpacity = opacity.Value / 100.0);
            counterColor.Click += (s, e) => PickColor(model.CounterColor, c => model.CounterColor = c);
            alertColor.Click += (s, e) => PickColor(model.AlertColor, c => model.AlertColor = c);
            backgroundColor.Click += (s, e) =>
                PickColor(model.BackgroundColor, c => model.BackgroundColor = Color.FromArgb(model.BackgroundColor.A, c));
            backgroundAlpha.ValueChanged += (s, e) => Set(() =>
                model.BackgroundColor = Color.FromArgb((int)Math.Round(backgroundAlpha.Value * 2.55), model.BackgroundColor));

            return Page(
                new SectionHeader("Vista previa"),
                new SectionCard(
                    new FormRow(null, preview, RowLayout.Stretch, 112),
                    Row("Ver en alerta", previewAlert)),
                new SectionHeader("En Tibia"),
                new SectionCard(
                    Row("Mostrar overlay", overlayVisible),
                    Row("Tamaño", scale),
                    Row("Opacidad", opacity),
                    Row("Desbloquear para moverlo", unlocked),
                    ButtonRow(ActionButton("Volver a la esquina superior derecha", () => ResetOverlayPosition?.Invoke()))),
                new NoteLabel("Solo aparece con Tibia al frente y deja pasar los clics al juego. Mientras está desbloqueado se ve siempre, para que puedas arrastrarlo a su sitio; luego vuelve a bloquearlo."),
                new SectionHeader("Colores"),
                new SectionCard(
                    Row("Contador", counterColor),
                    Row("Contador en alerta", alertColor),
                    Row("Fondo", backgroundColor),
                    Row("Opacidad del fondo", backgroundAlpha),
                    ButtonRow(ActionButton("Restaurar colores", () => Apply(model.RestoreColors)))),
                new NoteLabel("Pon la opacidad del fondo en cero para ver solo el número."));
        }

        StackPanel BuildShortcutsPage()
        {
            var titles = new Dictionary<HotKeyAction, string>
            {
                [HotKeyAction.Toggle] = "Iniciar / pausar",
                [HotKeyAction.Reset] = "Reiniciar",
                [HotKeyAction.Overlay] = "Mostrar / ocultar overlay",
            };
            var rows = new List<FormRow>();
            foreach (var pair in titles)
            {
                HotKeyAction action = pair.Key;
                var button = new ShortcutButton { Width = 150, Height = 26 };
                button.Recorded += keys =>
                {
                    model.SetShortcut(action, keys);
                    SyncFromModel();
                    Changed?.Invoke();
                    ShortcutsChanged?.Invoke();
                };
                button.RecordingChanged += recording => RecordingShortcut?.Invoke(recording);
                var clear = new RoundButton { Text = "✕", Pill = true, Size = new Size(24, 24), ForeColor = Theme.Muted };
                clear.Click += (s, e) => button.Assign(Keys.None);
                shortcutButtons[action] = button;
                rows.Add(Row(pair.Value, new InlineGroup(button, clear)));
            }
            rows.Add(ButtonRow(ActionButton("Restaurar atajos", () =>
            {
                model.RestoreShortcuts();
                SyncFromModel();
                Changed?.Invoke();
                ShortcutsChanged?.Invoke();
            })));

            return Page(
                new SectionHeader("Funcionan con Tibia al frente"),
                new SectionCard(rows.ToArray()),
                new NoteLabel("Haz clic en un atajo y pulsa la nueva combinación (Esc cancela). Usa al menos Ctrl, Alt o Mayús, o una tecla F sola."));
        }

        // ---------- Sincronización ----------

        void SetCycle(int seconds) => Apply(() => model.SetCycle(seconds));

        /// Cambia el modelo y vuelve a sincronizar toda la UI (para cambios que afectan a varios controles).
        void Apply(Action apply)
        {
            if (syncing) return;
            apply();
            SyncFromModel();
            Changed?.Invoke();
        }

        void Set(Action apply)
        {
            if (syncing) return;
            apply();
            SyncSwatches();
            preview.Invalidate();
            Changed?.Invoke();
        }

        public void SyncFromModel()
        {
            syncing = true;
            minutes.Value = model.Cycle / 60;
            seconds.Value = model.Cycle % 60;
            foreach (var pill in presetButtons) pill.Selected = pill.Text == Model.Mmss(model.Cycle);
            warningRow.Text = $"Alerta en los últimos {model.Warning} s";
            beepsRow.Text = $"Beeps en los últimos {model.BeepSeconds} s";
            cycleBeep.Checked = model.CycleBeep;
            muted.Checked = model.Muted;
            volume.Enabled = !model.Muted;
            volume.Value = (int)Math.Round(model.Volume * 100);
            overlayVisible.Checked = model.OverlayVisible;
            unlocked.Checked = model.OverlayUnlocked;
            scale.Value = (int)Math.Round(model.OverlayScale * 100);
            opacity.Value = (int)Math.Round(model.OverlayOpacity * 100);
            backgroundAlpha.Value = (int)Math.Round(model.BackgroundColor.A / 2.55);
            foreach (var pair in shortcutButtons) pair.Value.Display(model.Shortcuts.TryGetValue(pair.Key, out Keys k) ? k : Keys.None);
            SyncSwatches();
            preview.Invalidate();
            hero.Invalidate();
            syncing = false;
        }

        void SyncSwatches()
        {
            counterColor.FillColor = model.CounterColor;
            alertColor.FillColor = model.AlertColor;
            backgroundColor.FillColor = Color.FromArgb(255, model.BackgroundColor);
        }

        /// Refresca la cabecera (se llama en cada tick).
        public void RefreshTimer() => hero.UpdateState();

        public void OnOverlayVisibilityToggled() => SyncFromModel();

        void PickColor(Color current, Action<Color> apply)
        {
            using (var dialog = new ColorDialog { Color = Color.FromArgb(255, current), FullOpen = true, AnyColor = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                apply(dialog.Color);
                SyncSwatches();
                preview.Invalidate();
                hero.Invalidate();
                Changed?.Invoke();
            }
        }

        // ---------- Ventana ----------

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY) HotKeyPressed?.Invoke(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Cerrar el hub solo lo oculta: el timer y el overlay siguen (salir desde el ícono de la bandeja).
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }
    }

    /// Franja con el personaje y su frase en una nube de diálogo, alineada a la derecha.
    sealed class MascotPanel : Control
    {
        const string Phrase = "Slayer macaco e mielda";

        public MascotPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float k = DeviceDpi / 96f;
            var g = e.Graphics;
            g.Clear(Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // Personaje pegado a la derecha.
            Image sprite = Resources.Character;
            float w = (sprite?.Width ?? 37) * 2 * k, h = (sprite?.Height ?? 35) * 2 * k;
            var rect = new RectangleF(Width - 6 * k - w, (Height - h) / 2, w, h);
            if (sprite != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(sprite, rect);
                g.PixelOffsetMode = PixelOffsetMode.Default;
                g.InterpolationMode = InterpolationMode.Default;
            }

            // Nube de diálogo a su izquierda, a la altura de la cabeza, con la punta hacia él.
            using (var font = new Font("Segoe UI", 12.5f * k, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF text = g.MeasureString(Phrase, font);
                float tail = 9 * k, padX = 12 * k, padY = 6 * k;
                float bw = text.Width + padX * 2, bh = text.Height + padY * 2;
                var body = new RectangleF(rect.X - 6 * k - tail - bw, Height / 2f - 8 * k - bh / 2, bw, bh);
                using (var bubble = CounterRenderer.RoundedRect(body, Math.Min(12 * k, bh / 2)))
                {
                    float mid = body.Y + body.Height / 2;
                    bubble.AddPolygon(new[]
                    {
                        new PointF(body.Right - 1, mid - 6 * k),
                        new PointF(body.Right + tail, mid + 4 * k),
                        new PointF(body.Right - 1, mid + 6 * k),
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
                    g.DrawString(Phrase, font, ink, body.X + padX, body.Y + padY);
            }
        }
    }

    /// Tarjeta del timer: estado, contador, progreso y botones.
    sealed class HeroPanel : Control
    {
        readonly Model model;
        readonly RoundButton toggle = new RoundButton(), reset = new RoundButton { Text = "↺  Reiniciar" };
        string lastState;

        public event Action ToggleClicked, ResetClicked;

        public HeroPanel(Model model)
        {
            this.model = model;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
            foreach (var b in new[] { toggle, reset })
            {
                b.Font = new Font("Segoe UI Semibold", 11f);
                b.Radius = 10;
                b.BackColor = Theme.HeroCard;
                Controls.Add(b);
            }
            reset.FillColor = Color.FromArgb(76, 73, 73);
            reset.BorderColor = Color.FromArgb(92, 89, 89);
            toggle.Click += (s, e) => ToggleClicked?.Invoke();
            reset.Click += (s, e) => ResetClicked?.Invoke();
        }

        float K => DeviceDpi / 96f;

        /// Redibuja solo si cambió algo visible (o mientras parpadea la alerta).
        public void UpdateState()
        {
            toggle.Text = model.Running ? "❚❚  Pausar" : "▶  Iniciar";
            toggle.FillColor = model.Running ? Theme.Amber : Theme.Blue;
            string state = string.Join("|", model.Seconds, model.Running, model.Cycle, model.Warning,
                model.CounterColor.ToArgb(), model.AlertColor.ToArgb());
            if (state != lastState || (model.Critical && model.Running))
            {
                lastState = state;
                Invalidate();
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float k = K;
            int y = (int)(128 * k), h = (int)(38 * k), resetW = (int)(130 * k), gap = (int)(10 * k), pad = (int)(16 * k);
            reset.Bounds = new Rectangle(Width - pad - resetW, y, resetW, h);
            toggle.Bounds = new Rectangle(pad, y, reset.Left - gap - pad, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float k = K;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Theme.Back);

            var card = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var shape = CounterRenderer.RoundedRect(card, 14 * k))
            {
                using (var fill = new SolidBrush(Theme.HeroCard)) g.FillPath(fill, shape);
                using (var border = new Pen(Color.FromArgb(84, 81, 81))) g.DrawPath(border, shape);
            }

            float left = 16 * k, right = Width - 16 * k;
            bool alert = model.Critical && model.Running;
            // El contador del hub va en blanco hueso; el color del contador solo aplica al overlay.
            Color accent = model.Critical ? model.AlertColor : Theme.Accent;

            DrawChip(g, left, 14 * k, k);
            using (var font = new Font("Consolas", 11f * k, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Theme.Muted))
            using (var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                g.DrawString($"Ciclo {model.Cycles + 1} · Total {Model.Mmss(model.Seconds)}", font, brush,
                    new RectangleF(left, 14 * k, right - left, 20 * k), format);

            using (var font = new Font("Consolas", 52f * k, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.FromArgb((int)(255 * CounterRenderer.Pulse(alert)), accent)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(Model.Mmss(model.Remaining), font, brush, new RectangleF(0, 38 * k, Width, 64 * k), format);

            var track = new RectangleF(left, 110 * k, right - left, 5 * k);
            using (var trackBrush = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
            using (var trackPath = CounterRenderer.RoundedRect(track, 2.5f * k))
                g.FillPath(trackBrush, trackPath);
            var fillRect = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * (float)model.Progress), track.Height);
            using (var fillBrush = new SolidBrush(accent))
            using (var fillPath = CounterRenderer.RoundedRect(fillRect, 2.5f * k))
                g.FillPath(fillBrush, fillPath);
        }

        void DrawChip(Graphics g, float left, float top, float k)
        {
            string label;
            Color color;
            if (!model.Running) { label = "En pausa"; color = Theme.Muted; }
            else if (model.Critical) { label = "¡Alerta!"; color = model.AlertColor; }
            else { label = "En marcha"; color = Theme.Green; }
            using (var font = new Font("Segoe UI Semibold", 11f * k, GraphicsUnit.Pixel))
            {
                SizeF text = g.MeasureString(label, font);
                float w = text.Width + 22 * k, h = 20 * k;
                var rect = new RectangleF(left, top, w, h);
                using (var path = CounterRenderer.RoundedRect(rect, h / 2))
                using (var fill = new SolidBrush(Color.FromArgb(40, color)))
                    g.FillPath(fill, path);
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
        readonly Model model;
        bool showAlert;

        public PreviewPanel(Model model)
        {
            this.model = model;
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

            int value = showAlert ? Math.Min(model.Warning, model.Cycle) : model.Cycle;
            using (var counter = CounterRenderer.Draw(model, (float)model.OverlayScale * k, Model.Mmss(value), showAlert, true, false))
            {
                float w = counter.Width, h = counter.Height;
                float fit = Math.Min(1f, Math.Min((Width - 16 * k) / w, (Height - 12 * k) / h));
                w *= fit; h *= fit;
                var dest = new Rectangle((int)((Width - w) / 2), (int)((Height - h) / 2), (int)w, (int)h);
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)model.OverlayOpacity });
                    g.DrawImage(counter, dest, 0, 0, counter.Width, counter.Height, GraphicsUnit.Pixel, attributes);
                }
            }
        }
    }

    /// Botón que graba una combinación de teclas al pulsarlo. Esc cancela.
    sealed class ShortcutButton : RoundButton
    {
        Keys value;
        bool recording;

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
            Text = recording ? "Pulsa la combinación…" : Model.Describe(value);
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
            if (mods == Keys.None && !functionKey) { SystemSounds.Beep.Play(); return true; }
            recording = false;
            RecordingChanged?.Invoke(false);
            Assign(keyData);
            return true;
        }
    }
}

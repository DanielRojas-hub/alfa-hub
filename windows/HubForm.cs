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
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(30, 30, 34);
        public static readonly Color Card = Color.FromArgb(44, 44, 50);
        public static readonly Color Fore = Color.FromArgb(235, 235, 240);
        public static readonly Color Muted = Color.FromArgb(150, 150, 160);
        /// Contador del hub en blanco hueso; botones y selección en azul.
        public static readonly Color Accent = Color.FromArgb(237, 237, 239);
        public static readonly Color Blue = Color.FromArgb(10, 132, 255);
        public static readonly Color Amber = Color.FromArgb(217, 120, 5);
        public static readonly Color Green = Color.FromArgb(70, 200, 110);
        public static readonly Color TibiaYellow = Color.FromArgb(255, 242, 51);
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

    /// Ventana principal: cabecera con el personaje y el timer, y los ajustes en tres pestañas.
    sealed class HubForm : Form
    {
        readonly Model model;
        bool syncing;

        readonly HeroPanel hero;
        readonly Panel[] pages = new Panel[3];
        readonly Button[] tabButtons = new Button[3];
        readonly PreviewPanel preview;

        // Ciclo
        readonly NumericUpDown minutes = Number(0, 60), seconds = Number(0, 59), warning = Number(0, 3600), beeps = Number(0, 30);
        readonly CheckBox cycleBeep = Check("Tono al empezar cada ciclo");
        readonly List<Button> presetButtons = new List<Button>();
        static readonly int[] Presets = { 60, 90, 120, 150, 180 };
        // Sonido
        readonly CheckBox muted = Check("Silenciar");
        readonly TrackBar volume = Slider(0, 100);
        // Overlay
        readonly CheckBox overlayVisible = Check("Mostrar overlay"), unlocked = Check("Desbloquear para moverlo");
        readonly CheckBox previewAlert = Check("Ver en alerta");
        readonly TrackBar scale = Slider(60, 300), opacity = Slider(20, 100), backgroundAlpha = Slider(0, 100);
        // Colores
        readonly Button counterColor = Swatch(), alertColor = Swatch(), backgroundColor = Swatch();
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
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 780);
            MinimumSize = new Size(456, 560);
            Padding = new Padding(12, 12, 12, 0);

            var mascot = new MascotPanel { Dock = DockStyle.Top, Height = 80 };
            hero = new HeroPanel(model) { Dock = DockStyle.Top, Height = 184 };
            hero.ToggleClicked += () => { model.Toggle(); RefreshTimer(); };
            hero.ResetClicked += () => { model.Reset(); RefreshTimer(); };

            var tabs = new TableLayoutPanel { Dock = DockStyle.Top, Height = 34, ColumnCount = 3, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 0) };
            for (int i = 0; i < 3; i++) tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            string[] titles = { "Ciclo y sonido", "Overlay", "Atajos" };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var b = new Button { Text = titles[i], Dock = DockStyle.Fill, Margin = new Padding(i == 0 ? 0 : 2, 0, i == 2 ? 0 : 2, 0) };
                StyleButton(b, Theme.Card);
                b.Click += (s, e) => SelectTab(index);
                tabButtons[i] = b;
                tabs.Controls.Add(b, i, 0);
            }
            var spacer = new Panel { Dock = DockStyle.Top, Height = 10 };

            preview = new PreviewPanel(model) { Height = 90, Width = 400 };
            pages[0] = Page(BuildTimerPage);
            pages[1] = Page(BuildOverlayPage);
            pages[2] = Page(BuildShortcutsPage);

            // Orden de acoplado: lo último añadido con Dock.Top queda arriba.
            foreach (var page in pages) Controls.Add(page);
            Controls.Add(tabs);
            Controls.Add(spacer);
            Controls.Add(hero);
            Controls.Add(mascot);

            SyncFromModel();
            SelectTab(0);
            RefreshTimer();
            ResumeLayout(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkTitleBar(Handle);
        }

        void SelectTab(int index)
        {
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = i == index;
                SetFill(tabButtons[i], i == index ? Theme.Blue : Theme.Card);
            }
        }

        // ---------- Páginas ----------

        Panel Page(Action<TableLayoutPanel> build)
        {
            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(4, 0, 4, 16) };
            var table = new TableLayoutPanel
            {
                ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            build(table);
            page.Controls.Add(table);
            return page;
        }

        void BuildTimerPage(TableLayoutPanel t)
        {
            Header(t, "Tiempo entre ciclos");
            var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
            foreach (int preset in Presets)
            {
                int value = preset;
                var b = new Button { Text = Model.Mmss(value), Width = 70, Font = new Font("Consolas", 10f, FontStyle.Bold), Tag = value };
                StyleButton(b, Theme.Card);
                b.Margin = new Padding(0, 0, 6, 0);
                b.Click += (s, e) => { model.SetCycle(value); SyncFromModel(); Changed?.Invoke(); };
                presetButtons.Add(b);
                presets.Controls.Add(b);
            }
            Full(t, presets);
            var custom = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            custom.Controls.Add(minutes); custom.Controls.Add(Caption("min"));
            custom.Controls.Add(seconds); custom.Controls.Add(Caption("s"));
            Row(t, "Personalizado", custom);

            Header(t, "Avisos");
            Row(t, "Alerta en los últimos (s)", warning);
            Row(t, "Beeps en los últimos (s)", beeps);
            Full(t, cycleBeep);
            Full(t, ActionButton("Restaurar valores del Pez (2:00 · 25 s · 5 s)", () => { model.RestoreTimerDefaults(); SyncFromModel(); Changed?.Invoke(); }));

            Header(t, "Sonido");
            Full(t, muted);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            volume.Width = 140;  // con 200 el botón "Probar" se salía de la ventana
            row.Controls.Add(volume);
            row.Controls.Add(ActionButton("Probar", () => Beeper.Beep(1200, 0.15, model)));
            Row(t, "Volumen", row);

            EventHandler cycleChanged = (s, e) =>
            {
                if (syncing) return;
                model.SetCycle((int)minutes.Value * 60 + (int)seconds.Value);
                SyncFromModel();  // por si quedó por debajo del mínimo de 5 s
                Changed?.Invoke();
            };
            minutes.ValueChanged += cycleChanged;
            seconds.ValueChanged += cycleChanged;
            warning.ValueChanged += (s, e) => Set(() => model.Warning = (int)warning.Value);
            beeps.ValueChanged += (s, e) => Set(() => model.BeepSeconds = (int)beeps.Value);
            cycleBeep.CheckedChanged += (s, e) => Set(() => model.CycleBeep = cycleBeep.Checked);
            muted.CheckedChanged += (s, e) => Set(() => { model.Muted = muted.Checked; volume.Enabled = !muted.Checked; });
            volume.ValueChanged += (s, e) => Set(() => model.Volume = volume.Value / 100.0);
        }

        void BuildOverlayPage(TableLayoutPanel t)
        {
            Header(t, "Vista previa");
            preview.Margin = new Padding(0, 2, 0, 2);
            Full(t, preview);
            Full(t, previewAlert);
            previewAlert.CheckedChanged += (s, e) => { preview.ShowAlert = previewAlert.Checked; };

            Header(t, "En Tibia");
            Full(t, overlayVisible);
            Row(t, "Tamaño", scale);
            Row(t, "Opacidad", opacity);
            Full(t, unlocked);
            Full(t, ActionButton("Volver a la esquina superior derecha", () => ResetOverlayPosition?.Invoke()));
            Note(t, "Solo aparece con Tibia al frente y deja pasar los clics al juego. Mientras está desbloqueado se ve siempre, para que puedas arrastrarlo a su sitio; luego vuelve a bloquearlo.");

            Header(t, "Colores");
            Row(t, "Contador", counterColor);
            Row(t, "Contador en alerta", alertColor);
            Row(t, "Fondo", backgroundColor);
            Row(t, "Opacidad del fondo", backgroundAlpha);
            Full(t, ActionButton("Restaurar colores", () => { model.RestoreColors(); SyncFromModel(); Changed?.Invoke(); }));
            Note(t, "Pon la opacidad del fondo en cero para ver solo el número.");

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
        }

        void BuildShortcutsPage(TableLayoutPanel t)
        {
            Header(t, "Funcionan con Tibia al frente");
            var titles = new Dictionary<HotKeyAction, string>
            {
                [HotKeyAction.Toggle] = "Iniciar / pausar",
                [HotKeyAction.Reset] = "Reiniciar",
                [HotKeyAction.Overlay] = "Mostrar / ocultar overlay",
            };
            foreach (var pair in titles)
            {
                HotKeyAction action = pair.Key;
                var button = new ShortcutButton { Width = 170 };
                StyleButton(button, Theme.Card);
                button.Recorded += keys =>
                {
                    model.SetShortcut(action, keys);
                    SyncFromModel();
                    Changed?.Invoke();
                    ShortcutsChanged?.Invoke();
                };
                button.RecordingChanged += recording => RecordingShortcut?.Invoke(recording);
                var clear = ActionButton("✕", () => button.Assign(Keys.None));
                clear.AutoSize = false;
                clear.Width = 32;
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                row.Controls.Add(button);
                row.Controls.Add(clear);
                shortcutButtons[action] = button;
                Row(t, pair.Value, row);
            }
            Full(t, ActionButton("Restaurar atajos", () =>
            {
                model.RestoreShortcuts();
                SyncFromModel();
                Changed?.Invoke();
                ShortcutsChanged?.Invoke();
            }));
            Note(t, "Haz clic en un atajo y pulsa la nueva combinación (Esc cancela). Usa al menos Ctrl, Alt o Mayús, o una tecla F sola.");
        }

        // ---------- Sincronización ----------

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
            foreach (var b in presetButtons) SetFill(b, (int)b.Tag == model.Cycle ? Theme.Blue : Theme.Card);
            warning.Value = Model.Clamp(model.Warning, 0, 3600);
            beeps.Value = Model.Clamp(model.BeepSeconds, 0, 30);
            cycleBeep.Checked = model.CycleBeep;
            muted.Checked = model.Muted;
            volume.Enabled = !model.Muted;
            volume.Value = Model.Clamp((int)Math.Round(model.Volume * 100), 0, 100);
            overlayVisible.Checked = model.OverlayVisible;
            unlocked.Checked = model.OverlayUnlocked;
            scale.Value = Model.Clamp((int)Math.Round(model.OverlayScale * 100), 60, 300);
            opacity.Value = Model.Clamp((int)Math.Round(model.OverlayOpacity * 100), 20, 100);
            backgroundAlpha.Value = Model.Clamp((int)Math.Round(model.BackgroundColor.A / 2.55), 0, 100);
            foreach (var pair in shortcutButtons) pair.Value.Display(model.Shortcuts.TryGetValue(pair.Key, out Keys k) ? k : Keys.None);
            SyncSwatches();
            preview.Invalidate();
            hero.Invalidate();
            syncing = false;
        }

        void SyncSwatches()
        {
            counterColor.BackColor = model.CounterColor;
            alertColor.BackColor = model.AlertColor;
            backgroundColor.BackColor = Color.FromArgb(255, model.BackgroundColor);
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

        // ---------- Helpers de UI ----------

        void Header(TableLayoutPanel t, string text)
        {
            Full(t, new Label
            {
                Text = text, AutoSize = true, ForeColor = Theme.Fore,
                Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold), Margin = new Padding(0, 14, 0, 4),
            });
        }

        void Note(TableLayoutPanel t, string text)
        {
            Full(t, new Label
            {
                Text = text, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(390, 0),
                Font = new Font(Font.FontFamily, 8.5f), Margin = new Padding(0, 2, 0, 4),
            });
        }

        static void Row(TableLayoutPanel t, string label, Control control)
        {
            t.Controls.Add(new Label
            {
                Text = label, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Fore,
                Margin = new Padding(0, 6, 12, 6),
            });
            control.Anchor = AnchorStyles.Left;
            t.Controls.Add(control);
        }

        static void Full(TableLayoutPanel t, Control control)
        {
            t.Controls.Add(control);
            t.SetColumnSpan(control, 2);
        }

        static Label Caption(string text) =>
            new Label { Text = text, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(2, 6, 8, 0) };

        static NumericUpDown Number(int min, int max) => new NumericUpDown
        {
            Minimum = min, Maximum = max, Width = 64, BackColor = Theme.Card, ForeColor = Theme.Fore,
            BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right,
        };

        static CheckBox Check(string text) =>
            new CheckBox { Text = text, AutoSize = true, ForeColor = Theme.Fore, Margin = new Padding(0, 6, 0, 6) };

        static TrackBar Slider(int min, int max) => new TrackBar
        {
            Minimum = min, Maximum = max, TickStyle = TickStyle.None, Width = 200, AutoSize = false, Height = 30,
            BackColor = Theme.Back, SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10),
        };

        static Button Swatch()
        {
            var b = new Button { Width = 64, Height = 24, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Theme.Muted;
            return b;
        }

        static Button ActionButton(string text, Action onClick)
        {
            var b = new Button { Text = text, AutoSize = true };
            StyleButton(b, Theme.Card);
            b.Click += (s, e) => onClick();
            return b;
        }

        /// Cambia el color de un botón plano junto con sus colores de hover/pulsado; si no, el hover
        /// del color anterior tapa el nuevo mientras el ratón sigue encima (p. ej. la pestaña recién elegida).
        public static void SetFill(Button b, Color back)
        {
            b.BackColor = back;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.05f);
        }

        public static void StyleButton(Button b, Color back)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back;
            b.ForeColor = Color.White;
            b.Height = 30;
            b.Cursor = Cursors.Hand;
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 90);
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
            b.Margin = new Padding(0, 4, 8, 4);
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
        readonly Button toggle = new Button(), reset = new Button { Text = "↺  Reiniciar" };
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
                HubForm.StyleButton(b, Theme.Card);
                b.Font = new Font("Segoe UI Semibold", 11f);
                b.FlatAppearance.BorderSize = 0;
                Controls.Add(b);
            }
            reset.BackColor = Color.FromArgb(52, 52, 60);
            toggle.Click += (s, e) => ToggleClicked?.Invoke();
            reset.Click += (s, e) => ResetClicked?.Invoke();
        }

        float K => DeviceDpi / 96f;

        /// Redibuja solo si cambió algo visible (o mientras parpadea la alerta).
        public void UpdateState()
        {
            toggle.Text = model.Running ? "❚❚  Pausar" : "▶  Iniciar";
            HubForm.SetFill(toggle, model.Running ? Theme.Amber : Theme.Blue);
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
            int y = (int)(128 * k), h = (int)(40 * k), resetW = (int)(130 * k), gap = (int)(10 * k), pad = (int)(16 * k);
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
                using (var fill = new SolidBrush(Color.FromArgb(40, 40, 46))) g.FillPath(fill, shape);
                using (var border = new Pen(Color.FromArgb(62, 62, 70))) g.DrawPath(border, shape);
            }

            float left = 16 * k, right = Width - 16 * k;
            bool alert = model.Critical && model.Running;
            // El hub usa el azul de la app; el color del contador solo aplica al overlay.
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
            g.Clear(Theme.Back);
            float k = DeviceDpi / 96f;
            var rect = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var shape = CounterRenderer.RoundedRect(rect, 10 * k))
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
    sealed class ShortcutButton : Button
    {
        Keys value;
        bool recording;

        public event Action<Keys> Recorded;
        public event Action<bool> RecordingChanged;

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

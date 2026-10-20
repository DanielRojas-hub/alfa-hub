using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Páginas de Tibia Audio: al pulsar en Tibia la tecla de una habilidad empieza su cooldown y suena
    /// un aviso cuando vuelve a estar lista.
    sealed partial class HubForm
    {
        string AudioStatusText
        {
            get
            {
                int count = model.Audios.Count;
                string text = $"{count} {(count == 1 ? "habilidad" : "habilidades")}";
                if (!model.AudioEnabled) return text + " · Desactivado";
                return text + (model.Audios.Any(a => a.Config.Enabled && a.Config.Key != 0)
                    ? " · Escuchando con Tibia al frente" : " · Ninguna tiene tecla");
            }
        }

        Control BuildAudioPage()
        {
            var add = new RoundButton { Text = "＋  Añadir habilidad", FillColor = Theme.Blue, Pill = true, Height = 34, BackColor = Theme.Back };
            add.Font = new Font("Segoe UI Semibold", 10f);
            add.FitText(16);
            add.Click += (s, e) =>
            {
                var audio = model.AddAudio();
                Changed?.Invoke();
                editingAudioId = audio.Id;
                ShowPage(HubPage.Audio);
            };

            var enabled = new ToggleSwitch { Checked = model.AudioEnabled, Text = "Tibia Audio activado", BackColor = Theme.Back };
            audioStatus = new Label { AutoSize = true, ForeColor = Theme.Muted, BackColor = Theme.Back, Text = AudioStatusText };
            enabled.CheckedChanged += (s, e) => { model.AudioEnabled = enabled.Checked; Changed?.Invoke(); RefreshTimer(); };
            var testAll = SmallButton("▶  Probar todas", () =>
            {
                foreach (var a in model.Audios.Where(a => a.Config.Enabled)) a.Trigger();
                RefreshTimer();
            });

            var items = new List<Control>
            {
                new PageHeader("Tibia Audio", "Un aviso sonoro cuando una habilidad vuelve a estar lista", add),
                new ToolbarRow(audioStatus, testAll) { Margin = new Padding(0, 4, 0, 8) },
                new SectionCard(Row("Activar Tibia Audio", enabled)) { Margin = new Padding(0, 0, 0, 10) },
            };
            if (model.Audios.Count == 0)
                items.Add(new NoteLabel("Todavía no hay habilidades. Pulsa “Añadir habilidad”, pon la tecla con la que la usas en Tibia y su cooldown: cuando la pulses en el juego, sonará el aviso al terminar el cooldown."));
            foreach (var audio in model.Audios)
            {
                var a = audio;
                var row = new AudioRow(a) { Margin = new Padding(0, 0, 0, 8) };
                row.Changed += () => { Changed?.Invoke(); RefreshTimer(); };
                row.EditClicked += () => { editingAudioId = a.Id; ShowPage(HubPage.Audio); };
                row.DuplicateClicked += () => { model.Duplicate(a); Changed?.Invoke(); Rebuild(); };
                row.DeleteClicked += () => { model.Remove(a); Changed?.Invoke(); Rebuild(); };
                audioRows.Add(row);
                items.Add(row);
            }
            items.Add(new NoteLabel("Alfa Hub solo mira si están pulsadas las teclas de esta lista, y solo con Tibia al frente. No instala ganchos de teclado ni bloquea nada: Tibia recibe las teclas igual y Alfa Hub no envía nada al juego."));
            return Page(items.ToArray());
        }

        Control BuildAudioEditor(AudioRun audio)
        {
            var c = audio.Config;
            var back = new RoundButton { Text = "‹  Volver a Tibia Audio", ForeColor = Theme.Blue, FillColor = Theme.Back, BackColor = Theme.Back, Height = 26 };
            back.FitText(2);
            back.Click += (s, e) => { editingAudioId = null; ShowPage(HubPage.Audio); };
            var backRow = new FormRow(null, back, RowLayout.Leading, 28) { BackColor = Theme.Back, Margin = new Padding(-12, 0, 0, 0) };

            var name = new TextField { Value = c.Name };
            name.TextCommitted += (s, e) =>
            {
                string value = name.Value.Trim();
                if (value.Length == 0 || value == c.Name) return;
                c.Name = value;
                Changed?.Invoke();
                Rebuild();
            };
            var enabled = new ToggleSwitch { Checked = c.Enabled };
            enabled.CheckedChanged += (s, e) => { c.Enabled = enabled.Checked; Changed?.Invoke(); };

            var key = new ShortcutButton { Width = 150, Height = 26, AllowPlain = true, EmptyText = "Sin tecla" };
            key.Display((Keys)c.Key);
            key.Recorded += keys => { c.Key = (int)keys; Changed?.Invoke(); Rebuild(); };
            key.RecordingChanged += recording => RecordingShortcut?.Invoke(recording);
            var keyGroup = new InlineGroup(key) { Gap = 8 };
            if (c.Key != 0)
            {
                var clear = new RoundButton { Text = "✕", Pill = true, Size = new Size(24, 24), ForeColor = Theme.Muted };
                clear.Click += (s, e) => { c.Key = 0; Changed?.Invoke(); Rebuild(); };
                keyGroup.Controls.Add(clear);
            }

            var cooldown = new TextField { Value = Model.Seconds(c.Cooldown), Width = 60 };
            cooldown.TextCommitted += (s, e) =>
            {
                if (double.TryParse(cooldown.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    c.Cooldown = Math.Max(0.5, Math.Min(3600, v));
                cooldown.Value = Model.Seconds(c.Cooldown);
                Changed?.Invoke();
            };
            var stepper = new StepperArrows();
            stepper.Up += () => { c.Cooldown = Math.Min(3600, c.Cooldown + 0.5); cooldown.Value = Model.Seconds(c.Cooldown); Changed?.Invoke(); };
            stepper.Down += () => { c.Cooldown = Math.Max(0.5, c.Cooldown - 0.5); cooldown.Value = Model.Seconds(c.Cooldown); Changed?.Invoke(); };
            var retrigger = new SegmentedControl("Vuelve a empezar", "Se ignora") { Width = 250, Height = 30 };
            retrigger.SetSelected(c.Retrigger ? 0 : 1);
            retrigger.SelectedIndexChanged += i => { c.Retrigger = i == 0; Changed?.Invoke(); };

            var sound = SoundChooser(() => c.Sound, v => c.Sound = v, () => c.SoundPath, v => c.SoundPath = v,
                "Sonido para " + c.Name, () => audio.PlaySound(), () => Changed?.Invoke());
            var volume = new FlatSlider { Maximum = 100, Value = (int)Math.Round(c.Volume * 100), Width = 190 };
            var volumeLabel = new Caption($"{volume.Value} %") { Width = 48 };
            volume.ValueChanged += (s, e) => { c.Volume = volume.Value / 100.0; volumeLabel.Text = $"{volume.Value} %"; Changed?.Invoke(); };

            var delete = ActionButton("Eliminar esta habilidad", () =>
            {
                model.Remove(audio);
                editingAudioId = null;
                Changed?.Invoke();
                ShowPage(HubPage.Audio);
            });
            delete.ForeColor = Color.FromArgb(255, 105, 97);

            var items = new List<Control>
            {
                backRow,
                new PageHeader(c.Name, audio.Summary),
                new SectionHeader("Habilidad"),
                new SectionCard(Row("Nombre", name), Row("Activada", enabled), Row("Tecla en Tibia", keyGroup)),
            };
            if (model.AudioKeyConflicts((Keys)c.Key))
                items.Add(new NoteLabel("⚠  Esta tecla también es un atajo de Alfa Hub: Alfa Hub se la queda y no llega a Tibia. Cambia uno de los dos."));
            else
                items.Add(new NoteLabel("Haz clic en el botón de la tecla y pulsa la misma tecla (o combinación) con la que usas la habilidad en Tibia. Esc cancela."));
            items.Add(new SectionHeader("Cooldown"));
            items.Add(new SectionCard(
                Row("Duración", new InlineGroup(cooldown, new Caption("s"), stepper) { Height = 28 }),
                Row("Si la pulsas durante el cooldown", retrigger)));
            items.Add(new SectionHeader("Sonido"));
            items.Add(new SectionCard(
                Row("Aviso al estar lista", sound),
                Row("Volumen", new InlineGroup(volume, volumeLabel) { Gap = 8 }),
                new FormRow(null, ActionButton("Simular pulsación (cooldown completo)", () => { audio.Trigger(); RefreshTimer(); }), RowLayout.Leading)));
            items.Add(new SectionCard(new FormRow(null, delete, RowLayout.Leading)) { Margin = new Padding(0, 16, 0, 0) });
            return Page(items.ToArray());
        }
    }

    /// Fila de la lista de Tibia Audio: estado, nombre, cooldown en curso, activada, volumen y acciones.
    sealed class AudioRow : PaintedControl, IStackItem
    {
        readonly AudioRun audio;
        readonly ToggleSwitch enabled;
        readonly FlatSlider volume;
        readonly Caption volumeLabel;
        readonly RoundButton edit, more;
        string lastState;

        public event Action Changed, EditClicked, DuplicateClicked, DeleteClicked;

        public AudioRow(AudioRun audio)
        {
            this.audio = audio;
            BackColor = Theme.Back;
            Cursor = Cursors.Hand;
            enabled = new ToggleSwitch { Checked = audio.Config.Enabled, Text = "Activada " + audio.Config.Name, BackColor = Theme.Row };
            volume = new FlatSlider { Maximum = 100, Value = (int)Math.Round(audio.Config.Volume * 100), Width = 90, BackColor = Theme.Row };
            volumeLabel = new Caption($"{volume.Value} %") { Width = 44, BackColor = Theme.Row };
            edit = new RoundButton { Caption = "✎  Editar", Text = "Editar " + audio.Config.Name, Height = 28, FillColor = Theme.ControlFill, BackColor = Theme.Row };
            edit.FitText(11);
            more = new RoundButton { Caption = "⋮", Text = "Más opciones " + audio.Config.Name, Pill = true, Size = new Size(30, 30), FillColor = Theme.Row, BackColor = Theme.Row };
            enabled.CheckedChanged += (s, e) => { audio.Config.Enabled = enabled.Checked; Invalidate(); Changed?.Invoke(); };
            volume.ValueChanged += (s, e) => { audio.Config.Volume = volume.Value / 100.0; volumeLabel.Text = $"{volume.Value} %"; Changed?.Invoke(); };
            edit.Click += (s, e) => EditClicked?.Invoke();
            more.Click += (s, e) =>
            {
                var menu = DarkMenu.Create();
                menu.Items.Add("Probar", null, (a, b) => audio.Trigger());
                menu.Items.Add("Duplicar", null, (a, b) => DuplicateClicked?.Invoke());
                menu.Items.Add("Eliminar", null, (a, b) => DeleteClicked?.Invoke());
                menu.Show(more, new Point(0, more.Height + 2));
            };
            Controls.AddRange(new Control[] { enabled, volume, volumeLabel, edit, more });
        }

        public int HeightFor(int width) => (int)(64 * K);

        protected override void OnDoubleClick(EventArgs e) { base.OnDoubleClick(e); EditClicked?.Invoke(); }

        public void UpdateState()
        {
            string state = $"{audio.Active}|{Math.Ceiling(audio.Remaining * 10)}|{audio.Config.Enabled}|{audio.Summary}";
            if (state == lastState) return;
            lastState = state;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float k = K;
            int x = Width - (int)(10 * k);
            foreach (var c in new Control[] { more, edit, volumeLabel, volume, enabled })
            {
                x -= c.Width;
                c.Location = new Point(x, (Height - c.Height) / 2);
                x -= (int)((c == edit ? 14 : c == volume ? 18 : 6) * k);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            var card = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, card, 10 * k, Theme.Row);
            Stroke(g, card, 10 * k, Color.FromArgb(48, 48, 52));
            Color state = !audio.Config.Enabled ? Color.FromArgb(70, 70, 75) : audio.Active ? Theme.Amber : Theme.Green;
            Fill(g, new RectangleF(10 * k, 12 * k, 4 * k, Height - 24 * k), 2 * k, state);
            using (var name = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, audio.Config.Name, name, new Rectangle((int)(26 * k), (int)(12 * k), (int)(200 * k), (int)(22 * k)),
                    Color.White, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            using (var small = new Font("Segoe UI", 8.25f))
                TextRenderer.DrawText(g, audio.Summary, small, new Rectangle((int)(26 * k), (int)(36 * k), (int)(220 * k), (int)(18 * k)),
                    Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            // Cooldown en curso, a la izquierda del interruptor.
            float right = enabled.Left - 16 * k;
            string text = audio.Active ? Model.Seconds(Math.Ceiling(audio.Remaining * 10) / 10) + " s" : "Lista";
            Color color = audio.Active ? Theme.Amber : Theme.Accent;
            using (var font = new Font("Consolas", 15f * k, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString(text, font, brush, new RectangleF(right - 100 * k, 14 * k, 100 * k, 24 * k), format);
            var track = new RectangleF(right - 70 * k, 42 * k, 70 * k, 4 * k);
            Fill(g, track, 2 * k, Color.FromArgb(40, 255, 255, 255));
            if (audio.Active) Fill(g, new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * (float)audio.Progress), track.Height), 2 * k, Theme.Amber);
        }
    }
}

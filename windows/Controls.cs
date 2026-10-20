using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AlfaHub
{
    // Controles dibujados a mano para que el hub de Windows se parezca al de macOS
    // (interruptores, deslizadores, pestañas segmentadas, formularios agrupados…).
    // Cada control pinta su fondo con BackColor, que fija el contenedor (fila, tarjeta…).

    /// Base: doble búfer, pintado propio y medidas escaladas con el DPI.
    abstract class PaintedControl : Control
    {
        protected PaintedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
        }

        protected float K => DeviceDpi / 96f;

        protected Graphics Begin(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            return g;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AccessibleName = Text;  // nombre para UI Automation (lo usa el script de capturas)
            Invalidate();
        }

        protected static void Fill(Graphics g, RectangleF r, float radius, Color color)
        {
            using (var path = CounterRenderer.RoundedRect(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        protected static void Stroke(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
        {
            using (var path = CounterRenderer.RoundedRect(r, radius))
            using (var pen = new Pen(color, width))
                g.DrawPath(pen, path);
        }

        protected static void CenterText(Graphics g, string text, Font font, Color color, RectangleF r)
        {
            TextRenderer.DrawText(g, text, font, Rectangle.Round(r), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// Botón redondeado con estados de hover y pulsado.
    class RoundButton : PaintedControl
    {
        Color fill = Theme.ControlFill;
        bool hover, pressed;

        public RoundButton()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            ForeColor = Color.White;
            Height = 28;
        }

        public Color FillColor { get => fill; set { fill = value; Invalidate(); } }
        public Color BorderColor { get; set; } = Color.Empty;
        public float Radius { get; set; } = 7;
        public bool Pill { get; set; }

        int fitPadding = -1;

        /// Ancho justo para el texto con un margen a cada lado; se recalcula si cambian el texto
        /// o la fuente (la fuente heredada de la ventana llega después de crear el botón).
        public RoundButton FitText(int padding = 14)
        {
            fitPadding = padding;
            Refit();
            return this;
        }

        void Refit()
        {
            if (fitPadding >= 0) Width = TextRenderer.MeasureText(Text, Font).Width + (int)(fitPadding * 2 * K);
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Refit(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Refit(); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (CanFocus) Focus();
            pressed = true; Invalidate(); base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float radius = Pill ? r.Height / 2 : Radius * k;
            Fill(g, r, radius, fill);
            if (Enabled && (hover || pressed)) Fill(g, r, radius, Color.FromArgb(pressed ? 18 : 30, 255, 255, 255));
            if (BorderColor != Color.Empty) Stroke(g, r, radius, BorderColor);
            Color text = Enabled ? ForeColor : Color.FromArgb(110, ForeColor);
            CenterText(g, Text, Font, text, new RectangleF(0, 0, Width, Height));
        }
    }

    /// Cápsula de duración rápida (01:00, 01:30…): azul cuando está elegida.
    sealed class PillButton : RoundButton
    {
        bool selected;

        public PillButton()
        {
            Pill = true;
            Font = new Font("Consolas", 9.5f, FontStyle.Bold);
            Height = 26;
            FillColor = Color.FromArgb(20, 255, 255, 255);
        }

        public bool Selected
        {
            get => selected;
            set { selected = value; FillColor = value ? Theme.Blue : Color.FromArgb(20, 255, 255, 255); }
        }
    }

    /// Muestra de color en forma de cápsula.
    sealed class ColorSwatch : RoundButton
    {
        public ColorSwatch()
        {
            Pill = true;
            Size = new Size(38, 20);
            BorderColor = Color.FromArgb(60, 255, 255, 255);
        }
    }

    /// Interruptor estilo macOS.
    sealed class ToggleSwitch : PaintedControl
    {
        bool isChecked;

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(38, 22);
        }

        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, r, r.Height / 2, isChecked ? Theme.Blue : Color.FromArgb(90, 90, 94));
            float d = r.Height - 4 * k;
            float x = isChecked ? r.Right - 2 * k - d : r.X + 2 * k;
            using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(shadow, x, r.Y + 2 * k + 0.8f * k, d, d);
            using (var knob = new SolidBrush(Color.White)) g.FillEllipse(knob, x, r.Y + 2 * k, d, d);
        }
    }

    /// Deslizador: pista fina, tramo azul y pulgar blanco.
    sealed class FlatSlider : PaintedControl
    {
        int minimum, maximum = 100, value;
        bool dragging;

        public event EventHandler ValueChanged;

        public FlatSlider()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(170, 22);
        }

        public int Minimum { get => minimum; set { minimum = value; Value = this.value; } }
        public int Maximum { get => maximum; set { maximum = value; Value = this.value; } }

        public int Value
        {
            get => value;
            set
            {
                int v = Model.Clamp(value, minimum, maximum);
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        float Thumb => Height - 4 * K;

        void SetFromMouse(int x)
        {
            float usable = Width - Thumb;
            float t = Math.Max(0, Math.Min(1, (x - Thumb / 2) / usable));
            Value = minimum + (int)Math.Round(t * (maximum - minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); dragging = true; SetFromMouse(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromMouse(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = Math.Max(1, (maximum - minimum) / 50);
            if (e.KeyCode == Keys.Left) Value -= step;
            if (e.KeyCode == Keys.Right) Value += step;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K, d = Thumb;
            float t = maximum == minimum ? 0 : (float)(value - minimum) / (maximum - minimum);
            float cx = d / 2 + t * (Width - d);
            float trackH = 4 * k, ty = (Height - trackH) / 2;
            Fill(g, new RectangleF(d / 2, ty, Width - d, trackH), trackH / 2, Color.FromArgb(55, 255, 255, 255));
            Color active = Enabled ? Theme.Blue : Color.FromArgb(120, 120, 124);
            Fill(g, new RectangleF(d / 2, ty, Math.Max(trackH, cx - d / 2), trackH), trackH / 2, active);
            float y = (Height - d) / 2;
            using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(shadow, cx - d / 2, y + 1 * k, d, d);
            using (var knob = new SolidBrush(Enabled ? Color.White : Color.FromArgb(170, 170, 174))) g.FillEllipse(knob, cx - d / 2, y, d, d);
        }
    }

    /// Campo numérico oscuro y redondeado (sin flechas: van aparte en StepperArrows).
    sealed class NumberField : PaintedControl
    {
        readonly TextBox box = new TextBox();
        int minimum, maximum = 99, value;

        public event EventHandler ValueCommitted;

        public NumberField()
        {
            Size = new Size(42, 24);
            box.BorderStyle = BorderStyle.None;
            box.TextAlign = HorizontalAlignment.Right;
            box.BackColor = Theme.Field;
            box.ForeColor = Color.White;
            box.Text = "0";
            box.KeyPress += (s, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; } };
            box.Leave += (s, e) => Commit();
            Controls.Add(box);
        }

        public int Minimum { get => minimum; set => minimum = value; }
        public int Maximum { get => maximum; set => maximum = value; }

        public int Value
        {
            get => value;
            set { this.value = Model.Clamp(value, minimum, maximum); box.Text = this.value.ToString(); }
        }

        void Commit()
        {
            int.TryParse(box.Text, out int typed);
            Value = typed;
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = (int)(6 * K);
            box.Font = Font;
            box.SetBounds(pad, (Height - box.PreferredHeight) / 2, Width - pad * 2, box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, r, 5 * K, Theme.Field);
            Stroke(g, r, 5 * K, Color.FromArgb(40, 255, 255, 255));
        }
    }

    /// Campo de texto oscuro y redondeado.
    sealed class TextField : PaintedControl
    {
        readonly TextBox box = new TextBox();

        public event EventHandler TextCommitted;

        public TextField()
        {
            Size = new Size(170, 26);
            box.BorderStyle = BorderStyle.None;
            box.TextAlign = HorizontalAlignment.Right;
            box.BackColor = Theme.Field;
            box.ForeColor = Color.White;
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { TextCommitted?.Invoke(this, EventArgs.Empty); e.SuppressKeyPress = true; } };
            box.Leave += (s, e) => TextCommitted?.Invoke(this, EventArgs.Empty);
            Controls.Add(box);
        }

        public string Value { get => box.Text; set => box.Text = value; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = (int)(8 * K);
            box.Font = Font;
            box.SetBounds(pad, (Height - box.PreferredHeight) / 2, Width - pad * 2, box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, r, 5 * K, Theme.Field);
            Stroke(g, r, 5 * K, Color.FromArgb(40, 255, 255, 255));
        }
    }

    /// Flechas ⌃⌄ en una cápsula, como el Stepper de macOS.
    sealed class StepperArrows : PaintedControl
    {
        int pressedHalf;  // -1 abajo, 1 arriba

        public event Action Up, Down;

        public StepperArrows()
        {
            Size = new Size(18, 26);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressedHalf = e.Y < Height / 2 ? 1 : -1;
            Invalidate();
            if (pressedHalf > 0) Up?.Invoke(); else Down?.Invoke();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { pressedHalf = 0; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, r, 5 * k, Color.FromArgb(28, 255, 255, 255));
            if (pressedHalf != 0)
            {
                var half = pressedHalf > 0 ? new RectangleF(r.X, r.Y, r.Width, r.Height / 2) : new RectangleF(r.X, r.Y + r.Height / 2, r.Width, r.Height / 2);
                Fill(g, half, 5 * k, Color.FromArgb(30, 255, 255, 255));
            }
            using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.4f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float cx = Width / 2f, w = 3.2f * k, h = 2.2f * k;
                float upY = Height * 0.32f, downY = Height * 0.68f;
                g.DrawLines(pen, new[] { new PointF(cx - w, upY + h / 2), new PointF(cx, upY - h / 2), new PointF(cx + w, upY + h / 2) });
                g.DrawLines(pen, new[] { new PointF(cx - w, downY - h / 2), new PointF(cx, downY + h / 2), new PointF(cx + w, downY - h / 2) });
            }
        }
    }

    /// Texto secundario corto ("min", "s").
    sealed class Caption : PaintedControl
    {
        public Caption(string text)
        {
            Text = text;
            ForeColor = Theme.Muted;
            Height = 24;
            Width = TextRenderer.MeasureText(text, Font).Width + 2;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Width = TextRenderer.MeasureText(Text, Font).Width + 2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            CenterText(g, Text, Font, ForeColor, new RectangleF(0, 0, Width, Height));
        }
    }

    /// Varios controles en fila (de izquierda a derecha), con su ancho propio.
    sealed class InlineGroup : PaintedControl
    {
        public InlineGroup(params Control[] items)
        {
            foreach (var item in items) Controls.Add(item);
            Height = 28;
        }

        public int Gap { get; set; } = 6;

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            foreach (Control c in Controls) c.BackColor = BackColor;
        }

        public int PreferredWidth
        {
            get
            {
                int w = 0, gap = (int)(Gap * K);
                foreach (Control c in Controls) w += c.Width + gap;
                return Math.Max(0, w - gap);
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = 0, gap = (int)(Gap * K);
            foreach (Control c in Controls)
            {
                c.Location = new Point(x, (Height - c.Height) / 2);
                x += c.Width + gap;
            }
        }

        protected override void OnPaint(PaintEventArgs e) => Begin(e);
    }

    /// Pestañas segmentadas: cápsula gris con la opción elegida en azul.
    sealed class SegmentedControl : PaintedControl
    {
        readonly List<RoundButton> segments = new List<RoundButton>();
        int selected;

        public event Action<int> SelectedIndexChanged;

        public SegmentedControl(params string[] titles)
        {
            Height = 32;
            foreach (string title in titles)
            {
                int index = segments.Count;
                var b = new RoundButton { Text = title, Radius = 7 };
                b.Click += (s, e) => SelectedIndex = index;
                segments.Add(b);
                Controls.Add(b);
            }
            Restyle();
        }

        public int SelectedIndex
        {
            get => selected;
            set
            {
                if (selected == value) return;
                selected = value;
                Restyle();
                SelectedIndexChanged?.Invoke(value);
            }
        }

        /// Cambia la opción elegida sin avisar (para sincronizar con el modelo).
        public void SetSelected(int index) { selected = index; Restyle(); }

        void Restyle()
        {
            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].FillColor = i == selected ? Theme.Blue : Theme.Segment;
                segments[i].ForeColor = i == selected ? Color.White : Theme.Fore;
                segments[i].BackColor = Theme.Segment;
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = (int)(3 * K), gap = (int)(2 * K);
            float w = (Width - pad * 2 - gap * (segments.Count - 1)) / (float)segments.Count;
            for (int i = 0; i < segments.Count; i++)
                segments[i].SetBounds((int)(pad + i * (w + gap)), pad, (int)w, Height - pad * 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), 9 * K, Theme.Segment);
        }
    }

    // ---------- Formulario agrupado ----------

    /// Elemento de StackPanel que calcula su alto según el ancho disponible.
    interface IStackItem { int HeightFor(int width); }

    /// Panel con scroll que apila a sus hijos de arriba abajo, a todo el ancho.
    sealed class StackPanel : Panel
    {
        bool laying;

        public StackPanel()
        {
            AutoScroll = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkScrollbars(Handle);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (laying) return;
            laying = true;
            try
            {
                int width = ClientSize.Width - Padding.Horizontal;
                int y = Padding.Top;
                foreach (Control c in Controls)
                {
                    if (!c.Visible) continue;
                    int w = width - c.Margin.Horizontal;
                    int h = c is IStackItem item ? item.HeightFor(w) : c.Height;
                    c.SetBounds(Padding.Left + c.Margin.Left, AutoScrollPosition.Y + y + c.Margin.Top, w, h);
                    y += c.Margin.Vertical + h;
                }
                AutoScrollMinSize = new Size(0, y + Padding.Bottom);
            }
            finally { laying = false; }
        }
    }

    /// Título de sección ("Tiempo entre ciclos", "Avisos"…).
    sealed class SectionHeader : PaintedControl, IStackItem
    {
        public SectionHeader(string text)
        {
            Text = text;
            Font = new Font("Segoe UI Semibold", 10f);
            ForeColor = Color.White;
            Margin = new Padding(4, 14, 0, 6);
        }

        public int HeightFor(int width) => TextRenderer.MeasureText(Text, Font).Height;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            TextRenderer.DrawText(g, Text, Font, new Point(0, 0), ForeColor);
        }
    }

    /// Nota al pie de una sección, en gris y con salto de línea.
    sealed class NoteLabel : PaintedControl, IStackItem
    {
        const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

        public NoteLabel(string text)
        {
            Text = text;
            Font = new Font("Segoe UI", 8.5f);
            ForeColor = Theme.Muted;
            Margin = new Padding(4, 6, 4, 0);
        }

        public int HeightFor(int width) => TextRenderer.MeasureText(Text, Font, new Size(width, 0), Flags).Height;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, Flags);
        }
    }

    /// Tarjeta redondeada que agrupa filas con separadores finos.
    sealed class SectionCard : PaintedControl, IStackItem
    {
        public SectionCard(params FormRow[] rows)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].ShowSeparator = i < rows.Length - 1;
                Controls.Add(rows[i]);
            }
            Margin = new Padding(0);
            BackColor = Theme.Row;
        }

        public int HeightFor(int width)
        {
            int h = 0;
            foreach (Control c in Controls) if (c.Visible) h += c.Height;
            return h;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int y = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                c.SetBounds(0, y, Width, c.Height);
                y += c.Height;
            }
        }

        protected override void OnPaint(PaintEventArgs e) => Begin(e);

        // Esquinas redondeadas: se recorta la región de la tarjeta.
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using (var path = CounterRenderer.RoundedRect(new RectangleF(0, 0, Width, Height), 9 * K))
                Region = new Region(path);
        }
    }

    enum RowLayout { Trailing, Leading, Stretch, Centered }

    /// Fila de una tarjeta: texto a la izquierda y un control (accesorio) a la derecha.
    sealed class FormRow : PaintedControl
    {
        readonly Control accessory;
        readonly RowLayout layout;

        public FormRow(string text, Control accessory, RowLayout layout = RowLayout.Trailing, int height = 40)
        {
            Text = text ?? "";
            this.accessory = accessory;
            this.layout = layout;
            BackColor = Theme.Row;
            ForeColor = Theme.Fore;
            Height = height;
            if (accessory != null)
            {
                accessory.BackColor = Theme.Row;
                Controls.Add(accessory);
            }
        }

        public bool ShowSeparator { get; set; }

        // Clic en cualquier parte de la fila de un interruptor lo cambia (blanco más grande).
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (accessory is ToggleSwitch toggle && toggle.Enabled) toggle.Checked = !toggle.Checked;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (accessory == null) return;
            int pad = (int)(12 * K);
            int w = accessory is InlineGroup group ? group.PreferredWidth : accessory.Width;
            switch (layout)
            {
                case RowLayout.Trailing:
                    accessory.SetBounds(Width - pad - w, (Height - accessory.Height) / 2, w, accessory.Height);
                    break;
                case RowLayout.Leading:
                    accessory.SetBounds(pad, (Height - accessory.Height) / 2, w, accessory.Height);
                    break;
                case RowLayout.Centered:
                    accessory.SetBounds((Width - w) / 2, (Height - accessory.Height) / 2, w, accessory.Height);
                    break;
                case RowLayout.Stretch:
                    int inset = (int)(8 * K);
                    accessory.SetBounds(inset, inset, Width - inset * 2, Height - inset * 2);
                    break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            if (Text.Length > 0)
                TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(12 * k), 0, Width, Height), ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (ShowSeparator)
                using (var pen = new Pen(Color.FromArgb(22, 255, 255, 255)))
                    g.DrawLine(pen, 12 * k, Height - 1, Width, Height - 1);
        }
    }
}

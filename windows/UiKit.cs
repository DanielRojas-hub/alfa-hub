using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AlfaHub
{
    // Piezas de la página Perfiles: un lienzo que se dibuja entero y sabe qué hay debajo del ratón
    // (HitCanvas), utilidades de dibujo, campos de texto y ventanas emergentes (Popover).

    static class Palette
    {
        public static readonly Color Purple = Color.FromArgb(176, 115, 250);
        public static readonly Color Gold = Color.FromArgb(242, 179, 64);
        public static readonly Color Orange = Color.FromArgb(255, 115, 64);
        public static readonly Color Red = Color.FromArgb(255, 99, 99);
        public static readonly Color Cyan = Color.FromArgb(77, 204, 217);
        public static readonly Color Card = Color.FromArgb(40, 40, 43);
        public static readonly Color Inset = Color.FromArgb(48, 48, 52);
        public static readonly Color Edge = Color.FromArgb(18, 255, 255, 255);

        public static Color Place(string place)
        {
            switch (place)
            {
                case "equipped": return Theme.Green;
                case "backpack": return Gold;
                case "depot": return Theme.Blue;
                case "house": return Orange;
                case "stash": return Purple;
                case "otherChar": return Cyan;
                default: return Theme.Muted;
            }
        }
    }

    /// Fuentes en caché (Segoe UI de distintos tamaños).
    static class Fonts
    {
        static readonly Dictionary<(float, FontStyle, bool), Font> cache = new Dictionary<(float, FontStyle, bool), Font>();

        public static Font Get(float size, FontStyle style = FontStyle.Regular, bool semibold = false)
        {
            var key = (size, style, semibold);
            if (!cache.TryGetValue(key, out var font)) cache[key] = font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size, style);
            return font;
        }

        public static Font Bold(float size) => Get(size, FontStyle.Bold);
        public static Font Semi(float size) => Get(size, FontStyle.Regular, true);
    }

    /// Una zona clicable de un lienzo.
    struct Hot
    {
        public RectangleF Rect;
        public Action Click;
        public Action<Point> Menu;   // clic derecho o botón ⋮ (posición en pantalla)
        public string Tip;
    }

    /// Lienzo dibujado a mano para el StackPanel: calcula su alto según el ancho, se dibuja entero en
    /// OnPaint (solo lo visible) y guarda las zonas clicables de ese dibujo.
    abstract class HitCanvas : PaintedControl, IStackItem
    {
        protected readonly List<Hot> hots = new List<Hot>();
        readonly ToolTip tip = new ToolTip { InitialDelay = 400, ReshowDelay = 100 };
        int hover = -1;
        string shownTip;

        protected HitCanvas()
        {
            BackColor = Theme.Back;
            WikiImages.Loaded += OnImages;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { WikiImages.Loaded -= OnImages; tip.Dispose(); }
            base.Dispose(disposing);
        }

        void OnImages() { if (!IsDisposed) Invalidate(); }

        public abstract int HeightFor(int width);
        protected abstract void Draw(Graphics g, Rectangle visible);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            hots.Clear();
            Draw(g, e.ClipRectangle);
        }

        /// Vuelve a calcular el alto (en el StackPanel) y a dibujar.
        public void Relayout()
        {
            Parent?.PerformLayout();
            Invalidate();
        }

        protected void Hot(RectangleF r, Action click, string tip = null, Action<Point> menu = null) =>
            hots.Add(new Hot { Rect = r, Click = click, Tip = tip, Menu = menu });

        /// Zona con el ratón encima (para resaltarla al dibujar).
        protected bool Hovered(RectangleF r)
        {
            var p = PointToClient(Cursor.Position);
            return ClientRectangle.Contains(p) && r.Contains(p);
        }

        int Find(Point p)
        {
            for (int i = hots.Count - 1; i >= 0; i--) if (hots[i].Rect.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = Find(e.Location);
            Cursor = i >= 0 && (hots[i].Click != null || hots[i].Menu != null) ? Cursors.Hand : Cursors.Default;
            string text = i >= 0 ? hots[i].Tip : null;
            if (text != shownTip) { shownTip = text; tip.SetToolTip(this, text); }
            if (i != hover) { hover = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = Find(e.Location);
            if (i < 0) return;
            var hot = hots[i];
            if (e.Button == MouseButtons.Right) hot.Menu?.Invoke(Cursor.Position);
            else if (hot.Click != null) hot.Click();
            else hot.Menu?.Invoke(Cursor.Position);
        }

        // ---------- Dibujo ----------

        protected float S(float v) => v * K;

        protected static void Write(Graphics g, string text, Font font, Color color, RectangleF r,
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding)
        {
            TextRenderer.DrawText(g, text ?? "", font, Rectangle.Round(r), color, flags | TextFormatFlags.NoPrefix);
        }

        protected static void TextAt(Graphics g, string text, Font font, Color color, float x, float y) =>
            TextRenderer.DrawText(g, text ?? "", font, new Point((int)x, (int)y), color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        protected static Size Measure(string text, Font font) =>
            TextRenderer.MeasureText(text ?? "", font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        protected static int MeasureWrapped(string text, Font font, int width) =>
            TextRenderer.MeasureText(text ?? "", font, new Size(Math.Max(1, width), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;

        protected void Card(Graphics g, RectangleF r, Color? fill = null, Color? edge = null, float radius = 12)
        {
            Fill(g, r, S(radius), fill ?? Palette.Card);
            Stroke(g, r, S(radius), edge ?? Palette.Edge);
        }

        /// Cápsula de texto pequeña ("ÍCONO", "MUY RARA"…); devuelve su ancho.
        protected float Chip(Graphics g, string text, Color tint, float x, float y)
        {
            var font = Fonts.Bold(6.75f);
            var size = Measure(text, font);
            var r = new RectangleF(x, y, size.Width + S(12), size.Height + S(4));
            Fill(g, r, r.Height / 2, Color.FromArgb(46, tint));
            Write(g, text, font, tint, new RectangleF(r.X + S(6), r.Y + S(2), size.Width + 2, size.Height));
            return r.Width;
        }

        protected void Bar(Graphics g, RectangleF r, double value, Color color)
        {
            Fill(g, r, r.Height / 2, Color.FromArgb(31, 255, 255, 255));
            float w = (float)(r.Width * Math.Max(0, Math.Min(1, value)));
            if (w > 0) Fill(g, new RectangleF(r.X, r.Y, Math.Max(w, r.Height), r.Height), r.Height / 2, color);
        }

        /// Imagen pixel-art dentro de un recuadro, centrada y sin pasar de su tamaño × scale.
        protected void Sprite(Graphics g, Image image, RectangleF box, float maxScale = 1)
        {
            if (image == null) return;
            float s = Math.Min(maxScale * K, Math.Min(box.Width / image.Width, box.Height / image.Height));
            float w = image.Width * s, h = image.Height * s;
            Resources.DrawPixelArt(g, image, new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h));
        }

        /// Botón dibujado (con resaltado al pasar el ratón); devuelve su ancho.
        protected float Button(Graphics g, string text, float x, float y, Action click, Color? fill = null, Color? fore = null, float height = 28, string tip = null)
        {
            var font = Fonts.Get(9f);
            var size = Measure(text, font);
            var r = new RectangleF(x, y, size.Width + S(20), S(height));
            Fill(g, r, S(7), fill ?? Color.FromArgb(18, 255, 255, 255));
            if (Hovered(r)) Fill(g, r, S(7), Color.FromArgb(22, 255, 255, 255));
            Stroke(g, r, S(7), Color.FromArgb(20, 255, 255, 255));
            CenterText(g, text, font, fore ?? Color.White, r);
            Hot(r, click, tip);
            return r.Width;
        }

        /// Ancho de un botón de Button().
        protected float ButtonWidth(string text) => Measure(text, Fonts.Get(9f)).Width + S(20);

        /// Cápsulas de opciones (Todas / Single / Group…); devuelve el ancho total.
        protected float Pills(Graphics g, string[] options, int selected, float x, float y, Action<int> pick)
        {
            float start = x;
            var font = Fonts.Semi(8.75f);
            for (int i = 0; i < options.Length; i++)
            {
                var size = Measure(options[i], font);
                var r = new RectangleF(x, y, size.Width + S(24), S(26));
                Fill(g, r, r.Height / 2, i == selected ? Theme.Blue : Color.FromArgb(16, 255, 255, 255));
                if (i != selected && Hovered(r)) Fill(g, r, r.Height / 2, Color.FromArgb(18, 255, 255, 255));
                CenterText(g, options[i], font, i == selected ? Color.White : Color.FromArgb(200, 200, 205), r);
                int index = i;
                Hot(r, () => pick(index));
                x += r.Width + S(4);
            }
            return x - start;
        }

        /// Título de sección en mayúsculas, gris.
        protected void Label(Graphics g, string text, float x, float y) =>
            TextAt(g, text.ToUpperInvariant(), Fonts.Semi(7.5f), Theme.Muted, x, y);

        protected static string N(long value) => value.ToString("N0", Format.Spanish);
    }

    static class Format
    {
        public static readonly System.Globalization.CultureInfo Spanish = new System.Globalization.CultureInfo("es-ES");

        /// "hace 5 minutos".
        public static string Relative(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalSeconds < 60) return "ahora mismo";
            if (span.TotalMinutes < 60) return Plural((int)span.TotalMinutes, "minuto");
            if (span.TotalHours < 24) return Plural((int)span.TotalHours, "hora");
            if (span.TotalDays < 30) return Plural((int)span.TotalDays, "día");
            if (span.TotalDays < 365) return Plural((int)(span.TotalDays / 30), "mes", "meses");
            return Plural((int)(span.TotalDays / 365), "año");
        }

        static string Plural(int n, string one, string many = null) => "hace " + n + " " + (n == 1 ? one : many ?? one + "s");

        public static string Long(DateTime utc, bool time = false) =>
            utc.ToLocalTime().ToString(time ? "d 'de' MMMM yyyy, HH:mm" : "d 'de' MMMM yyyy", Spanish);

        public static string Short(DateTime date) => date.ToString("d MMM", Spanish);

        /// "54,5 mil M", "394,1 M": experiencia abreviada.
        public static string Compact(long value)
        {
            double v = Math.Abs((double)value);
            string sign = value < 0 ? "-" : "";
            if (v >= 1e9) return sign + (v / 1e9).ToString("0.#", Spanish) + " mil M";
            if (v >= 1e6) return sign + (v / 1e6).ToString("0.#", Spanish) + " M";
            if (v >= 1e3) return sign + (v / 1e3).ToString("0.#", Spanish) + " mil";
            return sign + v.ToString("0", Spanish);
        }
    }

    /// Campo de texto oscuro alineado a la izquierda, con texto de ayuda y aviso en cada cambio.
    sealed class InputField : PaintedControl
    {
        readonly TextBox box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Color.White };
        readonly string cue;

        public event Action<string> Changed;
        public event Action Submitted;

        public InputField(string cue, string text = "")
        {
            this.cue = cue;
            Size = new Size(200, 28);
            box.Text = text;
            box.AccessibleName = cue;
            box.TextChanged += (s, e) => Changed?.Invoke(box.Text);
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submitted?.Invoke(); } };
            box.HandleCreated += (s, e) => SendMessage(box.Handle, 0x1501, (IntPtr)1, cue);  // EM_SETCUEBANNER
            Controls.Add(box);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public string Value { get => box.Text; set { if (box.Text != value) box.Text = value; } }
        public Font TextFont { set { box.Font = value; PerformLayout(); } }
        public Color FieldColor { get; set; } = Theme.Field;
        public bool Borderless { get; set; }
        public void FocusText() { box.Focus(); box.SelectionStart = box.TextLength; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Borderless ? 2 : (int)(8 * K);
            box.BackColor = FieldColor;
            box.SetBounds(pad, (Height - box.PreferredHeight) / 2, Width - pad * 2, box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            if (Borderless) return;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, r, 6 * K, FieldColor);
            Stroke(g, r, 6 * K, Color.FromArgb(box.Focused ? 90 : 40, 255, 255, 255));
        }
    }

    /// Ventana emergente sin marco (como un popover de macOS): se cierra al hacer clic fuera.
    sealed class Popover : Form
    {
        public Popover(Control content, Size size)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Row;
            ForeColor = Theme.Fore;
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = size;
            Padding = new Padding(1);
            content.Dock = DockStyle.Fill;
            Controls.Add(content);
            KeyPreview = true;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            BeginInvoke((Action)Close);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(80, 80, 86))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkTitleBar(Handle);
        }

        /// La abre junto al punto (en pantalla), sin salirse de la pantalla.
        public static Popover Open(Control owner, Point screen, Control content, Size size)
        {
            var pop = new Popover(content, size);
            var area = Screen.FromPoint(screen).WorkingArea;
            int x = Math.Min(screen.X + 8, area.Right - pop.Width - 4), y = Math.Min(screen.Y + 8, area.Bottom - pop.Height - 4);
            pop.Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
            pop.Show(owner.FindForm());
            return pop;
        }
    }

    /// Lista de opciones con imagen de TibiaWiki y buscador (items, criaturas, zonas…).
    sealed class PickerList : PaintedControl
    {
        readonly InputField search;
        readonly ListBox list = new ListBox
        {
            BorderStyle = BorderStyle.None, BackColor = Theme.Row, ForeColor = Color.White,
            DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false,
        };
        readonly Func<string, IEnumerable<(string Name, string Detail, string Image)>> source;
        List<(string Name, string Detail, string Image)> shown = new List<(string, string, string)>();

        public event Action<string> Picked;

        public PickerList(string cue, Func<string, IEnumerable<(string Name, string Detail, string Image)>> source)
        {
            this.source = source;
            BackColor = Theme.Row;
            search = new InputField(cue);
            search.Changed += _ => Fill();
            search.Submitted += () => { if (shown.Count > 0) Picked?.Invoke(shown[0].Name); };
            list.ItemHeight = (int)(30 * K);
            list.DrawItem += DrawItem;
            list.MouseClick += (s, e) =>
            {
                int i = list.IndexFromPoint(e.Location);
                if (i >= 0 && i < shown.Count) Picked?.Invoke(shown[i].Name);
            };
            Controls.Add(search);
            Controls.Add(list);
            WikiImages.Loaded += Repaint;
            Fill();
        }

        void Repaint() { if (!IsDisposed) list.Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) WikiImages.Loaded -= Repaint;
            base.Dispose(disposing);
        }

        public string Query => search.Value.Trim();
        public void FocusSearch() => search.FocusText();

        void Fill()
        {
            shown = new List<(string, string, string)>(source(search.Value.Trim()));
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var entry in shown) list.Items.Add(entry.Name);
            list.EndUpdate();
        }

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= shown.Count) return;
            var entry = shown[e.Index];
            var g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(selected ? Color.FromArgb(50, Theme.Blue) : Theme.Row)) g.FillRectangle(back, e.Bounds);
            float k = DeviceDpi / 96f;
            var image = entry.Image == null ? null : WikiImages.Get(entry.Image);
            if (image != null)
            {
                float side = 24 * k, s = Math.Min(side / image.Width, side / image.Height);
                Resources.DrawPixelArt(g, image, new RectangleF(e.Bounds.X + 6 * k + (side - image.Width * s) / 2,
                    e.Bounds.Y + (e.Bounds.Height - image.Height * s) / 2, image.Width * s, image.Height * s));
            }
            var font = Fonts.Get(9f);
            TextRenderer.DrawText(g, entry.Name, font, new Rectangle(e.Bounds.X + (int)(36 * k), e.Bounds.Y, e.Bounds.Width - (int)(110 * k), e.Bounds.Height),
                Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (!string.IsNullOrEmpty(entry.Detail))
                TextRenderer.DrawText(g, entry.Detail, Fonts.Get(8f), new Rectangle(e.Bounds.Right - (int)(100 * k), e.Bounds.Y, (int)(94 * k), e.Bounds.Height),
                    Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int h = (int)(30 * K);
            search.SetBounds(0, 0, Width, h);
            list.SetBounds(0, h + (int)(6 * K), Width, Height - h - (int)(6 * K));
        }

        protected override void OnPaint(PaintEventArgs e) => Begin(e);
    }
}

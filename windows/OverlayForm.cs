using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Dibuja el contador tal como se ve sobre Tibia. Lo usan el overlay y la vista previa del hub.
    static class CounterRenderer
    {
        public static Font MakeFont(float zoom) => new Font("Consolas", 28f * zoom, FontStyle.Bold, GraphicsUnit.Pixel);

        public static Font MakeLabelFont(float zoom) => new Font("Segoe UI Semibold", 12f * zoom, GraphicsUnit.Pixel);

        static SizeF MeasureText(string text, Font font)
        {
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
                return g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        }

        /// Tamaño de un contador de 5 caracteres ("00:00"), para la vista previa.
        public static Size Measure(float zoom) => Measure(zoom, "00:00", null);

        public static Size Measure(float zoom, string text, string label)
        {
            using (var font = MakeFont(zoom))
            using (var labelFont = MakeLabelFont(zoom))
            {
                SizeF t = MeasureText(text, font);
                float w = t.Width + 24 * zoom;
                if (label != null) w += MeasureText(label, labelFont).Width + 8 * zoom;
                return new Size((int)Math.Ceiling(w), (int)Math.Ceiling(t.Height + 8 * zoom));
            }
        }

        public static Bitmap Draw(Model model, float zoom, string text, bool critical, bool running, bool unlocked, string label = null)
        {
            Size size = Measure(zoom, text, label);
            bool alert = critical && running;
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var font = MakeFont(zoom))
            using (var labelFont = MakeLabelFont(zoom))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                // Sin fondo (o casi), un contorno oscuro mantiene el número legible sobre el juego.
                bool bare = model.BackgroundColor.A < 77 && !unlocked;
                Color background = unlocked && bare ? Color.FromArgb(153, 0, 0, 0) : model.BackgroundColor;
                var rect = new RectangleF(0.5f, 0.5f, size.Width - 1, size.Height - 1);
                using (var shape = RoundedRect(rect, 9 * zoom))
                {
                    using (var brush = new SolidBrush(background)) g.FillPath(brush, shape);
                    if (unlocked)
                        using (var pen = new Pen(Theme.Amber, 1.5f * zoom) { DashPattern = new[] { 3f, 2f } })
                            g.DrawPath(pen, shape);
                    else if (alert && !bare)
                        using (var pen = new Pen(Color.FromArgb(153, model.AlertColor), 1.5f * zoom))
                            g.DrawPath(pen, shape);
                }

                float x = 12 * zoom;
                int alpha = running ? 255 : 153;
                using (var path = new GraphicsPath())
                {
                    if (label != null)
                    {
                        SizeF l = MeasureText(label, labelFont);
                        float baseline = 4 * zoom + MeasureText(text, font).Height * 0.62f - l.Height * 0.62f;
                        path.AddString(label, labelFont.FontFamily, (int)FontStyle.Bold, labelFont.Size,
                            new PointF(x, baseline), StringFormat.GenericTypographic);
                        if (bare)
                            using (var outline = new Pen(Color.FromArgb(210, 0, 0, 0), 2.5f * zoom) { LineJoin = LineJoin.Round })
                                g.DrawPath(outline, path);
                        using (var brush = new SolidBrush(Color.FromArgb(alpha * 220 / 255, Color.White))) g.FillPath(brush, path);
                        x += l.Width + 8 * zoom;
                        path.Reset();
                    }
                    Color color = critical ? model.AlertColor : model.CounterColor;
                    path.AddString(text, font.FontFamily, (int)FontStyle.Bold, font.Size, new PointF(x, 4 * zoom), StringFormat.GenericTypographic);
                    if (bare)
                        using (var outline = new Pen(Color.FromArgb(210, 0, 0, 0), 3f * zoom) { LineJoin = LineJoin.Round })
                            g.DrawPath(outline, path);
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, color))) g.FillPath(brush, path);
                }
            }
            return bmp;
        }

        /// Atenuación (0,55–1) que hace parpadear el contador durante la alerta.
        public static double Pulse(bool alert) =>
            alert ? 0.55 + 0.45 * Math.Abs(Math.Cos(Environment.TickCount / 1000.0 * Math.PI)) : 1;

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// Ventana del contador: transparente por píxel, siempre encima, sin foco y sin clics
    /// (los clics la atraviesan y llegan a Tibia) salvo mientras está desbloqueada para moverla.
    sealed class OverlayForm : Form
    {
        readonly Model model;
        bool unlocked;
        string lastSignature;
        bool shown;  // propio: la ventana se muestra/oculta con ShowWindow para no robar el foco

        public event Action<Point> MovedByUser;

        public OverlayForm(Model model)
        {
            this.model = model;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Alfa Hub – contador";
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE
                              | Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        float Zoom => (float)(model.OverlayScale * DeviceDpi / 96.0);

        /// Tamaño actual del overlay (todos los contadores visibles).
        public Size MeasureSize()
        {
            using (var bmp = Compose(out _)) return bmp.Size;
        }

        public void SetUnlocked(bool value)
        {
            unlocked = value;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            ex = value ? ex & ~Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
            Render(force: true);
        }

        /// Muestra sin quitar el foco y lo vuelve a poner encima (Tibia puede taparlo al recuperar el foco).
        public void ShowOnTop()
        {
            if (!shown)
            {
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                shown = true;
            }
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Render();
        }

        public void HideOverlay()
        {
            if (!shown) return;
            Native.ShowWindow(Handle, Native.SW_HIDE);
            shown = false;
        }

        protected override void WndProc(ref Message m)
        {
            // Desbloqueado: arrastrar desde cualquier punto mueve la ventana.
            if (m.Msg == Native.WM_NCHITTEST && unlocked)
            {
                m.Result = (IntPtr)Native.HTCAPTION;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == Native.WM_EXITSIZEMOVE) MovedByUser?.Invoke(Location);
        }

        public void Render(bool force = false)
        {
            if (!IsHandleCreated) return;
            var runs = model.OverlayRuns;
            bool anyAlert = runs.Any(r => r.Display.Critical && r.Running);
            string signature = string.Join("|", runs.Select(r => r.Id + r.Display.Signature + r.Running + r.Config.Name))
                + string.Join("|", unlocked, model.OverlayScale, model.OverlayOpacity, model.CounterColor.ToArgb(),
                    model.AlertColor.ToArgb(), model.BackgroundColor.ToArgb());
            if (!force && !anyAlert && signature == lastSignature) return;
            lastSignature = signature;

            using (var bmp = Compose(out _))
                Push(bmp, (byte)Math.Round(255 * model.OverlayOpacity));
        }

        /// Dibuja los contadores de los timers visibles, uno debajo de otro y alineados a la derecha.
        /// Los que están en alerta parpadean (cada uno por separado).
        Bitmap Compose(out int count)
        {
            var runs = model.OverlayRuns;
            count = runs.Count;
            bool showNames = runs.Count > 1;
            float zoom = Zoom;
            var badges = runs.Select(r => new
            {
                Bitmap = CounterRenderer.Draw(model, zoom, r.Display.Text, r.Display.Critical, r.Running, unlocked,
                    showNames ? r.Config.Name : null),
                Pulse = (float)CounterRenderer.Pulse(r.Display.Critical && r.Running),
            }).ToList();
            int gap = (int)(4 * zoom);
            int width = badges.Max(b => b.Bitmap.Width);
            int height = badges.Sum(b => b.Bitmap.Height) + gap * (badges.Count - 1);
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                int y = 0;
                foreach (var b in badges)
                {
                    using (var attributes = new ImageAttributes())
                    {
                        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = b.Pulse });
                        g.DrawImage(b.Bitmap, new Rectangle(width - b.Bitmap.Width, y, b.Bitmap.Width, b.Bitmap.Height),
                            0, 0, b.Bitmap.Width, b.Bitmap.Height, GraphicsUnit.Pixel, attributes);
                    }
                    y += b.Bitmap.Height + gap;
                    b.Bitmap.Dispose();
                }
            }
            return result;
        }

        void Push(Bitmap bmp, byte alpha)
        {
            if (Size != bmp.Size)
            {
                // Crece o encoge hacia la izquierda: el borde derecho queda fijo (suele estar en la esquina).
                int right = Left + Width;
                Size = bmp.Size;
                if (right > 0) Left = right - bmp.Width;
            }
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memDc, hBitmap);
            try
            {
                var dst = new Native.POINT(Left, Top);
                var size = new Native.SIZE(bmp.Width, bmp.Height);
                var src = new Native.POINT(0, 0);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = Native.AC_SRC_ALPHA,
                };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(hBitmap);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }
}

using System;
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

        public static Size Measure(float zoom)
        {
            using (var font = MakeFont(zoom))
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                SizeF text = g.MeasureString("00:00", font, PointF.Empty, StringFormat.GenericTypographic);
                return new Size((int)Math.Ceiling(text.Width + 24 * zoom), (int)Math.Ceiling(text.Height + 8 * zoom));
            }
        }

        public static Bitmap Draw(Model model, float zoom, string text, bool critical, bool running, bool unlocked)
        {
            Size size = Measure(zoom);
            bool alert = critical && running;
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var font = MakeFont(zoom))
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

                Color color = critical ? model.AlertColor : model.CounterColor;
                if (!running) color = Color.FromArgb(153, color);
                using (var path = new GraphicsPath())
                {
                    path.AddString(text, font.FontFamily, (int)FontStyle.Bold, font.Size,
                        new PointF(12 * zoom, 4 * zoom), StringFormat.GenericTypographic);
                    if (bare)
                        using (var outline = new Pen(Color.FromArgb(210, 0, 0, 0), 3f * zoom) { LineJoin = LineJoin.Round })
                            g.DrawPath(outline, path);
                    using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
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

        public Size MeasureSize() => CounterRenderer.Measure(Zoom);

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
            bool alert = model.Critical && model.Running;
            string text = Model.Mmss(model.Remaining);
            string signature = string.Join("|", text, model.Critical, model.Running, unlocked, model.OverlayScale,
                model.OverlayOpacity, model.CounterColor.ToArgb(), model.AlertColor.ToArgb(), model.BackgroundColor.ToArgb());
            if (!force && !alert && signature == lastSignature) return;
            lastSignature = signature;

            using (var bmp = CounterRenderer.Draw(model, Zoom, text, model.Critical, model.Running, unlocked))
                Push(bmp, (byte)Math.Round(255 * model.OverlayOpacity * CounterRenderer.Pulse(alert)));
        }

        void Push(Bitmap bmp, byte alpha)
        {
            if (Size != bmp.Size) Size = bmp.Size;
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

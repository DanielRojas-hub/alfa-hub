using System;
using System.Collections.Generic;
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

        public static Bitmap Draw(OverlayStyle style, float zoom, string text, bool critical, bool running, bool unlocked, string label = null)
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
                Color styleBackground = style.Background;
                bool bare = styleBackground.A < 77 && !unlocked;
                Color background = unlocked && bare ? Color.FromArgb(153, 0, 0, 0) : styleBackground;
                var rect = new RectangleF(0.5f, 0.5f, size.Width - 1, size.Height - 1);
                using (var shape = RoundedRect(rect, 9 * zoom))
                {
                    using (var brush = new SolidBrush(background)) g.FillPath(brush, shape);
                    if (unlocked)
                        using (var pen = new Pen(Theme.Amber, 1.5f * zoom) { DashPattern = new[] { 3f, 2f } })
                            g.DrawPath(pen, shape);
                    else if (alert && !bare)
                        using (var pen = new Pen(Color.FromArgb(153, style.Alert), 1.5f * zoom))
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
                    Color color = critical ? style.Alert : style.Counter;
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

    /// Ventana del contador de un timer: transparente por píxel, siempre encima, sin foco y sin clics
    /// (los clics la atraviesan y llegan a Tibia) salvo mientras está desbloqueada para moverla.
    sealed class OverlayForm : Form
    {
        readonly TimerRun run;
        bool unlocked;
        string lastSignature;
        bool shown;  // propio: la ventana se muestra/oculta con ShowWindow para no robar el foco

        /// Con más de un contador a la vista, cada uno lleva su nombre.
        public bool ShowName;
        /// El usuario la está arrastrando (el gestor no la recoloca mientras tanto).
        public bool Moving { get; private set; }
        public event Action MovedByUser;

        public OverlayForm(TimerRun run)
        {
            this.run = run;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Alfa Hub – " + run.Config.Name;
        }

        public TimerRun Run => run;

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

        float Zoom => (float)(run.Config.Overlay.Scale * DeviceDpi / 96.0);

        /// Tamaño del contador tal como se ve ahora.
        public Size MeasureSize() =>
            CounterRenderer.Measure(Zoom, run.Display.Text, ShowName ? run.Config.Name : null);

        public void SetUnlocked(bool value)
        {
            if (unlocked == value) return;
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
            if (m.Msg == Native.WM_ENTERSIZEMOVE) Moving = true;
            base.WndProc(ref m);
            if (m.Msg == Native.WM_EXITSIZEMOVE)
            {
                Moving = false;
                MovedByUser?.Invoke();
            }
        }

        public void Render(bool force = false)
        {
            if (!IsHandleCreated) return;
            var style = run.Config.Overlay;
            bool alert = run.Display.Critical && run.Running;
            string signature = string.Join("|", run.Display.Signature, run.Running, run.Config.Name, ShowName, unlocked,
                style.Scale, style.Opacity, style.CounterColor, style.AlertColor, style.BackgroundColor, Left, Top);
            if (!force && !alert && signature == lastSignature) return;
            lastSignature = signature;

            using (var badge = CounterRenderer.Draw(style, Zoom, run.Display.Text, run.Display.Critical, run.Running, unlocked,
                       ShowName ? run.Config.Name : null))
            {
                // Los que están en alerta parpadean.
                byte alpha = (byte)Math.Round(255 * style.Opacity * CounterRenderer.Pulse(alert));
                Push(badge, alpha);
            }
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

    /// Una ventana de overlay por timer: las muestra solo con Tibia al frente (o desbloqueadas), las
    /// coloca en su sitio (o apiladas arriba a la derecha si no tienen uno) y guarda el sitio al arrastrarlas.
    sealed class OverlayManager
    {
        readonly Model model;
        readonly Dictionary<string, OverlayForm> forms = new Dictionary<string, OverlayForm>();

        public event Action Moved;

        public OverlayManager(Model model) { this.model = model; }

        public void Sync(bool tibiaInFront)
        {
            foreach (var id in forms.Keys.ToList())
            {
                if (model.Find(id) != null) continue;
                forms[id].Close();
                forms.Remove(id);
            }
            bool visible = model.OverlayVisible && (model.OverlayUnlocked || tibiaInFront);
            var shown = visible ? model.OverlayRuns : new List<TimerRun>();
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int anchorRight = area.Right - 20, autoTop = area.Top + 20;
            foreach (var run in model.Runs)
            {
                forms.TryGetValue(run.Id, out OverlayForm form);
                if (!shown.Contains(run))
                {
                    form?.HideOverlay();
                    continue;
                }
                if (form == null) form = Make(run);
                form.ShowName = shown.Count > 1;
                form.SetUnlocked(model.OverlayUnlocked);
                var style = run.Config.Overlay;
                Size size = form.MeasureSize();
                Point place = style.HasPosition
                    ? new Point(style.Right - size.Width, style.Top)
                    : new Point(anchorRight - size.Width, autoTop);
                if (!style.HasPosition) autoTop += size.Height + (int)(4 * style.Scale * form.DeviceDpi / 96.0);
                if (!form.Moving && form.Location != place) form.Location = place;
                form.ShowOnTop();
            }
        }

        OverlayForm Make(TimerRun run)
        {
            var form = new OverlayForm(run);
            var _ = form.Handle;
            form.MovedByUser += () =>
            {
                var style = run.Config.Overlay;
                style.HasPosition = true;
                style.Right = form.Right;
                style.Top = form.Top;
                Moved?.Invoke();
            };
            forms[run.Id] = form;
            return form;
        }

        public void RenderAll() { foreach (var form in forms.Values) form.Render(force: true); }

        public void CloseAll()
        {
            foreach (var form in forms.Values) form.Close();
            forms.Clear();
        }
    }
}

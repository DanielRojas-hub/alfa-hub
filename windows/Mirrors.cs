using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Localiza la ventana del cliente de Tibia sin abrir su proceso: recorre las ventanas visibles
    /// (EnumWindows), mira el título y el nombre del proceso en la lista de procesos del sistema.
    static class TibiaWindow
    {
        /// Solo para pruebas automáticas: ALFAHUB_TEST_WINDOW=Notepad hace que esa ventana cuente como Tibia.
        static readonly string TestTitle = Environment.GetEnvironmentVariable("ALFAHUB_TEST_WINDOW");

        static string Title(IntPtr hWnd)
        {
            var text = new StringBuilder(256);
            Native.GetWindowText(hWnd, text, text.Capacity);
            return text.ToString();
        }

        public static bool Matches(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            string title = Title(hWnd);
            if (!string.IsNullOrEmpty(TestTitle)) return title.IndexOf(TestTitle, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!title.StartsWith("Tibia", StringComparison.OrdinalIgnoreCase)) return false;
            Native.GetWindowThreadProcessId(hWnd, out uint pid);
            try
            {
                using (var process = System.Diagnostics.Process.GetProcessById((int)pid))
                    return string.Equals(process.ProcessName, "client", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }  // el proceso ya terminó
        }

        public static IntPtr Find()
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hWnd, _) =>
            {
                if (!Native.IsWindowVisible(hWnd) || !Matches(hWnd)) return true;
                found = hWnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        public static bool InFront()
        {
            IntPtr window = Native.GetForegroundWindow();
            return window != IntPtr.Zero && !Native.IsIconic(window) && Matches(window);
        }

        /// Área de cliente de la ventana, en coordenadas de pantalla.
        public static Rectangle ClientScreenRect(IntPtr hWnd)
        {
            Native.GetClientRect(hWnd, out Native.RECT r);
            var origin = new Native.POINT(0, 0);
            Native.ClientToScreen(hWnd, ref origin);
            return new Rectangle(origin.X, origin.Y, r.Right - r.Left, r.Bottom - r.Top);
        }
    }

    /// Ventana de un espejo: el compositor de Windows dibuja dentro una región de la ventana de Tibia
    /// (miniatura de DWM). Siempre encima, sin foco y sin clics salvo desbloqueada.
    sealed class MirrorForm : Form
    {
        public readonly MirrorConfig Config;
        IntPtr thumbnail, source;
        bool unlocked, shown;
        string lastLayout;

        public event Action Moved;

        public MirrorForm(MirrorConfig config)
        {
            Config = config;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(20, 20, 22);
            Text = "Alfa Hub – espejo";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
            Bounds = new Rectangle(config.Left, config.Top, config.DisplaySize.Width, config.DisplaySize.Height);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // Capa + transparente: los clics atraviesan el espejo y llegan a Tibia.
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST
                              | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetLayeredWindowAttributes(Handle, 0, 255, Native.LWA_ALPHA);
        }

        public void SetUnlocked(bool value)
        {
            if (unlocked == value) return;
            unlocked = value;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            ex = value ? ex & ~Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
            lastLayout = null;
            Invalidate();
        }

        /// Conecta la miniatura a la ventana de Tibia (o la quita si source es cero).
        public void Attach(IntPtr window)
        {
            if (window == source) return;
            Detach();
            source = window;
            if (window != IntPtr.Zero && Native.DwmRegisterThumbnail(Handle, window, out thumbnail) != 0) thumbnail = IntPtr.Zero;
            lastLayout = null;
            Invalidate();
        }

        void Detach()
        {
            if (thumbnail != IntPtr.Zero) Native.DwmUnregisterThumbnail(thumbnail);
            thumbnail = IntPtr.Zero;
            source = IntPtr.Zero;
        }

        int BorderWidth => unlocked ? 2 : Config.Border ? 1 : 0;

        /// Aplica tamaño, posición, región y opacidad (solo si algo cambió).
        public void ApplyLayout()
        {
            var c = Config;
            string layout = string.Join(",", c.X, c.Y, c.Width, c.Height, c.Zoom, c.Opacity, c.Border, c.Left, c.Top, unlocked, thumbnail);
            if (layout == lastLayout) return;
            lastLayout = layout;

            int b = BorderWidth;
            Size inner = c.DisplaySize;
            var bounds = new Rectangle(c.Left, c.Top, inner.Width + b * 2, inner.Height + b * 2);
            if (Bounds != bounds) Bounds = bounds;
            if (thumbnail != IntPtr.Zero)
            {
                var props = new Native.DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_RECTSOURCE | Native.DWM_TNP_OPACITY
                              | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_SOURCECLIENTAREAONLY,
                    rcDestination = new Native.RECT(b, b, b + inner.Width, b + inner.Height),
                    rcSource = new Native.RECT(c.X, c.Y, c.X + c.Width, c.Y + c.Height),
                    opacity = (byte)Math.Round(255 * Model.Clamp(c.Opacity, 10, 100) / 100.0),
                    fVisible = true,
                    fSourceClientAreaOnly = true,
                };
                Native.DwmUpdateThumbnailProperties(thumbnail, ref props);
            }
            Invalidate();
        }

        public void ShowOnTop()
        {
            if (!shown)
            {
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                shown = true;
            }
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void HideMirror()
        {
            if (!shown) return;
            Native.ShowWindow(Handle, Native.SW_HIDE);
            shown = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (thumbnail == IntPtr.Zero)
                TextRenderer.DrawText(g, Config.Name + "\nTibia cerrado", Font, ClientRectangle, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            if (unlocked)
                using (var pen = new Pen(Theme.Amber, 2) { DashPattern = new[] { 3f, 2f } })
                    g.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
            else if (Config.Border)
                using (var pen = new Pen(Color.FromArgb(90, 90, 96)))
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void WndProc(ref Message m)
        {
            // Desbloqueado: arrastrar desde cualquier punto lo mueve.
            if (m.Msg == Native.WM_NCHITTEST && unlocked)
            {
                m.Result = (IntPtr)Native.HTCAPTION;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == Native.WM_EXITSIZEMOVE)
            {
                Config.Left = Left;
                Config.Top = Top;
                lastLayout = null;
                Moved?.Invoke();
            }
        }

        protected override void Dispose(bool disposing)
        {
            Detach();
            base.Dispose(disposing);
        }
    }

    /// Crea, quita, muestra y oculta las ventanas de los espejos según el modelo.
    sealed class MirrorManager
    {
        readonly Model model;
        readonly Dictionary<string, MirrorForm> forms = new Dictionary<string, MirrorForm>();

        public event Action Moved;

        public MirrorManager(Model model) { this.model = model; }

        /// tibia: ventana de Tibia (o cero); inFront: si Tibia está en primer plano.
        public void Sync(IntPtr tibia, bool inFront)
        {
            foreach (var id in forms.Keys.Where(id => model.Mirrors.All(m => m.Id != id)).ToList())
            {
                forms[id].Dispose();
                forms.Remove(id);
            }
            foreach (var config in model.Mirrors)
            {
                if (!forms.TryGetValue(config.Id, out MirrorForm form))
                {
                    form = new MirrorForm(config);
                    var _ = form.Handle;
                    form.Moved += () => Moved?.Invoke();
                    forms[config.Id] = form;
                }
                form.SetUnlocked(model.MirrorsUnlocked);
                bool show = model.MirrorsVisible && config.Visible
                            && (model.MirrorsUnlocked || (tibia != IntPtr.Zero && inFront));
                if (show)
                {
                    form.Attach(tibia);
                    form.ApplyLayout();
                    form.ShowOnTop();
                }
                else form.HideMirror();
            }
        }

        public void CloseAll()
        {
            foreach (var form in forms.Values) form.Dispose();
            forms.Clear();
        }
    }

    /// Capa oscura sobre la ventana de Tibia para elegir la región de un espejo arrastrando.
    sealed class SelectionForm : Form
    {
        Point start;
        Rectangle selection;
        bool dragging;

        /// Región elegida, en coordenadas del área de cliente de la ventana de Tibia.
        public Rectangle Result { get; private set; }

        public SelectionForm(Rectangle clientScreenRect)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = clientScreenRect;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Opacity = 0.55;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            Text = "Alfa Hub – elegir región";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            start = e.Location;
            selection = new Rectangle(e.Location, Size.Empty);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            selection = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
            selection.Intersect(ClientRectangle);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            if (selection.Width < 8 || selection.Height < 8) { Invalidate(); return; }
            Result = selection;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            const string hint = "Arrastra para elegir la región del espejo  ·  Esc para cancelar";
            using (var font = new Font("Segoe UI Semibold", 12f))
                TextRenderer.DrawText(g, hint, font, new Rectangle(0, 16, Width, 30), Color.White, TextFormatFlags.HorizontalCenter);
            if (selection.Width > 0 && selection.Height > 0)
            {
                using (var fill = new SolidBrush(Color.FromArgb(255, 70, 70, 70))) g.FillRectangle(fill, selection);
                using (var pen = new Pen(Theme.Blue, 2)) g.DrawRectangle(pen, selection);
                string size = $"{selection.Width} × {selection.Height}";
                TextRenderer.DrawText(g, size, Font, new Point(selection.X, Math.Max(0, selection.Y - 18)), Color.White);
            }
        }
    }
}

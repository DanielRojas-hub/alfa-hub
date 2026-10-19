using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AlfaHub
{
    /// Llamadas a la API de Windows que WinForms no expone.
    static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOPMOST = 0x8;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int WM_HOTKEY = 0x0312;
        public const int WM_NCHITTEST = 0x84;
        public const int WM_EXITSIZEMOVE = 0x232;
        public const int HTCAPTION = 2;

        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;

        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int Cx, Cy; public SIZE(int cx, int cy) { Cx = cx; Cy = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr dcDst, ref POINT dst, ref SIZE size,
            IntPtr dcSrc, ref POINT src, int colorKey, ref BLENDFUNCTION blend, int flags);

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        /// Barra de título oscura (Windows 10 20H1+ usa el atributo 20; versiones anteriores, el 19).
        public static void UseDarkTitleBar(IntPtr hWnd)
        {
            int on = 1;
            try
            {
                if (DwmSetWindowAttribute(hWnd, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hWnd, 19, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        /// Datos de la ventana en primer plano sin abrir su proceso: BattlEye protege el proceso de
        /// Tibia y vigila a quien le pide un handle, así que aquí solo se usa la lista de procesos
        /// del sistema (como el Administrador de tareas) y el título de la ventana.
        public static bool ForegroundWindowInfo(out string processName, out string title)
        {
            processName = null;
            title = null;
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero || IsIconic(window)) return false;
            GetWindowThreadProcessId(window, out uint pid);
            try
            {
                using (var process = System.Diagnostics.Process.GetProcessById((int)pid))
                    processName = process.ProcessName;
            }
            catch (ArgumentException) { return false; }  // el proceso ya terminó
            var text = new StringBuilder(256);
            GetWindowText(window, text, text.Capacity);
            title = text.ToString();
            return true;
        }
    }
}

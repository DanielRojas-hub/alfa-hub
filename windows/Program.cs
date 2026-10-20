// Alfa Hub para Windows — timer de ciclos para Tibia, basado en el "Peixe Timer" de
// cueistoreservices.com.br/timer. Misma app que la versión de macOS:
//  • Overlay: solo el contador, encima de Tibia y solo mientras el cliente de Tibia está en primer
//    plano. Los clics lo atraviesan y llegan al juego.
//  • Hub: controles del timer y todos los ajustes. Al cerrarlo queda en la bandeja del sistema.
//  • Atajos globales editables (por defecto Ctrl+Alt+P / R / O).

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AlfaHub
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            using (var mutex = new Mutex(true, "AlfaHub.SingleInstance", out bool firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Alfa Hub ya está abierto. Búscalo en la bandeja del sistema, junto al reloj.",
                        "Alfa Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new AlfaHubContext());
            }
        }
    }

    sealed class AlfaHubContext : ApplicationContext
    {
        readonly Model model = new Model();
        readonly HubForm hub;
        readonly OverlayForm overlay;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer ticker = new System.Windows.Forms.Timer { Interval = 50 };
        readonly MirrorManager mirrors;
        bool recordingShortcut;
        int tickCount;
        bool tibiaInFront;
        IntPtr tibiaWindow;

        public AlfaHubContext()
        {
            model.Load();

            hub = new HubForm(model) { Icon = Resources.AppIcon(new Size(32, 32)) };
            hub.Changed += () => { model.Save(); overlay.Render(force: true); ApplyUnlocked(); };
            hub.ShortcutsChanged += RegisterHotKeys;
            hub.RecordingShortcut += recording => { recordingShortcut = recording; RegisterHotKeys(); };
            hub.ResetOverlayPosition += PlaceOverlayTopRight;
            hub.HotKeyPressed += OnHotKey;
            hub.MirrorsChanged += () => { model.Save(); SyncMirrors(); };
            hub.CreateControl();
            var _ = hub.Handle;  // los atajos globales se registran en esta ventana

            overlay = new OverlayForm(model);
            var __ = overlay.Handle;
            overlay.Size = overlay.MeasureSize();
            if (model.OverlayPosition.HasValue && IsOnScreen(model.OverlayPosition.Value)) overlay.Location = model.OverlayPosition.Value;
            else PlaceOverlayTopRight();
            overlay.MovedByUser += p => { model.OverlayPosition = p; model.Save(); };

            mirrors = new MirrorManager(model);
            mirrors.Moved += () => model.Save();

            tray = new NotifyIcon { Icon = Resources.AppIcon(SystemInformation.SmallIconSize), Text = "Alfa Hub", Visible = true, ContextMenuStrip = TrayMenu() };
            tray.DoubleClick += (s, e) => ShowHub();

            RegisterHotKeys();
            ticker.Tick += (s, e) => Tick();
            ticker.Start();
            ShowHub();
        }

        ContextMenuStrip TrayMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir Alfa Hub", null, (s, e) => ShowHub());
            menu.Items.Add("Iniciar / pausar el timer elegido", null, (s, e) => { model.Toggle(model.Selected); hub.RefreshTimer(); });
            menu.Items.Add("Reiniciar el timer elegido", null, (s, e) => { model.Reset(model.Selected); hub.RefreshTimer(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (s, e) => Quit());
            return menu;
        }

        void Tick()
        {
            model.Tick();
            if (hub.Visible) hub.RefreshTimer();

            if (tickCount % 20 == 0 || (tibiaWindow != IntPtr.Zero && !Native.IsWindow(tibiaWindow)))
            {
                tibiaWindow = TibiaWindow.Find();  // cada segundo
                model.TibiaRunning = tibiaWindow != IntPtr.Zero;
            }
            if (tickCount % 5 == 0)  // cada 250 ms basta
            {
                tibiaInFront = TibiaWindow.InFront();
                SyncMirrors();
            }
            tickCount++;
            bool show = model.OverlayVisible && (model.OverlayUnlocked || tibiaInFront);
            if (show) overlay.ShowOnTop();
            else overlay.HideOverlay();
        }

        void SyncMirrors() => mirrors.Sync(tibiaWindow, tibiaInFront);

        // Ids de RegisterHotKey: 1–2 atajos globales (GlobalAction) y 100 + n el timer n.
        const int TimerHotKeyBase = 100;

        void OnHotKey(int id)
        {
            if (id >= TimerHotKeyBase)
            {
                int index = id - TimerHotKeyBase;
                if (index < model.Runs.Count) model.Toggle(model.Runs[index]);
            }
            else
            {
                model.Perform((GlobalAction)id);
                if ((GlobalAction)id == GlobalAction.Overlay)
                {
                    model.Save();
                    hub.OnOverlayVisibilityToggled();
                }
            }
            hub.RefreshTimer();
            overlay.Render(force: true);
        }

        void UnregisterAll()
        {
            foreach (GlobalAction action in Enum.GetValues(typeof(GlobalAction))) Native.UnregisterHotKey(hub.Handle, (int)action);
            for (int i = 0; i < Model.MaxTimers; i++) Native.UnregisterHotKey(hub.Handle, TimerHotKeyBase + i);
        }

        void RegisterHotKeys()
        {
            UnregisterAll();
            if (recordingShortcut) return;  // mientras se graba, cualquier combinación debe llegar al hub

            var bindings = new System.Collections.Generic.List<(int id, Keys keys)>();
            foreach (var pair in model.GlobalShortcuts) bindings.Add(((int)pair.Key, pair.Value));
            for (int i = 0; i < model.Runs.Count; i++) bindings.Add((TimerHotKeyBase + i, (Keys)model.Runs[i].Config.Shortcut));

            var failed = new System.Collections.Generic.List<string>();
            foreach (var (id, keys) in bindings)
            {
                if (keys == Keys.None) continue;
                uint mods = Native.MOD_NOREPEAT;
                if ((keys & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
                if ((keys & Keys.Alt) != 0) mods |= Native.MOD_ALT;
                if ((keys & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
                if (!Native.RegisterHotKey(hub.Handle, id, mods, (uint)(keys & Keys.KeyCode)))
                    failed.Add(Model.Describe(keys));
            }
            if (failed.Count > 0)
                tray?.ShowBalloonTip(4000, "Alfa Hub",
                    "Otro programa ya usa: " + string.Join(", ", failed) + ". Elige otra combinación en el hub.",
                    ToolTipIcon.Warning);
        }

        void ApplyUnlocked()
        {
            overlay.SetUnlocked(model.OverlayUnlocked);
            Size size = overlay.MeasureSize();
            if (overlay.Size != size) overlay.Size = size;
        }

        void PlaceOverlayTopRight()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Size size = overlay.MeasureSize();
            overlay.Location = new Point(area.Right - size.Width - 20, area.Top + 20);
            model.OverlayPosition = overlay.Location;
            model.Save();
            overlay.Render(force: true);
        }

        static bool IsOnScreen(Point p)
        {
            foreach (var screen in Screen.AllScreens)
                if (screen.WorkingArea.Contains(p)) return true;
            return false;
        }

        void ShowHub()
        {
            hub.Show();
            if (hub.WindowState == FormWindowState.Minimized) hub.WindowState = FormWindowState.Normal;
            hub.Activate();
        }

        void Quit()
        {
            ticker.Stop();
            UnregisterAll();
            model.Save();
            tray.Visible = false;
            tray.Dispose();
            overlay.Close();
            mirrors.CloseAll();
            hub.Dispose();
            ExitThread();
        }
    }
}

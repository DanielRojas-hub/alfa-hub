using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Tibia Audio: mira cada 15 ms si están pulsadas las teclas de las habilidades (GetAsyncKeyState),
    /// solo con Tibia al frente. No instala ganchos de teclado ni bloquea nada: Tibia recibe las teclas
    /// igual. Solo pregunta por las teclas configuradas y no guarda nada.
    sealed class KeyWatcher
    {
        readonly Model model;
        readonly Timer timer = new Timer { Interval = 15 };
        readonly HashSet<int> down = new HashSet<int>();

        /// Lo actualiza la app (cada 250 ms).
        public bool TibiaInFront;

        public KeyWatcher(Model model)
        {
            this.model = model;
            timer.Tick += (s, e) => Poll();
        }

        /// Solo funciona con Tibia Audio activado y alguna habilidad con tecla.
        public void Sync()
        {
            bool wanted = model.AudioEnabled && model.Audios.Any(a => a.Config.Enabled && a.Config.Key != 0);
            if (wanted != timer.Enabled) timer.Enabled = wanted;
            if (!wanted) down.Clear();
        }

        public void Stop() => timer.Stop();

        static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

        void Poll()
        {
            if (!TibiaInFront) { down.Clear(); return; }
            var codes = model.Audios.Where(a => a.Config.Enabled && a.Config.Key != 0)
                .Select(a => a.Config.Key & (int)Keys.KeyCode).Distinct().ToList();
            if (codes.Count == 0) return;
            Keys mods = Keys.None;
            if (IsDown((int)Keys.ControlKey)) mods |= Keys.Control;
            if (IsDown((int)Keys.Menu)) mods |= Keys.Alt;
            if (IsDown((int)Keys.ShiftKey)) mods |= Keys.Shift;
            foreach (int code in codes)
            {
                bool now = IsDown(code);
                // Solo cuenta al bajar la tecla (mantenerla pulsada no repite).
                if (now && down.Add(code)) model.KeyPressed((Keys)code | mods);
                else if (!now) down.Remove(code);
            }
        }
    }
}

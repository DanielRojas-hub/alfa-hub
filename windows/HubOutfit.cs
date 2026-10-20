using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Página Outfit: el personaje de Alfa Hub, como en el Outfiter de TibiaWiki.
    sealed partial class HubForm
    {
        Control BuildOutfitPage()
        {
            var original = SmallButton("↺  Personaje original", () => Outfit.Set(new OutfitConfig()));
            original.Enabled = !Outfit.Config.IsOriginal;
            Action onChange = () => original.Enabled = !Outfit.Config.IsOriginal;
            Outfit.Changed += onChange;
            var editor = new OutfitEditor(Outfit) { Margin = new Padding(0, 4, 0, 8) };
            editor.Disposed += (s, e) => Outfit.Changed -= onChange;
            return Page(
                new PageHeader("Outfit", "Tu personaje: ícono de la ventana, de la bandeja y barra lateral", original),
                editor,
                new NoteLabel("Los sprites vienen del Outfiter de TibiaWiki: se descargan la primera vez que eliges cada outfit o montura y luego funcionan sin conexión. El ícono del archivo AlfaHub.exe sigue siendo el original."));
        }
    }

    /// Lista de outfits a la izquierda; vista previa, addons, colores y montura a la derecha.
    sealed class OutfitEditor : PaintedControl, IStackItem
    {
        readonly OutfitState state;
        readonly OutfitCatalog catalog = OutfitCatalog.Shared;
        readonly TextBox search = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Color.White };
        readonly ListBox list = new ListBox
        {
            BorderStyle = BorderStyle.None, BackColor = Theme.Row, ForeColor = Color.White,
            DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false,
        };
        readonly OutfitPreview preview;
        readonly RoundButton left = new RoundButton { Caption = "↺", Text = "Girar a la izquierda", Pill = true, Size = new Size(30, 30), FillColor = Theme.ControlFill };
        readonly RoundButton right = new RoundButton { Caption = "↻", Text = "Girar a la derecha", Pill = true, Size = new Size(30, 30), FillColor = Theme.ControlFill };
        readonly ToggleSwitch female = new ToggleSwitch { Text = "Mujer" }, addon1 = new ToggleSwitch { Text = "Addon 1" }, addon2 = new ToggleSwitch { Text = "Addon 2" };
        readonly SegmentedControl part = new SegmentedControl("Cabeza", "Primario", "Secundario", "Detalle") { Height = 30 };
        readonly SegmentedControl target = new SegmentedControl("Outfit", "Montura") { Width = 170, Height = 28 };
        readonly ColorPaletteControl palette = new ColorPaletteControl();
        readonly ComboBox mount = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Field, ForeColor = Color.White };
        List<int> shown = new List<int>();
        readonly List<int> mounts;
        bool updating;

        public OutfitEditor(OutfitState state)
        {
            this.state = state;
            BackColor = Theme.Back;
            preview = new OutfitPreview(state);
            mounts = catalog.SortedMounts;
            foreach (int id in mounts) mount.Items.Add(catalog.MountTitle(id));
            list.DrawItem += DrawListItem;
            list.SelectedIndexChanged += (s, e) =>
            {
                if (updating || list.SelectedIndex < 0) return;
                SelectOutfit(shown[list.SelectedIndex]);
            };
            search.AccessibleName = "Buscar outfit";
            search.TextChanged += (s, e) => FillList();
            // Enter elige el que se llama exactamente así (o el primero de la lista).
            search.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter || shown.Count == 0) return;
                e.SuppressKeyPress = true;
                int exact = shown.FindIndex(i => string.Equals(catalog.Title(i), search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                SelectOutfit(shown[exact >= 0 ? exact : 0]);
            };
            left.Click += (s, e) => Change(c => c.Facing = (c.Facing + 3) % 4);
            right.Click += (s, e) => Change(c => c.Facing = (c.Facing + 1) % 4);
            female.CheckedChanged += (s, e) => { if (!updating) Change(c => c.Female = female.Checked); };
            addon1.CheckedChanged += (s, e) => { if (!updating) Change(c => { c.Addon1 = addon1.Checked; if (c.Addon1 && catalog.OnlyOneAddon(c.Outfit)) c.Addon2 = false; }); };
            addon2.CheckedChanged += (s, e) => { if (!updating) Change(c => { c.Addon2 = addon2.Checked; if (c.Addon2 && catalog.OnlyOneAddon(c.Outfit)) c.Addon1 = false; }); };
            part.SelectedIndexChanged += i => SyncPalette();
            target.SelectedIndexChanged += i => SyncPalette();
            palette.Picked += index => Change(c =>
            {
                bool m = target.SelectedIndex == 1;
                switch (part.SelectedIndex)
                {
                    case 0: if (m) c.Mc1 = index; else c.C1 = index; break;
                    case 1: if (m) c.Mc2 = index; else c.C2 = index; break;
                    case 2: if (m) c.Mc3 = index; else c.C3 = index; break;
                    default: if (m) c.Mc4 = index; else c.C4 = index; break;
                }
            });
            mount.SelectedIndexChanged += (s, e) => { if (!updating && mount.SelectedIndex >= 0) Change(c => c.Mount = mounts[mount.SelectedIndex]); };
            Controls.AddRange(new Control[] { search, list, preview, left, right, female, addon1, addon2, part, target, palette, mount });
            state.Changed += OnStateChanged;
            FillList();
            Sync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) state.Changed -= OnStateChanged;
            base.Dispose(disposing);
        }

        void OnStateChanged() { Sync(); preview.Invalidate(); }

        public int HeightFor(int width) => (int)(470 * K);

        void Change(Action<OutfitConfig> edit)
        {
            var c = state.Config.Clone();
            edit(c);
            state.Set(c);
        }

        void SelectOutfit(int id)
        {
            Change(c =>
            {
                c.Outfit = id;
                if (!catalog.HasAddons(id)) { c.Addon1 = false; c.Addon2 = false; }
                if (catalog.OnlyOneAddon(id) && c.Addon1 && c.Addon2) c.Addon2 = false;
                if (!catalog.CanBeFemale(id)) c.Female = false;
                if (!catalog.CanMount(id)) c.Mount = 0;
            });
        }

        void FillList()
        {
            string q = search.Text.Trim();
            shown = catalog.SortedOutfits.Where(i => q.Length == 0 || catalog.Title(i).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            updating = true;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (int id in shown) list.Items.Add(catalog.Title(id));
            list.SelectedIndex = shown.IndexOf(state.Config.Outfit);
            list.EndUpdate();
            updating = false;
        }

        void Sync()
        {
            var c = state.Config;
            updating = true;
            int index = shown.IndexOf(c.Outfit);
            if (list.SelectedIndex != index) list.SelectedIndex = index;
            female.Checked = c.Female; female.Enabled = catalog.CanBeFemale(c.Outfit);
            addon1.Checked = c.Addon1; addon2.Checked = c.Addon2;
            addon1.Enabled = addon2.Enabled = catalog.HasAddons(c.Outfit);
            mount.Enabled = catalog.CanMount(c.Outfit);
            int m = mounts.IndexOf(c.Mount);
            if (mount.SelectedIndex != m) mount.SelectedIndex = m;
            target.Visible = catalog.MountColorable(c.Mount);
            if (!target.Visible && target.SelectedIndex != 0) target.SetSelected(0);
            updating = false;
            SyncPalette();
            Invalidate();
        }

        void SyncPalette()
        {
            var c = state.Config;
            bool m = target.SelectedIndex == 1;
            int[] values = m ? new[] { c.Mc1, c.Mc2, c.Mc3, c.Mc4 } : new[] { c.C1, c.C2, c.C3, c.C4 };
            palette.Selected = values[Math.Max(0, Math.Min(3, part.SelectedIndex))];
        }

        void DrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(selected ? Color.FromArgb(60, Theme.Blue) : Theme.Row)) e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), list.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float k = K;
            int listW = (int)(210 * k), pad = (int)(8 * k);
            search.Font = Font;
            search.SetBounds(pad + (int)(6 * k), pad + (int)(5 * k), listW - pad * 2 - (int)(12 * k), search.PreferredHeight);
            list.ItemHeight = (int)(24 * k);
            list.SetBounds(pad, (int)(40 * k), listW - pad * 2, Height - (int)(48 * k));

            int x = listW + (int)(16 * k);
            int size = (int)(190 * k);
            preview.SetBounds(x, 0, size, size);
            int x2 = x + size + (int)(18 * k);
            int y = (int)(46 * k);
            left.Location = new Point(x2, y);
            right.Location = new Point(left.Right + (int)(6 * k), y);
            y += (int)(42 * k);
            foreach (var t in new[] { female, addon1, addon2 })
            {
                t.Location = new Point(x2, y);
                y += (int)(32 * k);
            }
            int x3 = x;
            int top = size + (int)(14 * k);
            target.Location = new Point(Width - target.Width, top);
            part.SetBounds(x3, top + (int)(34 * k), Width - x3, part.Height);
            palette.SetBounds(x3, part.Bottom + (int)(8 * k), Width - x3, (int)(7 * 18 * k));
            mount.SetBounds(Width - (int)(240 * k), palette.Bottom + (int)(14 * k), (int)(240 * k), mount.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            Fill(g, new RectangleF(0, 0, 210 * k, Height - 1), 10 * k, Theme.Row);
            Fill(g, new RectangleF(8 * k, 8 * k, 194 * k, 26 * k), 5 * k, Theme.Field);
            var c = state.Config;
            int x2 = preview.Right + (int)(18 * k);
            using (var title = new Font("Segoe UI", 14f, FontStyle.Bold))
                TextRenderer.DrawText(g, catalog.Title(c.Outfit, c.Female), title, new Point(x2, (int)(8 * k)), Color.White, TextFormatFlags.NoPadding);
            string facing = new[] { "Norte", "Este", "Sur", "Oeste" }[Math.Max(0, Math.Min(3, c.Facing))];
            TextRenderer.DrawText(g, facing, Font, new Point(right.Right + (int)(10 * k), left.Top + (int)(6 * k)), Theme.Muted, TextFormatFlags.NoPadding);
            string[] labels = { "Mujer", "Addon 1", "Addon 2" };
            var toggles = new[] { female, addon1, addon2 };
            for (int i = 0; i < 3; i++)
                TextRenderer.DrawText(g, labels[i], Font, new Point(toggles[i].Right + (int)(10 * k), toggles[i].Top + (toggles[i].Height - Font.Height) / 2),
                    toggles[i].Enabled ? Color.White : Theme.Muted, TextFormatFlags.NoPadding);
            using (var bold = new Font("Segoe UI Semibold", 10f))
            {
                TextRenderer.DrawText(g, "Colores", bold, new Point(preview.Left, target.Top + (int)(4 * k)), Color.White, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "Montura", bold, new Point(preview.Left, mount.Top + (int)(3 * k)), Color.White, TextFormatFlags.NoPadding);
            }
        }
    }

    /// Vista previa del personaje sobre un fondo de hierba.
    sealed class OutfitPreview : PaintedControl
    {
        readonly OutfitState state;

        public OutfitPreview(OutfitState state) { this.state = state; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), 12 * k, Color.FromArgb(51, 61, 41));
            Image sprite = state.Display;
            if (sprite != null)
            {
                float scale = Math.Min(3 * k, (Width - 20 * k) / Math.Max(sprite.Width, sprite.Height));
                float w = sprite.Width * scale, h = sprite.Height * scale;
                Resources.DrawPixelArt(g, sprite, new RectangleF((Width - w) / 2, (Height - h) / 2, w, h));
            }
            string message = state.Loading ? "Descargando…" : state.Failed ? "No se pudo descargar de TibiaWiki" : null;
            if (message != null)
                CenterText(g, message, Font, state.Failed ? Theme.Amber : Theme.Muted, new RectangleF(0, Height - 28 * k, Width, 22 * k));
        }
    }

    /// Los 133 colores de Tibia (7 filas × 19), como en el Outfiter.
    sealed class ColorPaletteControl : PaintedControl
    {
        const int Columns = 19;
        int selected;
        public event Action<int> Picked;

        public ColorPaletteControl() { Cursor = Cursors.Hand; AccessibleName = "Colores"; }

        public int Selected { get => selected; set { if (selected != value) { selected = value; Invalidate(); } } }

        float Cell => 18 * K;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int col = (int)(e.X / Cell), row = (int)(e.Y / Cell);
            int index = row * Columns + col;
            if (col < Columns && index >= 0 && index < OutfitCatalog.Shared.Colors.Count) { Selected = index; Picked?.Invoke(index); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            g.SmoothingMode = SmoothingMode.None;
            var colors = OutfitCatalog.Shared.Colors;
            float cell = Cell, gap = 2 * K;
            for (int i = 0; i < colors.Count; i++)
            {
                var r = new RectangleF((i % Columns) * cell, (i / Columns) * cell, cell - gap, cell - gap);
                using (var brush = new SolidBrush(Color.FromArgb(colors[i][0], colors[i][1], colors[i][2]))) g.FillRectangle(brush, r);
                if (i == selected)
                    using (var pen = new Pen(Color.White, 2 * K)) g.DrawRectangle(pen, r.X + K, r.Y + K, r.Width - 2 * K, r.Height - 2 * K);
            }
        }
    }
}

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
            var discard = SmallButton("✕  Descartar", () => Outfit.Discard());
            var save = new RoundButton { Text = "✓  Guardar cambios", FillColor = Theme.Blue, Pill = true, Height = 34, BackColor = Theme.Back };
            save.Font = new Font("Segoe UI Semibold", 10f);
            save.FitText(16);
            save.Click += (s, e) => Outfit.Save();
            var actions = new InlineGroup(discard, save) { BackColor = Theme.Back, Height = 34 };
            Action fit = () => { actions.Width = actions.PreferredWidth; actions.Parent?.PerformLayout(); };
            discard.SizeChanged += (s, e) => fit();
            save.SizeChanged += (s, e) => fit();
            fit();
            Action onChange = () => discard.Enabled = save.Enabled = Outfit.HasChanges;
            onChange();
            Outfit.Changed += onChange;
            var editor = new OutfitEditor(Outfit) { Margin = new Padding(0, 4, 0, 8) };
            editor.Disposed += (s, e) => Outfit.Changed -= onChange;
            return Page(
                new PageHeader("Outfit", "Tu personaje: ícono de la ventana, de la bandeja y barra lateral", actions),
                editor,
                new NoteLabel("Pulsa Guardar cambios para que tu personaje pase al ícono de la ventana, a la bandeja y a la barra lateral. Los sprites vienen del Outfiter de TibiaWiki: se descargan la primera vez que eliges cada outfit o montura y luego funcionan sin conexión. El ícono del archivo AlfaHub.exe sigue siendo el original."));
        }
    }

    /// Listas de outfits y monturas a la izquierda; vista previa, opciones, colores y "Mis outfits" a la derecha.
    sealed class OutfitEditor : PaintedControl, IStackItem
    {
        readonly OutfitState state;
        readonly OutfitCatalog catalog = OutfitCatalog.Shared;
        readonly TextBox search = NewTextBox("Buscar outfit"), mountSearch = NewTextBox("Buscar montura"), presetName = NewTextBox("Nombre");
        readonly ListBox list = NewList(), mountList = NewList();
        readonly OutfitPreview preview;
        readonly RoundButton left = new RoundButton { Caption = "↺", Text = "Girar a la izquierda", Pill = true, Size = new Size(30, 30), FillColor = Theme.ControlFill };
        readonly RoundButton right = new RoundButton { Caption = "↻", Text = "Girar a la derecha", Pill = true, Size = new Size(30, 30), FillColor = Theme.ControlFill };
        readonly SegmentedControl sex = new SegmentedControl("Hombre", "Mujer") { Width = 170, Height = 28 };
        readonly ToggleSwitch addon1 = new ToggleSwitch { Text = "Addon 1" }, addon2 = new ToggleSwitch { Text = "Addon 2" },
            mounted = new ToggleSwitch { Text = "Montura" }, animate = new ToggleSwitch { Text = "Animar caminata" };
        readonly SegmentedControl part = new SegmentedControl("Cabeza", "Primario", "Secundario", "Detalle") { Height = 30 };
        readonly SegmentedControl target = new SegmentedControl("Outfit", "Montura") { Width = 170, Height = 28 };
        readonly ColorPaletteControl palette = new ColorPaletteControl();
        readonly RoundButton addPreset = new RoundButton { Text = "＋  Guardar aquí", Height = 28, FillColor = Theme.ControlFill };
        readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer { Interval = 100 };
        List<int> shown = new List<int>(), shownMounts = new List<int>();
        /// Casillas de "Mis outfits" (la primera es el personaje original) y su botón ×.
        readonly List<(Rectangle tile, Rectangle remove, OutfitPreset preset)> tiles = new List<(Rectangle, Rectangle, OutfitPreset)>();
        int hoverTile = -1;
        bool updating;

        static TextBox NewTextBox(string name) =>
            new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Color.White, AccessibleName = name };

        static ListBox NewList() => new ListBox
        {
            BorderStyle = BorderStyle.None, BackColor = Theme.Row, ForeColor = Color.White,
            DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false,
        };

        public OutfitEditor(OutfitState state)
        {
            this.state = state;
            BackColor = Theme.Back;
            preview = new OutfitPreview(state);
            list.DrawItem += (s, e) => DrawListItem(list, e);
            mountList.DrawItem += (s, e) => DrawListItem(mountList, e);
            list.SelectedIndexChanged += (s, e) =>
            {
                if (updating || list.SelectedIndex < 0) return;
                SelectOutfit(shown[list.SelectedIndex]);
            };
            mountList.SelectedIndexChanged += (s, e) =>
            {
                if (updating || mountList.SelectedIndex < 0) return;
                SelectMount(shownMounts[mountList.SelectedIndex]);
            };
            search.TextChanged += (s, e) => FillList();
            mountSearch.TextChanged += (s, e) => FillMounts();
            // Enter elige el que se llama exactamente así (o el primero de la lista).
            search.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter || shown.Count == 0) return;
                e.SuppressKeyPress = true;
                int exact = shown.FindIndex(i => string.Equals(catalog.Title(i), search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                SelectOutfit(shown[exact >= 0 ? exact : 0]);
            };
            mountSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter || shownMounts.Count == 0) return;
                e.SuppressKeyPress = true;
                int exact = shownMounts.FindIndex(i => string.Equals(catalog.MountTitle(i), mountSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                SelectMount(shownMounts[exact >= 0 ? exact : 0]);
            };
            presetName.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                AddPreset();
            };
            left.Click += (s, e) => Change(c => c.Facing = (c.Facing + 3) % 4);
            right.Click += (s, e) => Change(c => c.Facing = (c.Facing + 1) % 4);
            sex.SelectedIndexChanged += i => { if (!updating) Change(c => c.Female = i == 1); };
            addon1.CheckedChanged += (s, e) => { if (!updating) Change(c => { c.Addon1 = addon1.Checked; if (c.Addon1 && catalog.OnlyOneAddon(c.Outfit)) c.Addon2 = false; }); };
            addon2.CheckedChanged += (s, e) => { if (!updating) Change(c => { c.Addon2 = addon2.Checked; if (c.Addon2 && catalog.OnlyOneAddon(c.Outfit)) c.Addon1 = false; }); };
            mounted.CheckedChanged += (s, e) =>
            {
                if (updating) return;
                Change(c =>
                {
                    // Sin montura elegida todavía: la primera de la lista.
                    if (mounted.Checked && c.Mount == 0) c.Mount = catalog.SortedMounts.Skip(1).FirstOrDefault();
                    c.Mounted = mounted.Checked;
                });
            };
            animate.CheckedChanged += (s, e) => { if (!updating) Change(c => c.Animate = animate.Checked); };
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
            addPreset.BackColor = Theme.Row;
            addPreset.FitText(10);
            addPreset.Click += (s, e) => AddPreset();
            Controls.AddRange(new Control[] { search, list, mountSearch, mountList, preview, left, right, sex, addon1, addon2, mounted, animate, part, target, palette, presetName, addPreset });
            Text = "Editor de outfit";  // nombre para UI Automation (script de capturas)
            list.HandleCreated += (s, e) => Native.UseDarkScrollbars(list.Handle);
            mountList.HandleCreated += (s, e) => Native.UseDarkScrollbars(mountList.Handle);
            foreach (var box in new[] { search, mountSearch, presetName })
            {
                var b = box;
                b.HandleCreated += (s, e) => SendMessage(b.Handle, 0x1501, (IntPtr)1, b.AccessibleName);  // EM_SETCUEBANNER
            }
            // La caminata: vuelve a pintar la vista previa cada fotograma.
            animation.Tick += (s, e) => { if (state.Preview?.Frames.Count > 0) preview.Invalidate(); };
            animation.Start();
            state.Changed += OnStateChanged;
            FillList();
            FillMounts();
            Sync();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                state.Changed -= OnStateChanged;
                animation.Dispose();
            }
            base.Dispose(disposing);
        }

        void OnStateChanged()
        {
            Sync();
            preview.Invalidate();
            Parent?.PerformLayout();  // "Mis outfits" puede haber cambiado de alto
        }

        float Tile => 92 * K;
        float TileGap => 8 * K;
        int RightX => (int)(226 * K);
        int PresetsTop => (int)(472 * K);

        int TileColumns(int width) => Math.Max(1, (int)((width - RightX - 24 * K + TileGap) / (Tile + TileGap)));

        public int HeightFor(int width)
        {
            int rows = (state.Presets.Count + 1 + TileColumns(width) - 1) / TileColumns(width);
            int presets = PresetsTop + (int)(48 * K + rows * (Tile + 14 * K + TileGap) + 10 * K);
            return Math.Max((int)(600 * K), presets);
        }

        void Change(Action<OutfitConfig> edit)
        {
            var c = state.Draft.Clone();
            edit(c);
            state.SetDraft(c);
        }

        void SelectOutfit(int id)
        {
            Change(c =>
            {
                c.Outfit = id;
                if (!catalog.HasAddons(id)) { c.Addon1 = false; c.Addon2 = false; }
                if (catalog.OnlyOneAddon(id) && c.Addon1 && c.Addon2) c.Addon2 = false;
                if (!catalog.CanBeFemale(id)) c.Female = false;
                if (!catalog.CanMount(id)) c.Mounted = false;
            });
        }

        /// Elegir una montura de la lista la monta (si el outfit puede ir montado).
        void SelectMount(int id)
        {
            Change(c =>
            {
                c.Mount = id;
                if (catalog.CanMount(c.Outfit)) c.Mounted = true;
            });
        }

        void AddPreset()
        {
            state.AddPreset(presetName.Text);
            presetName.Text = "";
        }

        void FillList()
        {
            string q = search.Text.Trim();
            shown = catalog.SortedOutfits.Where(i => q.Length == 0 || catalog.Title(i).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Fill(list, shown, i => catalog.Title(i), state.Draft.Outfit);
        }

        void FillMounts()
        {
            string q = mountSearch.Text.Trim();
            shownMounts = catalog.SortedMounts.Skip(1).Where(i => q.Length == 0 || catalog.MountTitle(i).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Fill(mountList, shownMounts, catalog.MountTitle, state.Draft.Mount);
        }

        void Fill(ListBox box, List<int> ids, Func<int, string> title, int selected)
        {
            updating = true;
            box.BeginUpdate();
            box.Items.Clear();
            foreach (int id in ids) box.Items.Add(title(id));
            box.SelectedIndex = ids.IndexOf(selected);
            box.EndUpdate();
            updating = false;
        }

        void Sync()
        {
            var c = state.Draft;
            updating = true;
            int index = shown.IndexOf(c.Outfit);
            if (list.SelectedIndex != index) list.SelectedIndex = index;
            int m = shownMounts.IndexOf(c.Mount);
            if (mountList.SelectedIndex != m) mountList.SelectedIndex = m;
            sex.SetSelected(c.Female ? 1 : 0);
            sex.Enabled = catalog.CanBeFemale(c.Outfit);
            addon1.Checked = c.Addon1; addon2.Checked = c.Addon2;
            addon1.Enabled = addon2.Enabled = catalog.HasAddons(c.Outfit);
            mounted.Checked = c.Ridden > 0;
            mounted.Enabled = catalog.CanMount(c.Outfit);
            animate.Checked = c.Animate;
            // Los colores de la montura solo si va montado en una que se puede colorear.
            target.Enabled = catalog.MountColorable(c.Ridden);
            if (!target.Enabled && target.SelectedIndex != 0) target.SetSelected(0);
            updating = false;
            SyncPalette();
            Invalidate();
        }

        void SyncPalette()
        {
            var c = state.Draft;
            bool m = target.SelectedIndex == 1;
            int[] values = m ? new[] { c.Mc1, c.Mc2, c.Mc3, c.Mc4 } : new[] { c.C1, c.C2, c.C3, c.C4 };
            palette.Selected = values[Math.Max(0, Math.Min(3, part.SelectedIndex))];
        }

        void DrawListItem(ListBox box, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(selected ? Color.FromArgb(60, Theme.Blue) : Theme.Row)) e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, box.Items[e.Index].ToString(), box.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float k = K;
            int listW = (int)(210 * k), pad = (int)(8 * k);
            // Dos tarjetas a la izquierda: outfits arriba y monturas abajo.
            int half = (Height - (int)(10 * k)) / 2;
            foreach (var (box, items, top) in new[] { (search, list, 0), (mountSearch, mountList, half + (int)(10 * k)) })
            {
                box.Font = Font;
                box.SetBounds(pad + (int)(6 * k), top + pad + (int)(5 * k), listW - pad * 2 - (int)(12 * k), box.PreferredHeight);
                items.ItemHeight = (int)(24 * k);
                items.SetBounds(pad, top + (int)(40 * k), listW - pad * 2, half - (int)(48 * k));
            }

            int x = RightX;
            int size = (int)(190 * k);
            preview.SetBounds(x, 0, size, size);
            int x2 = x + size + (int)(18 * k);
            int y = (int)(56 * k);
            left.Location = new Point(x2, y);
            right.Location = new Point(left.Right + (int)(6 * k), y);
            y += (int)(38 * k);
            sex.Location = new Point(x2, y);
            y += (int)(38 * k);
            foreach (var t in new[] { addon1, addon2, mounted, animate })
            {
                t.Location = new Point(x2, y);
                y += (int)(30 * k);
            }
            int top2 = (int)(258 * k);
            target.Location = new Point(Width - target.Width, top2);
            part.SetBounds(x, top2 + (int)(34 * k), Width - x, part.Height);
            palette.SetBounds(x, part.Bottom + (int)(8 * k), Width - x, (int)(7 * 18 * k));

            // Mis outfits
            int pt = PresetsTop;
            addPreset.Location = new Point(Width - addPreset.Width - (int)(12 * k), pt + (int)(10 * k));
            int nameW = (int)(130 * k);
            presetName.Font = Font;
            presetName.SetBounds(addPreset.Left - nameW - (int)(8 * k) + (int)(6 * k), addPreset.Top + (addPreset.Height - presetName.PreferredHeight) / 2,
                nameW - (int)(12 * k), presetName.PreferredHeight);
            LayoutTiles();
        }

        void LayoutTiles()
        {
            float k = K;
            tiles.Clear();
            int columns = TileColumns(Width);
            float tile = Tile, gap = TileGap, h = tile + 14 * k;
            var presets = new List<OutfitPreset> { null }.Concat(state.Presets).ToList();
            for (int i = 0; i < presets.Count; i++)
            {
                var r = Rectangle.Round(new RectangleF(RightX + 12 * k + (i % columns) * (tile + gap), PresetsTop + 48 * k + (i / columns) * (h + gap), tile, h));
                var x = new Rectangle(r.Right - (int)(20 * k), r.Top + (int)(4 * k), (int)(16 * k), (int)(16 * k));
                tiles.Add((r, x, presets[i]));
            }
        }

        int TileAt(Point p) => tiles.FindIndex(t => t.tile.Contains(p));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = TileAt(e.Location);
            Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
            if (hover != hoverTile) { hoverTile = hover; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverTile >= 0) { hoverTile = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = TileAt(e.Location);
            if (i < 0) return;
            var preset = tiles[i].preset;
            if (preset != null && (e.Button == MouseButtons.Right || tiles[i].remove.Contains(e.Location)))
            {
                if (e.Button == MouseButtons.Right)
                {
                    var menu = DarkMenu.Create();
                    menu.Items.Add("Quitar de Mis outfits", null, (s, a) => state.RemovePreset(preset));
                    menu.Show(this, e.Location);
                }
                else state.RemovePreset(preset);
                return;
            }
            if (e.Button == MouseButtons.Left) state.SetDraft((preset?.Config ?? new OutfitConfig()).Clone());
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            int half = (Height - (int)(10 * k)) / 2;
            foreach (int top in new[] { 0, half + (int)(10 * k) })
            {
                Fill(g, new RectangleF(0, top, 210 * k, half - 1), 10 * k, Theme.Row);
                Fill(g, new RectangleF(8 * k, top + 8 * k, 194 * k, 26 * k), 5 * k, Theme.Field);
            }
            var c = state.Draft;
            int x2 = preview.Right + (int)(18 * k);
            using (var title = new Font("Segoe UI", 14f, FontStyle.Bold))
                TextRenderer.DrawText(g, catalog.Title(c.Outfit, c.Female), title, new Point(x2, (int)(4 * k)), Color.White, TextFormatFlags.NoPadding);
            if (c.Ridden > 0)
                TextRenderer.DrawText(g, "Sobre " + catalog.MountTitle(c.Ridden), Font, new Point(x2, (int)(32 * k)), Theme.Muted, TextFormatFlags.NoPadding);
            string facing = new[] { "Norte", "Este", "Sur", "Oeste" }[Math.Max(0, Math.Min(3, c.Facing))];
            TextRenderer.DrawText(g, facing, Font, new Point(right.Right + (int)(10 * k), left.Top + (int)(6 * k)), Theme.Muted, TextFormatFlags.NoPadding);
            foreach (var t in new[] { addon1, addon2, mounted, animate })
                TextRenderer.DrawText(g, t.Text, Font, new Point(t.Right + (int)(10 * k), t.Top + (t.Height - Font.Height) / 2),
                    t.Enabled ? Color.White : Theme.Muted, TextFormatFlags.NoPadding);
            using (var bold = new Font("Segoe UI Semibold", 10f))
            {
                TextRenderer.DrawText(g, "Colores", bold, new Point(preview.Left, target.Top + (int)(4 * k)), Color.White, TextFormatFlags.NoPadding);
                Fill(g, new RectangleF(RightX, PresetsTop, Width - RightX - 1, Height - PresetsTop - 1), 10 * k, Theme.Row);
                TextRenderer.DrawText(g, "Mis outfits", bold, new Point(RightX + (int)(12 * k), addPreset.Top + (int)(5 * k)), Color.White, TextFormatFlags.NoPadding);
            }
            Fill(g, new RectangleF(presetName.Left - 6 * k, addPreset.Top, presetName.Width + 12 * k, addPreset.Height), 5 * k, Theme.Field);

            for (int i = 0; i < tiles.Count; i++)
            {
                var (r, x, preset) = tiles[i];
                var config = preset?.Config ?? new OutfitConfig();
                bool selected = c.SameAs(config);
                Fill(g, r, 10 * k, selected ? Color.FromArgb(64, Theme.Blue) : Color.FromArgb(48, 48, 52));
                if (selected) Stroke(g, r, 10 * k, Theme.Blue, 1.5f * k);
                var box = new RectangleF(r.X + 6 * k, r.Y + 6 * k, r.Width - 12 * k, r.Width - 22 * k);
                Fill(g, box, 8 * k, Color.FromArgb(51, 61, 41));
                Image image = state.Thumbnail(config);
                if (image != null)
                {
                    float scale = Math.Min(1.5f * k, (box.Height - 6 * k) / Math.Max(image.Width, image.Height));
                    float w = image.Width * scale, h = image.Height * scale;
                    Resources.DrawPixelArt(g, image, new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h));
                }
                CenterText(g, preset?.Name ?? "Original", Font, Color.White, new RectangleF(r.X + 4 * k, box.Bottom + 2 * k, r.Width - 8 * k, r.Bottom - box.Bottom - 4 * k));
                if (preset != null && i == hoverTile)
                {
                    using (var back = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillEllipse(back, x);
                    CenterText(g, "✕", Font, Color.White, x);
                }
            }
            if (state.Presets.Count == 0 && tiles.Count > 0)
                TextRenderer.DrawText(g, "Guarda aquí tus combinaciones para volver a ellas con un clic.", Font,
                    new Point(tiles[0].tile.Right + (int)(12 * k), tiles[0].tile.Top + tiles[0].tile.Height / 2 - Font.Height / 2), Theme.Muted, TextFormatFlags.NoPadding);
        }
    }

    /// Vista previa del personaje (quieto o caminando) sobre un fondo de hierba.
    sealed class OutfitPreview : PaintedControl
    {
        readonly OutfitState state;

        public OutfitPreview(OutfitState state) { this.state = state; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), 12 * k, Color.FromArgb(51, 61, 41));
            var sprite = state.Preview;
            Image image = sprite?.Now ?? (state.Draft.IsOriginal ? Resources.Character : null);
            if (image != null)
            {
                // Escala del primer fotograma: todos miden lo mismo.
                Image size = sprite?.Frames.FirstOrDefault() ?? image;
                float scale = Math.Min(3 * k, (Width - 20 * k) / Math.Max(size.Width, size.Height));
                float w = image.Width * scale, h = image.Height * scale;
                Resources.DrawPixelArt(g, image, new RectangleF((Width - w) / 2, (Height - h) / 2, w, h));
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

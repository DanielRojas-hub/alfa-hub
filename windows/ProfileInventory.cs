using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Pestaña Inventario: los items del char en cuadrículas como en Tibia: el equipo en sus huecos y, al
    /// lado, un contenedor por sitio (mochila, depot, casa…) agrupado por detalle. Clic en una casilla para
    /// cambiar el item, cuántos hay, moverlo o quitarlo; en "+" para añadir uno.
    sealed class InventoryCanvas : HitCanvas
    {
        readonly GameChar c;
        readonly Profile profile;
        readonly ProfileStore store;
        readonly InputField search = new InputField("Buscar un item en todos los chars del perfil") { Height = 28 };
        string query = "";

        /// Los huecos del equipo como en Tibia (null = vacío).
        static readonly string[,] SlotGrid =
        {
            { "amulet", "helmet", null },
            { "weapon", "armor", "shield" },
            { "ring", "legs", null },
            { null, "boots", null },
        };

        const float Cell = 44, CellGap = 4, EquipW = 180;

        public InventoryCanvas(GameChar c, Profile profile, ProfileStore store)
        {
            this.c = c;
            this.profile = profile;
            this.store = store;
            Margin = new Padding(0, 0, 0, 8);
            search.Changed += text => { query = text.Trim(); Relayout(); };
            Controls.Add(search);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            search.SetBounds(0, 0, (int)Math.Min(S(340), Width - S(100)), (int)S(28));
        }

        static readonly string[] Containers = Places.All.Where(p => p != "equipped").ToArray();

        List<string> OtherChars => profile.Chars.Where(o => o.Id != c.Id && o.Name.Length > 0).Select(o => o.Name).ToList();

        // ---------- Medidas ----------

        float ContainerHeight(string place, float width)
        {
            var items = c.Items.Where(i => i.Place == place).ToList();
            int perRow = Math.Max(1, (int)((width - S(24) + S(CellGap)) / (S(Cell) + S(CellGap))));
            float h = S(40);
            var groups = items.GroupBy(i => i.Detail).OrderBy(gr => gr.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (groups.Count == 0) return h + S(Cell) + S(14);
            for (int i = 0; i < groups.Count; i++)
            {
                int count = groups[i].Count() + (i == groups.Count - 1 ? 1 : 0);
                if (groups[i].Key.Length > 0) h += S(20);
                h += ((count + perRow - 1) / perRow) * (S(Cell) + S(CellGap)) + S(4);
            }
            return h + S(10);
        }

        (float Left, float Right) Columns(float width)
        {
            float colW = (width - S(EquipW) - S(14) - S(10)) / 2;
            float left = 0, right = 0;
            for (int i = 0; i < Containers.Length; i++)
            {
                float h = ContainerHeight(Containers[i], colW) + S(10);
                if (i % 2 == 0) left += h; else right += h;
            }
            return (left, right);
        }

        List<(string Char, OwnedItem Item)> Results() =>
            profile.Chars.SelectMany(o => o.Items.Where(i => i.Item.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).Select(i => (o.Name, i))).ToList();

        public override int HeightFor(int width)
        {
            float top = S(40);
            if (query.Length > 0) return (int)(top + Math.Max(1, Results().Count) * S(52) + S(10));
            var (left, right) = Columns(width);
            float equip = S(40) + 4 * S(Cell) + 3 * S(CellGap) + S(16);
            return (int)(top + Math.Max(equip, Math.Max(left, right)) + S(10));
        }

        // ---------- Dibujo ----------

        protected override void Draw(Graphics g, Rectangle visible)
        {
            Write(g, $"{c.Items.Count} {(c.Items.Count == 1 ? "item" : "items")}", Fonts.Get(9f), Theme.Muted, new RectangleF(Width - S(120), 0, S(120), S(28)),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            float y = S(40);
            if (query.Length > 0) { DrawResults(g, y); return; }
            // Equipado
            var equip = new RectangleF(0.5f, y, S(EquipW), S(40) + 4 * S(Cell) + 3 * S(CellGap) + S(16));
            Card(g, equip);
            TextAt(g, "●  Equipado", Fonts.Semi(9f), Theme.Green, equip.X + S(12), equip.Y + S(12));
            float gx = equip.X + (equip.Width - (3 * S(Cell) + 2 * S(CellGap))) / 2;
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 3; col++)
                {
                    string slot = SlotGrid[row, col];
                    if (slot == null) continue;
                    var cell = new RectangleF(gx + col * (S(Cell) + S(CellGap)), equip.Y + S(40) + row * (S(Cell) + S(CellGap)), S(Cell), S(Cell));
                    var item = c.Equipped(slot);
                    if (item != null) DrawItem(g, cell, item);
                    else DrawAdd(g, cell, "equipped", slot, "", Slots.Title(slot));
                }
            // Contenedores en dos columnas
            float colW = (Width - S(EquipW) - S(14) - S(10)) / 2;
            float[] ys = { y, y };
            for (int i = 0; i < Containers.Length; i++)
            {
                int column = i % 2;
                float h = ContainerHeight(Containers[i], colW);
                var r = new RectangleF(S(EquipW) + S(14) + column * (colW + S(10)) + 0.5f, ys[column], colW - 1, h);
                DrawContainer(g, r, Containers[i]);
                ys[column] += h + S(10);
            }
        }

        void DrawContainer(Graphics g, RectangleF r, string place)
        {
            Card(g, r);
            var tint = Palette.Place(place);
            TextAt(g, "●  " + Places.Title(place), Fonts.Semi(9f), tint, r.X + S(12), r.Y + S(12));
            var items = c.Items.Where(i => i.Place == place).ToList();
            if (items.Count > 0) Write(g, items.Count.ToString(), Fonts.Get(8.5f), Theme.Muted, new RectangleF(r.Right - S(60), r.Y + S(12), S(48), S(18)), TextFormatFlags.Right | TextFormatFlags.SingleLine);
            int perRow = Math.Max(1, (int)((r.Width - S(24) + S(CellGap)) / (S(Cell) + S(CellGap))));
            float y = r.Y + S(40);
            var groups = items.GroupBy(i => i.Detail).OrderBy(gr => gr.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (groups.Count == 0) { DrawAdd(g, new RectangleF(r.X + S(12), y, S(Cell), S(Cell)), place, null, "", null); return; }
            for (int gi = 0; gi < groups.Count; gi++)
            {
                if (groups[gi].Key.Length > 0) { TextAt(g, groups[gi].Key, Fonts.Semi(8.25f), Theme.Muted, r.X + S(12), y); y += S(20); }
                var list = groups[gi].ToList();
                int count = list.Count + (gi == groups.Count - 1 ? 1 : 0);
                for (int i = 0; i < count; i++)
                {
                    var cell = new RectangleF(r.X + S(12) + (i % perRow) * (S(Cell) + S(CellGap)), y + (i / perRow) * (S(Cell) + S(CellGap)), S(Cell), S(Cell));
                    if (i < list.Count) DrawItem(g, cell, list[i]);
                    else DrawAdd(g, cell, place, null, groups[gi].Key, null);
                }
                y += ((count + perRow - 1) / perRow) * (S(Cell) + S(CellGap)) + S(4);
            }
        }

        void Square(Graphics g, RectangleF cell)
        {
            Fill(g, cell, S(5), Color.FromArgb(100, 0, 0, 0));
            Stroke(g, cell, S(5), Hovered(cell) ? Theme.Blue : Color.FromArgb(26, 255, 255, 255));
        }

        void DrawItem(Graphics g, RectangleF cell, OwnedItem item)
        {
            Square(g, cell);
            Sprite(g, WikiImages.Get(item.Item), RectangleF.Inflate(cell, -S(4), -S(4)), 1);
            if (item.Count > 1)
                Write(g, item.Count.ToString(), Fonts.Bold(7f), Color.White, new RectangleF(cell.X, cell.Bottom - S(15), cell.Width - S(3), S(14)), TextFormatFlags.Right | TextFormatFlags.SingleLine);
            Hot(cell, () => Edit(item), $"{(item.Count > 1 ? item.Count + " × " : "")}{item.Item} — {item.PlaceText}");
        }

        void DrawAdd(Graphics g, RectangleF cell, string place, string slot, string detail, string label)
        {
            Square(g, cell);
            if (label != null) CenterText(g, label, Fonts.Get(6.5f), Color.FromArgb(80, 255, 255, 255), cell);
            else CenterText(g, "+", Fonts.Get(12f), Color.FromArgb(90, 255, 255, 255), cell);
            Hot(cell, () => Add(place, slot, detail), slot != null ? "Equipar " + Slots.Title(slot).ToLowerInvariant() : "Añadir un item en " + Places.Title(place).ToLowerInvariant());
        }

        void DrawResults(Graphics g, float y)
        {
            var found = Results();
            if (found.Count == 0)
            {
                TextAt(g, $"Ningún char del perfil tiene “{query}”", Fonts.Get(9f), Theme.Muted, 0, y);
                return;
            }
            foreach (var (charName, item) in found)
            {
                var r = new RectangleF(0.5f, y, Width - 1, S(46));
                Card(g, r, radius: 10);
                Sprite(g, WikiImages.Get(item.Item), new RectangleF(r.X + S(10), r.Y + S(7), S(32), S(32)));
                TextAt(g, (item.Count > 1 ? item.Count + " × " : "") + item.Item, Fonts.Semi(9.5f), Color.White, r.X + S(52), r.Y + S(6));
                TextAt(g, charName.Length == 0 ? "Sin nombre" : charName, Fonts.Get(8.25f), Theme.Muted, r.X + S(52), r.Y + S(25));
                var font = Fonts.Bold(6.75f);
                float w = Measure(item.PlaceText.ToUpperInvariant(), font).Width + S(12);
                Chip(g, item.PlaceText.ToUpperInvariant(), Palette.Place(item.Place), r.Right - S(14) - w, r.Y + S(15));
                y += S(52);
            }
        }

        // ---------- Editar ----------

        IEnumerable<(string, string, string)> Catalog(string slot, string text) =>
            (text.Length > 0 && !ItemCatalog.For(slot).Any(i => string.Equals(i.Name, text, StringComparison.OrdinalIgnoreCase))
                ? new[] { (text, "usar este nombre", (string)null) } : Enumerable.Empty<(string, string, string)>())
            .Concat(ItemCatalog.For(slot).Where(i => text.Length == 0 || i.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(300).Select(i => (i.Name, Slots.Title(i.Slot), i.Name)));

        void Add(string place, string slot, string detail)
        {
            Popover pop = null;
            var picker = new PickerList(slot != null ? "Buscar " + Slots.Title(slot).ToLowerInvariant() : "Buscar item", text => Catalog(slot, text));
            picker.Padding = new Padding((int)S(10));
            picker.Picked += name =>
            {
                c.Items.Add(new OwnedItem { Item = name, Place = place, Slot = slot, Detail = detail ?? "" });
                store.Touch();
                pop?.Close();
                Relayout();
            };
            pop = Popover.Open(this, Cursor.Position, picker, new Size((int)S(320), (int)S(340)));
            picker.FocusSearch();
        }

        void Edit(OwnedItem item)
        {
            var panel = new ItemEditor(item, c, OtherChars, Catalog);
            Popover pop = null;
            panel.Changed += () => { store.Touch(); Relayout(); };
            panel.Removed += () => { c.Items.Remove(item); store.Touch(); pop?.Close(); Relayout(); };
            pop = Popover.Open(this, Cursor.Position, panel, new Size((int)S(330), (int)S(470)));
        }
    }

    /// Un item: cambiarlo por otro, cuántos hay, dónde está (moverlo) y quitarlo.
    sealed class ItemEditor : PaintedControl
    {
        readonly OwnedItem item;
        readonly GameChar c;
        readonly PickerList picker;
        readonly NumberField count = new NumberField { Minimum = 1, Maximum = 100000, Width = 70, Height = 26 };
        readonly RoundButton place = new RoundButton { Height = 28, FillColor = Theme.Field, BorderColor = Color.FromArgb(40, 255, 255, 255) };
        readonly RoundButton slot = new RoundButton { Height = 28, FillColor = Theme.Field, BorderColor = Color.FromArgb(40, 255, 255, 255) };
        readonly InputField detail = new InputField("");
        readonly RoundButton others = new RoundButton { Caption = "👥", Text = "Elegir otro char", Width = 32, Height = 28, FillColor = Theme.ControlFill };
        readonly RoundButton remove = new RoundButton { Text = "🗑  Quitar item", Height = 28, FillColor = Theme.Row, ForeColor = Color.FromArgb(255, 105, 97) };

        public event Action Changed, Removed;

        public ItemEditor(OwnedItem item, GameChar c, List<string> otherChars, Func<string, string, IEnumerable<(string, string, string)>> catalog)
        {
            this.item = item;
            this.c = c;
            BackColor = Theme.Row;
            picker = new PickerList("Cambiar por otro item", text => catalog(item.Place == "equipped" ? item.Slot : null, text));
            picker.Picked += name => { item.Item = name; Changed?.Invoke(); Invalidate(); };
            count.Value = item.Count;
            count.BackColor = Theme.Row;
            count.ValueCommitted += (s, e) => { item.Count = Math.Max(1, count.Value); Changed?.Invoke(); };
            place.Click += (s, e) =>
            {
                var menu = DarkMenu.Create();
                foreach (var p in Places.All)
                {
                    string value = p;
                    menu.Items.Add(Places.Title(p), null, (a, b) => { MoveTo(value); Changed?.Invoke(); Sync(); });
                }
                menu.Show(place, new Point(0, place.Height + 4));
            };
            slot.Click += (s, e) =>
            {
                var menu = DarkMenu.Create();
                foreach (var sl in Slots.All)
                {
                    string value = sl;
                    menu.Items.Add(Slots.Title(sl), null, (a, b) => { Equip(value); Changed?.Invoke(); Sync(); });
                }
                menu.Show(slot, new Point(0, slot.Height + 4));
            };
            detail.Changed += text => { item.Detail = text; Changed?.Invoke(); };
            others.Click += (s, e) =>
            {
                var menu = DarkMenu.Create();
                foreach (var name in otherChars) { string n = name; menu.Items.Add(n, null, (a, b) => { detail.Value = n; }); }
                if (otherChars.Count > 0) menu.Show(others, new Point(0, others.Height + 4));
            };
            others.Enabled = otherChars.Count > 0;
            remove.FitText(12);
            remove.Click += (s, e) => Removed?.Invoke();
            foreach (Control control in new Control[] { picker, count, place, slot, detail, others, remove }) Controls.Add(control);
            Sync();
        }

        /// Moverlo de sitio; al equiparlo va a su hueco (y lo que hubiera ahí pasa a la mochila).
        void MoveTo(string to)
        {
            if (to == "equipped") { Equip(item.Slot ?? ItemCatalog.SlotOf(item.Item) ?? "weapon"); return; }
            item.Place = to;
            item.Slot = null;
            item.Detail = "";
        }

        void Equip(string to)
        {
            foreach (var other in c.Items.Where(i => i != item && i.Place == "equipped" && i.Slot == to)) { other.Place = "backpack"; other.Slot = null; }
            item.Place = "equipped";
            item.Slot = to;
            item.Detail = "";
        }

        void Sync()
        {
            place.Text = Places.Title(item.Place) + "  ▾";
            place.FitText(10);
            slot.Text = Slots.Title(item.Slot ?? "weapon") + "  ▾";
            slot.FitText(10);
            bool equipped = item.Place == "equipped";
            slot.Visible = equipped;
            detail.Visible = !equipped;
            others.Visible = item.Place == "otherChar";
            detail.Value = item.Detail;
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float k = K;
            int pad = (int)(14 * k), w = Width - pad * 2;
            picker.SetBounds(pad, (int)(56 * k), w, (int)(200 * k));
            count.Location = new Point(pad + (int)(80 * k), (int)(272 * k));
            place.Location = new Point(pad, (int)(330 * k));
            slot.Location = new Point(pad, (int)(366 * k));
            int dw = w - (others.Visible ? others.Width + (int)(6 * k) : 0);
            detail.SetBounds(pad, (int)(366 * k), dw, (int)(28 * k));
            others.Location = new Point(pad + dw + (int)(6 * k), (int)(366 * k));
            remove.Location = new Point(pad - (int)(6 * k), Height - pad - remove.Height);
            detail.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            var image = WikiImages.Get(item.Item);
            if (image != null) Resources.DrawPixelArt(g, image, new RectangleF(14 * k, 12 * k, 32 * k, 32 * k));
            TextRenderer.DrawText(g, item.Item, Fonts.Bold(11f), new Rectangle((int)(54 * k), (int)(16 * k), Width - (int)(68 * k), (int)(24 * k)), Color.White,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "Cantidad", Font, new Point((int)(14 * k), (int)(276 * k)), Theme.Muted, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "DÓNDE ESTÁ", Fonts.Semi(7.5f), new Point((int)(14 * k), (int)(312 * k)), Theme.Muted, TextFormatFlags.NoPadding);
            if (item.Place != "equipped")
                TextRenderer.DrawText(g, Places.DetailPrompt(item.Place), Fonts.Get(7.5f), new Point((int)(14 * k), (int)(398 * k)), Theme.Muted, TextFormatFlags.NoPadding);
        }
    }
}

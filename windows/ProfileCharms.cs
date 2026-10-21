using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Pestaña Charms, como el Charm Optimizer de hakaimarket.com: primero el optimizador (con los charms
    /// de daño que tiene el char, las criaturas de su hunt y cuántas mata por hora, qué charm poner en cada
    /// una) y, en "Mis charms", los charm points del Bestiary y gastados y el nivel de cada charm.
    sealed class CharmsCanvas : HitCanvas
    {
        readonly GameChar c;
        readonly ProfileStore store;
        readonly InputField search = new InputField("Añadir criatura…") { Height = 28 };
        static int mode;          // 0 optimizador, 1 mis charms
        static bool showTable;
        // También para las capturas de prueba.
        public static int Mode { get => mode; set => mode = value; }
        public static bool ShowTable { get => showTable; set => showTable = value; }
        string query = "";
        float searchY = -1;

        public CharmsCanvas(GameChar c, ProfileStore store)
        {
            this.c = c;
            this.store = store;
            Margin = new Padding(0, 0, 0, 8);
            search.FieldColor = Theme.Field;
            search.Changed += text => { query = text.Trim(); Relayout(); };
            search.Submitted += () => { var first = Matches().FirstOrDefault(); if (first != null) AddCreature(first.Name); };
            Controls.Add(search);
        }

        // ---------- Datos ----------

        List<(CharmInfo Charm, int Tier)> Usable()
        {
            var owned = CharmCatalog.Damage.Where(ch => c.CharmTier(ch.Name) > 0).Select(ch => (ch, c.CharmTier(ch.Name))).ToList();
            return owned.Count > 0 ? owned : CharmCatalog.Damage.Select(ch => (ch, 1)).ToList();
        }

        List<(HuntCreature Entry, BestiaryCreature Creature)> Hunt() =>
            c.Hunt.Select(h => (h, BestiaryCatalog.Find(h.Name))).Where(p => p.Item2 != null).ToList();

        int Damage(CharmInfo charm, BestiaryCreature b) => CharmCatalog.DamageOf(charm, b, c.Level, c.Vocation);

        /// Daño extra por hora (en proporción): los ataques que van a esa criatura (sus kills por hora por su
        /// vida) × la probabilidad del charm × su daño.
        double Score(HuntCreature entry, BestiaryCreature b, CharmInfo charm, int tier) =>
            (double)entry.PerHour * b.Hp * CharmInfo.Chances[tier - 1] * Damage(charm, b);

        /// El mejor reparto: cada charm en una sola criatura y cada criatura con un solo charm (programación
        /// dinámica sobre qué charms ya se usaron).
        List<(BestiaryCreature Creature, CharmInfo Charm, int Tier)> Assignment()
        {
            var creatures = Hunt();
            var charms = Usable();
            if (creatures.Count == 0 || charms.Count > 16) return new List<(BestiaryCreature, CharmInfo, int)>();
            var memo = new Dictionary<(int, int), (double, List<int>)>();
            (double, List<int>) Best(int i, int used)
            {
                if (i == creatures.Count) return (0, new List<int>());
                if (memo.TryGetValue((i, used), out var hit)) return hit;
                var skip = Best(i + 1, used);
                var result = (skip.Item1, new List<int> { -1 }.Concat(skip.Item2).ToList());
                for (int j = 0; j < charms.Count; j++)
                {
                    if ((used & (1 << j)) != 0) continue;
                    double gain = Score(creatures[i].Entry, creatures[i].Creature, charms[j].Charm, charms[j].Tier);
                    if (gain <= 0) continue;
                    var rest = Best(i + 1, used | (1 << j));
                    if (rest.Item1 + gain > result.Item1) result = (rest.Item1 + gain, new List<int> { j }.Concat(rest.Item2).ToList());
                }
                memo[(i, used)] = result;
                return result;
            }
            var picks = Best(0, 0).Item2;
            var output = new List<(BestiaryCreature, CharmInfo, int)>();
            for (int i = 0; i < creatures.Count; i++)
                if (picks[i] >= 0) output.Add((creatures[i].Creature, charms[picks[i]].Charm, charms[picks[i]].Tier));
            return output;
        }

        List<BestiaryCreature> Matches() => query.Length == 0 ? new List<BestiaryCreature>()
            : BestiaryCatalog.Creatures.Where(b => b.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 && !c.Hunt.Any(h => h.Name == b.Name)).Take(6).ToList();

        void AddCreature(string name)
        {
            if (c.Hunt.Any(h => h.Name == name)) return;
            c.Hunt.Add(new HuntCreature { Name = name });
            store.Touch();
            search.Value = "";
            query = "";
            Relayout();
        }

        /// El elemento al que más daño recibe (si es más del 100 %); si empatan, todos.
        static string Weakness(BestiaryCreature b)
        {
            var names = new Dictionary<string, string> { ["physical"] = "físico", ["fire"] = "fuego", ["ice"] = "hielo", ["energy"] = "energía", ["earth"] = "tierra", ["holy"] = "sagrado", ["death"] = "muerte" };
            var weak = names.Keys.Where(k => b.Mod(k) > 100).ToList();
            if (weak.Count == 0) return null;
            int top = weak.Max(k => b.Mod(k));
            return $"débil a {string.Join(" y ", weak.Where(k => b.Mod(k) == top).Select(k => names[k]))} ({top} %)";
        }

        // ---------- Medidas ----------

        const float LabelW = 98, HuntCard = 76, ResultCard = 168;

        int Columns(float width, float min) => Math.Max(1, (int)((width + S(8)) / (S(min) + S(8))));

        float SetupHeight(float width)
        {
            float inner = width - S(32) - S(LabelW);
            int charms = CharmCatalog.Damage.Count;
            int perRow = Columns(inner, 66);
            float h = S(16) + S(28) + S(10) + ((charms + perRow - 1) / perRow) * S(76) + S(14) + S(36);
            if (Matches().Count > 0) h += Matches().Count * S(30) + S(8);
            int hunt = Hunt().Count;
            h += hunt == 0 ? S(40) : ((hunt + Columns(width - S(32), 220) - 1) / Columns(width - S(32), 220)) * (S(HuntCard) + S(8));
            return h + S(12);
        }

        float ResultsHeight(float width)
        {
            var picks = Assignment();
            float h = S(16) + S(28) + ((picks.Count + Columns(width - S(32), 160) - 1) / Columns(width - S(32), 160)) * (S(ResultCard) + S(10));
            if (picks.Count < Hunt().Count) h += S(20);
            h += S(30);
            if (showTable) h += S(40) + Hunt().Count * S(26) + S(8);
            return h + S(10);
        }

        float MineHeight(float width)
        {
            float h = S(78) + S(16);
            foreach (var list in new[] { CharmCatalog.Major.ToList(), CharmCatalog.Minor.ToList() })
                h += S(30) + ((list.Count + Columns(width, 108) - 1) / Columns(width, 108)) * (S(92) + S(8)) + S(10);
            return h;
        }

        public override int HeightFor(int width)
        {
            float h = S(40);
            if (mode == 0) { h += SetupHeight(width); if (Hunt().Count > 0) h += S(14) + ResultsHeight(width); }
            else h += MineHeight(width);
            return (int)(h + S(10));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            search.Visible = searchY >= 0;
            if (searchY >= 0)
            {
                float zone = ButtonWidth("🗺  Toda una zona") + (c.Hunt.Count > 0 ? ButtonWidth("🗑  Vaciar") + S(8) : 0) + S(8);
                search.SetBounds((int)(S(16) + S(LabelW)), (int)searchY, (int)(Width - S(32) - S(LabelW) - zone), (int)S(28));
            }
        }

        void PlaceSearch(float y)
        {
            if (Math.Abs(searchY - y) < 0.5f) return;
            searchY = y;
            BeginInvoke((Action)(() => { if (!IsDisposed) PerformLayout(); }));
        }

        // ---------- Dibujo ----------

        protected override void Draw(Graphics g, Rectangle visible)
        {
            Pills(g, new[] { "Optimizador", "Mis charms" }, mode, 0, 0, i => { mode = i; Relayout(); });
            if (mode == 0)
            {
                var info = new RectangleF(Width - S(28), 0, S(28), S(26));
                CenterText(g, "ⓘ", Fonts.Get(12f), Theme.Muted, info);
                Hot(info, null, "Pon las criaturas de tu hunt y cuántas matas por hora: el optimizador reparte tus charms de daño\n(los elementales, Overpower y Overflux) para sacar el máximo daño extra. Los elementales hacen el 5 % de\nla vida de la criatura (como mucho el doble de tu nivel) por su debilidad a ese elemento; Overpower, el 5 %\nde tu vida, y Overflux, el 2,5 % de tu maná (los dos hasta el 8 % de la vida de la criatura). Se activan con\nun 5 / 10 / 11 % de probabilidad según el nivel del charm. Low Blow y Savage Blow dependen de los críticos\nde tu arma y no se calculan. En el juego solo se puede poner un charm en criaturas con el Bestiary completo.");
                float y = S(40);
                DrawSetup(g, new RectangleF(0.5f, y, Width - 1, SetupHeight(Width)));
                y += SetupHeight(Width) + S(14);
                if (Hunt().Count > 0) DrawResults(g, new RectangleF(0.5f, y, Width - 1, ResultsHeight(Width)));
            }
            else
            {
                PlaceSearch(-1);
                DrawMine(g, S(40));
            }
        }

        void DrawSetup(Graphics g, RectangleF r)
        {
            Card(g, r);
            float x = r.X + S(16), y = r.Y + S(16), content = x + S(LabelW);
            // Personaje
            TextAt(g, "Personaje", Fonts.Semi(9f), Theme.Muted, x, y + S(5));
            float cx = content;
            cx += Chip(g, "NIVEL " + N(c.Level), Theme.Green, cx, y + S(6)) + S(6);
            Chip(g, Vocations.Title(c.Vocation).ToUpperInvariant(), Theme.Blue, cx, y + S(6));
            string stats = $"♥ {N(Vocations.HitPoints(c.Vocation, c.Level))}     ⚡ {N(Vocations.Mana(c.Vocation, c.Level))}";
            Write(g, stats, Fonts.Semi(9f), Color.FromArgb(220, 220, 225), new RectangleF(r.Right - S(260), y + S(4), S(244), S(20)), TextFormatFlags.Right | TextFormatFlags.SingleLine);
            y += S(28) + S(10);
            // Charms de daño
            TextAt(g, "Tus charms", Fonts.Semi(9f), Theme.Muted, x, y + S(4));
            TextAt(g, "clic: nivel", Fonts.Get(7.75f), Theme.Muted, x, y + S(22));
            float inner = r.Right - S(16) - content;
            int perRow = Columns(inner, 66);
            float tw = (inner - (perRow - 1) * S(6)) / perRow;
            var damage = CharmCatalog.Damage;
            for (int i = 0; i < damage.Count; i++)
            {
                var charm = damage[i];
                int tier = c.CharmTier(charm.Name);
                var t = new RectangleF(content + (i % perRow) * (tw + S(6)), y + (i / perRow) * S(76), tw, S(70));
                Fill(g, t, S(8), tier > 0 ? Color.FromArgb(36, Palette.Purple) : Color.FromArgb(8, 255, 255, 255));
                Stroke(g, t, S(8), Hovered(t) ? Theme.Blue : tier > 0 ? Color.FromArgb(120, Palette.Purple) : Palette.Edge);
                var image = WikiImages.Get(charm.Image);
                if (image != null)
                {
                    var box = new RectangleF(t.X + (t.Width - S(30)) / 2, t.Y + S(6), S(30), S(30));
                    if (tier == 0) using (var attrs = Dim()) g.DrawImage(image, Rectangle.Round(box), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attrs);
                    else Sprite(g, image, box, 2);
                }
                Write(g, charm.Name, Fonts.Get(7.75f), Color.White, new RectangleF(t.X + S(2), t.Y + S(38), t.Width - S(4), S(14)), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                CenterText(g, tier > 0 ? new[] { "I", "II", "III" }[tier - 1] : "—", Fonts.Bold(7.5f), tier > 0 ? Palette.Purple : Theme.Muted, new RectangleF(t.X, t.Y + S(53), t.Width, S(14)));
                var ch = charm;
                Hot(t, () => { SetTier(ch, (c.CharmTier(ch.Name) + 1) % 4); }, tier > 0 ? $"{charm.Name} nivel {tier}. Clic para cambiarlo." : $"No tienes {charm.Name}. Clic para marcarlo.");
            }
            y += ((damage.Count + perRow - 1) / perRow) * S(76) + S(4);
            using (var pen = new Pen(Color.FromArgb(18, 255, 255, 255))) g.DrawLine(pen, x, y + S(4), r.Right - S(16), y + S(4));
            y += S(14);
            // Hunt
            TextAt(g, "Tu hunt", Fonts.Semi(9f), Theme.Muted, x, y + S(6));
            PlaceSearch(y);
            float bx = r.Right - S(16);
            if (c.Hunt.Count > 0)
            {
                bx -= ButtonWidth("🗑  Vaciar");
                Button(g, "🗑  Vaciar", bx, y, () => { c.Hunt.Clear(); store.Touch(); Relayout(); });
                bx -= S(8);
            }
            bx -= ButtonWidth("🗺  Toda una zona");
            Button(g, "🗺  Toda una zona", bx, y, OpenPlaces, tip: "Añade todas las criaturas de una zona (según TibiaWiki)");
            y += S(36);
            var matches = Matches();
            if (matches.Count > 0)
            {
                var list = new RectangleF(content, y, r.Right - S(16) - content, matches.Count * S(30));
                Fill(g, list, S(8), Color.FromArgb(14, 255, 255, 255));
                for (int i = 0; i < matches.Count; i++)
                {
                    var b = matches[i];
                    var row = new RectangleF(list.X, list.Y + i * S(30), list.Width, S(30));
                    if (Hovered(row)) Fill(g, row, S(6), Color.FromArgb(20, 255, 255, 255));
                    Sprite(g, WikiImages.Get(b.Name), new RectangleF(row.X + S(8), row.Y + S(3), S(24), S(24)));
                    TextAt(g, b.Name, Fonts.Get(9f), Color.White, row.X + S(40), row.Y + S(7));
                    Write(g, N(b.Hp) + " HP   ⊕", Fonts.Get(8.5f), Theme.Muted, new RectangleF(row.Right - S(140), row.Y, S(130), row.Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    string name = b.Name;
                    Hot(row, () => AddCreature(name));
                }
                y += list.Height + S(8);
            }
            var hunt = Hunt();
            if (hunt.Count == 0)
            {
                TextAt(g, "Añade las criaturas que cazas (o toda una zona) y verás qué charm conviene en cada una.", Fonts.Get(9f), Theme.Muted, content, y + S(8));
                return;
            }
            int cols = Columns(r.Width - S(32), 220);
            float cw = (r.Width - S(32) - (cols - 1) * S(8)) / cols;
            for (int i = 0; i < hunt.Count; i++)
            {
                var (entry, b) = hunt[i];
                var card = new RectangleF(x + (i % cols) * (cw + S(8)), y + (i / cols) * (S(HuntCard) + S(8)), cw, S(HuntCard));
                Fill(g, card, S(10), Color.FromArgb(10, 255, 255, 255));
                Sprite(g, WikiImages.Get(b.Name), new RectangleF(card.X + S(10), card.Y + S(14), S(46), S(46)));
                float tx = card.X + S(64);
                var nameFont = Fonts.Semi(9f);
                Write(g, b.Name, nameFont, Color.White, new RectangleF(tx, card.Y + S(8), card.Right - tx - S(40), S(18)));
                if (!c.Completed(b))
                {
                    var warn = new RectangleF(tx + Math.Min(Measure(b.Name, nameFont).Width + S(4), card.Right - tx - S(56)), card.Y + S(8), S(16), S(16));
                    TextAt(g, "⚠", Fonts.Get(8.5f), Theme.Amber, warn.X, warn.Y);
                    Hot(warn, null, "Bestiary sin completar: en el juego todavía no se le puede poner un charm");
                }
                Write(g, N(b.Hp) + " HP" + (Weakness(b) is string weak ? " · " + weak : ""), Fonts.Get(7.75f), Theme.Muted, new RectangleF(tx, card.Y + S(27), card.Right - tx - S(10), S(14)));
                var minus = new RectangleF(tx, card.Y + S(46), S(22), S(22));
                var plus = new RectangleF(tx + S(84), card.Y + S(46), S(22), S(22));
                foreach (var (button, glyph, delta) in new[] { (minus, "−", -50), (plus, "+", 50) })
                {
                    using (var brush = new SolidBrush(Hovered(button) ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(20, 255, 255, 255))) g.FillEllipse(brush, button);
                    CenterText(g, glyph, Fonts.Bold(9f), Color.White, button);
                    var e2 = entry;
                    int d = delta;
                    Hot(button, () => { e2.PerHour = Math.Max(1, e2.PerHour + d); store.Touch(); Invalidate(); }, d < 0 ? "50 menos por hora" : "50 más por hora");
                }
                var value = new RectangleF(tx + S(26), card.Y + S(45), S(54), S(24));
                Fill(g, value, S(6), Theme.Field);
                CenterText(g, N(entry.PerHour), Fonts.Semi(9f), Color.White, value);
                TextAt(g, "/ hora", Fonts.Get(8f), Theme.Muted, plus.Right + S(6), card.Y + S(49));
                var close = new RectangleF(card.Right - S(26), card.Y + S(6), S(20), S(20));
                CenterText(g, "✕", Fonts.Get(8f), Hovered(close) ? Color.White : Theme.Muted, close);
                var e3 = entry;
                Hot(close, () => { c.Hunt.Remove(e3); store.Touch(); Relayout(); }, "Quitar de la hunt");
            }
        }

        static System.Drawing.Imaging.ImageAttributes Dim()
        {
            var attrs = new System.Drawing.Imaging.ImageAttributes();
            attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.35f });
            return attrs;
        }

        void SetTier(CharmInfo charm, int tier)
        {
            if (tier > 0) c.Charms[charm.Name] = tier; else c.Charms.Remove(charm.Name);
            store.Touch();
            Relayout();
        }

        void OpenPlaces()
        {
            var counts = new Dictionary<string, int>();
            foreach (var b in BestiaryCatalog.Creatures) foreach (var p in b.Places) counts[p] = counts.TryGetValue(p, out int n) ? n + 1 : 1;
            var places = counts.Where(p => p.Value >= 2).OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
            Popover pop = null;
            var picker = new PickerList("Buscar zona (Roshamuul, Edron…)", text => places
                .Where(p => text.Length == 0 || p.Key.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => (p.Key, p.Value + " criaturas", (string)null)));
            picker.Picked += place =>
            {
                foreach (var b in BestiaryCatalog.Creatures.Where(b => b.Places.Contains(place) && !b.Rare))
                    if (!c.Hunt.Any(h => h.Name == b.Name)) c.Hunt.Add(new HuntCreature { Name = b.Name });
                store.Touch();
                pop?.Close();
                Relayout();
            };
            pop = Popover.Open(this, Cursor.Position, picker, new Size((int)S(320), (int)S(320)));
            picker.Padding = new Padding((int)S(10));
            picker.FocusSearch();
        }

        void DrawResults(Graphics g, RectangleF r)
        {
            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.FromArgb(48, 40, 30), Palette.Card, System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            using (var path = CounterRenderer.RoundedRect(r, S(12)))
                g.FillPath(brush, path);
            Stroke(g, r, S(12), Color.FromArgb(120, Theme.Amber), S(1.5f));
            float x = r.X + S(16), y = r.Y + S(16);
            TextAt(g, "🏆  Mejor combinación", Fonts.Bold(11f), Color.White, x, y);
            Chip(g, "BEST", Theme.Amber, x + Measure("🏆  Mejor combinación", Fonts.Bold(11f)).Width + S(10), y + S(3));
            if (CharmCatalog.Damage.All(ch => c.CharmTier(ch.Name) == 0))
                Write(g, "Con todos los charms de daño: marca arriba los que tienes", Fonts.Get(8.5f), Theme.Amber, new RectangleF(r.Right - S(380), y + S(2), S(364), S(18)), TextFormatFlags.Right | TextFormatFlags.SingleLine);
            y += S(28);
            var picks = Assignment();
            int cols = Columns(r.Width - S(32), 160);
            float cw = (r.Width - S(32) - (cols - 1) * S(10)) / cols;
            for (int i = 0; i < picks.Count; i++)
            {
                var (b, charm, tier) = picks[i];
                var card = new RectangleF(x + (i % cols) * (cw + S(10)), y + (i / cols) * (S(ResultCard) + S(10)), cw, S(ResultCard));
                Fill(g, card, S(10), Color.FromArgb(60, 0, 0, 0));
                Stroke(g, card, S(10), Palette.Edge);
                float mid = card.X + card.Width / 2;
                Sprite(g, WikiImages.Get(charm.Image), new RectangleF(mid - S(64), card.Y + S(12), S(42), S(42)), 2);
                CenterText(g, "→", Fonts.Bold(11f), Theme.Muted, new RectangleF(mid - S(16), card.Y + S(22), S(32), S(22)));
                Sprite(g, WikiImages.Get(b.Name), new RectangleF(mid + S(22), card.Y + S(12), S(42), S(42)));
                CenterText(g, charm.Name, Fonts.Bold(10f), Color.White, new RectangleF(card.X, card.Y + S(60), card.Width, S(20)));
                CenterText(g, b.Name, Fonts.Get(8.5f), Theme.Muted, new RectangleF(card.X + S(4), card.Y + S(80), card.Width - S(8), S(18)));
                CenterText(g, N(Damage(charm, b)), Fonts.Bold(15f), Theme.Green, new RectangleF(card.X, card.Y + S(100), card.Width, S(30)));
                CenterText(g, $"por activación · {(int)(CharmInfo.Chances[tier - 1] * 100)} % (nivel {new[] { "I", "II", "III" }[tier - 1]})", Fonts.Get(7.75f), Theme.Muted,
                    new RectangleF(card.X, card.Y + S(134), card.Width, S(16)));
            }
            y += ((picks.Count + cols - 1) / cols) * (S(ResultCard) + S(10));
            if (picks.Count < Hunt().Count)
            {
                TextAt(g, "Hay más criaturas que charms de daño: las que quedan sin charm son en las que menos aporta.", Fonts.Get(8.5f), Theme.Muted, x, y);
                y += S(20);
            }
            string toggle = showTable ? "⌃  Ocultar el daño de cada charm" : "▦  Ver el daño de cada charm en cada criatura";
            var link = new RectangleF(x, y + S(4), Measure(toggle, Fonts.Semi(9f)).Width + 4, S(20));
            TextAt(g, toggle, Fonts.Semi(9f), Theme.Blue, link.X, link.Y);
            Hot(link, () => { showTable = !showTable; Relayout(); });
            y += S(30);
            if (showTable) DrawTable(g, x, y, r.Width - S(32));
        }

        void DrawTable(Graphics g, float x, float y, float width)
        {
            var charms = Usable().Select(u => u.Charm).ToList();
            float nameW = S(160), colW = Math.Min(S(76), (width - nameW) / Math.Max(1, charms.Count));
            for (int i = 0; i < charms.Count; i++)
            {
                float cx = x + nameW + i * colW;
                Sprite(g, WikiImages.Get(charms[i].Image), new RectangleF(cx + (colW - S(20)) / 2, y, S(20), S(20)), 2);
                Write(g, charms[i].Name, Fonts.Get(7.25f), Theme.Muted, new RectangleF(cx, y + S(22), colW, S(14)), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            y += S(40);
            var hunt = Hunt();
            for (int r = 0; r < hunt.Count; r++)
            {
                var b = hunt[r].Creature;
                if (r % 2 == 0) using (var brush = new SolidBrush(Color.FromArgb(10, 255, 255, 255))) g.FillRectangle(brush, x, y, nameW + charms.Count * colW, S(26));
                Sprite(g, WikiImages.Get(b.Name), new RectangleF(x + S(4), y + S(3), S(20), S(20)));
                Write(g, b.Name, Fonts.Get(8.5f), Color.White, new RectangleF(x + S(30), y, nameW - S(34), S(26)), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                var values = charms.Select(ch => Damage(ch, b)).ToList();
                int top = values.Count == 0 ? 0 : values.Max();
                for (int i = 0; i < values.Count; i++)
                    Write(g, N(values[i]), values[i] == top && top > 0 ? Fonts.Bold(8.5f) : Fonts.Get(8.5f), values[i] == top && top > 0 ? Theme.Green : Color.FromArgb(200, 200, 205),
                        new RectangleF(x + nameW + i * colW, y, colW, S(26)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                y += S(26);
            }
        }

        // Mis charms

        int Spent(IEnumerable<CharmInfo> charms) => charms.Sum(ch => ch.Costs.Take(c.CharmTier(ch.Name)).Sum());

        void DrawMine(Graphics g, float y)
        {
            int earned = c.CharmPoints, major = Spent(CharmCatalog.Major), minor = Spent(CharmCatalog.Minor);
            var tiles = new[]
            {
                ("✦  Charm points del Bestiary", Palette.Purple, N(earned), $"de {N(BestiaryCatalog.TotalCharms)} con todo completo"),
                ("🛒  Gastados en mayores", Theme.Amber, N(major), earned >= major ? $"quedan {N(earned - major)}" : "más de los que da su Bestiary"),
                ("⬡  Gastados en menores", Theme.Blue, N(minor), $"{CharmCatalog.Minor.Count(ch => c.CharmTier(ch.Name) > 0)} de {CharmCatalog.Minor.Count()} desbloqueados"),
            };
            float w = (Width - S(20)) / 3;
            for (int i = 0; i < tiles.Length; i++)
            {
                var (title, tint, value, detail) = tiles[i];
                var r = new RectangleF(i * (w + S(10)) + 0.5f, y, w - 1, S(78));
                Card(g, r);
                Write(g, title, Fonts.Semi(8.5f), tint, new RectangleF(r.X + S(12), r.Y + S(10), r.Width - S(24), S(18)));
                Write(g, value, Fonts.Bold(15f), Color.White, new RectangleF(r.X + S(12), r.Y + S(28), r.Width - S(24), S(28)));
                Write(g, detail, Fonts.Get(8f), Theme.Muted, new RectangleF(r.X + S(12), r.Y + S(56), r.Width - S(24), S(16)));
            }
            y += S(78) + S(16);
            foreach (var (title, list) in new[] { ("Charms mayores", CharmCatalog.Major.ToList()), ("Charms menores", CharmCatalog.Minor.ToList()) })
            {
                var font = Fonts.Bold(10.5f);
                TextAt(g, title, font, Color.White, 0, y);
                float tx = Measure(title, font).Width + S(8);
                TextAt(g, $"{list.Count(ch => c.CharmTier(ch.Name) > 0)}/{list.Count}", Fonts.Semi(9f), Theme.Muted, tx, y + S(3));
                TextAt(g, "Clic para cambiar el nivel; clic derecho para elegirlo con su coste", Fonts.Get(8.25f), Theme.Muted, tx + S(40), y + S(3));
                y += S(30);
                int cols = Columns(Width, 108);
                float cw = (Width - (cols - 1) * S(8)) / cols;
                for (int i = 0; i < list.Count; i++)
                {
                    var charm = list[i];
                    int tier = c.CharmTier(charm.Name);
                    var t = new RectangleF((i % cols) * (cw + S(8)) + 0.5f, y + (i / cols) * (S(92) + S(8)), cw - 1, S(92));
                    Fill(g, t, S(10), tier > 0 ? Color.FromArgb(30, Palette.Purple) : Palette.Card);
                    Stroke(g, t, S(10), Hovered(t) ? Theme.Blue : tier > 0 ? Color.FromArgb(110, Palette.Purple) : Palette.Edge);
                    var image = WikiImages.Get(charm.Image);
                    var box = new RectangleF(t.X + (t.Width - S(36)) / 2, t.Y + S(8), S(36), S(36));
                    if (image != null)
                    {
                        if (tier == 0) using (var attrs = Dim()) g.DrawImage(image, Rectangle.Round(box), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attrs);
                        else Sprite(g, image, box, 2);
                    }
                    Write(g, charm.Name, Fonts.Semi(8.25f), Color.White, new RectangleF(t.X + S(4), t.Y + S(50), t.Width - S(8), S(16)), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    for (int level = 1; level <= 3; level++)
                        Fill(g, new RectangleF(t.X + t.Width / 2 - S(26) + (level - 1) * S(18), t.Y + S(74), S(14), S(4)), S(2),
                            level <= tier ? Palette.Purple : Color.FromArgb(30, 255, 255, 255));
                    var ch = charm;
                    string costs = string.Join(" / ", charm.Costs.Select(v => N(v)));
                    Hot(t, () => SetTier(ch, (c.CharmTier(ch.Name) + 1) % 4), $"{charm.Name} ({(charm.Major ? "mayor" : "menor")}): {charm.Effect}\nCoste por nivel: {costs}", at => TierMenu(ch, at));
                }
                y += ((list.Count + cols - 1) / cols) * (S(92) + S(8)) + S(10);
            }
        }

        void TierMenu(CharmInfo charm, Point at)
        {
            var menu = DarkMenu.Create();
            menu.Items.Add("Sin desbloquear", null, (a, b) => SetTier(charm, 0));
            for (int level = 1; level <= 3; level++)
            {
                int l = level;
                string cost = charm.Costs.Count >= l ? $" ({N(charm.Costs[l - 1])})" : "";
                menu.Items.Add($"Nivel {new[] { "I", "II", "III" }[l - 1]}{cost}", null, (a, b) => SetTier(charm, l));
            }
            menu.Show(at);
        }
    }
}

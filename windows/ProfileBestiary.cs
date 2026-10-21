using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Pestaña Bestiary, como el Bestiary Tracker de tibiaroute.com: resumen, todas las criaturas en
    /// cuadrícula por clase (con filtros de clase, dificultad y estado, y orden), o las recomendaciones
    /// para terminar solo (Single) o en grupo por zona (Group). Clic en una criatura: su ficha. Las
    /// recomendaciones se calculan con los datos de TibiaWiki y el nivel del char; son orientativas.
    sealed class BestiaryCanvas : HitCanvas
    {
        readonly GameChar c;
        readonly ProfileStore store;
        readonly InputField search;
        // Lo elegido se mantiene al cambiar de pestaña o de char.
        static int mode, filter, sort;
        /// Todas / Single / Group (también para las capturas de prueba).
        public static int Mode { get => mode; set => mode = value; }
        static string klass = "", difficulty = "", query = "", openPlace;
        static readonly string[] Filters = { "Todas", "En curso", "Completas", "Sin empezar" };
        static readonly string[] Sorts = { "Clase", "Nombre", "Charm points", "Progreso", "HP", "Kills que faltan" };

        const float TileW = 104, TileH = 124, Gap = 8, TopH = 76;

        public BestiaryCanvas(GameChar c, ProfileStore store)
        {
            this.c = c;
            this.store = store;
            Margin = new Padding(0, 0, 0, 8);
            search = new InputField("Buscar criatura", query) { Width = 200, Height = 28 };
            search.Changed += text => { query = text; Relayout(); };
            Controls.Add(search);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float pills = Measure("Todas", Fonts.Semi(8.75f)).Width + Measure("Single", Fonts.Semi(8.75f)).Width + Measure("Group", Fonts.Semi(8.75f)).Width + S(24) * 3 + S(8);
            search.SetBounds((int)(pills + S(10)), 0, (int)Math.Min(S(220), Width - pills - S(60)), (int)S(28));
        }

        // ---------- Datos ----------

        int Remaining(BestiaryCreature b) => Math.Max(0, b.Total - c.KillsOf(b));

        IEnumerable<BestiaryCreature> Base()
        {
            string text = query.Trim();
            return BestiaryCatalog.Creatures.Where(b => (klass.Length == 0 || b.Class == klass) && (difficulty.Length == 0 || b.Level == difficulty)
                && (text.Length == 0 || b.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        List<BestiaryCreature> Creatures()
        {
            var list = Base().Where(b =>
            {
                int kills = c.KillsOf(b);
                switch (filter)
                {
                    case 1: return kills > 0 && kills < b.Total;
                    case 2: return kills >= b.Total;
                    case 3: return kills == 0;
                    default: return true;
                }
            });
            double Progress(BestiaryCreature b) => (double)Math.Min(c.KillsOf(b), b.Total) / b.Total;
            switch (sort)
            {
                case 2: return list.OrderByDescending(b => b.Charms).ThenBy(b => b.Name).ToList();
                case 3: return list.OrderByDescending(Progress).ThenBy(b => b.Name).ToList();
                case 4: return list.OrderBy(b => b.Hp).ThenBy(b => b.Name).ToList();
                case 5: return list.OrderBy(Remaining).ThenBy(b => b.Name).ToList();
                default: return list.ToList();
            }
        }

        /// Golpes (aprox.) para matar una criatura según el nivel del char: cuanto más HP, más tarda.
        double Effort(BestiaryCreature b) => Math.Max(1, (double)b.Hp / (c.Level * 4 + 100));

        /// Charm points de la criatura, por lo fácil que es matarla, por las kills que faltan (raíz) y por
        /// en cuántos sitios vive (las de un solo sitio suelen ser de evento o escasas).
        double SingleScore(BestiaryCreature b) => b.Charms * (Math.Min(b.Places.Count, 6) / 6.0) * 100 / (Effort(b) * Math.Sqrt(Remaining(b)));

        List<BestiaryCreature> Singles() =>
            Base().Where(b => !b.Rare && Remaining(b) > 0 && b.Hp <= c.Level * 60 && b.Places.Count > 0)
                .OrderByDescending(SingleScore).Take(40).ToList();

        (string Word, Color Tint) Ease(BestiaryCreature b)
        {
            double ratio = (double)b.Hp / Math.Max(1, c.Level);
            return ratio < 5 ? ("FÁCIL", Theme.Green) : ratio < 20 ? ("NORMAL", Theme.Amber) : ("DIFÍCIL", Palette.Red);
        }

        /// Zonas con 2 o más criaturas sin terminar, por charm points por conseguir.
        List<(string Name, List<BestiaryCreature> Creatures, int Charms, int Difficulty)> Places()
        {
            var byPlace = new Dictionary<string, List<BestiaryCreature>>();
            foreach (var b in Base().Where(b => !b.Rare && Remaining(b) > 0))
                foreach (var place in b.Places)
                {
                    if (!byPlace.TryGetValue(place, out var list)) byPlace[place] = list = new List<BestiaryCreature>();
                    list.Add(b);
                }
            return byPlace.Where(p => p.Value.Count >= 2).Select(p =>
            {
                int maxHp = p.Value.Max(b => b.Hp);
                int diff = Math.Min(10, Math.Max(1, (int)((double)maxHp / Math.Max(1, c.Level) / 4) + 1));
                return (p.Key, p.Value.OrderBy(Remaining).ToList(), p.Value.Sum(b => b.Charms), diff);
            }).OrderByDescending(p => p.Item3).ThenByDescending(p => p.Item2.Count).Take(30).ToList();
        }

        // ---------- Medidas ----------

        int Columns(float width) => Math.Max(1, (int)((width + S(Gap)) / (S(TileW) + S(Gap))));

        float GridHeight(int count, float width)
        {
            int rows = (count + Columns(width) - 1) / Columns(width);
            return rows == 0 ? 0 : rows * (S(TileH) + S(Gap)) - S(Gap);
        }

        public override int HeightFor(int width)
        {
            float h = S(TopH);
            switch (mode)
            {
                case 1:
                    h += Math.Max(1, Singles().Count) * S(62);
                    break;
                case 2:
                    foreach (var p in Places())
                        h += S(104) + (p.Name == openPlace ? GridHeight(p.Creatures.Count, width - S(24)) + S(10) : 0) + S(8);
                    if (openPlace == null && !Places().Any()) h += S(100);
                    break;
                default:
                    var list = Creatures();
                    h += S(64) + S(12);
                    if (list.Count == 0) h += S(100);
                    else if (sort == 0)
                        foreach (var group in list.GroupBy(b => b.Class).OrderBy(gr => gr.Key, StringComparer.Ordinal))
                            h += S(32) + GridHeight(group.Count(), width) + S(16);
                    else h += GridHeight(list.Count, width);
                    break;
            }
            return (int)(h + S(16));
        }

        // ---------- Dibujo ----------

        protected override void Draw(Graphics g, Rectangle visible)
        {
            Pills(g, new[] { "Todas", "Single", "Group" }, mode, 0, S(1), i => { mode = i; Relayout(); });
            float right = Width;
            if (mode == 0)
            {
                var more = new RectangleF(right - S(30), 0, S(30), S(28));
                if (Hovered(more)) Fill(g, more, S(14), Color.FromArgb(30, 255, 255, 255));
                CenterText(g, "⋮", Fonts.Bold(12f), Color.White, more);
                Hot(more, null, "Más opciones", MoreMenu);
            }
            else
            {
                var info = new RectangleF(right - S(28), 0, S(28), S(28));
                CenterText(g, "ⓘ", Fonts.Get(12f), Theme.Muted, info);
                Hot(info, null, mode == 1
                    ? $"Criaturas que conviene terminar yendo solo con nivel {N(c.Level)}: las que dan más charm points\nsegún lo fácil que se matan a tu nivel, las kills que faltan y en cuántos sitios viven según TibiaWiki.\nSin las muy raras ni las que tienen más de 60 HP por cada nivel tuyo. Es orientativo."
                    : $"Zonas donde terminar varias criaturas a la vez (los sitios que enlaza TibiaWiki en cada criatura),\nordenadas por los charm points que faltan. La dificultad (1–10) compara la vida de la criatura más dura\ncon el nivel {N(c.Level)}. Es orientativo.");
            }
            // Filtros
            float x = 0, y = S(38);
            x += Menu(g, "☰  " + (klass.Length == 0 ? "Todas las clases" : klass), x, y, at =>
            {
                var menu = DarkMenu.Create();
                menu.Items.Add("Todas las clases · " + Progress(_ => true), null, (a, b) => { klass = ""; Relayout(); });
                menu.Items.Add(new ToolStripSeparator());
                foreach (var name in BestiaryCatalog.Classes)
                {
                    string n = name;
                    menu.Items.Add($"{n} · {Progress(b => b.Class == n)}", null, (a, b) => { klass = n; Relayout(); });
                }
                menu.Show(at);
            }) + S(8);
            x += Menu(g, "◔  " + (difficulty.Length == 0 ? "Todas las dificultades" : difficulty), x, y, at =>
            {
                var menu = DarkMenu.Create();
                menu.Items.Add("Todas las dificultades", null, (a, b) => { difficulty = ""; Relayout(); });
                menu.Items.Add(new ToolStripSeparator());
                foreach (var level in BestiaryCatalog.Levels)
                {
                    string l = level;
                    menu.Items.Add($"{l} · {Progress(b => b.Level == l)}", null, (a, b) => { difficulty = l; Relayout(); });
                }
                menu.Show(at);
            }) + S(8);
            if (mode == 0)
            {
                x += Menu(g, "☑  " + Filters[filter], x, y, at => Choose(Filters, i => filter = i, at)) + S(8);
                Menu(g, "⇅  Orden: " + Sorts[sort], x, y, at => Choose(Sorts, i => sort = i, at));
            }
            y = S(TopH);
            switch (mode)
            {
                case 1: DrawSingles(g, y, visible); break;
                case 2: DrawGroups(g, y, visible); break;
                default: DrawAll(g, y, visible); break;
            }
        }

        float Menu(Graphics g, string text, float x, float y, Action<Point> open)
        {
            float w = Button(g, text, x, y, null);
            hots[hots.Count - 1] = new Hot { Rect = hots[hots.Count - 1].Rect, Menu = open };
            return w;
        }

        void Choose(string[] options, Action<int> pick, Point at)
        {
            var menu = DarkMenu.Create();
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                menu.Items.Add(options[i], null, (a, b) => { pick(index); Relayout(); });
            }
            menu.Show(at);
        }

        string Progress(Func<BestiaryCreature, bool> include)
        {
            var all = BestiaryCatalog.Creatures.Where(include).ToList();
            return $"{all.Count(c.Completed)}/{all.Count}";
        }

        void MoreMenu(Point at)
        {
            var list = Creatures();
            var menu = DarkMenu.Create();
            menu.Items.Add($"Marcar las {list.Count} de la lista como completas", null, (a, b) =>
            {
                foreach (var b2 in list) c.Kills[b2.Name] = Math.Max(c.KillsOf(b2), b2.Total);
                Edited();
            });
            menu.Items.Add($"Poner a 0 las {list.Count} de la lista", null, (a, b) =>
            {
                if (MessageBox.Show(FindForm(), "Se borran las kills apuntadas de las criaturas que se ven en la lista.", $"¿Poner a 0 las kills de {list.Count} criaturas?",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                foreach (var b2 in list) c.Kills.Remove(b2.Name);
                Edited();
            }).ForeColor = Color.FromArgb(255, 105, 97);
            menu.Show(at);
        }

        void Edited()
        {
            store.Touch();
            Relayout();
            Parent?.Invalidate(true);  // la cabecera del char también cuenta las completas
        }

        void Open(BestiaryCreature b)
        {
            var panel = new CreatureDetail(b, c, () => { store.Touch(); Invalidate(); Parent?.Invalidate(true); });
            Popover.Open(this, Cursor.Position, panel, new Size((int)S(380), (int)S(430)));
        }

        // Todas

        void DrawAll(Graphics g, float y, Rectangle visible)
        {
            var all = BestiaryCatalog.Creatures;
            int done = all.Count(c.Completed), started = all.Count(b => c.KillsOf(b) > 0 && !c.Completed(b));
            var sum = new RectangleF(0.5f, y, Width - 1, S(64));
            Card(g, sum);
            TextAt(g, "Completas", Fonts.Get(9f), Theme.Green, sum.X + S(14), sum.Y + S(12));
            TextAt(g, $"{done}/{all.Count}", Fonts.Bold(13f), Color.White, sum.X + S(84), sum.Y + S(8));
            TextAt(g, $"{(int)((double)done / Math.Max(1, all.Count) * 100)} %", Fonts.Semi(9f), Theme.Amber, sum.X + S(160), sum.Y + S(12));
            Bar(g, new RectangleF(sum.X + S(14), sum.Y + S(40), Math.Min(S(280), sum.Width / 2), S(5)), (double)done / Math.Max(1, all.Count), Theme.Green);
            string right = $"●  En curso {started}     ●  Sin empezar {all.Count - done - started}     ✦  {N(c.CharmPoints)} / {N(BestiaryCatalog.TotalCharms)}";
            Write(g, right, Fonts.Semi(9f), Color.FromArgb(210, 210, 215), new RectangleF(sum.Width / 2, sum.Y, sum.Width / 2 - S(14), sum.Height),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            y += S(64) + S(12);
            var list = Creatures();
            if (list.Count == 0)
            {
                Write(g, "No hay criaturas con estos filtros.", Fonts.Get(9.5f), Theme.Muted, new RectangleF(0, y + S(30), Width, S(30)), TextFormatFlags.HorizontalCenter);
                return;
            }
            if (sort != 0) { DrawGrid(g, list, 0, y, Width, visible); return; }
            // Por clase, en orden alfabético (como en el juego).
            foreach (var group in list.GroupBy(b => b.Class).OrderBy(gr => gr.Key, StringComparer.Ordinal))
            {
                var everyone = BestiaryCatalog.Creatures.Where(b => b.Class == group.Key).ToList();
                if (y + S(32) >= visible.Top - S(40) && y <= visible.Bottom)
                {
                    var titleFont = Fonts.Bold(10.5f);
                    TextAt(g, group.Key, titleFont, Theme.Amber, 0, y + S(4));
                    float tx = Measure(group.Key, titleFont).Width + S(10);
                    TextAt(g, $"{everyone.Count(c.Completed)}/{everyone.Count}", Fonts.Semi(9f), Color.White, tx, y + S(7));
                    tx += S(48);
                    TextAt(g, $"✦ {N(everyone.Where(c.Completed).Sum(b => b.Charms))} / {N(everyone.Sum(b => b.Charms))}", Fonts.Get(8.5f), Theme.Muted, tx, y + S(8));
                    using (var pen = new Pen(Color.FromArgb(20, 255, 255, 255))) g.DrawLine(pen, tx + S(110), y + S(16), Width, y + S(16));
                }
                y += S(32);
                DrawGrid(g, group.ToList(), 0, y, Width, visible);
                y += GridHeight(group.Count(), Width) + S(16);
            }
        }

        void DrawGrid(Graphics g, List<BestiaryCreature> list, float x0, float y0, float width, Rectangle visible)
        {
            int columns = Columns(width);
            float w = (width - (columns - 1) * S(Gap)) / columns;
            for (int i = 0; i < list.Count; i++)
            {
                var r = new RectangleF(x0 + (i % columns) * (w + S(Gap)) + 0.5f, y0 + (i / columns) * (S(TileH) + S(Gap)), w - 1, S(TileH));
                if (r.Bottom < visible.Top || r.Top > visible.Bottom) continue;
                DrawTile(g, list[i], r);
            }
        }

        void DrawTile(Graphics g, BestiaryCreature b, RectangleF r)
        {
            int kills = c.KillsOf(b);
            bool done = kills >= b.Total, started = kills > 0 && !done;
            bool hover = Hovered(r);
            Fill(g, r, S(10), done ? Color.FromArgb(28, 55, 38) : hover ? Color.FromArgb(48, 48, 52) : Palette.Card);
            Stroke(g, r, S(10), hover ? Theme.Blue : done ? Color.FromArgb(110, Theme.Green) : started ? Color.FromArgb(100, Theme.Blue) : Palette.Edge);
            if (b.Rare) using (var dot = new SolidBrush(Theme.Amber)) g.FillEllipse(dot, r.Right - S(12), r.Y + S(6), S(6), S(6));
            Sprite(g, WikiImages.Get(b.Name), new RectangleF(r.X + (r.Width - S(56)) / 2, r.Y + S(6), S(56), S(56)));
            Write(g, b.Name, Fonts.Semi(8.25f), Color.White, new RectangleF(r.X + S(6), r.Y + S(66), r.Width - S(12), S(16)), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Bar(g, new RectangleF(r.X + S(10), r.Y + S(88), r.Width - S(20), S(4)), (double)kills / b.Total, done ? Theme.Green : Theme.Blue);
            Write(g, done ? "Completa" : $"{Format.Compact(kills)}/{Format.Compact(b.Total)}", done ? Fonts.Semi(8f) : Fonts.Get(8f), done ? Theme.Green : Theme.Muted,
                new RectangleF(r.X, r.Y + S(98), r.Width, S(16)), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            Hot(r, () => Open(b), $"{b.Name} · {b.Level} · {b.Class}");
        }

        // Single

        void DrawSingles(Graphics g, float y, Rectangle visible)
        {
            var list = Singles();
            if (list.Count == 0)
            {
                Write(g, "Nada que recomendar: no queda ninguna criatura sin terminar con estos filtros.", Fonts.Get(9.5f), Theme.Muted, new RectangleF(0, y + S(20), Width, S(30)), TextFormatFlags.HorizontalCenter);
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                var r = new RectangleF(0.5f, y, Width - 1, S(56));
                y += S(62);
                if (r.Bottom < visible.Top || r.Top > visible.Bottom) continue;
                Card(g, r, Hovered(r) ? Color.FromArgb(46, 46, 50) : (Color?)null, radius: 10);
                Rank(g, i + 1, r.X + S(12), r.Y + S(15));
                Sprite(g, WikiImages.Get(b.Name), new RectangleF(r.X + S(46), r.Y + S(8), S(40), S(40)));
                var nameFont = Fonts.Semi(9.75f);
                TextAt(g, b.Name, nameFont, Color.White, r.X + S(96), r.Y + S(8));
                TextAt(g, $"{b.Class} · {b.Level}", Fonts.Get(8.25f), Theme.Muted, r.X + S(104) + Measure(b.Name, nameFont).Width, r.Y + S(10));
                float cx = r.X + S(96);
                var (word, tint) = Ease(b);
                cx += Chip(g, $"FALTAN {N(Remaining(b))}", Theme.Blue, cx, r.Y + S(32)) + S(5);
                cx += Chip(g, word, tint, cx, r.Y + S(32)) + S(5);
                cx += Chip(g, $"{N(b.Hp)} HP", Color.FromArgb(200, 200, 205), cx, r.Y + S(32)) + S(5);
                Chip(g, $"+{b.Charms} CP", Palette.Purple, cx, r.Y + S(32));
                int kills = c.KillsOf(b);
                Write(g, $"{N(kills)} / {N(b.Total)}", Fonts.Get(8.25f), Theme.Muted, new RectangleF(r.Right - S(140), r.Y + S(12), S(126), S(16)), TextFormatFlags.Right | TextFormatFlags.SingleLine);
                Bar(g, new RectangleF(r.Right - S(124), r.Y + S(32), S(110), S(5)), (double)kills / b.Total, Theme.Blue);
                Hot(r, () => Open(b));
            }
        }

        void Rank(Graphics g, int rank, float x, float y)
        {
            var circle = new RectangleF(x, y, S(26), S(26));
            using (var brush = new SolidBrush(Color.FromArgb(40, Theme.Amber))) g.FillEllipse(brush, circle);
            CenterText(g, rank.ToString(), Fonts.Bold(8.5f), Theme.Amber, circle);
        }

        // Group

        void DrawGroups(Graphics g, float y, Rectangle visible)
        {
            var places = Places();
            if (places.Count == 0)
            {
                Write(g, "Nada que recomendar: no hay zonas con varias criaturas sin terminar con estos filtros.", Fonts.Get(9.5f), Theme.Muted, new RectangleF(0, y + S(20), Width, S(30)), TextFormatFlags.HorizontalCenter);
                return;
            }
            for (int i = 0; i < places.Count; i++)
            {
                var p = places[i];
                bool open = p.Name == openPlace;
                float h = S(104) + (open ? GridHeight(p.Creatures.Count, Width - S(24)) + S(10) : 0);
                var r = new RectangleF(0.5f, y, Width - 1, h);
                y += h + S(8);
                if (r.Bottom < visible.Top || r.Top > visible.Bottom) continue;
                Card(g, r);
                var header = new RectangleF(r.X, r.Y, r.Width, S(104));
                if (Hovered(header)) Fill(g, header, S(12), Color.FromArgb(10, 255, 255, 255));
                Rank(g, i + 1, r.X + S(12), r.Y + S(14));
                var titleFont = Fonts.Semi(10.5f);
                TextAt(g, p.Name, titleFont, Color.White, r.X + S(48), r.Y + S(12));
                float cx = r.X + S(56) + Measure(p.Name, titleFont).Width;
                cx += Chip(g, $"{p.Creatures.Count} POR TERMINAR", Theme.Blue, cx, r.Y + S(15)) + S(5);
                cx += Chip(g, $"+{N(p.Charms)} CP", Palette.Purple, cx, r.Y + S(15)) + S(5);
                Chip(g, $"DIFICULTAD {p.Difficulty}/10", p.Difficulty <= 3 ? Theme.Green : p.Difficulty <= 6 ? Theme.Amber : Palette.Red, cx, r.Y + S(15));
                float sx = r.X + S(48);
                foreach (var b in p.Creatures.Take(12))
                {
                    var box = new RectangleF(sx, r.Y + S(40), S(34), S(34));
                    Sprite(g, WikiImages.Get(b.Name), box);
                    Write(g, Format.Compact(Remaining(b)), Fonts.Semi(7.25f), Theme.Muted, new RectangleF(sx - S(4), r.Y + S(76), S(42), S(14)), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
                    Hot(box, null, $"{b.Name}: faltan {N(Remaining(b))} kills");
                    sx += S(40);
                }
                if (p.Creatures.Count > 12) TextAt(g, "+" + (p.Creatures.Count - 12), Fonts.Semi(9f), Theme.Muted, sx + S(4), r.Y + S(50));
                CenterText(g, open ? "▴" : "▾", Fonts.Bold(11f), Theme.Muted, new RectangleF(r.Right - S(36), r.Y + S(36), S(24), S(24)));
                var name = p.Name;
                hots.Insert(0, new Hot { Rect = header, Click = () => { openPlace = open ? null : name; Relayout(); } });
                if (open) DrawGrid(g, p.Creatures, r.X + S(12), r.Y + S(104), r.Width - S(24), visible);
            }
        }
    }

    /// Ficha de una criatura: datos, etapas, kills, daño que recibe de cada elemento y dónde vive.
    sealed class CreatureDetail : HitCanvas
    {
        readonly BestiaryCreature b;
        readonly GameChar c;
        readonly Action changed;
        readonly NumberField kills = new NumberField { Minimum = 0, Maximum = 100000, Width = 80, Height = 26 };
        static readonly (string Key, string Title, Color Tint)[] Elements =
        {
            ("physical", "Físico", Color.FromArgb(190, 190, 195)), ("fire", "Fuego", Color.Orange), ("ice", "Hielo", Color.Cyan),
            ("energy", "Energía", Color.MediumPurple), ("earth", "Tierra", Color.LimeGreen), ("holy", "Sagrado", Color.Gold), ("death", "Muerte", Color.IndianRed),
        };

        public CreatureDetail(BestiaryCreature b, GameChar c, Action changed)
        {
            this.b = b;
            this.c = c;
            this.changed = changed;
            BackColor = Theme.Row;
            kills.Value = c.KillsOf(b);
            kills.BackColor = Theme.Row;
            kills.ValueCommitted += (s, e) => Set(kills.Value);
            Controls.Add(kills);
        }

        void Set(int value)
        {
            value = Math.Max(0, value);
            if (value > 0) c.Kills[b.Name] = value; else c.Kills.Remove(b.Name);
            kills.Value = value;
            changed();
            Invalidate();
        }

        public override int HeightFor(int width) => Height;

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            kills.Location = new Point((int)S(56), (int)S(156));
        }

        protected override void Draw(Graphics g, Rectangle visible)
        {
            g.Clear(Theme.Row);
            int k = c.KillsOf(b);
            bool done = k >= b.Total;
            float x = S(16);
            Sprite(g, WikiImages.Get(b.Name), new RectangleF(x, S(14), S(64), S(64)));
            TextAt(g, b.Name, Fonts.Bold(12f), Color.White, x + S(76), S(16));
            float cx = x + S(76);
            cx += Chip(g, b.Class.ToUpperInvariant(), Theme.Amber, cx, S(42)) + S(4);
            cx += Chip(g, b.Level.ToUpperInvariant(), Theme.Blue, cx, S(42)) + S(4);
            if (b.Rare) Chip(g, "MUY RARA", Palette.Red, cx, S(42));
            TextAt(g, $"{N(b.Hp)} HP · {N(b.Exp)} exp · {b.Charms} charm points", Fonts.Get(8.25f), Theme.Muted, x + S(76), S(62));
            // Etapas
            float w = (Width - S(32) - S(12)) / 3;
            for (int i = 0; i < 3; i++)
            {
                bool reached = k >= b.Stages[i];
                var r = new RectangleF(x + i * (w + S(6)), S(92), w, S(52));
                Fill(g, r, S(8), reached ? Color.FromArgb(30, Theme.Green) : Color.FromArgb(10, 255, 255, 255));
                CenterText(g, reached ? "✓" : "○", Fonts.Bold(10f), reached ? Theme.Green : Theme.Muted, new RectangleF(r.X, r.Y + S(4), r.Width, S(16)));
                CenterText(g, "Etapa " + (i + 1), Fonts.Semi(8f), Color.White, new RectangleF(r.X, r.Y + S(20), r.Width, S(14)));
                CenterText(g, N(b.Stages[i]), Fonts.Get(8f), Theme.Muted, new RectangleF(r.X, r.Y + S(34), r.Width, S(14)));
            }
            // Kills
            TextAt(g, "Kills", Fonts.Get(9f), Theme.Muted, x, S(160));
            TextAt(g, "/ " + N(b.Total), Fonts.Get(9f), Theme.Muted, S(142), S(160));
            float bx = Width - S(16);
            foreach (int step in new[] { 100, 10, 1 })
            {
                int add = step;
                float bw = ButtonWidth("+" + step);
                bx -= bw;
                Button(g, "+" + step, bx, S(155), () => Set(Math.Min(b.Total, c.KillsOf(b) + add)), height: 26);
                bx -= S(6);
            }
            Bar(g, new RectangleF(x, S(192), Width - S(32), S(5)), (double)k / b.Total, done ? Theme.Green : Theme.Blue);
            var mark = new RectangleF(x, S(206), Width - S(32), S(30));
            Fill(g, mark, S(8), done ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(200, 40, 167, 69));
            if (Hovered(mark)) Fill(g, mark, S(8), Color.FromArgb(20, 255, 255, 255));
            CenterText(g, done ? "Quitar completa" : "✓  Marcar completa", Fonts.Semi(9f), Color.White, mark);
            Hot(mark, () => Set(done ? 0 : b.Total));
            // Debilidades
            Label(g, "Daño que recibe", x, S(250));
            float ew = (Width - S(32) - S(6) * 6) / 7;
            for (int i = 0; i < Elements.Length; i++)
            {
                int value = b.Mod(Elements[i].Key);
                var r = new RectangleF(x + i * (ew + S(6)), S(268), ew, S(40));
                Fill(g, r, S(6), value > 100 ? Color.FromArgb(30, Theme.Green) : Color.FromArgb(10, 255, 255, 255));
                CenterText(g, Elements[i].Title, Fonts.Get(7f), Elements[i].Tint, new RectangleF(r.X, r.Y + S(4), r.Width, S(14)));
                CenterText(g, value + " %", Fonts.Semi(8f), value > 100 ? Theme.Green : value < 100 ? Theme.Muted : Color.White, new RectangleF(r.X, r.Y + S(20), r.Width, S(16)));
            }
            if (b.Places.Count > 0)
            {
                Label(g, "Dónde vive (TibiaWiki)", x, S(322));
                Write(g, string.Join(" · ", b.Places.Take(8)), Fonts.Get(8.5f), Color.FromArgb(215, 215, 220), new RectangleF(x, S(340), Width - S(32), S(80)), TextFormatFlags.WordBreak);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Pestaña General, al estilo de guildstats.eu: arriba el nivel con su progreso y HP, maná, capacidad y
    /// rango de party (calculados); debajo la ficha en dos columnas y, al lado, gremio, casas, otros chars
    /// de la cuenta, muertes y comentario.
    sealed class GeneralCanvas : HitCanvas
    {
        readonly GameChar c;
        readonly Profile profile;
        public Action<TibiaInfo.OtherChar> Add;

        public GeneralCanvas(GameChar c, Profile profile)
        {
            this.c = c;
            this.profile = profile;
            Margin = new Padding(0, 0, 0, 8);
        }

        TibiaInfo Info => c.IsSynced ? c.Tibia : null;

        // ---------- Medidas ----------

        const float Strip = 104, Row = 26, Head = 36, Gap = 12;

        List<(string Glyph, string Label, string Value)> SheetRows(TibiaInfo info)
        {
            var rows = new List<(string, string, string)>
            {
                ("", "Vocación", info.Vocation),
                ("", "Sexo", info.Sex == "female" ? "Mujer" : info.Sex == "male" ? "Hombre" : info.Sex),
                ("", "Mundo", info.World),
                ("", "Residencia", info.Residence),
                ("", "Título", info.Title.Length == 0 ? $"Ninguno ({info.UnlockedTitles} desbloqueados)" : $"{info.Title} ({info.UnlockedTitles})"),
                ("", "Puntos de logro", N(info.AchievementPoints)),
                ("", "Cuenta", info.AccountStatus),
                ("", "Última conexión", Ids.Parse(info.LastLogin) is DateTime last ? $"{Format.Long(last, true)} · {Format.Relative(last)}" : "—"),
                ("", "Cuenta creada", Ids.Parse(info.AccountCreated) is DateTime created ? $"{Format.Long(created)} · {Format.Relative(created)}" : "—"),
                ("", "Lealtad", info.LoyaltyTitle.Length == 0 ? "—" : info.LoyaltyTitle),
            };
            if (info.MarriedTo.Length > 0) rows.Add(("", "Casado con", info.MarriedTo));
            return rows;
        }

        float DeathsHeight(TibiaInfo info, float w)
        {
            if (info.Deaths.Count == 0) return S(Head) + S(28);
            return S(Head) + info.Deaths.Take(10).Sum(d => Math.Max(S(34), MeasureWrapped(d.Reason, Fonts.Get(9f), (int)(w - S(90))) + S(18))) + S(8);
        }

        List<(string Title, float Height, Action<Graphics, RectangleF> Draw)> RightCards(TibiaInfo info, float w)
        {
            var cards = new List<(string, float, Action<Graphics, RectangleF>)>();
            if (info.Guild.Length > 0) cards.Add(("Gremio", S(Head) + S(50), (g, r) => DrawGuild(g, r, info)));
            if (info.Houses.Count > 0) cards.Add(("Casas", S(Head) + info.Houses.Count * S(38) + S(6), (g, r) => DrawHouses(g, r, info)));
            if (info.OtherChars.Count > 0) cards.Add(("Otros chars de la cuenta", S(Head) + info.OtherChars.Count * S(28) + S(8), (g, r) => DrawOthers(g, r, info)));
            if (info.FormerNames.Count > 0 || info.FormerWorlds.Count > 0)
                cards.Add(("Antes", S(Head) + (info.FormerNames.Count > 0 ? S(Row) : 0) + (info.FormerWorlds.Count > 0 ? S(Row) : 0) + S(6), (g, r) => DrawFormer(g, r, info)));
            string comment = info.Comment.Trim();
            if (comment.Length > 0)
                cards.Add(("Comentario", S(Head) + MeasureWrapped(comment, Fonts.Get(9f), (int)(w - S(28))) + S(16), (g, r) => DrawComment(g, r, comment)));
            return cards;
        }

        public override int HeightFor(int width)
        {
            var info = Info;
            if (info == null) return (int)(S(Strip) + S(Gap) + S(110));
            float col = (width - S(Gap)) / 2;
            float left = S(Head) + SheetRows(info).Count * S(Row) + S(10) + S(Gap) + DeathsHeight(info, col);
            float right = RightCards(info, col).Sum(card => card.Height + S(Gap)) - S(Gap);
            return (int)(S(Strip) + S(Gap) + Math.Max(left, right) + S(Gap) + S(20));
        }

        // ---------- Dibujo ----------

        protected override void Draw(Graphics g, Rectangle visible)
        {
            var info = Info;
            DrawStrip(g, new RectangleF(0.5f, 0.5f, Width - 1, S(Strip)), info);
            float y = S(Strip) + S(Gap);
            if (info == null)
            {
                var r = new RectangleF(0.5f, y, Width - 1, S(100));
                Card(g, r);
                Write(g, "Sin datos de tibia.com", Fonts.Semi(10.5f), Color.White, new RectangleF(0, r.Y + S(20), Width, S(22)), TextFormatFlags.HorizontalCenter);
                Write(g, "Escribe arriba el nombre exacto del char: en cuanto lo encuentre en tibia.com se rellenan nivel, vocación, mundo, gremio, cuenta, casas, muertes y los otros chars de la cuenta.",
                    Fonts.Get(9f), Theme.Muted, new RectangleF(S(40), r.Y + S(48), Width - S(80), S(44)), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
                return;
            }
            float col = (Width - S(Gap)) / 2;
            // Izquierda: ficha y muertes.
            var rows = SheetRows(info);
            var sheet = new RectangleF(0.5f, y, col - 1, S(Head) + rows.Count * S(Row) + S(10));
            CardTitle(g, sheet, "Personaje", "");
            using (var icons = new Font("Segoe MDL2 Assets", 8f))
                for (int i = 0; i < rows.Count; i++)
                {
                    float ry = sheet.Y + S(Head) + i * S(Row);
                    TextRenderer.DrawText(g, rows[i].Glyph, icons, new Rectangle((int)(sheet.X + S(14)), (int)ry, (int)S(16), (int)S(Row)), Theme.Muted,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                    Write(g, rows[i].Label, Fonts.Get(9f), Theme.Muted, new RectangleF(sheet.X + S(38), ry, S(130), S(Row)), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    Write(g, rows[i].Value, Fonts.Semi(9f), Color.White, new RectangleF(sheet.X + S(150), ry, sheet.Width - S(164), S(Row)),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    if (i < rows.Count - 1)
                        using (var pen = new Pen(Color.FromArgb(14, 255, 255, 255))) g.DrawLine(pen, sheet.X + S(14), ry + S(Row), sheet.Right - S(14), ry + S(Row));
                }
            var deaths = new RectangleF(0.5f, sheet.Bottom + S(Gap), col - 1, DeathsHeight(info, col));
            DrawDeaths(g, deaths, info);
            // Derecha: gremio, casas, otros chars, antes y comentario.
            float ry2 = y;
            foreach (var (title, height, draw) in RightCards(info, col))
            {
                var r = new RectangleF(col + S(Gap) + 0.5f, ry2, col - 1, height);
                CardTitle(g, r, title, null);
                draw(g, r);
                ry2 = r.Bottom + S(Gap);
            }
            Write(g, $"Datos de tibia.com (TibiaData), sincronizados {Format.Relative(info.Synced)}. HP, maná, capacidad y party se calculan con el nivel y la vocación.",
                Fonts.Get(8.25f), Theme.Muted, new RectangleF(0, Height - S(18), Width, S(18)));
        }

        void CardTitle(Graphics g, RectangleF r, string title, string glyph)
        {
            Card(g, r);
            using (var icons = new Font("Segoe MDL2 Assets", 8.5f))
                TextRenderer.DrawText(g, glyph ?? "", icons, new Rectangle((int)(r.X + S(14)), (int)(r.Y + S(12)), (int)S(16), (int)S(16)), Theme.Blue,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextAt(g, title, Fonts.Semi(10f), Color.White, r.X + S(36), r.Y + S(11));
        }

        /// Nivel con su barra al siguiente y HP, maná, capacidad y party con su barra de color.
        void DrawStrip(Graphics g, RectangleF r, TibiaInfo info)
        {
            using (var brush = new LinearGradientBrush(r, Color.FromArgb(34, 48, 38), Palette.Card, LinearGradientMode.Horizontal))
            using (var path = CounterRenderer.RoundedRect(r, S(12)))
                g.FillPath(brush, path);
            Stroke(g, r, S(12), Palette.Edge);
            int level = c.Level;
            long start = Experience.ForLevel(level), next = Experience.ForLevel(level + 1);
            double? progress = info?.Experience is long exp ? (double)(exp - start) / (next - start) : (double?)null;
            float x = r.X + S(18);
            TextAt(g, "NIVEL", Fonts.Semi(7.5f), Theme.Muted, x, r.Y + S(26));
            var big = Fonts.Bold(21f);
            TextAt(g, N(level), big, Color.White, x + S(44), r.Y + S(12));
            float after = x + S(44) + Measure(N(level), big).Width + S(10);
            if (info?.WorldRank is int rank) Chip(g, $"#{rank} EN {info.World.ToUpperInvariant()}", Theme.Amber, after, r.Y + S(24));
            Bar(g, new RectangleF(x, r.Y + S(56), Math.Min(S(260), r.Width / 2 - S(40)), S(5)), progress ?? 0, Theme.Green);
            string detail = progress is double p && info?.Experience is long e
                ? $"{(int)(p * 100)} % · faltan {Format.Compact(next - e)} exp para el {N(level + 1)}"
                : $"{Format.Compact(next - start)} exp de este nivel al siguiente";
            TextAt(g, detail, Fonts.Get(8.25f), Theme.Muted, x, r.Y + S(70));
            // Medidores
            var range = Experience.ShareRange(level);
            var meters = new[]
            {
                ("", Palette.Red, "HP", N(Vocations.HitPoints(c.Vocation, level))),
                ("", Theme.Blue, "Maná", N(Vocations.Mana(c.Vocation, level))),
                ("", Palette.Gold, "Capacidad", N(Vocations.Capacity(c.Vocation, level))),
                ("", Palette.Cyan, "Party", $"{N(range.Low)}–{N(range.High)}"),
            };
            float mw = S(140), mx = r.Right - S(16) - mw * 2;
            using (var pen = new Pen(Color.FromArgb(20, 255, 255, 255))) g.DrawLine(pen, mx - S(16), r.Y + S(18), mx - S(16), r.Bottom - S(18));
            using (var icons = new Font("Segoe MDL2 Assets", 9f))
                for (int i = 0; i < meters.Length; i++)
                {
                    var (glyph, tint, label, value) = meters[i];
                    float cx = mx + (i % 2) * mw, cy = r.Y + S(14) + (i / 2) * S(44);
                    var circle = new RectangleF(cx, cy + S(4), S(28), S(28));
                    using (var brush = new SolidBrush(Color.FromArgb(40, tint))) g.FillEllipse(brush, circle);
                    CenterText(g, glyph, icons, tint, circle);
                    TextAt(g, label, Fonts.Get(7.5f), Theme.Muted, cx + S(36), cy);
                    TextAt(g, value, Fonts.Semi(10f), Color.White, cx + S(36), cy + S(12));
                    Fill(g, new RectangleF(cx + S(36), cy + S(32), S(62), S(3)), S(1.5f), Color.FromArgb(180, tint));
                    if (label == "Party") Hot(new RectangleF(cx, cy, mw, S(40)), null, "Niveles con los que comparte experiencia en party");
                }
        }

        void DrawDeaths(Graphics g, RectangleF r, TibiaInfo info)
        {
            CardTitle(g, r, "Muertes recientes", "");
            if (info.Deaths.Count > 0) Chip(g, info.Deaths.Count.ToString(), Theme.Muted, r.Right - S(40), r.Y + S(13));
            float y = r.Y + S(Head);
            if (info.Deaths.Count == 0)
            {
                TextAt(g, "✓ Ninguna en los últimos 30 días", Fonts.Get(9f), Theme.Green, r.X + S(14), y);
                return;
            }
            foreach (var d in info.Deaths.Take(10))
            {
                var badge = new RectangleF(r.X + S(14), y, S(50), S(20));
                Fill(g, badge, S(6), Color.FromArgb(40, Palette.Red));
                CenterText(g, d.Level.ToString(), Fonts.Bold(8.5f), Palette.Red, badge);
                float th = MeasureWrapped(d.Reason, Fonts.Get(9f), (int)(r.Width - S(90)));
                Write(g, d.Reason, Fonts.Get(9f), Color.White, new RectangleF(r.X + S(74), y, r.Width - S(90), th), TextFormatFlags.WordBreak);
                if (Ids.Parse(d.Time) is DateTime when)
                    TextAt(g, $"{Format.Long(when, true)} · {Format.Relative(when)}", Fonts.Get(7.75f), Theme.Muted, r.X + S(74), y + th + S(1));
                y += Math.Max(S(34), th + S(18));
            }
        }

        void DrawGuild(Graphics g, RectangleF r, TibiaInfo info)
        {
            var icon = new RectangleF(r.X + S(14), r.Y + S(Head), S(38), S(38));
            Fill(g, icon, S(9), Color.FromArgb(40, Theme.Green));
            using (var icons = new Font("Segoe MDL2 Assets", 13f)) CenterText(g, "", icons, Theme.Green, icon);
            TextAt(g, info.Guild, Fonts.Semi(10.5f), Color.White, icon.Right + S(12), icon.Y + S(1));
            TextAt(g, info.GuildRank.Length == 0 ? "Miembro" : info.GuildRank, Fonts.Get(8.5f), Theme.Muted, icon.Right + S(12), icon.Y + S(21));
        }

        void DrawHouses(Graphics g, RectangleF r, TibiaInfo info)
        {
            float y = r.Y + S(Head);
            foreach (var house in info.Houses)
            {
                TextAt(g, "⌂", Fonts.Bold(11f), Palette.Orange, r.X + S(14), y);
                TextAt(g, house.Name, Fonts.Semi(9f), Color.White, r.X + S(34), y);
                string detail = string.Join(" · ", new[] { house.Town, house.Paid.Length == 0 ? "" : "pagada hasta " + house.Paid }.Where(t => t.Length > 0));
                TextAt(g, detail, Fonts.Get(8.25f), Theme.Muted, r.X + S(34), y + S(17));
                y += S(38);
            }
        }

        void DrawOthers(Graphics g, RectangleF r, TibiaInfo info)
        {
            Chip(g, info.OtherChars.Count.ToString(), Theme.Muted, r.Right - S(40), r.Y + S(13));
            float y = r.Y + S(Head);
            foreach (var other in info.OtherChars)
            {
                using (var dot = new SolidBrush(other.Online ? Theme.Green : Color.FromArgb(60, 255, 255, 255)))
                    g.FillEllipse(dot, r.X + S(14), y + S(9), S(6), S(6));
                var font = Fonts.Semi(9f);
                TextAt(g, other.Name, font, Color.White, r.X + S(28), y + S(3));
                if (other.Main) Chip(g, "MAIN", Theme.Blue, r.X + S(36) + Measure(other.Name, font).Width, y + S(4));
                float right = r.Right - S(14);
                bool added = profile.Chars.Any(ch => string.Equals(ch.Name, other.Name, StringComparison.OrdinalIgnoreCase));
                var button = new RectangleF(right - S(24), y + S(2), S(24), S(20));
                if (added) CenterText(g, "✓", Fonts.Bold(9f), Theme.Muted, button);
                else
                {
                    Fill(g, button, S(5), Hovered(button) ? Color.FromArgb(60, 150, 255) : Theme.Blue);
                    CenterText(g, "+", Fonts.Bold(10f), Color.White, button);
                    var o = other;
                    Hot(button, () => Add(o), "Añadir " + other.Name + " al perfil");
                }
                Write(g, other.World, Fonts.Get(8.5f), Theme.Muted, new RectangleF(right - S(150), y + S(3), S(118), S(18)), TextFormatFlags.Right | TextFormatFlags.SingleLine);
                y += S(28);
            }
        }

        void DrawFormer(Graphics g, RectangleF r, TibiaInfo info)
        {
            float y = r.Y + S(Head);
            if (info.FormerNames.Count > 0) { Pair(g, r, y, "Nombres", string.Join(", ", info.FormerNames)); y += S(Row); }
            if (info.FormerWorlds.Count > 0) Pair(g, r, y, "Mundos", string.Join(", ", info.FormerWorlds));
        }

        void Pair(Graphics g, RectangleF r, float y, string label, string value)
        {
            Write(g, label, Fonts.Get(9f), Theme.Muted, new RectangleF(r.X + S(14), y, S(100), S(Row)), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            Write(g, value, Fonts.Semi(9f), Color.White, new RectangleF(r.X + S(110), y, r.Width - S(124), S(Row)),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        void DrawComment(Graphics g, RectangleF r, string comment) =>
            Write(g, comment, Fonts.Get(9f), Color.FromArgb(215, 215, 220), new RectangleF(r.X + S(14), r.Y + S(Head), r.Width - S(28), r.Height - S(Head) - S(8)), TextFormatFlags.WordBreak);
    }

    /// Pestaña Experiencia, al estilo de guildstats.eu: mejor día, media diaria, puesto en el mundo,
    /// evolución de la experiencia o el nivel, experiencia ganada cada día, predicción de nivel y una tabla
    /// por día. Los datos son los que guarda Alfa Hub en cada sincronización (una por día de Tibia, también
    /// sin abrir el char); los días de antes no se pueden traer (guildstats.eu protege su historial contra
    /// programas), así que hasta el segundo día se muestra solo el primer registro.
    sealed class ExperienceCanvas : HitCanvas
    {
        readonly GameChar c;
        readonly NumberField target = new NumberField { Minimum = 1, Maximum = 9999, Width = 64, Height = 26 };
        static int chartMode;   // 0 experiencia, 1 nivel
        float predictionY = -1;

        sealed class Day
        {
            public DateTime Date; public long? Gained; public int Level, Levels; public long? Experience; public int? Rank;
        }

        public ExperienceCanvas(GameChar c)
        {
            this.c = c;
            Margin = new Padding(0, 0, 0, 8);
            target.Value = c.Level + 10;
            target.ValueCommitted += (s, e) => Invalidate();
            target.BackColor = Palette.Card;
            Controls.Add(target);
        }

        List<Day> Days()
        {
            var points = c.History;
            var days = new List<Day>();
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                days.Add(new Day
                {
                    // El día de Tibia en que se ganó: el anterior al server save del registro.
                    Date = TibiaCalendar.TibiaDay(b.When).AddDays(-1),
                    Gained = a.Experience.HasValue && b.Experience.HasValue ? b.Experience - a.Experience : null,
                    Level = b.Level, Levels = b.Level - a.Level, Experience = b.Experience, Rank = b.Rank,
                });
            }
            return days;
        }

        const float Tiles = 78, ChartH = 230, BarsH = 190, Prediction = 96, RowH = 28;

        public override int HeightFor(int width)
        {
            if (!c.IsSynced) return (int)S(110);
            var days = Days();
            if (days.Count == 0) return (int)(S(Tiles) + S(12) + FirstDayHeight(width) + S(28));
            float h = S(Tiles) + S(12) + S(ChartH) + S(12);
            if (days.Any(d => d.Gained > 0)) h += S(BarsH) + S(12);
            h += S(Prediction) + S(12) + S(34) + days.Count * S(RowH) + S(8) + S(24);
            return (int)h;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            target.Visible = predictionY >= 0;
            if (predictionY >= 0) target.Location = new Point((int)S(110), (int)(predictionY + S(40)));
        }

        protected override void Draw(Graphics g, Rectangle visible)
        {
            float y = 0;
            if (!c.IsSynced)
            {
                Empty(g, 0, "Sin datos de tibia.com", "Sincroniza el char con tibia.com (escribe su nombre arriba) y Alfa Hub empezará a guardar su experiencia cada día.");
                SetPrediction(-1);
                return;
            }
            var days = Days();
            var gains = days.Where(d => d.Gained.HasValue).Select(d => d.Gained.Value).ToList();
            DrawTiles(g, y, days, gains);
            y += S(Tiles) + S(12);
            if (days.Count == 0)
            {
                DrawFirstDay(g, y);
                SetPrediction(-1);
                Note(g, y + FirstDayHeight(Width) + S(6));
                return;
            }
            DrawEvolution(g, new RectangleF(0.5f, y, Width - 1, S(ChartH)));
            y += S(ChartH) + S(12);
            // Sin ningún día con experiencia ganada las barras salen vacías.
            if (gains.Any(v => v > 0))
            {
                DrawBars(g, new RectangleF(0.5f, y, Width - 1, S(BarsH)), days);
                y += S(BarsH) + S(12);
            }
            DrawPrediction(g, new RectangleF(0.5f, y, Width - 1, S(Prediction)), gains);
            SetPrediction(y);
            y += S(Prediction) + S(12);
            DrawTable(g, y, days);
            Note(g, Height - S(20));
        }

        void SetPrediction(float y)
        {
            if (Math.Abs(predictionY - y) < 0.5f) return;
            predictionY = y;
            BeginInvoke((Action)(() => { if (!IsDisposed) PerformLayout(); }));
        }

        void Note(Graphics g, float y)
        {
            if (c.Tibia?.Experience == null)
                Write(g, "Este char no sale entre los 1000 primeros de los highscores de su mundo: se guarda su nivel, pero no la experiencia exacta.",
                    Fonts.Get(8.25f), Theme.Muted, new RectangleF(0, y, Width, S(18)));
        }

        void Empty(Graphics g, float y, string title, string text)
        {
            var r = new RectangleF(0.5f, y, Width - 1, S(100));
            Card(g, r);
            Write(g, title, Fonts.Semi(10.5f), Color.White, new RectangleF(0, y + S(20), Width, S(22)), TextFormatFlags.HorizontalCenter);
            Write(g, text, Fonts.Get(9f), Theme.Muted, new RectangleF(S(40), y + S(48), Width - S(80), S(44)), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }

        void DrawTiles(Graphics g, float y, List<Day> days, List<long> gains)
        {
            var best = days.Where(d => d.Gained > 0).OrderByDescending(d => d.Gained).FirstOrDefault();
            var recent = gains.Skip(Math.Max(0, gains.Count - 7)).ToList();
            long? average = recent.Count == 0 ? (long?)null : recent.Sum() / recent.Count;
            int level = c.Level;
            long start = Experience.ForLevel(level), next = Experience.ForLevel(level + 1);
            long? exp = c.Tibia?.Experience;
            var ranks = days.Where(d => d.Rank.HasValue).Select(d => d.Rank.Value).ToList();
            string rankDetail = ranks.Count >= 2
                ? (ranks[0] == ranks[ranks.Count - 1] ? $"igual que hace {ranks.Count - 1} días" : $"{(ranks[0] > ranks[ranks.Count - 1] ? "▲" : "▼")} {Math.Abs(ranks[0] - ranks[ranks.Count - 1])} en {ranks.Count - 1} días")
                : "";
            var tiles = new[]
            {
                ("♛  Mejor día", Theme.Amber, best?.Gained is long b ? "+" + Format.Compact(b) : "—", best == null ? "" : $"{Format.Short(best.Date)} · nivel {N(best.Level)}"),
                ("▦  Media diaria", Theme.Green, average is long a ? "+" + Format.Compact(a) : "—", recent.Count == 0 ? "" : recent.Count == 1 ? "último día" : $"últimos {recent.Count} días"),
                ("🏆  Puesto en " + c.World, Palette.Purple, c.Tibia?.WorldRank is int rank ? "#" + rank : "—", rankDetail),
                ("↗  Al nivel " + N(level + 1), Theme.Blue, exp is long e ? $"{(int)((double)(e - start) / (next - start) * 100)} %" : "—", exp is long e2 ? $"faltan {Format.Compact(next - e2)} exp" : ""),
            };
            float w = (Width - S(10) * 3) / 4;
            for (int i = 0; i < tiles.Length; i++)
            {
                var (title, tint, value, detail) = tiles[i];
                var r = new RectangleF(i * (w + S(10)) + 0.5f, y, w - 1, S(Tiles));
                Card(g, r);
                Write(g, title, Fonts.Semi(8.5f), tint, new RectangleF(r.X + S(12), r.Y + S(10), r.Width - S(24), S(18)));
                Write(g, value, Fonts.Bold(15f), Color.White, new RectangleF(r.X + S(12), r.Y + S(28), r.Width - S(24), S(28)));
                Write(g, detail, Fonts.Get(8f), Theme.Muted, new RectangleF(r.X + S(12), r.Y + S(56), r.Width - S(24), S(16)));
            }
        }

        const string FirstDayText = "Alfa Hub guarda el nivel y la experiencia de este char una vez por día de Tibia (después del server save), aunque no lo abras. Mañana, tras el server save, aparece lo que ganó hoy y se empiezan a llenar los gráficos, la media y la predicción.";

        float FirstDayHeight(float width) => S(60) + MeasureWrapped(FirstDayText, Fonts.Get(8.5f), (int)(width - S(90))) + S(18);

        void DrawFirstDay(Graphics g, float y)
        {
            var r = new RectangleF(0.5f, y, Width - 1, FirstDayHeight(Width));
            Card(g, r);
            var circle = new RectangleF(r.X + S(16), r.Y + S(18), S(44), S(44));
            using (var brush = new SolidBrush(Color.FromArgb(40, Theme.Blue))) g.FillEllipse(brush, circle);
            using (var icons = new Font("Segoe MDL2 Assets", 14f)) CenterText(g, "", icons, Theme.Blue, circle);
            var first = c.History.FirstOrDefault();
            float x = circle.Right + S(14);
            TextAt(g, "Primer registro: " + (first == null ? "hoy" : Format.Long(first.When, true)), Fonts.Semi(10.5f), Color.White, x, r.Y + S(14));
            if (first != null)
                TextAt(g, $"Nivel {N(first.Level)}{(first.Experience is long e ? " · " + N(e) + " exp" : "")}{(first.Rank is int rank ? $" · #{rank} en {c.World}" : "")}",
                    Fonts.Get(9f), Color.FromArgb(220, 220, 225), x, r.Y + S(36));
            Write(g, FirstDayText, Fonts.Get(8.5f), Theme.Muted, new RectangleF(x, r.Y + S(58), r.Right - x - S(16), r.Height - S(62)), TextFormatFlags.WordBreak);
        }

        void ChartFrame(Graphics g, RectangleF r, string title)
        {
            Card(g, r);
            Label(g, title, r.X + S(14), r.Y + S(14));
        }

        void DrawEvolution(Graphics g, RectangleF r)
        {
            ChartFrame(g, r, "Evolución");
            float px = r.Right - S(14) - (Measure("Experiencia", Fonts.Semi(8.75f)).Width + Measure("Nivel", Fonts.Semi(8.75f)).Width + S(52));
            Pills(g, new[] { "Experiencia", "Nivel" }, chartMode, px, r.Y + S(9), i => { chartMode = i; Invalidate(); });
            var points = c.History;
            bool byExp = chartMode == 0 && points.Any(p => p.Experience.HasValue);
            var values = points.Select(p => byExp ? (double)(p.Experience ?? 0) : p.Level).ToList();
            var valid = values.Where(v => v > 0).ToList();
            if (valid.Count == 0) return;
            double low = valid.Min(), high = valid.Max(), margin = Math.Max((high - low) * 0.15, byExp ? high * 0.005 : 1);
            low -= margin; high += margin;
            // Niveles: el eje se parte en 3, así que el rango va de 3 en 3 para que las marcas sean enteras.
            if (!byExp) { low = Math.Floor(low); high = low + 3 * Math.Ceiling((high - low) / 3); }
            var plot = new RectangleF(r.X + S(70), r.Y + S(44), r.Width - S(90), r.Height - S(74));
            DrawAxes(g, plot, low, high, byExp, points.Select(p => p.When).ToList());
            var pts = new List<PointF>();
            for (int i = 0; i < points.Count; i++)
            {
                if (values[i] <= 0) continue;
                float x = points.Count == 1 ? plot.X + plot.Width / 2 : plot.X + plot.Width * i / (points.Count - 1);
                float y = plot.Bottom - (float)((values[i] - low) / (high - low)) * plot.Height;
                pts.Add(new PointF(x, y));
            }
            if (pts.Count >= 2)
            {
                var area = new List<PointF>(pts) { new PointF(pts[pts.Count - 1].X, plot.Bottom), new PointF(pts[0].X, plot.Bottom) };
                using (var brush = new LinearGradientBrush(plot, Color.FromArgb(90, Theme.Green), Color.FromArgb(5, Theme.Green), LinearGradientMode.Vertical))
                    g.FillPolygon(brush, area.ToArray());
                using (var pen = new Pen(Theme.Green, S(2))) g.DrawLines(pen, pts.ToArray());
            }
            using (var dot = new SolidBrush(Theme.Green))
                foreach (var p in pts) g.FillEllipse(dot, p.X - S(3), p.Y - S(3), S(6), S(6));
        }

        void DrawAxes(Graphics g, RectangleF plot, double low, double high, bool exp, List<DateTime> dates)
        {
            using (var pen = new Pen(Color.FromArgb(18, 255, 255, 255)))
                for (int i = 0; i <= 3; i++)
                {
                    float y = plot.Bottom - plot.Height * i / 3;
                    g.DrawLine(pen, plot.X, y, plot.Right, y);
                    double v = low + (high - low) * i / 3;
                    Write(g, exp ? Format.Compact((long)v) : ((int)v).ToString(), Fonts.Get(7.5f), Theme.Muted,
                        new RectangleF(plot.X - S(66), y - S(8), S(60), S(16)), TextFormatFlags.Right | TextFormatFlags.SingleLine);
                }
            int step = Math.Max(1, dates.Count / 6);
            for (int i = 0; i < dates.Count; i += step)
            {
                float x = dates.Count == 1 ? plot.X + plot.Width / 2 : plot.X + plot.Width * i / (dates.Count - 1);
                Write(g, Format.Short(dates[i].ToLocalTime()), Fonts.Get(7.5f), Theme.Muted, new RectangleF(x - S(30), plot.Bottom + S(6), S(60), S(16)),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            }
        }

        void DrawBars(Graphics g, RectangleF r, List<Day> days)
        {
            ChartFrame(g, r, "Experiencia por día");
            var list = days.Where(d => d.Gained.HasValue).ToList();
            long best = list.Max(d => d.Gained.Value);
            double high = Math.Max(1, best) * 1.1;
            var plot = new RectangleF(r.X + S(70), r.Y + S(40), r.Width - S(90), r.Height - S(68));
            DrawAxes(g, plot, 0, high, true, new List<DateTime>());
            float slot = plot.Width / list.Count, bw = Math.Max(S(4), Math.Min(S(40), slot * 0.6f));
            for (int i = 0; i < list.Count; i++)
            {
                float h = (float)(Math.Max(0, list[i].Gained.Value) / high) * plot.Height;
                var bar = new RectangleF(plot.X + slot * i + (slot - bw) / 2, plot.Bottom - h, bw, Math.Max(1, h));
                Fill(g, bar, S(3), list[i].Gained == best ? Theme.Amber : Color.FromArgb(190, Theme.Green));
                Hot(bar, null, $"{Format.Short(list[i].Date)}: +{N(list[i].Gained.Value)} exp");
                if (list.Count <= 14 || i % Math.Max(1, list.Count / 8) == 0)
                    Write(g, Format.Short(list[i].Date), Fonts.Get(7.5f), Theme.Muted, new RectangleF(bar.X + bw / 2 - S(30), plot.Bottom + S(6), S(60), S(16)),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            }
        }

        void DrawPrediction(Graphics g, RectangleF r, List<long> gains)
        {
            Card(g, r);
            Label(g, "Predicción de nivel", r.X + S(14), r.Y + S(14));
            TextAt(g, "Nivel objetivo", Fonts.Get(9f), Theme.Muted, r.X + S(14), r.Y + S(44));
            float bx = r.X + S(110) + S(64) + S(10);
            foreach (int step in new[] { 10, 50, 100 })
            {
                int add = step;
                bx += Button(g, "+" + step, bx, r.Y + S(40), () => { target.Value = c.Level + add; Invalidate(); }, height: 26) + S(6);
            }
            var recent = gains.Skip(Math.Max(0, gains.Count - 7)).ToList();
            long average = recent.Count == 0 ? 0 : recent.Sum() / recent.Count;
            int goal = Math.Max(c.Level + 1, target.Value);
            long current = c.Tibia?.Experience ?? Experience.ForLevel(c.Level);
            long missing = Experience.ForLevel(goal) - current;
            string text = average > 0
                ? $"Con una media de +{Format.Compact(average)} exp/día llegarías al nivel {N(goal)} el {Format.Long(DateTime.UtcNow.AddDays(Math.Ceiling((double)missing / average)))} (en {Math.Ceiling((double)missing / average)} días). Faltan {N(missing)} exp."
                : "Hace falta al menos un día con experiencia ganada para calcularlo.";
            Write(g, text, Fonts.Get(9f), average > 0 ? Color.White : Theme.Muted, new RectangleF(r.X + S(14), r.Y + S(72), r.Width - S(28), S(20)));
        }

        void DrawTable(Graphics g, float y, List<Day> days)
        {
            float rows = days.Count * S(RowH);
            var r = new RectangleF(0.5f, y, Width - 1, S(34) + rows + S(8));
            Card(g, r);
            float[] widths = { 0.18f, 0.22f, 0.12f, 0.18f, 0.30f };
            string[] heads = { "Día", "Exp ganada", "Puesto", "Nivel", "Experiencia" };
            float inner = r.Width - S(28);
            float x = r.X + S(14);
            for (int i = 0; i < heads.Length; i++)
            {
                Write(g, heads[i], Fonts.Semi(8f), Theme.Muted, new RectangleF(x, r.Y + S(10), inner * widths[i], S(18)),
                    (i == 0 ? TextFormatFlags.Left : TextFormatFlags.Right) | TextFormatFlags.SingleLine);
                x += inner * widths[i];
            }
            float ry = r.Y + S(34);
            int index = 0;
            foreach (var day in Enumerable.Reverse(days))
            {
                if (index % 2 == 0) using (var brush = new SolidBrush(Color.FromArgb(8, 255, 255, 255))) g.FillRectangle(brush, r.X + 1, ry, r.Width - 2, S(RowH));
                var cells = new[]
                {
                    (day.Date.ToString("d MMM yyyy", Format.Spanish), Color.White),
                    (day.Gained is long gained ? (gained >= 0 ? "+" : "") + N(gained) : "—", (day.Gained ?? 0) > 0 ? Theme.Green : Color.White),
                    (day.Rank is int rank ? "#" + rank : "—", Color.White),
                    (N(day.Level) + (day.Levels != 0 ? $" ({(day.Levels > 0 ? "+" : "")}{day.Levels})" : ""), day.Levels > 0 ? Theme.Green : day.Levels < 0 ? Palette.Red : Color.White),
                    (day.Experience is long e ? N(e) : "—", Color.White),
                };
                x = r.X + S(14);
                for (int i = 0; i < cells.Length; i++)
                {
                    Write(g, cells[i].Item1, Fonts.Get(9f), cells[i].Item2, new RectangleF(x, ry, inner * widths[i], S(RowH)),
                        (i == 0 ? TextFormatFlags.Left : TextFormatFlags.Right) | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    x += inner * widths[i];
                }
                ry += S(RowH);
                index++;
            }
        }
    }
}

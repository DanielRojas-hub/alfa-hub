using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlfaHub
{
    // Página Hoy: dónde está Rashid, cuándo es el server save, cuándo termina el Tibiadrome y las
    // criaturas boosteadas del día.
    //
    // Rashid, el server save y el Tibiadrome se calculan aquí: el server save es todos los días a las
    // 10:00 de Alemania (CET/CEST), Rashid cambia de ciudad en cada server save según el día de la semana
    // y el Tibiadrome rota cada dos semanas, también en el server save (una rotación terminó el
    // 14 oct 2026 y la siguiente termina el 28 oct 2026). La criatura y el boss boosteados se piden a la
    // API de TibiaData (api.tibiadata.com), que los saca de tibia.com.

    static class TibiaCalendar
    {
        static readonly TimeZoneInfo Berlin = FindBerlin();

        static TimeZoneInfo FindBerlin()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"); }
        }

        static DateTime ToUtc(DateTime berlin) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(berlin, DateTimeKind.Unspecified), Berlin);

        /// Fecha (en Alemania) del día de Tibia en curso: empieza en el server save.
        public static DateTime TibiaDay(DateTime utcNow)
        {
            var berlin = TimeZoneInfo.ConvertTimeFromUtc(utcNow, Berlin);
            return berlin.Hour < 10 ? berlin.Date.AddDays(-1) : berlin.Date;
        }

        public static DateTime LastServerSave(DateTime utcNow) => ToUtc(TibiaDay(utcNow).AddHours(10));
        public static DateTime NextServerSave(DateTime utcNow) => ToUtc(TibiaDay(utcNow).AddDays(1).AddHours(10));

        /// Fin de una rotación conocida del Tibiadrome; las demás van de dos en dos semanas.
        static readonly DateTime DromeAnchor = new DateTime(2026, 10, 28);

        public static DateTime DromeEnd(DateTime utcNow)
        {
            int days = (TibiaDay(utcNow) - DromeAnchor).Days;
            int rotations = (int)Math.Floor(days / 14.0) + 1;
            return ToUtc(DromeAnchor.AddDays(rotations * 14).AddHours(10));
        }

        /// Ciudad de Rashid y dónde encontrarlo, según el día de Tibia.
        static readonly Dictionary<DayOfWeek, (string City, string Place)> rashid = new Dictionary<DayOfWeek, (string, string)>
        {
            [DayOfWeek.Sunday] = ("Carlin", "En el depot, un piso arriba"),
            [DayOfWeek.Monday] = ("Svargrond", "En la taberna de Dankwart, al sur del templo"),
            [DayOfWeek.Tuesday] = ("Liberty Bay", "En la taberna de Lyonel, al oeste del depot"),
            [DayOfWeek.Wednesday] = ("Port Hope", "En la taberna de Clyde, al oeste del depot"),
            [DayOfWeek.Thursday] = ("Ankrahmun", "En la taberna de Arito, encima de la oficina de correos"),
            [DayOfWeek.Friday] = ("Darashia", "En la taberna de Miraia, al sur de los guildhalls"),
            [DayOfWeek.Saturday] = ("Edron", "En la taberna de Mirabell, encima del depot"),
        };

        public static (string City, string Place) Rashid(DateTime tibiaDay) => rashid[tibiaDay.DayOfWeek];
    }

    /// Criatura y boss boosteados (TibiaData). Se guardan en boosteados.json para verlos sin conexión.
    [DataContract]
    sealed class Boosted
    {
        [DataMember] public string Creature, CreatureImage, Boss, BossImage;
        [DataMember] public string Day;   // día de Tibia en el que empezaron (aaaa-mm-dd)
    }

    static class TodayData
    {
        static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlfaHub", "boosteados.json");

        public static Boosted Current = Load();
        public static bool Failed;
        public static event Action Changed;
        static readonly Dictionary<string, Image> images = new Dictionary<string, Image>();
        static bool loading;
        static DateTime lastTry = DateTime.MinValue;

        [DataContract] sealed class Entry { [DataMember(Name = "name")] public string Name; [DataMember(Name = "image_url")] public string Image; }
        [DataContract] sealed class Body { [DataMember(Name = "boosted")] public Entry Boosted; }
        [DataContract] sealed class Creatures { [DataMember(Name = "creatures")] public Body Body; }
        [DataContract] sealed class Bosses { [DataMember(Name = "boostable_bosses")] public Body Body; }

        static Boosted Load()
        {
            try
            {
                using (var stream = File.OpenRead(FilePath))
                    return (Boosted)new DataContractJsonSerializer(typeof(Boosted)).ReadObject(stream);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SerializationException) { return null; }
        }

        public static Image Image(string url) => url != null && images.TryGetValue(url, out var image) ? image : null;

        /// Pide los boosteados si los guardados son de otro día. Justo después del server save la API
        /// puede seguir dando los de ayer, así que en esos minutos se vuelve a preguntar.
        public static void RefreshIfNeeded(Control ui)
        {
            var now = DateTime.UtcNow;
            string day = TibiaCalendar.TibiaDay(now).ToString("yyyy-MM-dd");
            if (Current != null && Current.Day == day) { LoadImages(ui, Current); return; }
            if (loading || (now - lastTry).TotalSeconds < 60) return;
            loading = true;
            lastTry = now;
            var previous = Current;
            Task.Run(() =>
            {
                Boosted fresh = null;
                try { fresh = Fetch(); }
                catch (Exception e) when (e is WebException || e is IOException || e is SerializationException) { }
                if (!ui.IsHandleCreated) { loading = false; return; }
                ui.BeginInvoke((Action)(() =>
                {
                    loading = false;
                    Failed = fresh == null;
                    if (fresh != null)
                    {
                        // En los primeros 15 min tras el server save no se da por buena si no ha cambiado.
                        bool settled = (now - TibiaCalendar.LastServerSave(now)).TotalMinutes > 15 || fresh.Creature != previous?.Creature;
                        fresh.Day = settled ? day : previous?.Day;
                        Current = fresh;
                        Save(fresh);
                        LoadImages(ui, fresh);
                    }
                    Changed?.Invoke();
                }));
            });
        }

        static void LoadImages(Control ui, Boosted boosted)
        {
            foreach (var (url, name) in new[] { (boosted.CreatureImage, boosted.Creature), (boosted.BossImage, boosted.Boss) })
            {
                if (url == null || images.ContainsKey(url)) continue;
                images[url] = null;  // pedida
                Task.Run(() =>
                {
                    Image image = null;
                    try
                    {
                        // El GIF necesita su stream abierto para animarse.
                        image = System.Drawing.Image.FromStream(new MemoryStream(DownloadSprite(url, name)));
                    }
                    catch (Exception e) when (e is WebException || e is IOException || e is ArgumentException || e is SerializationException) { }
                    if (!ui.IsHandleCreated) return;
                    ui.BeginInvoke((Action)(() =>
                    {
                        if (image == null) images.Remove(url); else images[url] = image;
                        Changed?.Invoke();
                    }));
                });
            }
        }

        static byte[] Download(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "AlfaHub";
                return client.DownloadData(url);
            }
        }

        [DataContract] sealed class WikiResponse { [DataMember(Name = "query")] public WikiQuery Query; }
        [DataContract] sealed class WikiQuery { [DataMember(Name = "pages")] public List<WikiPage> Pages; }
        [DataContract] sealed class WikiPage { [DataMember(Name = "imageinfo")] public List<WikiImage> Info; }
        [DataContract] sealed class WikiImage { [DataMember(Name = "url")] public string Url; }

        /// GIF de tibia.com; si no deja descargarlo (Cloudflare rechaza a veces a quien no es un
        /// navegador), el de la criatura en TibiaWiki.
        static byte[] DownloadSprite(string url, string name)
        {
            try { return Download(url); }
            catch (WebException)
            {
                var wiki = Get<WikiResponse>("https://tibia.fandom.com/api.php?action=query&prop=imageinfo&iiprop=url&format=json&formatversion=2&titles="
                                             + Uri.EscapeDataString("File:" + name.Replace(' ', '_') + ".gif"));
                string file = wiki?.Query?.Pages?.FirstOrDefault()?.Info?.FirstOrDefault()?.Url;
                if (file == null) throw;
                return Download(file + "&format=original");
            }
        }

        static T Get<T>(string url)
        {
            using (var stream = new MemoryStream(Download(url)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        static Boosted Fetch()
        {
            var creature = Get<Creatures>("https://api.tibiadata.com/v4/creatures")?.Body?.Boosted;
            var boss = Get<Bosses>("https://api.tibiadata.com/v4/boostablebosses")?.Body?.Boosted;
            if (creature == null || boss == null) throw new IOException("Respuesta de TibiaData incompleta");
            return new Boosted { Creature = creature.Name, CreatureImage = creature.Image, Boss = boss.Name, BossImage = boss.Image };
        }

        static void Save(Boosted boosted)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var stream = File.Create(FilePath))
                    new DataContractJsonSerializer(typeof(Boosted)).WriteObject(stream, boosted);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    sealed partial class HubForm
    {
        TodayGrid todayGrid;

        Control BuildTodayPage()
        {
            todayGrid = new TodayGrid(this) { Margin = new Padding(0, 4, 0, 8) };
            return Page(
                new PageHeader("Hoy", "Rashid, server save, Tibiadrome y criaturas boosteadas"),
                todayGrid);
        }
    }

    /// Las cuatro tarjetas de la página Hoy (2 × 2): Rashid, Tibiadrome, server save y boosteados.
    sealed class TodayGrid : PaintedControl, IStackItem
    {
        static readonly Color Gold = Color.FromArgb(242, 179, 64);
        static readonly Color Purple = Color.FromArgb(176, 115, 250);
        static readonly Color Orange = Color.FromArgb(255, 115, 64);
        static readonly Color Red = Color.FromArgb(245, 77, 77);
        static readonly Image RashidSprite = LoadSprite("rashid.gif");
        static readonly Image ServerSaveSprite = LoadSprite("server-save.gif");
        static readonly Image DromeSprite = LoadSprite("tibiadrome.gif");

        readonly Control ui;
        readonly HashSet<Image> animating = new HashSet<Image>();
        long lastSecond;

        public TodayGrid(Control ui)
        {
            this.ui = ui;
            BackColor = Theme.Back;
            AccessibleName = "Hoy en Tibia";
            TodayData.Changed += Invalidate;
            TodayData.RefreshIfNeeded(ui);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TodayData.Changed -= Invalidate;
                foreach (var image in animating) ImageAnimator.StopAnimate(image, OnFrame);
                animating.Clear();
            }
            base.Dispose(disposing);
        }

        static Image LoadSprite(string name)
        {
            var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AlfaHub." + name);
            return stream == null ? null : Image.FromStream(stream);  // el stream queda abierto: es un GIF animado
        }

        /// Se llama en cada tick: redibuja una vez por segundo y pide los boosteados al cambiar de día.
        public void Tick()
        {
            long second = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            if (second == lastSecond) return;
            lastSecond = second;
            if (second % 60 == 0) TodayData.RefreshIfNeeded(ui);
            Invalidate();
        }

        void OnFrame(object sender, EventArgs e)
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => { if (!IsDisposed) Invalidate(); }));
        }

        const int CardHeight = 196, Gap = 12;

        public int HeightFor(int width) => (int)((CardHeight * 2 + Gap) * K);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            ImageAnimator.UpdateFrames();
            float gap = Gap * k, w = (Width - gap) / 2, h = CardHeight * k;
            var now = DateTime.UtcNow;
            var day = TibiaCalendar.TibiaDay(now);

            // Rashid
            var rashid = TibiaCalendar.Rashid(day);
            var r = Card(g, new RectangleF(0, 0, w, h), "RASHID", "", Gold, RashidSprite);
            BigText(g, rashid.City, r);
            Detail(g, rashid.Place, r, 40);
            Footer(g, "Mañana: " + TibiaCalendar.Rashid(day.AddDays(1)).City, r);

            // Tibiadrome
            var end = TibiaCalendar.DromeEnd(now);
            var left = end - now;
            r = Card(g, new RectangleF(w + gap, 0, w, h), "TIBIADROME", "", Purple, DromeSprite);
            BigText(g, left.TotalDays >= 1 ? $"{(int)left.TotalDays} d {left.Hours} h" : Hms(left), r);
            Bar(g, r, 1 - left.TotalDays / 14, Purple);
            Detail(g, "Cada rotación dura 2 semanas", r, 58);
            Footer(g, "Termina el " + end.ToLocalTime().ToString("dddd d 'de' MMMM 'a las' HH:mm", Spanish), r);

            // Server save
            var next = TibiaCalendar.NextServerSave(now);
            left = next - now;
            var tint = left.TotalMinutes < 15 ? Red : Theme.Blue;
            r = Card(g, new RectangleF(0, h + gap, w, h), "SERVER SAVE", "", tint, ServerSaveSprite);
            BigText(g, Hms(left), r);
            Bar(g, r, 1 - left.TotalHours / 24, tint);
            Detail(g, "Todos los días a las 10:00 de Alemania. Rashid y los boosteados cambian con él.", r, 58);
            Footer(g, $"{(next.ToLocalTime().Date == DateTime.Now.Date ? "Hoy" : "Mañana")} a las {next.ToLocalTime():HH:mm} (tu hora)", r);

            // Boosteados
            r = Card(g, new RectangleF(w + gap, h + gap, w, h), "BOOSTEADOS", "", Orange, null);
            var boosted = TodayData.Current;
            if (boosted == null)
            {
                using (var font = new Font("Segoe UI", 9.75f))
                    CenterText(g, TodayData.Failed ? "Sin conexión con TibiaData" : "Cargando…", font, Theme.Muted, r);
            }
            else
            {
                float half = r.Width / 2;
                Creature(g, boosted.Creature, TodayData.Image(boosted.CreatureImage), "CRIATURA", new RectangleF(r.X, r.Y, half, r.Height - 20 * k));
                Creature(g, boosted.Boss, TodayData.Image(boosted.BossImage), "BOSS", new RectangleF(r.X + half, r.Y, half, r.Height - 20 * k));
            }
            Footer(g, boosted != null && boosted.Day != day.ToString("yyyy-MM-dd") ? "Pueden ser los de ayer: actualizando…" : "Más experiencia y loot hasta el server save", r);
        }

        static readonly System.Globalization.CultureInfo Spanish = new System.Globalization.CultureInfo("es-ES");

        static string Hms(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        }

        /// Fondo, franja de color, ícono y título; devuelve la zona de contenido (bajo el título).
        RectangleF Card(Graphics g, RectangleF card, string title, string glyph, Color tint, Image sprite)
        {
            float k = K;
            card.Inflate(-0.5f, -0.5f);
            Fill(g, card, 12 * k, Theme.Row);
            using (var path = CounterRenderer.RoundedRect(card, 12 * k))
            {
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var brush = new SolidBrush(tint)) g.FillRectangle(brush, card.X, card.Y, card.Width, 3 * k);
                g.Clip = clip;
            }
            var circle = new RectangleF(card.X + 16 * k, card.Y + 16 * k, 26 * k, 26 * k);
            using (var brush = new SolidBrush(Color.FromArgb(46, tint))) g.FillEllipse(brush, circle);
            using (var icons = new Font("Segoe MDL2 Assets", 9f))
                CenterText(g, glyph, icons, tint, circle);
            using (var font = new Font("Segoe UI Semibold", 8f))
                TextRenderer.DrawText(g, title, font, new Point((int)(circle.Right + 8 * k), (int)(circle.Y + 5 * k)), Theme.Muted, TextFormatFlags.NoPadding);
            if (sprite != null)
            {
                Animate(sprite);
                var size = new SizeF(sprite.Width * k, sprite.Height * k);
                Resources.DrawPixelArt(g, sprite, new RectangleF(card.Right - 10 * k - 64 * k + (64 * k - size.Width) / 2,
                    card.Y + 10 * k + (64 * k - size.Height) / 2, size.Width, size.Height));
            }
            return new RectangleF(card.X + 16 * k, circle.Bottom + 8 * k, card.Width - 32 * k, card.Bottom - circle.Bottom - 8 * k - 14 * k);
        }

        void Animate(Image image)
        {
            if (image == null || animating.Contains(image) || !ImageAnimator.CanAnimate(image)) return;
            animating.Add(image);
            ImageAnimator.Animate(image, OnFrame);
        }

        void BigText(Graphics g, string text, RectangleF r)
        {
            using (var font = new Font("Segoe UI", 20f, FontStyle.Bold))
                TextRenderer.DrawText(g, text, font, new Rectangle((int)r.X - (int)(2 * K), (int)r.Y, (int)(r.Width - 70 * K), (int)(40 * K)), Color.White,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        void Detail(Graphics g, string text, RectangleF r, float top)
        {
            using (var font = new Font("Segoe UI", 8.5f))
                TextRenderer.DrawText(g, text, font, new Rectangle((int)r.X, (int)(r.Y + top * K), (int)r.Width, (int)(36 * K)), Theme.Muted,
                    TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        }

        void Bar(Graphics g, RectangleF r, double value, Color color)
        {
            float k = K;
            var track = new RectangleF(r.X, r.Y + 44 * k, r.Width, 5 * k);
            Fill(g, track, 2.5f * k, Color.FromArgb(31, 255, 255, 255));
            float filled = (float)(track.Width * Math.Min(1, Math.Max(0, value)));
            if (filled > 0) Fill(g, new RectangleF(track.X, track.Y, Math.Max(filled, 5 * k), track.Height), 2.5f * k, color);
        }

        void Footer(Graphics g, string text, RectangleF r)
        {
            using (var font = new Font("Segoe UI Semibold", 8.75f))
                TextRenderer.DrawText(g, text, font, new Rectangle((int)r.X, (int)(r.Bottom - 16 * K), (int)r.Width, (int)(20 * K)),
                    Color.FromArgb(200, 200, 205), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        void Creature(Graphics g, string name, Image image, string label, RectangleF r)
        {
            float k = K;
            var box = new RectangleF(r.X + (r.Width - 64 * k) / 2, r.Y, 64 * k, 64 * k);
            if (image != null)
            {
                Animate(image);
                float s = Math.Min(1, Math.Min(64f / image.Width, 64f / image.Height)) * k;
                var size = new SizeF(image.Width * s, image.Height * s);
                Resources.DrawPixelArt(g, image, new RectangleF(box.X + (box.Width - size.Width) / 2, box.Bottom - size.Height, size.Width, size.Height));
            }
            using (var font = new Font("Segoe UI Semibold", 9.75f))
                CenterText(g, name, font, Color.White, new RectangleF(r.X, box.Bottom + 4 * k, r.Width, 20 * k));
            using (var font = new Font("Segoe UI Semibold", 7f))
                CenterText(g, label, font, Theme.Muted, new RectangleF(r.X, box.Bottom + 24 * k, r.Width, 14 * k));
        }
    }
}

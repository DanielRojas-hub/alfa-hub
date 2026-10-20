using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlfaHub
{
    // Outfiter: el personaje de Alfa Hub (ícono de la ventana y de la bandeja, y barra lateral) se elige
    // como en el Outfiter de TibiaWiki (https://tibia.fandom.com/wiki/Outfiter). Misma receta que la
    // versión de macOS (macos/Outfit.swift): hojas de sprites del wiki descargadas de su API y guardadas
    // en caché; cuadrícula de 64 × 64 con sprite y máscara de colores por orientación.

    [DataContract]
    sealed class OutfitConfig
    {
        [DataMember] public int Outfit = 1;       // Hunter: el de un personaje recién creado (el original de Alfa Hub)
        [DataMember] public bool Female;
        [DataMember] public bool Addon1;
        [DataMember] public bool Addon2;
        [DataMember] public int C1 = 95, C2 = 113, C3 = 39, C4 = 115;
        [DataMember] public int Facing = 2;       // 0 norte, 1 este, 2 sur, 3 oeste
        [DataMember] public int Mount;            // montura elegida en la lista
        [DataMember] public int Mc1, Mc2, Mc3, Mc4;
        [DataMember] public bool Animate;         // caminar en la vista previa y en la barra lateral
        [DataMember(Name = "Mounted")] bool? mounted;

        /// Montado encima (interruptor Montura). Antes, elegir montura era ir montado.
        public bool Mounted { get => mounted ?? Mount > 0; set => mounted = value; }

        /// La montura sobre la que va (0 = a pie).
        public int Ridden => Mounted && OutfitCatalog.Shared.CanMount(Outfit) ? Mount : 0;

        public OutfitConfig Clone() => (OutfitConfig)MemberwiseClone();

        /// Clave con todos los ajustes (para comparar y para la caché de miniaturas).
        public string Key => string.Join(",", Outfit, Female, Addon1, Addon2, C1, C2, C3, C4, Facing, Mount, Mounted, Mc1, Mc2, Mc3, Mc4, Animate);

        public bool SameAs(OutfitConfig other) => other != null && Key == other.Key;

        public bool IsOriginal => SameAs(new OutfitConfig());

        [OnDeserializing]
        void Defaults(StreamingContext context)
        {
            Outfit = 1; Addon1 = false; C1 = 95; C2 = 113; C3 = 39; C4 = 115; Facing = 2;
        }
    }

    /// Un outfit guardado en "Mis outfits".
    [DataContract]
    sealed class OutfitPreset
    {
        [DataMember] public string Name;
        [DataMember] public OutfitConfig Config;
    }

    /// Lista de outfits y monturas del Outfiter (outfits.json, sacado de MediaWiki:Outfiter.js).
    [DataContract]
    sealed class OutfitCatalog
    {
        [DataMember(Name = "names")] public List<string> Names;
        [DataMember(Name = "mounts")] public List<string> Mounts;
        [DataMember(Name = "colors")] public List<int[]> Colors;
        [DataMember(Name = "noAddons")] public List<string> NoAddons;
        [DataMember(Name = "oneAddon")] public List<string> OneAddon;
        [DataMember(Name = "noMount")] public List<string> NoMount;
        [DataMember(Name = "noFemale")] public List<string> NoFemale;
        [DataMember(Name = "femaleSuffix")] public List<string> FemaleSuffix;
        [DataMember(Name = "femaleNames")] public Dictionary<string, string> FemaleNames;
        [DataMember(Name = "mountColor")] public List<string> MountColor;
        [DataMember(Name = "noRide")] public List<string> NoRide;                       // sin filas de montado
        [DataMember(Name = "standing")] public Dictionary<string, int> Standing;         // "quieto animado" antes de caminar
        [DataMember(Name = "walking")] public Dictionary<string, int> Walking;           // fotogramas de caminata (si no son 8)
        [DataMember(Name = "mountStanding")] public Dictionary<string, int> MountStanding;
        [DataMember(Name = "mountWalking")] public Dictionary<string, int> MountWalking;

        public static readonly OutfitCatalog Shared = Load();

        static OutfitCatalog Load()
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AlfaHub.outfits.json"))
            {
                var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
                return (OutfitCatalog)new DataContractJsonSerializer(typeof(OutfitCatalog), settings).ReadObject(stream);
            }
        }

        public string Name(int id) => id >= 0 && id < Names.Count ? Names[id] : null;
        public bool HasAddons(int id) { string n = Name(id); return n != null && n != "None" && !NoAddons.Contains(n); }
        public bool OnlyOneAddon(int id) => OneAddon.Contains(Name(id) ?? "");
        public bool CanMount(int id) { string n = Name(id); return n != null && n != "None" && !NoMount.Contains(n); }
        public bool CanBeFemale(int id) { string n = Name(id); return n != null && !NoFemale.Contains(n) && !(id >= 100 && id < 200); }
        public bool MountColorable(int id) => id > 0 && id < Mounts.Count && MountColor.Contains(Mounts[id]);
        public bool CanRide(int id) { string n = Name(id); return n != null && !NoRide.Contains(n); }
        public string MountName(int id) => id > 0 && id < Mounts.Count ? Mounts[id] : null;

        public static int Get(Dictionary<string, int> table, string key, int fallback) =>
            key != null && table != null && table.TryGetValue(key, out int value) ? value : fallback;

        public string Title(int id, bool female = false)
        {
            string n = Name(id);
            if (n == null) return "?";
            if (female && FemaleNames.TryGetValue(n, out string f)) n = f;
            return n.Replace('_', ' ');
        }

        public string MountTitle(int id) => id > 0 && id < Mounts.Count ? Mounts[id].Replace('_', ' ') : "Sin montura";

        /// Página del wiki con la hoja de sprites del outfit.
        public string OutfitPage(int id, bool female)
        {
            string n = Name(id);
            if (n == null || n == "None") return null;
            if (id >= 100 && id < 200) return "Other/" + n;
            if (female && CanBeFemale(id)) return "Female/" + n + (FemaleSuffix.Contains(n) ? "_Female" : "");
            return "Male/" + n;
        }

        public string MountPage(int id) => id > 0 && id < Mounts.Count ? "Mount/" + Mounts[id] : null;

        public List<int> SortedOutfits =>
            Enumerable.Range(0, Names.Count).Where(i => Names[i] != null && Names[i] != "None")
                .OrderBy(i => Title(i), StringComparer.OrdinalIgnoreCase).ToList();

        public List<int> SortedMounts =>
            new[] { 0 }.Concat(Enumerable.Range(1, Mounts.Count - 1).OrderBy(i => MountTitle(i), StringComparer.OrdinalIgnoreCase)).ToList();

        /// Descripción corta ("Captains · addon 1 · sobre Widow Queen") para "Mis outfits".
        public string Summary(OutfitConfig c)
        {
            var parts = new List<string> { Title(c.Outfit, c.Female) };
            if (HasAddons(c.Outfit) && (c.Addon1 || c.Addon2))
                parts.Add(c.Addon1 && c.Addon2 ? "addons 1 y 2" : c.Addon1 ? "addon 1" : "addon 2");
            if (c.Ridden > 0) parts.Add("sobre " + MountTitle(c.Ridden));
            return string.Join(" · ", parts);
        }
    }

    /// Descarga (de la API de TibiaWiki) y caché de las hojas de sprites.
    static class OutfitSheets
    {
        static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlfaHub", "Outfiter");

        [DataContract] sealed class Response { [DataMember(Name = "query")] public Query Q; }
        [DataContract] sealed class Query { [DataMember(Name = "pages")] public List<Page> Pages; }
        [DataContract] sealed class Page { [DataMember(Name = "revisions")] public List<Revision> Revisions; }
        [DataContract] sealed class Revision { [DataMember(Name = "slots")] public Slots Slots; }
        [DataContract] sealed class Slots { [DataMember(Name = "main")] public Slot Main; }
        [DataContract] sealed class Slot { [DataMember(Name = "content")] public string Content; }

        public static byte[] Load(string page)
        {
            string file = Path.Combine(Folder, page.Replace('/', '-') + ".png");
            if (File.Exists(file)) return File.ReadAllBytes(file);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string url = "https://tibia.fandom.com/api.php?action=query&prop=revisions&rvprop=content&rvslots=main&format=json&formatversion=2&titles="
                         + Uri.EscapeDataString("Outfiter:" + page);
            byte[] json;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "AlfaHub (outfiter personal)";
                json = client.DownloadData(url);
            }
            Response response;
            using (var stream = new MemoryStream(json))
                response = (Response)new DataContractJsonSerializer(typeof(Response)).ReadObject(stream);
            string text = response?.Q?.Pages?.FirstOrDefault()?.Revisions?.FirstOrDefault()?.Slots?.Main?.Content;
            int start = text?.IndexOf("base64,", StringComparison.Ordinal) ?? -1;
            if (start < 0) throw new InvalidDataException("Sin sprite: " + page);
            start += 7;
            int end = text.IndexOf('<', start);
            string base64 = new string(text.Substring(start, (end < 0 ? text.Length : end) - start).Where(c => !char.IsWhiteSpace(c)).ToArray());
            byte[] png = Convert.FromBase64String(base64);
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(file, png);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return png;
        }

        static readonly Dictionary<string, OutfitSheet> decoded = new Dictionary<string, OutfitSheet>();
        static readonly List<string> order = new List<string>();

        /// La hoja ya decodificada (se guardan las últimas en memoria: cambiar un color no vuelve a leer el PNG).
        public static OutfitSheet Sheet(string page)
        {
            lock (decoded) if (decoded.TryGetValue(page, out var cached)) return cached;
            var sheet = new OutfitSheet(Load(page));
            lock (decoded)
            {
                decoded[page] = sheet;
                order.Remove(page);
                order.Add(page);
                if (order.Count > 12) { decoded.Remove(order[0]); order.RemoveAt(0); }
            }
            return sheet;
        }
    }

    /// Píxeles ARGB de toda la hoja (sin gestión de color, para comparar los colores de la máscara).
    sealed class OutfitSheet
    {
        const int Tile = 64;
        public readonly int Width, Height;
        readonly int[] pixels;

        public int Rows => Height / Tile;

        public OutfitSheet(byte[] png)
        {
            using (var ms = new MemoryStream(png))
            using (var image = Image.FromStream(ms, false, false))
            using (var bmp = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(image, 0, 0); }
                Width = bmp.Width; Height = bmp.Height;
                pixels = new int[Width * Height];
                var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                bmp.UnlockBits(data);
            }
        }

        /// Una celda (A, R, G, B por píxel); el rosa pasa a transparente.
        public int[] Cell(int col, int row)
        {
            var cell = new int[Tile * Tile * 4];
            for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++)
            {
                int sx = col * Tile + x, sy = row * Tile + y;
                if (sx >= Width || sy >= Height) continue;
                int argb = pixels[sy * Width + sx];
                int i = (y * Tile + x) * 4;
                int a = (argb >> 24) & 255, r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255;
                if (r == 255 && g == 0 && b == 255) a = 0;
                cell[i] = a; cell[i + 1] = r; cell[i + 2] = g; cell[i + 3] = b;
            }
            return cell;
        }
    }

    /// Un personaje dibujado: quieto (para íconos) y los fotogramas de la caminata, recortados al contorno.
    sealed class OutfitSprite : IDisposable
    {
        public Bitmap Still;
        /// Fotogramas de la caminata, todos del mismo tamaño (vacío si no se pidió animar).
        public readonly List<Bitmap> Frames = new List<Bitmap>();
        public int FrameMs = 100;

        /// El fotograma que toca en este momento.
        public Image Now => Frames.Count == 0 ? Still : Frames[(int)((uint)Environment.TickCount / (uint)FrameMs % (uint)Frames.Count)];

        public void Dispose()
        {
            Still?.Dispose();
            foreach (var f in Frames) f.Dispose();
        }
    }

    static class OutfitRenderer
    {
        const int Tile = 64;

        static int[] Blend(int[] main, int[] mask, int[] colors)
        {
            var table = OutfitCatalog.Shared.Colors;
            int[] C(int index) => table[Math.Max(0, Math.Min(table.Count - 1, index))];
            var res = (int[])main.Clone();
            for (int i = 0; i < res.Length; i += 4)
            {
                int r = mask[i + 1], g = mask[i + 2], b = mask[i + 3];
                int[] c = null;
                if (r == 255 && g == 255 && b == 0) c = C(colors[0]);
                else if (r == 255 && g == 0 && b == 0) c = C(colors[1]);
                else if (r == 0 && g == 255 && b == 0) c = C(colors[2]);
                else if (r == 0 && g == 0 && b == 255) c = C(colors[3]);
                if (c != null) { r = c[0]; g = c[1]; b = c[2]; }
                if (!(r == 255 && g == 0 && b == 255))
                {
                    res[i + 1] = r * res[i + 1] / 255;
                    res[i + 2] = g * res[i + 2] / 255;
                    res[i + 3] = b * res[i + 3] / 255;
                }
            }
            return res;
        }

        static int[] Merge(int[] bottom, int[] top)
        {
            if (bottom == null) return top;
            if (top == null) return bottom;
            var res = (int[])bottom.Clone();
            for (int i = 0; i < res.Length; i += 4)
                if (top[i] != 0) { res[i] = top[i]; res[i + 1] = top[i + 1]; res[i + 2] = top[i + 2]; res[i + 3] = top[i + 3]; }
            return res;
        }

        /// Un fotograma (64 × 64) del personaje, con la montura debajo si la hay. Fotograma 0: quieto.
        static int[] Pixels(OutfitConfig c, OutfitSheet sheet, OutfitSheet mountSheet, int frame, int mountFrame)
        {
            var catalog = OutfitCatalog.Shared;
            int facing = Math.Max(0, Math.Min(3, c.Facing));
            bool addons = catalog.HasAddons(c.Outfit);
            bool mounted = c.Ridden > 0 && mountSheet != null;
            // Filas por fotograma: capas (sprite + addons) × (a pie + montado).
            int perFrame = (addons ? 3 : 1) * (catalog.CanRide(c.Outfit) ? 2 : 1);
            int first = (mounted ? (addons ? 3 : 1) : 0) + frame * perFrame;
            if (first + (addons ? 3 : 1) > sheet.Rows) first -= frame * perFrame;  // hoja sin ese fotograma: quieto
            int[] pixels = null;
            bool[] layers = { true, addons && c.Addon1, addons && c.Addon2 };
            for (int i = 0; i < 3; i++)
            {
                if (!layers[i]) continue;
                pixels = Merge(pixels, Blend(sheet.Cell(facing * 2, first + i), sheet.Cell(facing * 2 + 1, first + i),
                    new[] { c.C1, c.C2, c.C3, c.C4 }));
            }
            if (mounted)
            {
                bool colorable = catalog.MountColorable(c.Ridden);
                int row = mountFrame < mountSheet.Rows ? mountFrame : 0;
                int[] m = mountSheet.Cell(colorable ? facing * 2 : facing, row);
                if (colorable) m = Blend(m, mountSheet.Cell(facing * 2 + 1, row), new[] { c.Mc1, c.Mc2, c.Mc3, c.Mc4 });
                pixels = Merge(m, pixels);
            }
            return pixels;
        }

        /// Descarga (si hace falta) las hojas y dibuja el personaje quieto y, si c.Animate, su caminata
        /// (como "Animate" en el Outfiter). Se llama fuera del hilo de la interfaz.
        public static OutfitSprite Render(OutfitConfig c)
        {
            var catalog = OutfitCatalog.Shared;
            string page = catalog.OutfitPage(c.Outfit, c.Female);
            if (page == null) return null;
            var sheet = OutfitSheets.Sheet(page);
            string mountPage = c.Ridden > 0 ? catalog.MountPage(c.Ridden) : null;
            var mountSheet = mountPage != null ? OutfitSheets.Sheet(mountPage) : null;
            int[] still = Pixels(c, sheet, mountSheet, 0, 0);
            if (still == null) return null;
            var sprite = new OutfitSprite { Still = ToBitmap(still, Bounds(still)) };
            if (!c.Animate) return sprite;
            // Fotogramas de la caminata: después del quieto (o de los de "quieto animado").
            string name = catalog.Name(c.Outfit), mountName = catalog.MountName(c.Ridden);
            int walkO = OutfitCatalog.Get(catalog.Walking, name, 8), firstO = OutfitCatalog.Get(catalog.Standing, name, 1);
            int walkM = c.Ridden > 0 ? OutfitCatalog.Get(catalog.MountWalking, mountName, 8) : walkO;
            int firstM = OutfitCatalog.Get(catalog.MountStanding, mountName, 1);
            int count = walkO * walkM / Gcd(walkO, walkM);
            var frames = new List<int[]>();
            for (int i = 0; i < count; i++)
            {
                int[] p = Pixels(c, sheet, mountSheet, firstO + i % walkO, firstM + i % walkM);
                if (p != null) frames.Add(p);
            }
            // Todos los fotogramas con el mismo recorte, para que no "salte" al caminar.
            Rectangle box = Bounds(still);
            foreach (var f in frames) box = Rectangle.Union(box, Bounds(f));
            foreach (var f in frames) sprite.Frames.Add(ToBitmap(f, box));
            sprite.FrameMs = Math.Max(100, 800 / walkO);
            return sprite;
        }

        static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

        /// Contorno de los píxeles visibles (toda la celda si no hay ninguno).
        static Rectangle Bounds(int[] pixels)
        {
            int minX = Tile, minY = Tile, maxX = -1, maxY = -1;
            for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++)
                if (pixels[(y * Tile + x) * 4] > 5) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            return maxX < minX ? new Rectangle(0, 0, Tile, Tile) : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// La parte box de un fotograma, como Bitmap.
        static Bitmap ToBitmap(int[] pixels, Rectangle box)
        {
            var argb = new int[box.Width * box.Height];
            for (int y = 0; y < box.Height; y++)
            for (int x = 0; x < box.Width; x++)
            {
                int i = ((box.Y + y) * Tile + box.X + x) * 4;
                argb[y * box.Width + x] = (pixels[i] << 24) | (pixels[i + 1] << 16) | (pixels[i + 2] << 8) | pixels[i + 3];
            }
            var bmp = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, box.Width, box.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(argb, 0, data.Scan0, argb.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        /// Ícono cuadrado: el personaje centrado sin fondo, con escala entera si se puede.
        public static Icon Icon(Image sprite, int size)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    float max = size * 0.95f;
                    float scale = Math.Min(max / sprite.Width, max / sprite.Height);
                    if (scale >= 1) scale = (float)Math.Floor(scale);
                    g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    float w = sprite.Width * scale, h = sprite.Height * scale;
                    g.DrawImage(sprite, (float)Math.Round((size - w) / 2), (float)Math.Round((size - h) / 2), w, h);
                }
                IntPtr handle = bmp.GetHicon();
                using (var temp = System.Drawing.Icon.FromHandle(handle))
                {
                    var icon = (Icon)temp.Clone();
                    DestroyIcon(handle);
                    return icon;
                }
            }
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    }

    /// El personaje: lo que se edita en la página Outfit (borrador) y el que está en uso (guardado). El
    /// borrador solo pasa al ícono de la ventana, la bandeja y la barra lateral con "Guardar cambios".
    sealed class OutfitState
    {
        static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlfaHub");
        static string FilePath => Path.Combine(Folder, "outfit.json");
        static string PresetsPath => Path.Combine(Folder, "outfit-presets.json");

        public OutfitConfig Draft { get; private set; } = new OutfitConfig();
        public OutfitConfig Saved { get; private set; } = new OutfitConfig();
        /// Dibujo del borrador (vista previa) y del guardado (barra lateral, íconos); null = el original del .exe.
        public OutfitSprite Preview { get; private set; }
        public OutfitSprite Current { get; private set; }
        public bool Loading { get; private set; }
        public bool Failed { get; private set; }
        public readonly List<OutfitPreset> Presets = new List<OutfitPreset>();
        /// Borrador, vista previa, "Mis outfits" o sus miniaturas cambiaron.
        public event Action Changed;
        /// Cambió el personaje en uso.
        public event Action SavedChanged;
        readonly Control ui;
        readonly Dictionary<string, Bitmap> thumbnails = new Dictionary<string, Bitmap>();
        readonly HashSet<string> pending = new HashSet<string>();
        int draftVersion, savedVersion;

        public OutfitState(Control ui)
        {
            this.ui = ui;
            Saved = Read<OutfitConfig>(FilePath) ?? new OutfitConfig();
            Draft = Saved.Clone();
            var presets = Read<List<OutfitPreset>>(PresetsPath);
            if (presets != null) Presets.AddRange(presets.Where(p => p?.Config != null));
        }

        static T Read<T>(string path) where T : class
        {
            try
            {
                if (File.Exists(path))
                    using (var stream = File.OpenRead(path))
                        return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
            catch (SerializationException) { }
            catch (IOException) { }
            return null;
        }

        static void Write<T>(string path, T value)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                using (var stream = File.Create(path))
                    new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// Personaje de la barra lateral (el fotograma que toca si camina).
        public Image Display => Current?.Now ?? Resources.Character;

        public bool HasChanges => !Draft.SameAs(Saved);

        public void SetDraft(OutfitConfig config)
        {
            Draft = config;
            RenderDraft();
        }

        /// "Guardar cambios": el borrador pasa a ser el personaje.
        public void Save()
        {
            Saved = Draft.Clone();
            Write(FilePath, Saved);
            RenderSaved();
            Changed?.Invoke();
        }

        public void Discard() => SetDraft(Saved.Clone());

        public void AddPreset(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = OutfitCatalog.Shared.Title(Draft.Outfit, Draft.Female);
            Presets.Add(new OutfitPreset { Name = name, Config = Draft.Clone() });
            Write(PresetsPath, Presets);
            Changed?.Invoke();
        }

        public void RemovePreset(OutfitPreset preset)
        {
            Presets.Remove(preset);
            Write(PresetsPath, Presets);
            Changed?.Invoke();
        }

        /// Dibuja en segundo plano y entrega el resultado en el hilo de la interfaz (si sigue vigente).
        void RenderAsync(OutfitConfig c, Func<bool> current, Action<OutfitSprite> done)
        {
            Task.Run(() =>
            {
                OutfitSprite sprite = null;
                try { sprite = OutfitRenderer.Render(c); }
                catch (Exception e) when (e is WebException || e is IOException || e is InvalidDataException || e is FormatException
                                          || e is SerializationException || e is ArgumentException)
                {
                    sprite = null;
                }
                if (!ui.IsHandleCreated) { sprite?.Dispose(); return; }
                ui.BeginInvoke((Action)(() =>
                {
                    if (!current()) { sprite?.Dispose(); return; }
                    done(sprite);
                }));
            });
        }

        void RenderDraft()
        {
            int v = ++draftVersion;
            var c = Draft.Clone();
            if (c.IsOriginal)
            {
                // El original va dentro del .exe: no hace falta descargarlo.
                Preview?.Dispose();
                Preview = null;
                Loading = Failed = false;
                Changed?.Invoke();
                return;
            }
            Loading = true;
            Failed = false;
            Changed?.Invoke();
            RenderAsync(c, () => v == draftVersion, sprite =>
            {
                Loading = false;
                Failed = sprite == null;
                if (sprite != null) { Preview?.Dispose(); Preview = sprite; }
                Changed?.Invoke();
            });
        }

        public void RenderSaved()
        {
            int v = ++savedVersion;
            var c = Saved.Clone();
            if (c.IsOriginal)
            {
                Current?.Dispose();
                Current = null;
                SavedChanged?.Invoke();
                return;
            }
            RenderAsync(c, () => v == savedVersion, sprite =>
            {
                if (sprite == null) return;
                Current?.Dispose();
                Current = sprite;
                SavedChanged?.Invoke();
            });
        }

        /// Miniatura de un outfit de "Mis outfits" (null mientras se dibuja).
        public Image Thumbnail(OutfitConfig config)
        {
            if (config.IsOriginal) return Resources.Character;
            var still = config.Clone();
            still.Animate = false;
            string key = still.Key;
            if (thumbnails.TryGetValue(key, out var image)) return image;
            if (pending.Add(key))
                RenderAsync(still, () => true, sprite =>
                {
                    if (sprite == null) return;
                    thumbnails[key] = sprite.Still;
                    sprite.Still = null;
                    sprite.Dispose();
                    Changed?.Invoke();
                });
            return null;
        }
    }
}

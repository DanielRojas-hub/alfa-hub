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
        [DataMember] public int Outfit = 235;     // Captains: el personaje original de Alfa Hub
        [DataMember] public bool Female;
        [DataMember] public bool Addon1 = true;
        [DataMember] public bool Addon2;
        [DataMember] public int C1 = 95, C2 = 95, C3 = 95, C4 = 114;
        [DataMember] public int Facing = 2;       // 0 norte, 1 este, 2 sur, 3 oeste
        [DataMember] public int Mount;
        [DataMember] public int Mc1, Mc2, Mc3, Mc4;

        public OutfitConfig Clone() => (OutfitConfig)MemberwiseClone();

        public bool IsOriginal =>
            Outfit == 235 && !Female && Addon1 && !Addon2 && C1 == 95 && C2 == 95 && C3 == 95 && C4 == 114
            && Facing == 2 && Mount == 0;

        [OnDeserializing]
        void Defaults(StreamingContext context)
        {
            Outfit = 235; Addon1 = true; C1 = 95; C2 = 95; C3 = 95; C4 = 114; Facing = 2;
        }
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
    }

    static class OutfitRenderer
    {
        const int Tile = 64;

        /// Píxeles ARGB de toda la hoja (sin gestión de color, para comparar los colores de la máscara).
        sealed class Sheet
        {
            public int Width, Height;
            public int[] Pixels;

            public Sheet(byte[] png)
            {
                using (var ms = new MemoryStream(png))
                using (var image = Image.FromStream(ms, false, false))
                using (var bmp = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(image, 0, 0); }
                    Width = bmp.Width; Height = bmp.Height;
                    Pixels = new int[Width * Height];
                    var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    Marshal.Copy(data.Scan0, Pixels, 0, Pixels.Length);
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
                    int argb = Pixels[sy * Width + sx];
                    int i = (y * Tile + x) * 4;
                    int a = (argb >> 24) & 255, r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255;
                    if (r == 255 && g == 0 && b == 255) a = 0;
                    cell[i] = a; cell[i + 1] = r; cell[i + 2] = g; cell[i + 3] = b;
                }
                return cell;
            }
        }

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

        /// El personaje quieto (64 × 64), con la montura debajo si la hay.
        public static Bitmap Render(OutfitConfig c, byte[] outfitPng, byte[] mountPng)
        {
            var catalog = OutfitCatalog.Shared;
            var sheet = new Sheet(outfitPng);
            int facing = Math.Max(0, Math.Min(3, c.Facing));
            bool addons = catalog.HasAddons(c.Outfit);
            bool mounted = c.Mount > 0 && mountPng != null && catalog.CanMount(c.Outfit);
            int baseRow = mounted ? (addons ? 3 : 1) : 0;
            int[] pixels = null;
            bool[] layers = { true, addons && c.Addon1, addons && c.Addon2 };
            for (int i = 0; i < 3; i++)
            {
                if (!layers[i]) continue;
                pixels = Merge(pixels, Blend(sheet.Cell(facing * 2, baseRow + i), sheet.Cell(facing * 2 + 1, baseRow + i),
                    new[] { c.C1, c.C2, c.C3, c.C4 }));
            }
            if (mounted)
            {
                var mountSheet = new Sheet(mountPng);
                bool colorable = catalog.MountColorable(c.Mount);
                int[] m = mountSheet.Cell(colorable ? facing * 2 : facing, 0);
                if (colorable) m = Blend(m, mountSheet.Cell(facing * 2 + 1, 0), new[] { c.Mc1, c.Mc2, c.Mc3, c.Mc4 });
                pixels = Merge(m, pixels);
            }
            var bmp = new Bitmap(Tile, Tile, PixelFormat.Format32bppArgb);
            var argb = new int[Tile * Tile];
            for (int p = 0; p < argb.Length; p++)
            {
                int i = p * 4;
                argb[p] = pixels == null ? 0 : (pixels[i] << 24) | (pixels[i + 1] << 16) | (pixels[i + 2] << 8) | pixels[i + 3];
            }
            var data = bmp.LockBits(new Rectangle(0, 0, Tile, Tile), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(argb, 0, data.Scan0, argb.Length);
            bmp.UnlockBits(data);
            return Cropped(bmp);
        }

        /// Recorta los bordes transparentes (como character.png).
        static Bitmap Cropped(Bitmap bmp)
        {
            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y).A > 5) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            if (maxX < minX) return bmp;
            var crop = bmp.Clone(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1), PixelFormat.Format32bppArgb);
            bmp.Dispose();
            return crop;
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

    /// El personaje elegido: lo dibuja en segundo plano (descargando las hojas si hace falta).
    sealed class OutfitState
    {
        static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlfaHub", "outfit.json");

        public OutfitConfig Config = new OutfitConfig();
        /// Sprite recortado (null = el personaje original del .exe).
        public Bitmap Sprite { get; private set; }
        public bool Loading { get; private set; }
        public bool Failed { get; private set; }
        public event Action Changed;
        readonly Control ui;
        int version;

        public OutfitState(Control ui)
        {
            this.ui = ui;
            try
            {
                if (File.Exists(FilePath))
                    using (var stream = File.OpenRead(FilePath))
                        Config = (OutfitConfig)new DataContractJsonSerializer(typeof(OutfitConfig)).ReadObject(stream) ?? new OutfitConfig();
            }
            catch (SerializationException) { }
            catch (IOException) { }
        }

        public Image Display => Sprite ?? Resources.Character;

        public void Set(OutfitConfig config)
        {
            Config = config;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var stream = File.Create(FilePath))
                    new DataContractJsonSerializer(typeof(OutfitConfig)).WriteObject(stream, config);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Render();
        }

        public void Render()
        {
            int v = ++version;
            var c = Config.Clone();
            var catalog = OutfitCatalog.Shared;
            string page = catalog.OutfitPage(c.Outfit, c.Female);
            if (page == null) return;
            if (c.IsOriginal)
            {
                // El original va dentro del .exe: no hace falta descargarlo.
                Sprite?.Dispose();
                Sprite = null;
                Loading = Failed = false;
                Changed?.Invoke();
                return;
            }
            Loading = true;
            Failed = false;
            Changed?.Invoke();
            Task.Run(() =>
            {
                Bitmap sprite = null;
                try
                {
                    byte[] outfit = OutfitSheets.Load(page);
                    string mountPage = c.Mount > 0 && catalog.CanMount(c.Outfit) ? catalog.MountPage(c.Mount) : null;
                    byte[] mount = mountPage != null ? OutfitSheets.Load(mountPage) : null;
                    sprite = OutfitRenderer.Render(c, outfit, mount);
                }
                catch (Exception e) when (e is WebException || e is IOException || e is InvalidDataException || e is FormatException
                                          || e is SerializationException || e is ArgumentException)
                {
                    sprite = null;
                }
                if (!ui.IsHandleCreated) return;
                ui.BeginInvoke((Action)(() =>
                {
                    if (v != version) { sprite?.Dispose(); return; }
                    Loading = false;
                    Failed = sprite == null;
                    if (sprite != null) { Sprite?.Dispose(); Sprite = sprite; }
                    Changed?.Invoke();
                }));
            });
        }
    }
}

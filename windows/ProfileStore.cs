using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlfaHub
{
    /// Los perfiles guardados, el perfil y el char abiertos en la página, el char del ícono y la
    /// sincronización sola con tibia.com (una vez por día de Tibia, también sin abrir el char).
    sealed class ProfileStore
    {
        public readonly List<Profile> Profiles = ProfileFiles.Load();
        /// Perfil y char abiertos en la página (no se guardan).
        public string OpenProfile, OpenChar;
        /// Se llama cuando cambian datos que la página tiene que volver a dibujar (sincronizaciones…).
        public event Action Changed;
        /// Cambió el outfit del ícono (otro char, o el mismo con otro outfit); null = el original.
        public event Action<SharedOutfit> IconOutfitChanged;

        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 500 };
        readonly System.Windows.Forms.Timer syncTimer = new System.Windows.Forms.Timer { Interval = 30 * 60 * 1000 };
        // El hilo de la interfaz: se toma al arrancar (al crear el hub todavía no existe).
        SynchronizationContext ui;
        string iconChar;
        string lastIconKey;
        bool syncing;

        static string IconPath => Path.Combine(ProfileFiles.Folder, "icon-char.txt");

        public ProfileStore()
        {
            try { iconChar = File.Exists(IconPath) ? File.ReadAllText(IconPath).Trim() : null; } catch (IOException) { }
            if (string.IsNullOrEmpty(iconChar)) iconChar = null;
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); Save(); };
            syncTimer.Tick += (s, e) => SyncStale();
        }

        public void Start()
        {
            ui = SynchronizationContext.Current;
            syncTimer.Start();
            SyncStale();
        }

        /// Escribir las kills tecla a tecla no guarda cada vez: se espera medio segundo.
        public void Touch()
        {
            saveTimer.Stop();
            saveTimer.Start();
            IconCheck();
        }

        public void Save()
        {
            saveTimer.Stop();
            ProfileFiles.Save(Profiles);
        }

        public Profile Find(string id) => Profiles.FirstOrDefault(p => p.Id == id);
        public GameChar FindChar(Profile profile, string id) => profile?.Chars.FirstOrDefault(c => c.Id == id);

        public Profile AddProfile()
        {
            var profile = new Profile { Name = "Perfil " + (Profiles.Count + 1) };
            profile.Chars.Add(new GameChar());
            Profiles.Add(profile);
            Touch();
            return profile;
        }

        public void Duplicate(Profile profile)
        {
            var copy = ProfileFiles.Deserialize<Profile>(ProfileFiles.Serialize(profile));
            copy.Id = Ids.New();
            copy.Name += " (copia)";
            copy.SourceId = null;
            copy.ImportedAt = null;
            foreach (var c in copy.Chars) c.Id = Ids.New();
            Profiles.Insert(Profiles.IndexOf(profile) + 1, copy);
            Touch();
        }

        public void Remove(Profile profile)
        {
            Profiles.Remove(profile);
            if (OpenProfile == profile.Id) { OpenProfile = null; OpenChar = null; }
            Touch();
        }

        /// Añade un perfil compartido. Si ya se había importado (mismo original), se actualiza esa copia.
        public (Profile Profile, bool Updated) Import(Profile shared)
        {
            var profile = shared;
            profile.SourceId = shared.Id;
            profile.ImportedAt = Ids.Date(DateTime.UtcNow);
            int index = Profiles.FindIndex(p => p.SourceId == shared.Id);
            if (index >= 0)
            {
                profile.Id = Profiles[index].Id;
                Profiles[index] = profile;
                Touch();
                return (profile, true);
            }
            profile.Id = Ids.New();
            Profiles.Add(profile);
            Touch();
            return (profile, false);
        }

        // ---------- Ícono ----------

        /// El char cuyo outfit va en el ícono de Alfa Hub (ventana, barra de tareas, bandeja y barra lateral).
        public string IconChar
        {
            get => iconChar;
            set
            {
                iconChar = value;
                try
                {
                    Directory.CreateDirectory(ProfileFiles.Folder);
                    if (value == null) File.Delete(IconPath); else File.WriteAllText(IconPath, value);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                IconCheck();
            }
        }

        /// El outfit del ícono: el del char elegido (null = el original, también si se borró).
        public SharedOutfit IconOutfit => iconChar == null ? null : Profiles.SelectMany(p => p.Chars).FirstOrDefault(c => c.Id == iconChar)?.Outfit;

        void IconCheck()
        {
            // Hasta que se elige un char se deja el ícono que hubiera (el de la antigua página Outfit).
            if (iconChar == null && lastIconKey == null) return;
            var outfit = IconOutfit;
            string key = outfit == null ? "original" : string.Join(",", outfit.Outfit, outfit.Female, outfit.Addon1, outfit.Addon2, outfit.C1, outfit.C2,
                outfit.C3, outfit.C4, outfit.Facing, outfit.Mount, outfit.Mounted, outfit.Mc1, outfit.Mc2, outfit.Mc3, outfit.Mc4, outfit.Animate);
            if (key == lastIconKey) return;
            lastIconKey = key;
            IconOutfitChanged?.Invoke(outfit);
        }

        // ---------- Sincronización ----------

        /// Busca un char en tibia.com en segundo plano y entrega el resultado en el hilo de la interfaz.
        public void Lookup(string name, Action<TibiaInfo> done)
        {
            ui = ui ?? SynchronizationContext.Current;
            Task.Run(() =>
            {
                var info = CharLookup.Fetch(name);
                ui.Post(_ => done(info), null);
            });
        }

        /// Vuelve a sincronizar, una vez por día de Tibia, cada char ya sincronizado: así el historial de
        /// experiencia se llena aunque no se abra el char. Unos minutos después del server save, cuando
        /// tibia.com ya tiene los highscores nuevos.
        void SyncStale()
        {
            if (syncing || ui == null) return;
            var now = DateTime.UtcNow;
            var day = TibiaCalendar.LastServerSave(now);
            if ((now - day).TotalMinutes < 20) return;
            var stale = Profiles.SelectMany(p => p.Chars).Where(c => c.IsSynced && c.Tibia.Synced < day.AddMinutes(20)).ToList();
            if (stale.Count == 0) return;
            syncing = true;
            Task.Run(() =>
            {
                foreach (var c in stale)
                {
                    var info = CharLookup.Fetch(c.Name);
                    if (info != null)
                        ui.Post(_ =>
                        {
                            if (string.Equals(c.Name, info.Name, StringComparison.OrdinalIgnoreCase)) { c.Apply(info); Touch(); Changed?.Invoke(); }
                        }, null);
                    Thread.Sleep(2000);  // sin agobiar a TibiaData
                }
                ui.Post(_ => syncing = false, null);
            });
        }
    }

    /// Imágenes de TibiaWiki (File:<nombre>.gif o el archivo tal cual, "Zap.png"): criaturas, items y
    /// charms. Se piden de 50 en 50 a la API (la URL de cada archivo), se descargan y se guardan en
    /// %APPDATA%\AlfaHub\Wiki. Se usa el primer fotograma.
    static class WikiImages
    {
        static readonly Dictionary<string, Image> memory = new Dictionary<string, Image>();
        static readonly HashSet<string> failed = new HashSet<string>(), pending = new HashSet<string>();
        static readonly List<string> queue = new List<string>();
        static SynchronizationContext ui;
        static bool flushing;
        /// Llegó alguna imagen nueva (para volver a dibujar).
        public static event Action Loaded;

        static string Folder => Path.Combine(ProfileFiles.Folder, "Wiki");
        static string WikiFile(string name) => name.EndsWith(".png") || name.EndsWith(".gif") ? name : name + ".gif";
        static string FilePath(string name) => Path.Combine(Folder, string.Join("-", WikiFile(name).Split(Path.GetInvalidFileNameChars())));

        /// La imagen si ya está; si no, la pide y devuelve null.
        public static Image Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (memory.TryGetValue(name, out var image)) return image;
            if (failed.Contains(name)) return null;
            if (ui == null) ui = SynchronizationContext.Current;
            string path = FilePath(name);
            if (File.Exists(path))
            {
                image = Decode(File.ReadAllBytes(path));
                if (image != null) { memory[name] = image; return image; }
            }
            if (pending.Add(name))
            {
                lock (queue) queue.Add(name);
                if (!flushing) { flushing = true; Task.Delay(120).ContinueWith(_ => Flush()); }
            }
            return null;
        }

        static Image Decode(byte[] data)
        {
            try
            {
                using (var stream = new MemoryStream(data))
                using (var image = Image.FromStream(stream))
                    return new Bitmap(image);
            }
            catch (ArgumentException) { return null; }
        }

        [DataContract] sealed class Response { [DataMember(Name = "query")] public Query Query; }
        [DataContract] sealed class Query { [DataMember(Name = "normalized")] public List<Rename> Normalized; [DataMember(Name = "pages")] public List<Page> Pages; }
        [DataContract] sealed class Rename { [DataMember(Name = "from")] public string From; [DataMember(Name = "to")] public string To; }
        [DataContract] sealed class Page { [DataMember(Name = "title")] public string Title; [DataMember(Name = "imageinfo")] public List<Info> Info; }
        [DataContract] sealed class Info { [DataMember(Name = "url")] public string Url; }

        static void Flush()
        {
            while (true)
            {
                List<string> batch;
                lock (queue)
                {
                    batch = queue.Take(50).ToList();
                    queue.RemoveRange(0, batch.Count);
                }
                if (batch.Count == 0) break;
                var urls = new Dictionary<string, string>();
                try
                {
                    var titles = batch.Select(n => "File:" + WikiFile(n)).ToList();
                    byte[] json = CharLookup.Download("https://tibia.fandom.com/api.php?action=query&prop=imageinfo&iiprop=url&format=json&formatversion=2&titles="
                                                      + Uri.EscapeDataString(string.Join("|", titles)));
                    Response response;
                    using (var stream = new MemoryStream(json)) response = (Response)new DataContractJsonSerializer(typeof(Response)).ReadObject(stream);
                    var renamed = (response?.Query?.Normalized ?? new List<Rename>()).GroupBy(r => r.To).ToDictionary(g => g.Key, g => g.First().From);
                    foreach (var page in response?.Query?.Pages ?? new List<Page>())
                    {
                        string url = page.Info?.FirstOrDefault()?.Url;
                        if (url == null) continue;
                        string title = renamed.TryGetValue(page.Title, out var from) ? from : page.Title;
                        int i = titles.IndexOf(title);
                        if (i >= 0) urls[batch[i]] = url;
                    }
                }
                catch (Exception e) when (e is WebException || e is IOException || e is SerializationException) { }
                Parallel.ForEach(batch, new ParallelOptions { MaxDegreeOfParallelism = 6 }, name =>
                {
                    byte[] data = null;
                    if (urls.TryGetValue(name, out var url))
                        try { data = CharLookup.Download(url + "&format=original"); }
                        catch (Exception e) when (e is WebException || e is IOException) { }
                    var image = data == null ? null : Decode(data);
                    if (image != null)
                        try { Directory.CreateDirectory(Folder); File.WriteAllBytes(FilePath(name), data); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    ui?.Post(_ =>
                    {
                        pending.Remove(name);
                        if (image != null) memory[name] = image; else failed.Add(name);
                    }, null);
                });
                ui?.Post(_ => Loaded?.Invoke(), null);
            }
            ui?.Post(_ =>
            {
                flushing = false;
                bool more;
                lock (queue) more = queue.Count > 0;
                if (more) { flushing = true; Task.Run(() => Flush()); }
            }, null);
        }
    }
}

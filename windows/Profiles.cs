using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AlfaHub
{
    // Perfiles: cada perfil tiene varios chars; de cada char, su outfit, sus items (equipados en su hueco o
    // guardados en la mochila, el depot, la casa…), su progreso en el Bestiary, sus charms y la hunt del
    // optimizador, y lo que dice tibia.com (TibiaData), con un historial de nivel que se va guardando en
    // cada sincronización. Igual que la versión de macOS (macos/Profiles.swift).
    //
    // Se guardan en %APPDATA%\AlfaHub\perfiles.json con las mismas claves que en macOS, para que un perfil
    // compartido se pueda abrir en las dos: archivo .alfahub (JSON {"alfaHubProfile": 1, "profile": …}) o
    // código "ALFAHUB1:" + base64 del mismo JSON comprimido con DEFLATE sin cabecera (DeflateStream). Las
    // fechas van en ISO 8601 ("2026-10-21T02:10:00Z").

    static class Ids
    {
        public static string New() => Guid.NewGuid().ToString().ToUpperInvariant();

        public static string Date(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        public static DateTime? Parse(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
                ? d : (DateTime?)null;
        }
    }

    // ---------- Datos ----------

    static class Vocations
    {
        public static readonly string[] All = { "none", "knight", "paladin", "sorcerer", "druid", "monk" };

        public static string Title(string v)
        {
            switch (v)
            {
                case "knight": return "Knight";
                case "paladin": return "Paladin";
                case "sorcerer": return "Sorcerer";
                case "druid": return "Druid";
                case "monk": return "Monk";
                default: return "Sin vocación";
            }
        }

        /// La vocación que da tibia.com ("Elite Knight", "Master Sorcerer"…).
        public static string FromTibia(string text) =>
            All.Skip(1).FirstOrDefault(v => (text ?? "").IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0) ?? "none";

        // HP, maná y capacidad según el nivel (TibiaWiki, Formulae; sin vocación, los de Rookgaard).
        public static int HitPoints(string v, int level) =>
            v == "knight" ? 5 * (3 * level + 13) : v == "paladin" || v == "monk" ? 5 * (2 * level + 21) : 5 * (level + 29);

        public static int Mana(string v, int level)
        {
            switch (v)
            {
                case "paladin": return 5 * (3 * level - 6);
                case "monk": return 5 * (2 * level - 9);
                case "sorcerer": case "druid": return 5 * (6 * level - 30);
                default: return 5 * (level + 10);
            }
        }

        public static int Capacity(string v, int level) =>
            v == "knight" || v == "monk" ? 5 * (5 * level + 54) : v == "paladin" ? 10 * (2 * level + 31) : 10 * (level + 39);
    }

    static class Experience
    {
        /// Experiencia con la que se llega a un nivel.
        public static long ForLevel(int level)
        {
            long n = Math.Max(0, level - 1);
            return (50 * n * n * n - 150 * n * n + 400 * n) / 3;
        }

        /// Niveles con los que se comparte experiencia en party (el más bajo, al menos 2/3 del más alto).
        public static (int Low, int High) ShareRange(int level) => ((int)Math.Ceiling(level * 2 / 3.0), level * 3 / 2);
    }

    /// Huecos del equipo (el mismo nombre que "slot" en items.json).
    static class Slots
    {
        public static readonly string[] All = { "helmet", "amulet", "armor", "weapon", "shield", "legs", "ring", "boots" };

        public static string Title(string slot)
        {
            switch (slot)
            {
                case "helmet": return "Casco";
                case "amulet": return "Amuleto";
                case "armor": return "Armadura";
                case "weapon": return "Arma";
                case "shield": return "Escudo";
                case "legs": return "Piernas";
                case "ring": return "Anillo";
                case "boots": return "Botas";
                default: return slot;
            }
        }
    }

    /// Dónde está un item.
    static class Places
    {
        public static readonly string[] All = { "equipped", "backpack", "depot", "house", "stash", "otherChar", "other" };

        public static string Title(string place)
        {
            switch (place)
            {
                case "equipped": return "Equipado";
                case "backpack": return "Mochila";
                case "depot": return "Depot";
                case "house": return "Casa";
                case "stash": return "Stash";
                case "otherChar": return "Otro char";
                default: return "Otro sitio";
            }
        }

        public static string DetailPrompt(string place)
        {
            switch (place)
            {
                case "backpack": return "Qué mochila";
                case "depot": return "Ciudad, cajón…";
                case "house": return "Casa, ciudad, cajón…";
                case "otherChar": return "Nombre del char";
                case "other": return "Dónde";
                default: return "Detalle (opcional)";
            }
        }
    }

    /// El outfit de un char tal como se guarda y se comparte (las claves de la versión de macOS).
    [DataContract]
    sealed class SharedOutfit
    {
        [DataMember(Name = "outfit")] public int Outfit = 1;
        [DataMember(Name = "female")] public bool Female;
        [DataMember(Name = "addon1")] public bool Addon1;
        [DataMember(Name = "addon2")] public bool Addon2;
        [DataMember(Name = "c1")] public int C1 = 95;
        [DataMember(Name = "c2")] public int C2 = 113;
        [DataMember(Name = "c3")] public int C3 = 39;
        [DataMember(Name = "c4")] public int C4 = 115;
        [DataMember(Name = "facing")] public int Facing = 2;
        [DataMember(Name = "mount")] public int Mount;
        [DataMember(Name = "mounted")] public bool Mounted;
        [DataMember(Name = "mc1")] public int Mc1;
        [DataMember(Name = "mc2")] public int Mc2;
        [DataMember(Name = "mc3")] public int Mc3;
        [DataMember(Name = "mc4")] public int Mc4;
        [DataMember(Name = "animate")] public bool Animate;

        [OnDeserializing]
        void Defaults(StreamingContext context) { Outfit = 1; C1 = 95; C2 = 113; C3 = 39; C4 = 115; Facing = 2; }
    }

    /// Un item del char (nombre de TibiaWiki): cuántos, dónde está, en qué hueco si va equipado y un
    /// detalle ("Thais, cajón 2", el nombre del otro char…).
    [DataContract]
    sealed class OwnedItem
    {
        [DataMember(Name = "id")] public string Id = Ids.New();
        [DataMember(Name = "item")] public string Item = "";
        [DataMember(Name = "count")] public int Count = 1;
        [DataMember(Name = "place")] public string Place = "backpack";
        [DataMember(Name = "slot", EmitDefaultValue = false)] public string Slot;
        [DataMember(Name = "detail")] public string Detail = "";

        public string PlaceText => string.IsNullOrEmpty(Detail) ? Places.Title(Place) : Places.Title(Place) + " · " + Detail;

        [OnDeserializing] void Defaults(StreamingContext c) { Id = Ids.New(); Item = ""; Count = 1; Place = "other"; Detail = ""; }
        [OnDeserialized] void Fix(StreamingContext c) { if (Detail == null) Detail = ""; if (Count < 1) Count = 1; }
    }

    /// Una criatura de la hunt del optimizador de charms y cuántas se matan por hora.
    [DataContract]
    sealed class HuntCreature
    {
        [DataMember(Name = "name")] public string Name = "";
        [DataMember(Name = "perHour")] public int PerHour = 100;
    }

    /// Nivel (y experiencia y puesto, si sale en los highscores) en una sincronización: uno por día de Tibia.
    [DataContract]
    sealed class LevelPoint
    {
        [DataMember(Name = "date")] public string Date = "";
        [DataMember(Name = "level")] public int Level;
        [DataMember(Name = "experience", EmitDefaultValue = false)] public long? Experience;
        [DataMember(Name = "rank", EmitDefaultValue = false)] public int? Rank;

        public DateTime When => Ids.Parse(Date) ?? DateTime.MinValue;
    }

    /// Lo que tibia.com dice de un char (API de TibiaData, /v4/character/<nombre>).
    [DataContract]
    sealed class TibiaInfo
    {
        [DataContract] public sealed class House { [DataMember(Name = "name")] public string Name = ""; [DataMember(Name = "town")] public string Town = ""; [DataMember(Name = "paid")] public string Paid = ""; }
        [DataContract] public sealed class Death { [DataMember(Name = "time", EmitDefaultValue = false)] public string Time; [DataMember(Name = "level")] public int Level; [DataMember(Name = "reason")] public string Reason = ""; }
        [DataContract] public sealed class OtherChar { [DataMember(Name = "name")] public string Name = ""; [DataMember(Name = "world")] public string World = ""; [DataMember(Name = "online")] public bool Online; [DataMember(Name = "main")] public bool Main; }

        [DataMember(Name = "name")] public string Name = "";
        [DataMember(Name = "sex")] public string Sex = "";
        [DataMember(Name = "title")] public string Title = "";
        [DataMember(Name = "unlockedTitles")] public int UnlockedTitles;
        [DataMember(Name = "vocation")] public string Vocation = "";
        [DataMember(Name = "level")] public int Level;
        [DataMember(Name = "achievementPoints")] public int AchievementPoints;
        [DataMember(Name = "world")] public string World = "";
        [DataMember(Name = "residence")] public string Residence = "";
        [DataMember(Name = "marriedTo")] public string MarriedTo = "";
        [DataMember(Name = "guild")] public string Guild = "";
        [DataMember(Name = "guildRank")] public string GuildRank = "";
        [DataMember(Name = "houses")] public List<House> Houses = new List<House>();
        [DataMember(Name = "lastLogin", EmitDefaultValue = false)] public string LastLogin;
        [DataMember(Name = "accountStatus")] public string AccountStatus = "";
        [DataMember(Name = "formerNames")] public List<string> FormerNames = new List<string>();
        [DataMember(Name = "formerWorlds")] public List<string> FormerWorlds = new List<string>();
        [DataMember(Name = "accountCreated", EmitDefaultValue = false)] public string AccountCreated;
        [DataMember(Name = "loyaltyTitle")] public string LoyaltyTitle = "";
        [DataMember(Name = "deaths")] public List<Death> Deaths = new List<Death>();
        [DataMember(Name = "otherChars")] public List<OtherChar> OtherChars = new List<OtherChar>();
        [DataMember(Name = "comment")] public string Comment = "";
        /// Experiencia y puesto en su mundo, si sale entre los 1000 primeros de los highscores.
        [DataMember(Name = "experience", EmitDefaultValue = false)] public long? Experience;
        [DataMember(Name = "worldRank", EmitDefaultValue = false)] public int? WorldRank;
        [DataMember(Name = "syncedAt")] public string SyncedAt = Ids.Date(DateTime.UtcNow);

        public DateTime Synced => Ids.Parse(SyncedAt) ?? DateTime.MinValue;

        // La versión de macOS necesita todas las claves que no son opcionales: nunca null.
        [OnDeserialized]
        void Fix(StreamingContext c)
        {
            Name = Name ?? ""; Sex = Sex ?? ""; Title = Title ?? ""; Vocation = Vocation ?? ""; World = World ?? "";
            Residence = Residence ?? ""; MarriedTo = MarriedTo ?? ""; Guild = Guild ?? ""; GuildRank = GuildRank ?? "";
            AccountStatus = AccountStatus ?? ""; LoyaltyTitle = LoyaltyTitle ?? ""; Comment = Comment ?? "";
            Houses = Houses ?? new List<House>(); FormerNames = FormerNames ?? new List<string>(); FormerWorlds = FormerWorlds ?? new List<string>();
            Deaths = Deaths ?? new List<Death>(); OtherChars = OtherChars ?? new List<OtherChar>();
            SyncedAt = SyncedAt ?? Ids.Date(DateTime.UtcNow);
        }
    }

    [DataContract]
    sealed class GameChar
    {
        [DataMember(Name = "id")] public string Id = Ids.New();
        [DataMember(Name = "name")] public string Name = "";
        [DataMember(Name = "vocation")] public string Vocation = "none";
        [DataMember(Name = "level")] public int Level = 8;
        [DataMember(Name = "world")] public string World = "";
        /// Su outfit (null = el personaje de Alfa Hub).
        [DataMember(Name = "outfit", EmitDefaultValue = false)] public SharedOutfit Outfit;
        [DataMember(Name = "items")] public List<OwnedItem> Items = new List<OwnedItem>();
        /// Kills de cada criatura del Bestiary (por nombre).
        [DataMember(Name = "kills")] public Dictionary<string, int> Kills = new Dictionary<string, int>();
        /// Nivel (1–3) de cada charm desbloqueado, por nombre.
        [DataMember(Name = "charms")] public Dictionary<string, int> Charms = new Dictionary<string, int>();
        [DataMember(Name = "hunt")] public List<HuntCreature> Hunt = new List<HuntCreature>();
        /// Lo último que se trajo de tibia.com (null = sin sincronizar o no existe).
        [DataMember(Name = "tibia", EmitDefaultValue = false)] public TibiaInfo Tibia;
        /// Nivel en cada día de Tibia que se sincronizó (el más reciente al final).
        [DataMember(Name = "history")] public List<LevelPoint> History = new List<LevelPoint>();

        [OnDeserializing] void Defaults(StreamingContext c) { Id = Ids.New(); Name = ""; Vocation = "none"; Level = 8; World = ""; }

        [OnDeserialized]
        void Fix(StreamingContext c)
        {
            Name = Name ?? ""; World = World ?? ""; Vocation = Vocation ?? "none";
            Items = Items ?? new List<OwnedItem>(); Kills = Kills ?? new Dictionary<string, int>();
            Charms = Charms ?? new Dictionary<string, int>(); Hunt = Hunt ?? new List<HuntCreature>();
            History = History ?? new List<LevelPoint>();
        }

        public bool IsSynced => Tibia != null && string.Equals(Tibia.Name, Name.Trim(), StringComparison.OrdinalIgnoreCase);

        public string Summary
        {
            get
            {
                var parts = new List<string> { IsSynced ? Tibia.Vocation : Vocations.Title(Vocation), "Nivel " + Level };
                if (World.Length > 0) parts.Add(World);
                return string.Join(" · ", parts);
            }
        }

        /// Pasa al char lo que dice tibia.com y apunta el nivel del día (uno por día de Tibia).
        public void Apply(TibiaInfo info)
        {
            Name = info.Name;
            Level = info.Level;
            Vocation = Vocations.FromTibia(info.Vocation);
            World = info.World;
            Tibia = info;
            var point = new LevelPoint { Date = info.SyncedAt, Level = info.Level, Experience = info.Experience, Rank = info.WorldRank };
            var last = History.LastOrDefault();
            if (last != null && TibiaCalendar.LastServerSave(last.When) == TibiaCalendar.LastServerSave(point.When)) History[History.Count - 1] = point;
            else History.Add(point);
            if (History.Count > 400) History.RemoveRange(0, History.Count - 400);
        }

        public int KillsOf(BestiaryCreature c) => Kills.TryGetValue(c.Name, out int k) ? k : 0;
        public bool Completed(BestiaryCreature c) => KillsOf(c) >= c.Total;
        public int CompletedCount => BestiaryCatalog.Creatures.Count(Completed);
        public int CharmPoints => BestiaryCatalog.Creatures.Where(Completed).Sum(c => c.Charms);
        public int CharmTier(string name) => Charms.TryGetValue(name, out int t) ? t : 0;
        public OwnedItem Equipped(string slot) => Items.FirstOrDefault(i => i.Place == "equipped" && i.Slot == slot);
    }

    [DataContract]
    sealed class Profile
    {
        [DataMember(Name = "id")] public string Id = Ids.New();
        [DataMember(Name = "name")] public string Name = "Perfil";
        [DataMember(Name = "chars")] public List<GameChar> Chars = new List<GameChar>();
        /// Si es la copia de un perfil compartido: el id del original y cuándo se importó.
        [DataMember(Name = "sourceID", EmitDefaultValue = false)] public string SourceId;
        [DataMember(Name = "importedAt", EmitDefaultValue = false)] public string ImportedAt;

        [OnDeserializing] void Defaults(StreamingContext c) { Id = Ids.New(); Name = "Perfil"; }
        [OnDeserialized] void Fix(StreamingContext c) { Chars = Chars ?? new List<GameChar>(); Name = Name ?? "Perfil"; }

        public string Summary
        {
            get
            {
                int items = Chars.Sum(c => c.Items.Count);
                return $"{Chars.Count} {(Chars.Count == 1 ? "char" : "chars")} · {items} {(items == 1 ? "item" : "items")}";
            }
        }
    }

    // ---------- Bestiary, items y charms (de TibiaWiki) ----------

    [DataContract]
    sealed class BestiaryCreature
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "class")] public string Class;
        [DataMember(Name = "level")] public string Level;      // Harmless, Trivial, Easy, Medium, Hard o Challenging
        [DataMember(Name = "rare")] public bool Rare;           // muy rara: 2, 3 y 5 kills
        [DataMember(Name = "hp")] public int Hp;
        [DataMember(Name = "exp")] public int Exp;
        [DataMember(Name = "places")] public List<string> Places = new List<string>();
        [DataMember(Name = "mods")] public Dictionary<string, int> Mods = new Dictionary<string, int>();

        public int Mod(string element) => Mods != null && Mods.TryGetValue(element, out int v) ? v : 100;

        /// Kills de las tres etapas (la última completa la criatura).
        public int[] Stages
        {
            get
            {
                if (Rare) return new[] { 2, 3, 5 };
                switch (Level)
                {
                    case "Harmless": return new[] { 5, 10, 25 };
                    case "Trivial": return new[] { 10, 100, 250 };
                    case "Easy": return new[] { 25, 250, 500 };
                    case "Medium": return new[] { 50, 500, 1000 };
                    case "Hard": return new[] { 100, 1000, 2500 };
                    default: return new[] { 200, 2000, 5000 };
                }
            }
        }

        public int Total => Stages[2];
        public int Stage(int kills) => Stages.Count(s => kills >= s);

        /// Charm points al completarla.
        public int Charms
        {
            get
            {
                switch (Level)
                {
                    case "Harmless": return Rare ? 5 : 1;
                    case "Trivial": return Rare ? 10 : 5;
                    case "Easy": return Rare ? 30 : 15;
                    case "Medium": return Rare ? 50 : 25;
                    case "Hard": return Rare ? 100 : 50;
                    default: return Rare ? 200 : 100;
                }
            }
        }
    }

    [DataContract]
    sealed class ItemInfo
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "slot")] public string Slot;
    }

    /// Un charm de TibiaWiki (charms.json).
    [DataContract]
    sealed class CharmInfo
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "type")] public string Type;      // Major o Minor
        [DataMember(Name = "costs")] public List<int> Costs; // nivel 1, 2 y 3
        [DataMember(Name = "effect")] public string Effect;
        [DataMember(Name = "image")] public string Image;    // archivo del wiki ("Zap.png")

        public bool Major => Type == "Major";

        static readonly Dictionary<string, string> Elements = new Dictionary<string, string>
        {
            ["Curse"] = "death", ["Enflame"] = "fire", ["Freeze"] = "ice", ["Poison"] = "earth", ["Zap"] = "energy",
            ["Divine Wrath"] = "holy", ["Wound"] = "physical",
        };

        /// Elemento de los charms de daño elemental (5 % de la vida de la criatura).
        public string Element => Elements.TryGetValue(Name, out var e) ? e : null;

        /// Probabilidad de activarse de los charms mayores de cada nivel.
        public static readonly double[] Chances = { 0.05, 0.10, 0.11 };
    }

    static class Catalogs
    {
        [DataContract] sealed class CreatureFile { [DataMember(Name = "creatures")] public List<BestiaryCreature> Creatures; }
        [DataContract] sealed class ItemFile { [DataMember(Name = "items")] public List<ItemInfo> Items; }
        [DataContract] sealed class CharmFile { [DataMember(Name = "charms")] public List<CharmInfo> Charms; }
        [DataContract] sealed class HuntFile { [DataMember(Name = "hunts")] public List<HuntPreset> Hunts; }

        static readonly DataContractJsonSerializerSettings Settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };

        public static T Resource<T>(string name) where T : class
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AlfaHub." + name))
                return stream == null ? null : (T)new DataContractJsonSerializer(typeof(T), Settings).ReadObject(stream);
        }

        public static List<BestiaryCreature> LoadCreatures() => Resource<CreatureFile>("bestiary.json")?.Creatures ?? new List<BestiaryCreature>();
        public static List<ItemInfo> LoadItems() => Resource<ItemFile>("items.json")?.Items ?? new List<ItemInfo>();
        public static List<CharmInfo> LoadCharms() => Resource<CharmFile>("charms.json")?.Charms ?? new List<CharmInfo>();
        public static List<HuntPreset> LoadHunts() => Resource<HuntFile>("hunts.json")?.Hunts ?? new List<HuntPreset>();
    }

    static class BestiaryCatalog
    {
        public static readonly List<BestiaryCreature> Creatures = Catalogs.LoadCreatures();
        public static readonly List<string> Classes = Creatures.Select(c => c.Class).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        public static readonly string[] Levels = { "Harmless", "Trivial", "Easy", "Medium", "Hard", "Challenging" };
        public static readonly int TotalCharms = Creatures.Sum(c => c.Charms);
        public static BestiaryCreature Find(string name) => Creatures.FirstOrDefault(c => c.Name == name);
    }

    static class ItemCatalog
    {
        public static readonly List<ItemInfo> Items = Catalogs.LoadItems();
        public static IEnumerable<ItemInfo> For(string slot) => slot == null ? Items : Items.Where(i => i.Slot == slot);
        public static string SlotOf(string item) => Items.FirstOrDefault(i => i.Name == item)?.Slot;
    }

    static class CharmCatalog
    {
        public static readonly List<CharmInfo> Charms = Catalogs.LoadCharms();
        public static IEnumerable<CharmInfo> Major => Charms.Where(c => c.Major);
        public static IEnumerable<CharmInfo> Minor => Charms.Where(c => !c.Major);
        /// Los que hacen daño y se pueden comparar: los elementales, Overpower y Overflux.
        public static List<CharmInfo> Damage => Charms.Where(c => c.Element != null || c.Name == "Overpower" || c.Name == "Overflux").ToList();

        /// Daño de una activación contra una criatura (TibiaWiki): los elementales, 5 % de su vida (como
        /// mucho el doble del nivel) por su debilidad a ese elemento; Overpower, 5 % de la vida del char, y
        /// Overflux, 2,5 % de su maná (los dos hasta el 8 % de la vida de la criatura y sin resistencias).
        public static int DamageOf(CharmInfo charm, BestiaryCreature creature, int level, string vocation)
        {
            if (charm.Element != null)
            {
                double baseDamage = Math.Min(creature.Hp * 0.05, level * 2.0);
                return (int)(baseDamage * creature.Mod(charm.Element) / 100);
            }
            double cap = creature.Hp * 0.08;
            if (charm.Name == "Overpower") return (int)Math.Min(Vocations.HitPoints(vocation, level) * 0.05, cap);
            if (charm.Name == "Overflux") return (int)Math.Min(Vocations.Mana(vocation, level) * 0.025, cap);
            return 0;
        }
    }

    /// Una hunt conocida (preset del Charm Optimizer de hakaimarket.com): dónde, desde qué nivel y cuántas
    /// criaturas de cada tipo se matan por hora. Vienen en hunts.json (assets/make_hunts.py).
    [DataContract]
    sealed class HuntPreset
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "city")] public string City;
        [DataMember(Name = "minLevel")] public int MinLevel;
        [DataMember(Name = "premium")] public bool Premium;
        [DataMember(Name = "creatures")] public List<HuntCreature> Creatures;
    }

    static class HuntCatalog
    {
        public static readonly List<HuntPreset> Presets = Catalogs.LoadHunts();
    }

    // ---------- tibia.com (TibiaData) ----------

    static class CharLookup
    {
        [DataContract] sealed class Response { [DataMember(Name = "character")] public Body Character; }
        [DataContract] sealed class Body
        {
            [DataMember(Name = "character")] public Character Character;
            [DataMember(Name = "deaths")] public List<DeathEntry> Deaths;
            [DataMember(Name = "account_information")] public Account Account;
            [DataMember(Name = "other_characters")] public List<Other> Others;
        }
        [DataContract] sealed class Character
        {
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "sex")] public string Sex;
            [DataMember(Name = "title")] public string Title;
            [DataMember(Name = "unlocked_titles")] public int UnlockedTitles;
            [DataMember(Name = "vocation")] public string Vocation;
            [DataMember(Name = "level")] public int Level;
            [DataMember(Name = "achievement_points")] public int AchievementPoints;
            [DataMember(Name = "world")] public string World;
            [DataMember(Name = "residence")] public string Residence;
            [DataMember(Name = "married_to")] public string MarriedTo;
            [DataMember(Name = "guild")] public Guild Guild;
            [DataMember(Name = "houses")] public List<HouseEntry> Houses;
            [DataMember(Name = "last_login")] public string LastLogin;
            [DataMember(Name = "account_status")] public string AccountStatus;
            [DataMember(Name = "former_names")] public List<string> FormerNames;
            [DataMember(Name = "former_worlds")] public List<string> FormerWorlds;
            [DataMember(Name = "comment")] public string Comment;
        }
        [DataContract] sealed class Guild { [DataMember(Name = "name")] public string Name; [DataMember(Name = "rank")] public string Rank; }
        [DataContract] sealed class HouseEntry { [DataMember(Name = "name")] public string Name; [DataMember(Name = "town")] public string Town; [DataMember(Name = "paid")] public string Paid; }
        [DataContract] sealed class DeathEntry { [DataMember(Name = "time")] public string Time; [DataMember(Name = "level")] public int Level; [DataMember(Name = "reason")] public string Reason; }
        [DataContract] sealed class Account { [DataMember(Name = "created")] public string Created; [DataMember(Name = "loyalty_title")] public string LoyaltyTitle; }
        [DataContract] sealed class Other { [DataMember(Name = "name")] public string Name; [DataMember(Name = "world")] public string World; [DataMember(Name = "status")] public string Status; [DataMember(Name = "main")] public bool Main; [DataMember(Name = "deleted")] public bool Deleted; }
        [DataContract] sealed class Highscores { [DataMember(Name = "highscores")] public HighscoreBody Body; }
        [DataContract] sealed class HighscoreBody { [DataMember(Name = "highscore_list")] public List<HighscoreEntry> List; }
        [DataContract] sealed class HighscoreEntry { [DataMember(Name = "rank")] public int Rank; [DataMember(Name = "name")] public string Name; [DataMember(Name = "level")] public int Level; [DataMember(Name = "value")] public long Value; }

        public static byte[] Download(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "AlfaHub";
                return client.DownloadData(url);
            }
        }

        static T Get<T>(string url)
        {
            using (var stream = new MemoryStream(Download(url)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        /// Lo que dice tibia.com del char. Un nombre que no existe devuelve un error (502), igual que si
        /// TibiaData no responde: en los dos casos, null.
        public static TibiaInfo Fetch(string name)
        {
            Body body;
            try { body = Get<Response>("https://api.tibiadata.com/v4/character/" + Uri.EscapeDataString(name.Trim()))?.Character; }
            catch (Exception e) when (e is WebException || e is IOException || e is SerializationException) { return null; }
            var c = body?.Character;
            if (c == null || string.IsNullOrEmpty(c.Name)) return null;
            var info = new TibiaInfo
            {
                Name = c.Name, Sex = c.Sex ?? "", Title = c.Title == "None" ? "" : c.Title ?? "", UnlockedTitles = c.UnlockedTitles,
                Vocation = c.Vocation ?? "", Level = c.Level, AchievementPoints = c.AchievementPoints, World = c.World ?? "",
                Residence = c.Residence ?? "", MarriedTo = c.MarriedTo ?? "", Guild = c.Guild?.Name ?? "", GuildRank = c.Guild?.Rank ?? "",
                Houses = (c.Houses ?? new List<HouseEntry>()).Select(h => new TibiaInfo.House { Name = h.Name ?? "", Town = h.Town ?? "", Paid = h.Paid ?? "" }).ToList(),
                LastLogin = string.IsNullOrEmpty(c.LastLogin) ? null : c.LastLogin, AccountStatus = c.AccountStatus ?? "",
                FormerNames = c.FormerNames ?? new List<string>(), FormerWorlds = c.FormerWorlds ?? new List<string>(), Comment = c.Comment ?? "",
                AccountCreated = string.IsNullOrEmpty(body.Account?.Created) ? null : body.Account.Created, LoyaltyTitle = body.Account?.LoyaltyTitle ?? "",
                Deaths = (body.Deaths ?? new List<DeathEntry>()).Select(d => new TibiaInfo.Death { Time = d.Time, Level = d.Level, Reason = d.Reason ?? "" }).ToList(),
                OtherChars = (body.Others ?? new List<Other>()).Where(o => !o.Deleted && o.Name != c.Name)
                    .Select(o => new TibiaInfo.OtherChar { Name = o.Name ?? "", World = o.World ?? "", Online = o.Status == "online", Main = o.Main }).ToList(),
                SyncedAt = Ids.Date(DateTime.UtcNow),
            };
            // Si TibiaData no responde con los highscores, error: guardar el día sin experiencia lo dejaría
            // como "fuera de los highscores".
            var hit = Highscore(info.Name, info.World, info.Level, out bool failed);
            if (failed) return null;
            if (hit != null) { info.Experience = hit.Value; info.WorldRank = hit.Rank; }
            return info;
        }

        /// Busca el char en los highscores de experiencia de su mundo (20 páginas de 50, ordenadas por
        /// experiencia): una búsqueda binaria por nivel, unas 5 peticiones. TibiaData no deja filtrar por
        /// vocación más allá de la primera página, así que se usa la de todas.
        static HighscoreEntry Highscore(string name, string world, int level, out bool failed)
        {
            failed = false;
            if (string.IsNullOrEmpty(world)) return null;
            bool error = false;
            var cache = new Dictionary<int, List<HighscoreEntry>>();
            List<HighscoreEntry> Page(int number)
            {
                if (cache.TryGetValue(number, out var hit)) return hit;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0) System.Threading.Thread.Sleep(attempt * 1500);
                    try
                    {
                        var list = Get<Highscores>($"https://api.tibiadata.com/v4/highscores/{Uri.EscapeDataString(world)}/experience/all/{number}")?.Body?.List;
                        if (list == null) continue;
                        return cache[number] = list;
                    }
                    catch (Exception e) when (e is WebException || e is IOException || e is SerializationException) { }
                }
                error = true;
                return null;
            }
            int low = 1, high = 20;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                var list = Page(mid);
                if (list == null) { failed = error; return null; }
                if (list.Count == 0) return null;
                var found = list.FirstOrDefault(e => e.Name == name);
                if (found != null) return found;
                if (level > list[0].Level) high = mid - 1;
                else if (level < list[list.Count - 1].Level) low = mid + 1;
                else
                {
                    // Mismo nivel que los de esta página: puede estar en la de al lado.
                    foreach (int other in new[] { mid - 1, mid + 1 })
                        if (other >= 1 && other <= 20)
                        {
                            found = Page(other)?.FirstOrDefault(e => e.Name == name);
                            if (found != null) return found;
                        }
                    failed = error;
                    return null;
                }
            }
            return null;
        }
    }

    // ---------- Guardar y compartir ----------

    static class ProfileFiles
    {
        public static readonly DataContractJsonSerializerSettings Settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlfaHub");
        public static string FilePath => Path.Combine(Folder, "perfiles.json");

        public static byte[] Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T), Settings).WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        public static T Deserialize<T>(byte[] data) where T : class
        {
            using (var stream = new MemoryStream(data))
                return (T)new DataContractJsonSerializer(typeof(T), Settings).ReadObject(stream);
        }

        public static List<Profile> Load()
        {
            try { return File.Exists(FilePath) ? Deserialize<List<Profile>>(File.ReadAllBytes(FilePath)) ?? new List<Profile>() : new List<Profile>(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SerializationException) { return new List<Profile>(); }
        }

        public static void Save(List<Profile> profiles)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string temp = FilePath + ".tmp";
                File.WriteAllBytes(temp, Serialize(profiles));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    static class ProfileShare
    {
        public const string Prefix = "ALFAHUB1:";
        public const string FileExtension = "alfahub";

        /// Lo que se comparte (archivo o código): el perfil sin los datos de importación.
        [DataContract]
        sealed class Envelope
        {
            [DataMember(Name = "alfaHubProfile")] public int Version = 1;
            [DataMember(Name = "profile")] public Profile Profile;
        }

        public static byte[] Json(Profile profile)
        {
            var copy = ProfileFiles.Deserialize<Profile>(ProfileFiles.Serialize(profile));
            copy.SourceId = null;
            copy.ImportedAt = null;
            return ProfileFiles.Serialize(new Envelope { Profile = copy });
        }

        public static string Code(Profile profile)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                {
                    byte[] json = Json(profile);
                    deflate.Write(json, 0, json.Length);
                }
                return Prefix + Convert.ToBase64String(output.ToArray());
            }
        }

        /// Un código ("ALFAHUB1:…") o el contenido de un archivo .alfahub; null si no es un perfil.
        public static Profile Decode(byte[] data)
        {
            byte[] json = data;
            string text = Encoding.UTF8.GetString(data).Trim().TrimStart('﻿');
            if (text.StartsWith(Prefix, StringComparison.Ordinal))
            {
                try
                {
                    string base64 = new string(text.Substring(Prefix.Length).Where(ch => !char.IsWhiteSpace(ch)).ToArray());
                    using (var input = new DeflateStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        input.CopyTo(output);
                        json = output.ToArray();
                    }
                }
                catch (Exception e) when (e is FormatException || e is InvalidDataException) { return null; }
            }
            try
            {
                var envelope = ProfileFiles.Deserialize<Envelope>(json);
                return envelope?.Version >= 1 ? envelope.Profile : null;
            }
            catch (SerializationException) { return null; }
        }
    }
}

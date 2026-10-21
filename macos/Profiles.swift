// Perfiles: cada perfil tiene varios chars; de cada char, su outfit, sus items (equipados en su hueco o
// guardados en la mochila, el depot, la casa…), su progreso en el Bestiary (kills de cada criatura) y lo
// que dice tibia.com (TibiaData), con un historial de nivel que se va guardando en cada sincronización.
//
// Se guardan en Application Support/Alfa Hub/perfiles.json. Para compartir un perfil se exporta como
// archivo .alfahub (JSON) o como código de texto: "ALFAHUB1:" + base64 del mismo JSON comprimido con
// DEFLATE sin cabecera (lo que da NSData con .zlib y lee DeflateStream en .NET). Al importar el mismo
// perfil otra vez se actualiza la copia que ya había.
//
// La lista de criaturas (assets/bestiary.json) y la de items (assets/items.json) salen de TibiaWiki con
// assets/make_profiles_data.py; sus imágenes (File:<nombre>.gif) se descargan del wiki al verlas y
// quedan en caché.

import AppKit
import SwiftUI

// MARK: - Datos

enum Vocation: String, Codable, CaseIterable, Identifiable {
    case none, knight, paladin, sorcerer, druid, monk
    var id: String { rawValue }
    var title: String {
        switch self {
        case .none: "Sin vocación"
        case .knight: "Knight"
        case .paladin: "Paladin"
        case .sorcerer: "Sorcerer"
        case .druid: "Druid"
        case .monk: "Monk"
        }
    }

    /// La vocación que da tibia.com ("Elite Knight", "Master Sorcerer"…).
    init(tibia text: String) {
        self = Vocation.allCases.dropFirst().first { text.localizedCaseInsensitiveContains($0.rawValue) } ?? .none
    }

    // HP, maná y capacidad según el nivel (TibiaWiki, Formulae; sin vocación, los de Rookgaard).
    func hitPoints(_ level: Int) -> Int {
        switch self {
        case .knight: 5 * (3 * level + 13)
        case .paladin, .monk: 5 * (2 * level + 21)
        default: 5 * (level + 29)
        }
    }
    func mana(_ level: Int) -> Int {
        switch self {
        case .knight, .none: 5 * (level + 10)
        case .paladin: 5 * (3 * level - 6)
        case .monk: 5 * (2 * level - 9)
        case .sorcerer, .druid: 5 * (6 * level - 30)
        }
    }
    func capacity(_ level: Int) -> Int {
        switch self {
        case .knight, .monk: 5 * (5 * level + 54)
        case .paladin: 10 * (2 * level + 31)
        default: 10 * (level + 39)
        }
    }
}

enum Experience {
    /// Experiencia con la que se llega a un nivel.
    static func forLevel(_ level: Int) -> Int64 {
        let n = Int64(max(0, level - 1))
        return (50 * n * n * n - 150 * n * n + 400 * n) / 3
    }

    /// Niveles con los que se comparte experiencia en party (el más bajo, al menos 2/3 del más alto).
    static func shareRange(_ level: Int) -> ClosedRange<Int> {
        Int((Double(level) * 2 / 3).rounded(.up))...(level * 3 / 2)
    }
}

/// Hueco del equipo (el mismo nombre que "slot" en items.json).
enum ItemSlot: String, Codable, CaseIterable, Identifiable {
    case helmet, amulet, armor, weapon, shield, legs, ring, boots
    var id: String { rawValue }
    var title: String {
        switch self {
        case .helmet: "Casco"
        case .amulet: "Amuleto"
        case .armor: "Armadura"
        case .weapon: "Arma"
        case .shield: "Escudo"
        case .legs: "Piernas"
        case .ring: "Anillo"
        case .boots: "Botas"
        }
    }
}

/// Dónde está un item.
enum ItemPlace: String, Codable, CaseIterable, Identifiable {
    case equipped, backpack, depot, house, stash, otherChar, other
    var id: String { rawValue }
    var title: String {
        switch self {
        case .equipped: "Equipado"
        case .backpack: "Mochila"
        case .depot: "Depot"
        case .house: "Casa"
        case .stash: "Stash"
        case .otherChar: "Otro char"
        case .other: "Otro sitio"
        }
    }
    var icon: String {
        switch self {
        case .equipped: "person.fill"
        case .backpack: "bag.fill"
        case .depot: "archivebox.fill"
        case .house: "house.fill"
        case .stash: "shippingbox.fill"
        case .otherChar: "person.2.fill"
        case .other: "mappin"
        }
    }
    var tint: Color {
        switch self {
        case .equipped: .green
        case .backpack: Color(red: 0.95, green: 0.70, blue: 0.25)
        case .depot: Color.accentBlue
        case .house: Color(red: 1.0, green: 0.45, blue: 0.25)
        case .stash: Color(red: 0.69, green: 0.45, blue: 0.98)
        case .otherChar: Color(red: 0.3, green: 0.8, blue: 0.85)
        case .other: Color.muted
        }
    }
    /// Texto de ayuda del detalle.
    var detailPrompt: String {
        switch self {
        case .equipped, .stash: "Detalle (opcional)"
        case .backpack: "Qué mochila"
        case .depot: "Ciudad, cajón…"
        case .house: "Casa, ciudad, cajón…"
        case .otherChar: "Nombre del char"
        case .other: "Dónde"
        }
    }
}

/// Un item del char (nombre de TibiaWiki): cuántos, dónde está, en qué hueco si va equipado y un
/// detalle ("Thais, cajón 2", el nombre del otro char…).
struct OwnedItem: Codable, Identifiable, Hashable {
    var id = UUID()
    var item: String
    var count = 1
    var place = ItemPlace.backpack
    var slot: ItemSlot?
    var detail = ""

    var placeText: String { detail.isEmpty ? place.title : "\(place.title) · \(detail)" }
}

extension OwnedItem {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = (try? c.decodeIfPresent(UUID.self, forKey: .id)) ?? UUID()
        item = try c.decode(String.self, forKey: .item)
        count = (try? c.decodeIfPresent(Int.self, forKey: .count)) ?? 1
        place = (try? c.decodeIfPresent(ItemPlace.self, forKey: .place)) ?? .other
        slot = try? c.decodeIfPresent(ItemSlot.self, forKey: .slot)
        detail = (try? c.decodeIfPresent(String.self, forKey: .detail)) ?? ""
    }
}

/// Una criatura de la hunt del optimizador de charms y cuántas se matan por hora.
struct HuntCreature: Codable, Hashable {
    var name: String
    var perHour = 100
}

/// Nivel (y experiencia, si sale en los highscores) en una sincronización: uno por día.
struct LevelPoint: Codable, Hashable {
    var date: Date
    var level: Int
    var experience: Int64?
    var rank: Int?
}

struct GameChar: Codable, Identifiable, Hashable {
    var id = UUID()
    var name = ""
    var vocation = Vocation.none
    var level = 8
    var world = ""
    /// Su outfit (nil = el personaje de Alfa Hub).
    var outfit: OutfitConfig?
    var items: [OwnedItem] = []
    /// Kills de cada criatura del Bestiary (por nombre).
    var kills: [String: Int] = [:]
    /// Nivel (1–3) de cada charm desbloqueado, por nombre.
    var charms: [String: Int] = [:]
    /// Las criaturas de su hunt para el optimizador de charms.
    var hunt: [HuntCreature] = []
    /// Lo último que se trajo de tibia.com (nil = sin sincronizar o no existe).
    var tibia: TibiaInfo?
    /// Nivel en cada día que se sincronizó (el más reciente al final).
    var history: [LevelPoint] = []

    var summary: String {
        let synced = isSynced ? tibia : nil
        var parts = [synced?.vocation ?? vocation.title, "Nivel \(level)"]
        if !world.isEmpty { parts.append(world) }
        return parts.joined(separator: " · ")
    }

    /// Pasa al char lo que dice tibia.com: nombre (con sus mayúsculas), nivel, vocación y mundo.
    mutating func apply(_ info: TibiaInfo) {
        name = info.name
        level = info.level
        vocation = Vocation(tibia: info.vocation)
        world = info.world
        tibia = info
        // Uno por día de Tibia (de server save a server save), que es cuando cambian los highscores.
        let point = LevelPoint(date: info.syncedAt, level: info.level, experience: info.experience, rank: info.worldRank)
        if let last = history.last, TibiaCalendar.lastServerSave(last.date) == TibiaCalendar.lastServerSave(point.date) {
            history[history.count - 1] = point
        } else {
            history.append(point)
        }
        if history.count > 400 { history.removeFirst(history.count - 400) }
    }

    func equipped(_ slot: ItemSlot) -> OwnedItem? { items.first { $0.place == .equipped && $0.slot == slot } }

    /// El nombre ya es el del char sincronizado (no hace falta volver a pedirlo).
    var isSynced: Bool {
        tibia.map { $0.name.caseInsensitiveCompare(name.trimmingCharacters(in: .whitespaces)) == .orderedSame } ?? false
    }

    func kills(_ creature: BestiaryCreature) -> Int { kills[creature.name] ?? 0 }
    func completed(_ creature: BestiaryCreature) -> Bool { kills(creature) >= creature.total }
    var completedCount: Int { BestiaryCatalog.creatures.filter(completed).count }
    var charmPoints: Int { BestiaryCatalog.creatures.filter(completed).reduce(0) { $0 + $1.charms } }
}

extension GameChar {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func get<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T { (try? c.decodeIfPresent(T.self, forKey: key)) ?? fallback }
        self.init()
        id = get(.id, id); name = get(.name, name); vocation = get(.vocation, vocation)
        level = get(.level, level); world = get(.world, world)
        outfit = try? c.decodeIfPresent(OutfitConfig.self, forKey: .outfit)
        items = get(.items, items); kills = get(.kills, kills)
        charms = get(.charms, charms); hunt = get(.hunt, hunt)
        tibia = try? c.decodeIfPresent(TibiaInfo.self, forKey: .tibia)
        history = get(.history, history)
    }
}

struct Profile: Codable, Identifiable, Hashable {
    var id = UUID()
    var name = "Perfil"
    var chars: [GameChar] = []
    /// Si es la copia de un perfil compartido: el id del original (para actualizarla al importarlo
    /// otra vez) y cuándo se importó.
    var sourceID: UUID?
    var importedAt: Date?

    var summary: String {
        let items = chars.reduce(0) { $0 + $1.items.count }
        return "\(chars.count) \(chars.count == 1 ? "char" : "chars") · \(items) \(items == 1 ? "item" : "items")"
    }
}

extension Profile {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func get<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T { (try? c.decodeIfPresent(T.self, forKey: key)) ?? fallback }
        self.init()
        id = get(.id, id); name = get(.name, name); chars = get(.chars, chars)
        sourceID = try? c.decodeIfPresent(UUID.self, forKey: .sourceID)
        importedAt = try? c.decodeIfPresent(Date.self, forKey: .importedAt)
    }
}

// MARK: - Bestiary e items (de TibiaWiki)

struct BestiaryCreature: Decodable, Identifiable, Hashable {
    let name: String
    let `class`: String
    let level: String     // Harmless, Trivial, Easy, Medium, Hard o Challenging
    let rare: Bool        // muy rara: 2, 3 y 5 kills
    let hp: Int
    let exp: Int
    let places: [String]  // sitios enlazados en su "location" de TibiaWiki
    let mods: [String: Int]  // % de daño que recibe de cada elemento (physical, earth, fire…)
    var id: String { name }

    func mod(_ element: String) -> Int { mods[element] ?? 100 }

    /// Kills de las tres etapas (la última completa la criatura).
    var stages: [Int] {
        if rare { return [2, 3, 5] }
        switch level {
        case "Harmless": return [5, 10, 25]
        case "Trivial": return [10, 100, 250]
        case "Easy": return [25, 250, 500]
        case "Medium": return [50, 500, 1000]
        case "Hard": return [100, 1000, 2500]
        default: return [200, 2000, 5000]
        }
    }
    var total: Int { stages[2] }

    /// Charm points al completarla.
    var charms: Int {
        let points: [String: (Int, Int)] = ["Harmless": (1, 5), "Trivial": (5, 10), "Easy": (15, 30),
                                            "Medium": (25, 50), "Hard": (50, 100)]
        let (common, veryRare) = points[level] ?? (100, 200)
        return rare ? veryRare : common
    }

    func stage(_ kills: Int) -> Int { stages.filter { kills >= $0 }.count }
}

enum BestiaryCatalog {
    static let creatures: [BestiaryCreature] = {
        struct File: Decodable { let creatures: [BestiaryCreature] }
        return resource("bestiary").flatMap { try? JSONDecoder().decode(File.self, from: $0).creatures } ?? []
    }()
    static let classes: [String] = Array(Set(creatures.map(\.class))).sorted()
    static let levels = ["Harmless", "Trivial", "Easy", "Medium", "Hard", "Challenging"]
    static let totalCharms = creatures.reduce(0) { $0 + $1.charms }
}

/// Un charm de TibiaWiki (assets/charms.json).
struct CharmInfo: Decodable, Identifiable, Hashable {
    let name: String
    let type: String      // Major o Minor
    let costs: [Int]      // nivel 1, 2 y 3
    let effect: String
    let image: String     // archivo del wiki ("Zap.png")
    var id: String { name }
    var major: Bool { type == "Major" }

    /// Elemento de los charms de daño elemental (5 % de la vida de la criatura).
    var element: String? {
        ["Curse": "death", "Enflame": "fire", "Freeze": "ice", "Poison": "earth", "Zap": "energy",
         "Divine Wrath": "holy", "Wound": "physical"][name]
    }

    /// Probabilidad de activarse de los charms mayores de cada nivel.
    static let chances = [0.05, 0.10, 0.11]
}

enum CharmCatalog {
    static let charms: [CharmInfo] = {
        struct File: Decodable { let charms: [CharmInfo] }
        return resource("charms").flatMap { try? JSONDecoder().decode(File.self, from: $0).charms } ?? []
    }()
    static var major: [CharmInfo] { charms.filter(\.major) }
    static var minor: [CharmInfo] { charms.filter { !$0.major } }
    /// Los que hacen daño y se pueden comparar: los elementales, Overpower y Overflux.
    static var damage: [CharmInfo] { charms.filter { $0.element != nil || $0.name == "Overpower" || $0.name == "Overflux" } }

    /// Daño de una activación contra una criatura (TibiaWiki): los elementales, 5 % de su vida (como
    /// mucho el doble del nivel) por su debilidad a ese elemento; Overpower, 5 % de la vida del char, y
    /// Overflux, 2,5 % de su maná (los dos hasta el 8 % de la vida de la criatura y sin resistencias).
    static func damage(_ charm: CharmInfo, on creature: BestiaryCreature, level: Int, vocation: Vocation) -> Int {
        if let element = charm.element {
            let base = min(Double(creature.hp) * 0.05, Double(level * 2))
            return Int(base * Double(creature.mod(element)) / 100)
        }
        let cap = Double(creature.hp) * 0.08
        switch charm.name {
        case "Overpower": return Int(min(Double(vocation.hitPoints(level)) * 0.05, cap))
        case "Overflux": return Int(min(Double(vocation.mana(level)) * 0.025, cap))
        default: return 0
        }
    }
}

/// Una hunt conocida (preset del Charm Optimizer de hakaimarket.com): dónde, desde qué nivel y cuántas
/// criaturas de cada tipo se matan por hora. Vienen en Resources/hunts.json (assets/make_hunts.py).
struct HuntPreset: Decodable, Identifiable, Hashable {
    let name: String
    let city: String
    let minLevel: Int
    let premium: Bool
    let creatures: [HuntCreature]
    var id: String { city + "/" + name }
}

enum HuntCatalog {
    static let presets: [HuntPreset] = {
        struct File: Decodable { let hunts: [HuntPreset] }
        return resource("hunts").flatMap { try? JSONDecoder().decode(File.self, from: $0).hunts } ?? []
    }()
}

struct ItemInfo: Decodable, Hashable {
    let name: String
    let slot: String
}

enum ItemCatalog {
    static let items: [ItemInfo] = {
        struct File: Decodable { let items: [ItemInfo] }
        return resource("items").flatMap { try? JSONDecoder().decode(File.self, from: $0).items } ?? []
    }()
    static func items(for slot: ItemSlot?) -> [ItemInfo] { slot.map { s in items.filter { $0.slot == s.rawValue } } ?? items }
    static func slot(of item: String) -> ItemSlot? { items.first { $0.name == item }.flatMap { ItemSlot(rawValue: $0.slot) } }
}

/// Un JSON de Resources (o junto al ejecutable, como outfits.json).
private func resource(_ name: String) -> Data? {
    let url = Bundle.main.url(forResource: name, withExtension: "json")
        ?? URL(fileURLWithPath: CommandLine.arguments[0]).deletingLastPathComponent().appendingPathComponent("\(name).json")
    return try? Data(contentsOf: url)
}

/// Imágenes de TibiaWiki (File:<nombre>.gif): criaturas e items. Se piden de 50 en 50 a la API (la URL
/// de cada archivo), se descargan y se guardan en Application Support/Alfa Hub/Wiki.
actor WikiImages {
    static let shared = WikiImages()

    private var memory: [String: Data] = [:]   // vacío = no se pudo descargar
    private var waiting: [String: [CheckedContinuation<Data?, Never>]] = [:]
    private var queued: [String] = []
    private var flushing = false

    private static var folder: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Alfa Hub/Wiki", isDirectory: true)
    }

    /// Archivo del wiki: el nombre tal cual si ya lleva extensión ("Zap.png"), si no, "<nombre>.gif".
    private static func wikiFile(_ name: String) -> String {
        name.hasSuffix(".png") || name.hasSuffix(".gif") ? name : name + ".gif"
    }

    private static func file(_ name: String) -> URL {
        folder.appendingPathComponent(wikiFile(name).replacingOccurrences(of: "/", with: "-"))
    }

    func image(_ name: String) async -> Data? {
        if let data = memory[name] { return data.isEmpty ? nil : data }
        if let data = try? Data(contentsOf: Self.file(name)) {
            memory[name] = data
            return data
        }
        return await withCheckedContinuation { continuation in
            if waiting[name] != nil {
                waiting[name]!.append(continuation)
                return
            }
            waiting[name] = [continuation]
            queued.append(name)
            if !flushing {
                flushing = true
                Task { await self.flush() }
            }
        }
    }

    private func flush() async {
        try? await Task.sleep(for: .milliseconds(120))  // juntar las que pide la lista al aparecer
        while !queued.isEmpty {
            let batch = Array(queued.prefix(50))
            queued.removeFirst(batch.count)
            let urls = (try? await Self.urls(batch)) ?? [:]
            await withTaskGroup(of: (String, Data?).self) { group in
                for name in batch {
                    group.addTask {
                        guard let url = urls[name] else { return (name, nil) }
                        return (name, try? await Self.get(url + "&format=original"))
                    }
                }
                for await (name, data) in group { finish(name, data) }
            }
        }
        flushing = false
    }

    private func finish(_ name: String, _ data: Data?) {
        let image = data.flatMap { NSImage(data: $0) != nil ? $0 : nil }
        memory[name] = image ?? Data()
        if let image {
            try? FileManager.default.createDirectory(at: Self.folder, withIntermediateDirectories: true)
            try? image.write(to: Self.file(name))
        }
        for continuation in waiting.removeValue(forKey: name) ?? [] { continuation.resume(returning: image) }
    }

    /// URL de File:<nombre>.gif para cada nombre.
    private static func urls(_ names: [String]) async throws -> [String: String] {
        struct Response: Decodable {
            struct Query: Decodable { let normalized: [Rename]?; let pages: [Page] }
            struct Rename: Decodable { let from: String; let to: String }
            struct Page: Decodable { let title: String; let imageinfo: [Info]? }
            struct Info: Decodable { let url: String }
            let query: Query
        }
        let titles = names.map { "File:\(wikiFile($0))" }
        var components = URLComponents(string: "https://tibia.fandom.com/api.php")!
        components.queryItems = [
            .init(name: "action", value: "query"), .init(name: "prop", value: "imageinfo"), .init(name: "iiprop", value: "url"),
            .init(name: "format", value: "json"), .init(name: "formatversion", value: "2"),
            .init(name: "titles", value: titles.joined(separator: "|")),
        ]
        let query = try JSONDecoder().decode(Response.self, from: try await get(components.url!.absoluteString)).query
        let renamed = Dictionary((query.normalized ?? []).map { ($0.to, $0.from) }, uniquingKeysWith: { a, _ in a })
        var out: [String: String] = [:]
        for page in query.pages {
            guard let url = page.imageinfo?.first?.url else { continue }
            let title = renamed[page.title] ?? page.title
            if let i = titles.firstIndex(of: title) { out[names[i]] = url }
        }
        return out
    }

    private static func get(_ url: String) async throws -> Data {
        var request = URLRequest(url: URL(string: url)!)
        request.setValue("AlfaHub", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw URLError(.badServerResponse) }
        return data
    }
}

/// Lo que tibia.com dice de un char (API de TibiaData, /v4/character/<nombre>). Se guarda en el char
/// al sincronizarlo y viaja con el perfil al compartirlo.
struct TibiaInfo: Codable, Hashable {
    struct House: Codable, Hashable { var name: String; var town: String; var paid: String }
    struct Death: Codable, Hashable { var time: Date?; var level: Int; var reason: String }
    struct OtherChar: Codable, Hashable { var name: String; var world: String; var online: Bool; var main: Bool }

    var name = ""
    var sex = ""
    var title = ""
    var unlockedTitles = 0
    var vocation = ""             // como en tibia.com: "Elite Knight"
    var level = 0
    var achievementPoints = 0
    var world = ""
    var residence = ""
    var marriedTo = ""
    var guild = ""
    var guildRank = ""
    var houses: [House] = []
    var lastLogin: Date?
    var accountStatus = ""
    var formerNames: [String] = []
    var formerWorlds: [String] = []
    var accountCreated: Date?
    var loyaltyTitle = ""
    var deaths: [Death] = []
    var otherChars: [OtherChar] = []
    var comment = ""
    /// Experiencia y puesto en su mundo, si sale entre los 1000 primeros de los highscores.
    var experience: Int64?
    var worldRank: Int?
    var syncedAt = Date()
}

enum CharLookup {
    enum Failure: LocalizedError {
        case notFound
        var errorDescription: String? { "No existe en tibia.com (o TibiaData no responde)" }
    }

    static func fetch(_ name: String) async throws -> TibiaInfo {
        struct Response: Decodable {
            struct Body: Decodable {
                let character: Character
                let deaths: [Death]?
                let account_information: Account?
                let other_characters: [Other]?
            }
            struct Character: Decodable {
                let name: String
                let sex: String?
                let title: String?
                let unlocked_titles: Int?
                let vocation: String?
                let level: Int?
                let achievement_points: Int?
                let world: String?
                let residence: String?
                let married_to: String?
                let guild: Guild?
                let houses: [House]?
                let last_login: Date?
                let account_status: String?
                let former_names: [String]?
                let former_worlds: [String]?
                let comment: String?
            }
            struct Guild: Decodable { let name: String?; let rank: String? }
            struct House: Decodable { let name: String; let town: String?; let paid: String? }
            struct Death: Decodable { let time: Date?; let level: Int?; let reason: String? }
            struct Account: Decodable { let created: Date?; let loyalty_title: String? }
            struct Other: Decodable { let name: String; let world: String?; let status: String?; let main: Bool?; let deleted: Bool? }
            let character: Body
        }
        let escaped = name.trimmingCharacters(in: .whitespaces).addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? name
        var request = URLRequest(url: URL(string: "https://api.tibiadata.com/v4/character/\(escaped)")!)
        request.setValue("AlfaHub", forHTTPHeaderField: "User-Agent")
        request.cachePolicy = .reloadIgnoringLocalCacheData
        // Un nombre que no existe devuelve un error (502), igual que si TibiaData no responde.
        let (data, response) = try await URLSession.shared.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw Failure.notFound }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        guard let body = try? decoder.decode(Response.self, from: data).character, !body.character.name.isEmpty else {
            throw Failure.notFound
        }
        let c = body.character
        var info = TibiaInfo()
        info.name = c.name
        info.sex = c.sex ?? ""
        info.title = c.title == "None" ? "" : c.title ?? ""
        info.unlockedTitles = c.unlocked_titles ?? 0
        info.vocation = c.vocation ?? ""
        info.level = c.level ?? 0
        info.achievementPoints = c.achievement_points ?? 0
        info.world = c.world ?? ""
        info.residence = c.residence ?? ""
        info.marriedTo = c.married_to ?? ""
        info.guild = c.guild?.name ?? ""
        info.guildRank = c.guild?.rank ?? ""
        info.houses = (c.houses ?? []).map { .init(name: $0.name, town: $0.town ?? "", paid: $0.paid ?? "") }
        info.lastLogin = c.last_login
        info.accountStatus = c.account_status ?? ""
        info.formerNames = c.former_names ?? []
        info.formerWorlds = c.former_worlds ?? []
        info.comment = c.comment ?? ""
        info.accountCreated = body.account_information?.created
        info.loyaltyTitle = body.account_information?.loyalty_title ?? ""
        info.deaths = (body.deaths ?? []).map { .init(time: $0.time, level: $0.level ?? 0, reason: $0.reason ?? "") }
        info.otherChars = (body.other_characters ?? []).filter { $0.deleted != true && $0.name != c.name }
            .map { .init(name: $0.name, world: $0.world ?? "", online: $0.status == "online", main: $0.main ?? false) }
        if let entry = await highscore(info.name, world: info.world, level: info.level) {
            info.experience = entry.value
            info.worldRank = entry.rank
        }
        return info
    }

    /// Busca el char en los highscores de experiencia de su mundo (20 páginas de 50, ordenadas por
    /// experiencia): una búsqueda binaria por nivel, unas 5 peticiones. TibiaData no deja filtrar por
    /// vocación más allá de la primera página, así que se usa la de todas.
    static func highscore(_ name: String, world: String, level: Int) async -> (rank: Int, value: Int64)? {
        struct Response: Decodable {
            struct Body: Decodable { let highscore_list: [Entry] }
            struct Entry: Decodable { let rank: Int; let name: String; let level: Int; let value: Int64 }
            let highscores: Body
        }
        guard !world.isEmpty else { return nil }
        var cache: [Int: [Response.Entry]] = [:]
        func page(_ number: Int) async -> [Response.Entry]? {
            if let list = cache[number] { return list }
            let escaped = world.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? world
            var request = URLRequest(url: URL(string: "https://api.tibiadata.com/v4/highscores/\(escaped)/experience/all/\(number)")!)
            request.setValue("AlfaHub", forHTTPHeaderField: "User-Agent")
            guard let (data, _) = try? await URLSession.shared.data(for: request),
                  let list = try? JSONDecoder().decode(Response.self, from: data).highscores.highscore_list, !list.isEmpty else { return nil }
            cache[number] = list
            return list
        }
        var low = 1, high = 20
        while low <= high {
            let mid = (low + high) / 2
            guard let list = await page(mid) else { return nil }
            if let hit = list.first(where: { $0.name == name }) { return (hit.rank, hit.value) }
            if level > list.first!.level {
                high = mid - 1
            } else if level < list.last!.level {
                low = mid + 1
            } else {
                // Mismo nivel que los de esta página: puede estar en la de al lado.
                for other in [mid - 1, mid + 1] where (1...20).contains(other) {
                    if let hit = await page(other)?.first(where: { $0.name == name }) { return (hit.rank, hit.value) }
                }
                return nil
            }
        }
        return nil
    }
}

// MARK: - Compartir

enum ProfileShare {
    static let prefix = "ALFAHUB1:"
    static let fileExtension = "alfahub"

    enum Failure: LocalizedError {
        case invalid
        var errorDescription: String? { "No es un perfil de Alfa Hub (o el código está incompleto)." }
    }

    /// Lo que se comparte (archivo o código): el perfil sin los datos de importación.
    private struct Envelope: Codable {
        var alfaHubProfile = 1
        var profile: Profile
    }

    private static var encoder: JSONEncoder {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.sortedKeys]
        return encoder
    }

    static func json(_ profile: Profile) throws -> Data {
        var shared = profile
        shared.sourceID = nil
        shared.importedAt = nil
        return try encoder.encode(Envelope(profile: shared))
    }

    static func code(_ profile: Profile) throws -> String {
        let compressed = try (json(profile) as NSData).compressed(using: .zlib) as Data
        return prefix + compressed.base64EncodedString()
    }

    /// Un código ("ALFAHUB1:…") o el contenido de un archivo .alfahub.
    static func decode(_ data: Data) throws -> Profile {
        var json = data
        if let text = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines),
           text.hasPrefix(prefix) {
            let base64 = text.dropFirst(prefix.count).filter { !$0.isWhitespace }
            guard let compressed = Data(base64Encoded: String(base64)),
                  let raw = try? (compressed as NSData).decompressed(using: .zlib) as Data else { throw Failure.invalid }
            json = raw
        }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        guard let envelope = try? decoder.decode(Envelope.self, from: json) else { throw Failure.invalid }
        return envelope.profile
    }
}

// MARK: - Modelo

@MainActor
final class ProfileStore: ObservableObject {
    @Published var profiles: [Profile] { didSet { scheduleSave(); iconChanged() } }
    /// Perfil y char abiertos en la página (no se guarda).
    @Published var openProfile: UUID?
    @Published var openChar: UUID?
    /// Aviso pasajero al pie de la página ("Código copiado"…).
    @Published var notice: String?
    /// El char cuyo outfit va en el ícono de Alfa Hub (Dock, barra de menús y barra lateral).
    @Published var iconChar: UUID? {
        didSet { UserDefaults.standard.set(iconChar?.uuidString, forKey: "iconChar"); iconChanged() }
    }
    /// Se llama cuando cambia el outfit del ícono (otro char, o el mismo con otro outfit).
    var onIconOutfitChange: (OutfitConfig) -> Void = { _ in }
    private var lastIconOutfit: OutfitConfig?

    private func iconChanged() {
        // Hasta que se elige un char se deja el ícono que hubiera (el de la antigua página Outfit).
        guard iconChar != nil || lastIconOutfit != nil else { return }
        let config = iconOutfit
        guard config != lastIconOutfit else { return }
        lastIconOutfit = config
        onIconOutfitChange(config)
    }

    /// El nombre del char del ícono (nil si no hay o no tiene nombre), para la barra lateral.
    var iconCharName: String? {
        guard let iconChar,
              let name = profiles.lazy.flatMap(\.chars).first(where: { $0.id == iconChar })?.name
                .trimmingCharacters(in: .whitespaces), !name.isEmpty else { return nil }
        return name
    }

    /// El outfit del ícono: el del char elegido (el original si no hay o si se borró).
    var iconOutfit: OutfitConfig {
        guard let iconChar else { return .original }
        return profiles.lazy.flatMap(\.chars).first { $0.id == iconChar }?.outfit ?? .original
    }

    private var saveTask: Task<Void, Never>?

    static var file: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Alfa Hub/perfiles.json")
    }

    init() {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        profiles = (try? Data(contentsOf: Self.file)).flatMap { try? decoder.decode([Profile].self, from: $0) } ?? []
        iconChar = UserDefaults.standard.string(forKey: "iconChar").flatMap(UUID.init(uuidString:))
        NotificationCenter.default.addObserver(forName: NSApplication.willTerminateNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.save() }
        }
    }

    /// Vuelve a sincronizar sola, una vez por día de Tibia, cada char ya sincronizado: así el historial
    /// de experiencia se llena aunque no se abra el char. Se mira al abrir la app y cada 30 minutos.
    func startAutoSync() {
        guard autoSync == nil else { return }
        autoSync = Task { [weak self] in
            while !Task.isCancelled {
                await self?.syncStale()
                try? await Task.sleep(for: .seconds(1800))
            }
        }
    }

    private var autoSync: Task<Void, Never>?

    private func syncStale() async {
        let day = TibiaCalendar.lastServerSave()
        // Unos minutos después del server save, cuando tibia.com ya tiene los highscores nuevos.
        guard Date().timeIntervalSince(day) > 20 * 60 else { return }
        let stale = profiles.flatMap { p in p.chars.map { (p.id, $0) } }
            .filter { $0.1.isSynced && ($0.1.tibia?.syncedAt ?? .distantPast) < day.addingTimeInterval(20 * 60) }
        for (profileID, char) in stale {
            guard let info = try? await CharLookup.fetch(char.name) else { continue }
            let binding = self.binding(char: char.id, in: profileID)
            guard binding.wrappedValue.name.caseInsensitiveCompare(info.name) == .orderedSame else { continue }
            binding.wrappedValue.apply(info)
            try? await Task.sleep(for: .seconds(2))  // sin agobiar a TibiaData
        }
    }

    func binding(_ id: UUID) -> Binding<Profile> {
        Binding(get: { self.profiles.first { $0.id == id } ?? Profile() },
                set: { new in if let i = self.index(id) { self.profiles[i] = new } })
    }

    func binding(char id: UUID, in profile: UUID) -> Binding<GameChar> {
        Binding(get: { self.profiles.first { $0.id == profile }?.chars.first { $0.id == id } ?? GameChar() },
                set: { new in
                    guard let i = self.index(profile), let j = self.profiles[i].chars.firstIndex(where: { $0.id == id }) else { return }
                    self.profiles[i].chars[j] = new
                })
    }

    /// Escribir las kills tecla a tecla no guarda cada vez: se espera medio segundo.
    private func scheduleSave() {
        saveTask?.cancel()
        saveTask = Task {
            try? await Task.sleep(for: .milliseconds(500))
            guard !Task.isCancelled else { return }
            save()
        }
    }

    func save() {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        guard let data = try? encoder.encode(profiles) else { return }
        try? FileManager.default.createDirectory(at: Self.file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: Self.file, options: .atomic)
    }

    func index(_ id: UUID?) -> Int? { profiles.firstIndex { $0.id == id } }

    @discardableResult
    func addProfile() -> Profile {
        var profile = Profile(name: "Perfil \(profiles.count + 1)")
        profile.chars = [GameChar()]
        profiles.append(profile)
        return profile
    }

    func duplicate(_ profile: Profile) {
        var copy = profile
        copy.id = UUID()
        copy.name += " (copia)"
        copy.sourceID = nil
        copy.importedAt = nil
        profiles.insert(copy, at: (index(profile.id) ?? profiles.count - 1) + 1)
    }

    func remove(_ profile: Profile) {
        profiles.removeAll { $0.id == profile.id }
        if openProfile == profile.id { openProfile = nil; openChar = nil }
    }

    /// Añade un perfil compartido. Si ya se había importado (mismo original), se actualiza esa copia.
    /// Devuelve el perfil y si era una actualización.
    @discardableResult
    func importProfile(_ shared: Profile) -> (Profile, updated: Bool) {
        var profile = shared
        profile.sourceID = shared.id
        profile.importedAt = Date()
        if let i = profiles.firstIndex(where: { $0.sourceID == shared.id }) {
            profile.id = profiles[i].id
            profiles[i] = profile
            return (profile, true)
        }
        profile.id = UUID()
        profiles.append(profile)
        return (profile, false)
    }
}

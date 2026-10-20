// Outfiter: el personaje de Alfa Hub (ícono de la app, barra lateral y barra de menús) se elige como
// en el Outfiter de TibiaWiki (https://tibia.fandom.com/wiki/Outfiter): outfit, sexo, addons, los
// cuatro colores, orientación y montura.
//
// Las hojas de sprites son las del wiki (páginas Outfiter:Male/…, Female/…, Other/… y Mount/…): se
// descargan de su API la primera vez que se eligen y se guardan en caché. El dibujo sigue la misma
// receta que MediaWiki:Outfiter.js: cuadrícula de 64 × 64; por orientación, una columna con el sprite y
// otra con la máscara de colores (amarillo cabeza, rojo primario, verde secundario, azul detalle) que
// se multiplica; filas 0–2 sprite, addon 1 y addon 2 (montado: las siguientes); rosa = transparente.

import AppKit

struct OutfitConfig: Codable, Equatable {
    var outfit = 235          // Captains: el personaje original de Alfa Hub
    var female = false
    var addon1 = true
    var addon2 = false
    var c1 = 95, c2 = 95, c3 = 95, c4 = 114
    var facing = 2            // 0 norte, 1 este, 2 sur, 3 oeste
    var mount = 0
    var mc1 = 0, mc2 = 0, mc3 = 0, mc4 = 0

    static let original = OutfitConfig()
}

extension OutfitConfig {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func get<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T { (try? c.decodeIfPresent(T.self, forKey: key)) ?? fallback }
        self.init()
        outfit = get(.outfit, outfit); female = get(.female, female)
        addon1 = get(.addon1, addon1); addon2 = get(.addon2, addon2)
        c1 = get(.c1, c1); c2 = get(.c2, c2); c3 = get(.c3, c3); c4 = get(.c4, c4)
        facing = get(.facing, facing); mount = get(.mount, mount)
        mc1 = get(.mc1, mc1); mc2 = get(.mc2, mc2); mc3 = get(.mc3, mc3); mc4 = get(.mc4, mc4)
    }
}

/// Lista de outfits y monturas del Outfiter (assets/outfits.json, sacado de MediaWiki:Outfiter.js).
struct OutfitCatalog: Decodable {
    let names: [String?]      // el índice es el id del outfit (o=…)
    let mounts: [String]      // el índice es el id de la montura (m=…)
    let colors: [[Int]]       // 133 colores (7 filas × 19)
    let noAddons: [String]
    let oneAddon: [String]
    let noMount: [String]
    let noFemale: [String]
    let femaleSuffix: [String]
    let femaleNames: [String: String]
    let mountColor: [String]

    static let shared: OutfitCatalog = {
        let url = Bundle.main.url(forResource: "outfits", withExtension: "json")
            ?? URL(fileURLWithPath: CommandLine.arguments[0]).deletingLastPathComponent().appendingPathComponent("outfits.json")
        return try! JSONDecoder().decode(OutfitCatalog.self, from: Data(contentsOf: url))
    }()

    func name(_ id: Int) -> String? { id >= 0 && id < names.count ? names[id] : nil }
    func hasAddons(_ id: Int) -> Bool { name(id).map { !noAddons.contains($0) && $0 != "None" } ?? false }
    func onlyOneAddon(_ id: Int) -> Bool { name(id).map { oneAddon.contains($0) } ?? false }
    func canMount(_ id: Int) -> Bool { name(id).map { !noMount.contains($0) && $0 != "None" } ?? false }
    func canBeFemale(_ id: Int) -> Bool { name(id).map { !noFemale.contains($0) && !(100..<200).contains(id) } ?? false }
    func mountColorable(_ id: Int) -> Bool { id > 0 && id < mounts.count && mountColor.contains(mounts[id]) }

    /// Nombre para mostrar ("Captains", "Noblewoman"…).
    func title(_ id: Int, female: Bool = false) -> String {
        guard let n = name(id) else { return "?" }
        return ((female ? femaleNames[n] : nil) ?? n).replacingOccurrences(of: "_", with: " ")
    }

    func mountTitle(_ id: Int) -> String {
        id > 0 && id < mounts.count ? mounts[id].replacingOccurrences(of: "_", with: " ") : "Sin montura"
    }

    /// Página del wiki con la hoja de sprites del outfit (o de la montura).
    func page(outfit id: Int, female: Bool) -> String? {
        guard let n = name(id), n != "None" else { return nil }
        if (100..<200).contains(id) { return "Other/\(n)" }
        if female && canBeFemale(id) { return "Female/\(n)\(femaleSuffix.contains(n) ? "_Female" : "")" }
        return "Male/\(n)"
    }

    func page(mount id: Int) -> String? { id > 0 && id < mounts.count ? "Mount/\(mounts[id])" : nil }

    /// Ids de outfits por orden alfabético (sin huecos ni "None").
    var sortedOutfits: [Int] {
        names.indices.filter { names[$0] != nil && names[$0] != "None" }.sorted { title($0) < title($1) }
    }

    var sortedMounts: [Int] { [0] + mounts.indices.dropFirst().sorted { mountTitle($0) < mountTitle($1) } }
}

// MARK: - Hojas de sprites

enum OutfitSheets {
    enum Failure: Error { case notFound }

    static var cacheFolder: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("Alfa Hub/Outfiter", isDirectory: true)
    }

    static func cached(_ page: String) -> URL {
        cacheFolder.appendingPathComponent(page.replacingOccurrences(of: "/", with: "-") + ".png")
    }

    /// PNG de la hoja (de la caché o descargado de la API del wiki).
    static func load(_ page: String) async throws -> Data {
        let file = cached(page)
        if let data = try? Data(contentsOf: file) { return data }
        var components = URLComponents(string: "https://tibia.fandom.com/api.php")!
        components.queryItems = [
            .init(name: "action", value: "query"), .init(name: "prop", value: "revisions"),
            .init(name: "titles", value: "Outfiter:\(page)"), .init(name: "rvprop", value: "content"),
            .init(name: "rvslots", value: "main"), .init(name: "format", value: "json"), .init(name: "formatversion", value: "2"),
        ]
        var request = URLRequest(url: components.url!)
        request.setValue("AlfaHub (outfiter personal)", forHTTPHeaderField: "User-Agent")
        let (data, _) = try await URLSession.shared.data(for: request)
        // {"query":{"pages":[{"revisions":[{"slots":{"main":{"content":"<pre id=…>data:image/png;base64,…</pre>"}}}]}]}}
        struct Response: Decodable {
            struct Query: Decodable { let pages: [Page] }
            struct Page: Decodable { let revisions: [Revision]? }
            struct Revision: Decodable { let slots: [String: Slot] }
            struct Slot: Decodable { let content: String }
            let query: Query
        }
        guard let text = try JSONDecoder().decode(Response.self, from: data).query.pages.first?.revisions?.first?.slots["main"]?.content,
              let start = text.range(of: "base64,") else { throw Failure.notFound }
        let rest = text[start.upperBound...]
        let base64 = rest[..<(rest.range(of: "<")?.lowerBound ?? rest.endIndex)].filter { !$0.isWhitespace }
        guard let png = Data(base64Encoded: String(base64)) else { throw Failure.notFound }
        try? FileManager.default.createDirectory(at: cacheFolder, withIntermediateDirectories: true)
        try? png.write(to: file)
        return png
    }
}

// MARK: - Dibujo

enum OutfitRenderer {
    typealias Pixels = [UInt8]  // RGBA 64 × 64, sin premultiplicar
    static let tile = 64

    /// Una celda de la hoja; el rosa (255, 0, 255) pasa a transparente.
    static func cell(_ rep: NSBitmapImageRep, col: Int, row: Int) -> Pixels {
        var out = Pixels(repeating: 0, count: tile * tile * 4)
        let spp = rep.samplesPerPixel
        var p = [Int](repeating: 0, count: max(4, spp))
        for y in 0..<tile {
            for x in 0..<tile {
                let sx = col * tile + x, sy = row * tile + y
                guard sx < rep.pixelsWide, sy < rep.pixelsHigh else { continue }
                rep.getPixel(&p, atX: sx, y: sy)
                let i = (y * tile + x) * 4
                out[i] = UInt8(p[0]); out[i + 1] = UInt8(p[1]); out[i + 2] = UInt8(p[2])
                out[i + 3] = spp >= 4 ? UInt8(p[3]) : 255
                if p[0] == 255 && p[1] == 0 && p[2] == 255 { out[i + 3] = 0 }
            }
        }
        return out
    }

    /// Aplica los colores de la máscara multiplicando (igual que outfiter_pixels_blend).
    static func blend(_ main: Pixels, mask: Pixels, colors: [Int]) -> Pixels {
        let table = OutfitCatalog.shared.colors
        func color(_ index: Int) -> [Int] { table[max(0, min(table.count - 1, index))] }
        var out = main
        for i in stride(from: 0, to: out.count, by: 4) {
            var r = Int(mask[i]), g = Int(mask[i + 1]), b = Int(mask[i + 2])
            var c: [Int]?
            if r == 255 && g == 255 && b == 0 { c = color(colors[0]) }
            else if r == 255 && g == 0 && b == 0 { c = color(colors[1]) }
            else if r == 0 && g == 255 && b == 0 { c = color(colors[2]) }
            else if r == 0 && g == 0 && b == 255 { c = color(colors[3]) }
            if let c { r = c[0]; g = c[1]; b = c[2] }
            if !(r == 255 && g == 0 && b == 255) {
                out[i] = UInt8(r * Int(out[i]) / 255)
                out[i + 1] = UInt8(g * Int(out[i + 1]) / 255)
                out[i + 2] = UInt8(b * Int(out[i + 2]) / 255)
            }
        }
        return out
    }

    /// Pone encima los píxeles no transparentes de top.
    static func merge(_ bottom: Pixels?, _ top: Pixels?) -> Pixels? {
        guard let bottom else { return top }
        guard let top else { return bottom }
        var out = bottom
        for i in stride(from: 0, to: out.count, by: 4) where top[i + 3] != 0 {
            out[i] = top[i]; out[i + 1] = top[i + 1]; out[i + 2] = top[i + 2]; out[i + 3] = top[i + 3]
        }
        return out
    }

    /// El personaje quieto (64 × 64), con la montura debajo si la hay.
    static func render(_ config: OutfitConfig, outfit: Data, mount: Data?) -> CGImage? {
        let catalog = OutfitCatalog.shared
        guard let sheet = NSBitmapImageRep(data: outfit) else { return nil }
        let facing = max(0, min(3, config.facing))
        let addons = catalog.hasAddons(config.outfit)
        let mounted = config.mount > 0 && mount != nil && catalog.canMount(config.outfit)
        let baseRow = mounted ? (addons ? 3 : 1) : 0
        var pixels: Pixels?
        for (i, on) in [true, addons && config.addon1, addons && config.addon2].enumerated() where on {
            let layer = blend(cell(sheet, col: facing * 2, row: baseRow + i),
                              mask: cell(sheet, col: facing * 2 + 1, row: baseRow + i),
                              colors: [config.c1, config.c2, config.c3, config.c4])
            pixels = merge(pixels, layer)
        }
        if mounted, let mount, let mountSheet = NSBitmapImageRep(data: mount) {
            let colorable = catalog.mountColorable(config.mount)
            var m = cell(mountSheet, col: colorable ? facing * 2 : facing, row: 0)
            if colorable {
                m = blend(m, mask: cell(mountSheet, col: facing * 2 + 1, row: 0), colors: [config.mc1, config.mc2, config.mc3, config.mc4])
            }
            pixels = merge(m, pixels)
        }
        return pixels.flatMap(image)
    }

    static func image(_ pixels: Pixels) -> CGImage? {
        let data = Data(pixels) as CFData
        guard let provider = CGDataProvider(data: data) else { return nil }
        return CGImage(width: tile, height: tile, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: tile * 4,
                       space: CGColorSpace(name: CGColorSpace.sRGB)!,
                       bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.last.rawValue),
                       provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
    }

    /// Recorta los bordes transparentes (como character.png).
    static func cropped(_ image: CGImage) -> CGImage {
        let rep = NSBitmapImageRep(cgImage: image)
        var minX = rep.pixelsWide, minY = rep.pixelsHigh, maxX = -1, maxY = -1
        var p = [Int](repeating: 0, count: 4)
        for y in 0..<rep.pixelsHigh {
            for x in 0..<rep.pixelsWide {
                rep.getPixel(&p, atX: x, y: y)
                if p[3] > 5 { minX = min(minX, x); maxX = max(maxX, x); minY = min(minY, y); maxY = max(maxY, y) }
            }
        }
        guard maxX >= minX else { return image }
        return image.cropping(to: CGRect(x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1)) ?? image
    }

    /// Ícono cuadrado: el personaje centrado sin fondo, lo más grande posible y con escala entera
    /// (igual que assets/make_icons.swift).
    static func icon(_ sprite: CGImage, size: Int) -> NSImage {
        let s = CGFloat(size)
        let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                                   samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                                   bytesPerRow: 0, bitsPerPixel: 0)!
        let ctx = NSGraphicsContext(bitmapImageRep: rep)!.cgContext
        let maxSide = s * 0.9
        var scale = min(maxSide / CGFloat(sprite.width), maxSide / CGFloat(sprite.height))
        if scale >= 1 { scale = floor(scale) }
        let w = CGFloat(sprite.width) * scale, h = CGFloat(sprite.height) * scale
        ctx.interpolationQuality = scale >= 1 ? .none : .high
        ctx.draw(sprite, in: CGRect(x: ((s - w) / 2).rounded(), y: ((s - h) / 2).rounded(), width: w, height: h))
        let image = NSImage(size: NSSize(width: size, height: size))
        image.addRepresentation(rep)
        return image
    }
}

// Outfiter: el personaje de Alfa Hub (ícono de la app, barra lateral y barra de menús) se elige como
// en el Outfiter de TibiaWiki (https://tibia.fandom.com/wiki/Outfiter): outfit, sexo, addons, los
// cuatro colores, orientación, montura y la animación de caminata.
//
// Las hojas de sprites son las del wiki (páginas Outfiter:Male/…, Female/…, Other/… y Mount/…): se
// descargan de su API la primera vez que se eligen y se guardan en caché. El dibujo sigue la misma
// receta que MediaWiki:Outfiter.js: cuadrícula de 64 × 64; por orientación, una columna con el sprite y
// otra con la máscara de colores (amarillo cabeza, rojo primario, verde secundario, azul detalle) que
// se multiplica; filas 0–2 sprite, addon 1 y addon 2 (montado: las siguientes); rosa = transparente.
// Cada fotograma repite ese bloque de filas: el 0 es el personaje quieto y los 8 siguientes la caminata
// (algunos outfits y monturas tienen antes unos fotogramas de "quieto animado").

import AppKit

struct OutfitConfig: Codable, Hashable {
    var outfit = 1            // Hunter: el de un personaje recién creado (el original de Alfa Hub)
    var female = false
    var addon1 = false
    var addon2 = false
    var c1 = 95, c2 = 113, c3 = 39, c4 = 115
    var facing = 2            // 0 norte, 1 este, 2 sur, 3 oeste
    var mount = 0             // montura elegida en la lista
    var mounted = false       // montado encima (casilla Montura)
    var mc1 = 0, mc2 = 0, mc3 = 0, mc4 = 0
    var animate = false       // caminar en la vista previa y en la barra lateral

    static let original = OutfitConfig()

    /// La montura sobre la que va (0 = a pie).
    var ridden: Int { mounted && OutfitCatalog.shared.canMount(outfit) ? mount : 0 }
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
        animate = get(.animate, animate)
        mounted = get(.mounted, mount > 0)  // antes, elegir montura era ir montado
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
    let noRide: [String]                  // sin filas de montado
    let standing: [String: Int]           // fotogramas de "quieto animado" antes de la caminata
    let walking: [String: Int]            // fotogramas de caminata (si no son 8)
    let mountStanding: [String: Int]
    let mountWalking: [String: Int]

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
    func canRide(_ id: Int) -> Bool { name(id).map { !noRide.contains($0) } ?? false }
    func mountName(_ id: Int) -> String? { id > 0 && id < mounts.count ? mounts[id] : nil }

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

    /// Descripción corta ("Captains · addon 1 · sobre Widow Queen") para la lista de presets.
    func summary(_ c: OutfitConfig) -> String {
        var parts = [title(c.outfit, female: c.female)]
        if hasAddons(c.outfit) && (c.addon1 || c.addon2) {
            parts.append(c.addon1 && c.addon2 ? "addons 1 y 2" : c.addon1 ? "addon 1" : "addon 2")
        }
        if c.ridden > 0 { parts.append("sobre \(mountTitle(c.ridden))") }
        return parts.joined(separator: " · ")
    }
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

    private static let lock = NSLock()
    nonisolated(unsafe) private static var decoded: [String: OutfitSheet] = [:]
    nonisolated(unsafe) private static var order: [String] = []

    /// La hoja ya decodificada (se guardan las últimas en memoria: cambiar un color no vuelve a leer el PNG).
    static func sheet(_ page: String) async throws -> OutfitSheet {
        if let sheet = lock.withLock({ decoded[page] }) { return sheet }
        let png = try await load(page)
        guard let sheet = await Task.detached(operation: { OutfitSheet(png) }).value else { throw Failure.notFound }
        lock.withLock {
            decoded[page] = sheet
            order.removeAll { $0 == page }
            order.append(page)
            if order.count > 12 { decoded[order.removeFirst()] = nil }
        }
        return sheet
    }
}

// MARK: - Dibujo

/// Una hoja de sprites como RGBA tal cual viene en el PNG. Sin gestión de color: las hojas con paleta
/// (PNG indexado) se convertían al leerlas y el rosa (255, 0, 255) llegaba como (251, 0, 255), así que
/// no pasaba a transparente (fondo rosado y personaje mal centrado) ni se aplicaban los colores.
struct OutfitSheet: @unchecked Sendable {
    let width: Int, height: Int
    let rgba: [UInt8]

    var rows: Int { height / OutfitRenderer.tile }

    init?(_ png: Data) {
        guard let source = CGImageSourceCreateWithData(png as CFData, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else { return nil }
        width = image.width; height = image.height
        if let raw = OutfitSheet.raw(image) { rgba = raw; return }
        // Formato poco común: se dibuja en RGBA (con conversión de color).
        var out = [UInt8](repeating: 0, count: width * height * 4)
        guard let ctx = CGContext(data: &out, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
        rgba = out
    }

    /// Los bytes sin convertir de los PNG del wiki: con paleta, RGB y RGBA de 8 bits.
    private static func raw(_ image: CGImage) -> [UInt8]? {
        guard image.bitsPerComponent == 8, let space = image.colorSpace,
              let data = image.dataProvider?.data as Data? else { return nil }
        let w = image.width, h = image.height, bpr = image.bytesPerRow
        let order = image.bitmapInfo.intersection(.byteOrderMask)
        guard order == [] || order == .byteOrderDefault || order == .byteOrder32Big else { return nil }
        var out = [UInt8](repeating: 255, count: w * h * 4)
        return data.withUnsafeBytes { (src: UnsafeRawBufferPointer) -> [UInt8]? in
            switch (space.model, image.bitsPerPixel) {
            case (.indexed, 8):
                guard space.baseColorSpace?.model == .rgb, let table = space.colorTable else { return nil }
                for y in 0..<h {
                    for x in 0..<w {
                        let index = Int(src[y * bpr + x]) * 3, o = (y * w + x) * 4
                        guard index + 2 < table.count else { continue }
                        out[o] = table[index]; out[o + 1] = table[index + 1]; out[o + 2] = table[index + 2]
                    }
                }
            case (.rgb, 24), (.rgb, 32):
                let step = image.bitsPerPixel / 8
                let alpha = image.alphaInfo
                guard [.none, .last, .noneSkipLast].contains(alpha) else { return nil }
                for y in 0..<h {
                    for x in 0..<w {
                        let i = y * bpr + x * step, o = (y * w + x) * 4
                        out[o] = src[i]; out[o + 1] = src[i + 1]; out[o + 2] = src[i + 2]
                        if alpha == .last { out[o + 3] = src[i + 3] }
                    }
                }
            default:
                return nil
            }
            return out
        }
    }
}

/// Un personaje dibujado: quieto (para íconos) y los fotogramas de la caminata, recortados al contorno.
struct OutfitSprite: @unchecked Sendable {
    let still: CGImage
    /// Fotogramas de la caminata, todos del mismo tamaño (vacío si no se pidió animar).
    let frames: [CGImage]
    let frameDuration: Double

    /// El fotograma que toca en este momento.
    func frame(at date: Date) -> CGImage {
        guard !frames.isEmpty else { return still }
        return frames[Int(date.timeIntervalSinceReferenceDate / frameDuration) % frames.count]
    }
}

enum OutfitRenderer {
    typealias Pixels = [UInt8]  // RGBA 64 × 64, sin premultiplicar
    static let tile = 64

    /// Una celda de la hoja; el rosa (255, 0, 255) pasa a transparente.
    static func cell(_ sheet: OutfitSheet, col: Int, row: Int) -> Pixels {
        var out = Pixels(repeating: 0, count: tile * tile * 4)
        sheet.rgba.withUnsafeBufferPointer { src in
            for y in 0..<tile {
                let sy = row * tile + y
                guard sy < sheet.height else { break }
                for x in 0..<tile {
                    let sx = col * tile + x
                    guard sx < sheet.width else { break }
                    let s = (sy * sheet.width + sx) * 4, i = (y * tile + x) * 4
                    out[i] = src[s]; out[i + 1] = src[s + 1]; out[i + 2] = src[s + 2]; out[i + 3] = src[s + 3]
                    if src[s] == 255 && src[s + 1] == 0 && src[s + 2] == 255 { out[i + 3] = 0 }
                }
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

    /// Un fotograma (64 × 64) del personaje, con la montura debajo si la hay. Fotograma 0: quieto.
    static func pixels(_ config: OutfitConfig, outfit sheet: OutfitSheet, mount mountSheet: OutfitSheet?,
                       frame: Int, mountFrame: Int) -> Pixels? {
        let catalog = OutfitCatalog.shared
        let facing = max(0, min(3, config.facing))
        let addons = catalog.hasAddons(config.outfit)
        let mounted = config.ridden > 0 && mountSheet != nil
        // Filas por fotograma: capas (sprite + addons) × (a pie + montado).
        let perFrame = (addons ? 3 : 1) * (catalog.canRide(config.outfit) ? 2 : 1)
        var first = (mounted ? (addons ? 3 : 1) : 0) + frame * perFrame
        if first + (addons ? 3 : 1) > sheet.rows { first -= frame * perFrame }  // hoja sin ese fotograma: quieto
        var pixels: Pixels?
        for (i, on) in [true, addons && config.addon1, addons && config.addon2].enumerated() where on {
            let layer = blend(cell(sheet, col: facing * 2, row: first + i),
                              mask: cell(sheet, col: facing * 2 + 1, row: first + i),
                              colors: [config.c1, config.c2, config.c3, config.c4])
            pixels = merge(pixels, layer)
        }
        if mounted, let mountSheet {
            let colorable = catalog.mountColorable(config.ridden)
            let row = mountFrame < mountSheet.rows ? mountFrame : 0
            var m = cell(mountSheet, col: colorable ? facing * 2 : facing, row: row)
            if colorable {
                m = blend(m, mask: cell(mountSheet, col: facing * 2 + 1, row: row), colors: [config.mc1, config.mc2, config.mc3, config.mc4])
            }
            pixels = merge(m, pixels)
        }
        return pixels
    }

    /// El personaje quieto y, si config.animate, su caminata (como "Animate" en el Outfiter).
    static func render(_ config: OutfitConfig, outfit sheet: OutfitSheet, mount mountSheet: OutfitSheet?) -> OutfitSprite? {
        let catalog = OutfitCatalog.shared
        guard let still = pixels(config, outfit: sheet, mount: mountSheet, frame: 0, mountFrame: 0) else { return nil }
        guard let stillImage = cropped(still) else { return nil }
        guard config.animate else { return OutfitSprite(still: stillImage, frames: [], frameDuration: 0.1) }
        // Fotogramas de la caminata: después del quieto (o de los de "quieto animado").
        let name = catalog.name(config.outfit) ?? ""
        let walkO = catalog.walking[name] ?? 8, firstO = catalog.standing[name] ?? 1
        let mountName = catalog.mountName(config.ridden) ?? ""
        let mounted = config.ridden > 0 && mountSheet != nil
        let walkM = mounted ? catalog.mountWalking[mountName] ?? 8 : walkO
        let firstM = catalog.mountStanding[mountName] ?? 1
        let count = walkO * walkM / gcd(walkO, walkM)
        var frames: [Pixels] = []
        for i in 0..<count {
            guard let p = pixels(config, outfit: sheet, mount: mountSheet, frame: firstO + i % walkO, mountFrame: firstM + i % walkM)
            else { continue }
            frames.append(p)
        }
        // Todos los fotogramas con el mismo recorte, para que no "salte" al caminar.
        var box = bounds(still)
        for f in frames { box = box.union(bounds(f)) }
        let images = frames.compactMap { image($0)?.cropping(to: box) }
        return OutfitSprite(still: stillImage, frames: images,
                            frameDuration: max(0.1, 0.8 / Double(walkO)))
    }

    /// Descarga (si hace falta) las hojas de config y lo dibuja fuera del hilo principal.
    static func render(_ config: OutfitConfig) async throws -> OutfitSprite? {
        let catalog = OutfitCatalog.shared
        guard let page = catalog.page(outfit: config.outfit, female: config.female) else { return nil }
        let outfit = try await OutfitSheets.sheet(page)
        var mount: OutfitSheet?
        if config.ridden > 0, let mountPage = catalog.page(mount: config.ridden) {
            mount = try await OutfitSheets.sheet(mountPage)
        }
        return await Task.detached { render(config, outfit: outfit, mount: mount) }.value
    }

    private static func gcd(_ a: Int, _ b: Int) -> Int { b == 0 ? a : gcd(b, a % b) }

    /// Contorno de los píxeles visibles (vacío si no hay ninguno).
    static func bounds(_ pixels: Pixels) -> CGRect {
        var minX = tile, minY = tile, maxX = -1, maxY = -1
        for y in 0..<tile {
            for x in 0..<tile where pixels[(y * tile + x) * 4 + 3] > 5 {
                minX = min(minX, x); maxX = max(maxX, x); minY = min(minY, y); maxY = max(maxY, y)
            }
        }
        return maxX >= minX ? CGRect(x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1) : .null
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
    static func cropped(_ pixels: Pixels) -> CGImage? {
        let box = bounds(pixels)
        guard let full = image(pixels) else { return nil }
        return box.isNull ? full : full.cropping(to: box) ?? full
    }

    /// Ícono cuadrado: el personaje centrado, lo más grande posible y con escala entera (igual que
    /// assets/make_icons.swift). Con plate, sobre la placa gris que macOS pone a los íconos de las apps
    /// (al ícono del .app se la pone el sistema; al que se cambia en marcha, no).
    static func icon(_ sprite: CGImage, size: Int, plate: Bool = false) -> NSImage {
        let s = CGFloat(size)
        let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                                   samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                                   bytesPerRow: 0, bitsPerPixel: 0)!
        let ctx = NSGraphicsContext(bitmapImageRep: rep)!.cgContext
        var area = CGRect(x: 0, y: 0, width: s, height: s)
        if plate {
            // Medidas y colores sacados del ícono del .app tal como lo dibuja macOS.
            let body = area.insetBy(dx: s * 50 / 512, dy: s * 50 / 512)
            let path = CGPath(roundedRect: body, cornerWidth: body.width * 0.25, cornerHeight: body.width * 0.25, transform: nil)
            ctx.saveGState()
            ctx.setShadow(offset: CGSize(width: 0, height: -s * 4 / 512), blur: s * 10 / 512, color: CGColor(gray: 0, alpha: 0.35))
            ctx.addPath(path)
            ctx.setFillColor(CGColor(gray: 0.8, alpha: 1))
            ctx.fillPath()
            ctx.restoreGState()
            ctx.saveGState()
            ctx.addPath(path)
            ctx.clip()
            let gradient = CGGradient(colorSpace: CGColorSpace(name: CGColorSpace.genericGrayGamma2_2)!,
                                      colorComponents: [0.87, 1, 0.73, 1] as [CGFloat], locations: [0, 1], count: 2)!
            ctx.drawLinearGradient(gradient, start: CGPoint(x: 0, y: body.maxY), end: CGPoint(x: 0, y: body.minY), options: [])
            // Borde claro arriba, como la luz de la placa del sistema.
            ctx.addPath(path)
            ctx.setStrokeColor(CGColor(gray: 1, alpha: 0.85))
            ctx.setLineWidth(s * 4 / 512)
            ctx.replacePathWithStrokedPath()
            ctx.clip()
            let rim = CGGradient(colorSpace: CGColorSpace(name: CGColorSpace.genericGrayGamma2_2)!,
                                 colorComponents: [1, 0.9, 1, 0] as [CGFloat], locations: [0, 0.5], count: 2)!
            ctx.drawLinearGradient(rim, start: CGPoint(x: 0, y: body.maxY), end: CGPoint(x: 0, y: body.minY), options: [])
            ctx.restoreGState()
            area = body.insetBy(dx: body.width * 0.12, dy: body.width * 0.12)
        }
        let maxSide = plate ? area.width : s * 0.9
        var scale = min(maxSide / CGFloat(sprite.width), maxSide / CGFloat(sprite.height))
        if scale >= 1 { scale = floor(scale) }
        let w = CGFloat(sprite.width) * scale, h = CGFloat(sprite.height) * scale
        ctx.interpolationQuality = scale >= 1 ? .none : .high
        ctx.draw(sprite, in: CGRect(x: (area.midX - w / 2).rounded(), y: (area.midY - h / 2).rounded(), width: w, height: h))
        let image = NSImage(size: NSSize(width: size, height: size))
        image.addRepresentation(rep)
        return image
    }
}

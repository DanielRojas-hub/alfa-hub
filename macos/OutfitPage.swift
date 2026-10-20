// Página Outfit del hub y el personaje en uso (ícono de la app, barra lateral y barra de menús).

import AppKit
import SwiftUI

/// El personaje elegido: lo dibuja (descargando las hojas si hace falta) y avisa al cambiar.
@MainActor
final class OutfitManager: ObservableObject {
    @Published var config: OutfitConfig {
        didSet {
            guard config != oldValue else { return }
            UserDefaults.standard.set(try? JSONEncoder().encode(config), forKey: "outfit")
            render()
        }
    }
    /// Sprite de 64 × 64 sin recortar (vista previa) y recortado a su contorno (barra lateral, ícono).
    @Published private(set) var preview: CGImage?
    @Published private(set) var sprite: CGImage?
    @Published private(set) var loading = false
    @Published private(set) var failed = false
    /// Se llama con el sprite recortado (nil = el personaje original del .app).
    var onChange: (CGImage?) -> Void = { _ in }
    private var task: Task<Void, Never>?

    init() {
        config = UserDefaults.standard.data(forKey: "outfit").flatMap { try? JSONDecoder().decode(OutfitConfig.self, from: $0) } ?? .original
        render()
    }

    var isOriginal: Bool { config == .original }

    func render() {
        task?.cancel()
        let config = self.config
        let catalog = OutfitCatalog.shared
        guard let page = catalog.page(outfit: config.outfit, female: config.female) else { return }
        if config == .original {
            // El original va dentro del .app: no hace falta descargarlo.
            preview = nil; sprite = nil; loading = false; failed = false
            onChange(nil)
            return
        }
        loading = true
        failed = false
        task = Task { @MainActor in
            do {
                let outfit = try await OutfitSheets.load(page)
                var mount: Data?
                if config.mount > 0, catalog.canMount(config.outfit), let mountPage = catalog.page(mount: config.mount) {
                    mount = try await OutfitSheets.load(mountPage)
                }
                guard !Task.isCancelled else { return }
                let image = OutfitRenderer.render(config, outfit: outfit, mount: mount)
                preview = image
                sprite = image.map(OutfitRenderer.cropped)
                loading = false
                onChange(config == .original ? nil : sprite)
            } catch {
                guard !Task.isCancelled else { return }
                loading = false
                failed = true
            }
        }
    }
}

/// Ícono de la app (Dock y ⌘⇥) y de la barra de menús con el personaje elegido.
@MainActor
enum OutfitIcons {
    static func apply(_ sprite: CGImage?, statusItem: NSStatusItem?) {
        // nil: el ícono del .app (el personaje original).
        NSApp.applicationIconImage = sprite.map { OutfitRenderer.icon($0, size: 512) }
        if let sprite {
            let image = OutfitRenderer.icon(sprite, size: 44)
            image.size = NSSize(width: 22, height: 22)
            statusItem?.button?.image = image
        } else {
            statusItem?.button?.image = characterImage.map { original in
                let image = NSImage(size: NSSize(width: 22, height: 22), flipped: false) { rect in
                    NSGraphicsContext.current?.imageInterpolation = .none
                    let s = min(rect.width / original.size.width, rect.height / original.size.height)
                    let w = original.size.width * s, h = original.size.height * s
                    original.draw(in: NSRect(x: (rect.width - w) / 2, y: (rect.height - h) / 2, width: w, height: h))
                    return true
                }
                return image
            } ?? NSImage(systemSymbolName: "timer", accessibilityDescription: "Alfa Hub")
        }
    }
}

/// Sprite en pixel-art nítido.
struct PixelSprite: View {
    let image: CGImage
    let scale: CGFloat

    var body: some View {
        Image(decorative: image, scale: 1)
            .interpolation(.none)
            .resizable()
            .frame(width: CGFloat(image.width) * scale, height: CGFloat(image.height) * scale)
    }
}

// MARK: - Página

struct OutfitPage: View {
    @ObservedObject var outfit: OutfitManager
    @State private var search = ""
    @State private var part = 0          // 0 cabeza, 1 primario, 2 secundario, 3 detalle
    @State private var mountColors = false
    private let catalog = OutfitCatalog.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            PageHeader(title: HubPage.outfit.title, subtitle: HubPage.outfit.subtitle) {
                SmallButton(title: "Personaje original", icon: "arrow.uturn.backward") { outfit.config = .original }
                    .disabled(outfit.isOriginal)
            }
            HStack(alignment: .top, spacing: 16) {
                outfitList
                ScrollView { editor.padding(.bottom, 16) }
            }
        }
        .padding(.horizontal, 28)
        .padding(.top, 22)
    }

    private var outfitList: some View {
        VStack(spacing: 6) {
            TextField("Buscar outfit", text: $search)
                .onSubmit {
                    // Enter elige el que se llama exactamente así (o el primero de la lista).
                    let found = catalog.sortedOutfits.filter { catalog.title($0).localizedCaseInsensitiveContains(search) }
                    if let id = found.first(where: { catalog.title($0).caseInsensitiveCompare(search) == .orderedSame }) ?? found.first { select(id) }
                }
            ScrollViewReader { proxy in
                List(catalog.sortedOutfits.filter { search.isEmpty || catalog.title($0).localizedCaseInsensitiveContains(search) }, id: \.self) { id in
                    Button { select(id) } label: {
                        HStack {
                            Text(catalog.title(id)).lineLimit(1)
                            Spacer()
                            if id == outfit.config.outfit { Image(systemName: "checkmark").foregroundStyle(Color.accentBlue) }
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .id(id)
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
                .onAppear { proxy.scrollTo(outfit.config.outfit, anchor: .center) }
            }
        }
        .padding(8)
        .frame(width: 210)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color.rowBackground))
    }

    private func select(_ id: Int) {
        var c = outfit.config
        c.outfit = id
        if !catalog.hasAddons(id) { c.addon1 = false; c.addon2 = false }
        if catalog.onlyOneAddon(id) && c.addon1 && c.addon2 { c.addon2 = false }
        if !catalog.canBeFemale(id) { c.female = false }
        if !catalog.canMount(id) { c.mount = 0 }
        outfit.config = c
    }

    private var editor: some View {
        let c = outfit.config
        return VStack(alignment: .leading, spacing: 14) {
            // Vista previa
            HStack(alignment: .center, spacing: 18) {
                ZStack {
                    RoundedRectangle(cornerRadius: 12).fill(Color(red: 0.2, green: 0.24, blue: 0.16))
                    if let sprite = outfit.sprite {
                        // Centrado (el sprite de Tibia está abajo a la derecha de su celda de 64 × 64).
                        PixelSprite(image: sprite, scale: min(3, 190 / CGFloat(max(sprite.width, sprite.height))))
                    } else if outfit.isOriginal, let characterImage {
                        Image(nsImage: characterImage).interpolation(.none).resizable()
                            .frame(width: characterImage.size.width * 3, height: characterImage.size.height * 3)
                    }
                    if outfit.loading { ProgressView().controlSize(.small) }
                    if outfit.failed {
                        Text("No se pudo descargar de TibiaWiki.\nRevisa la conexión.")
                            .font(.system(size: 11)).multilineTextAlignment(.center).foregroundStyle(Color.amber)
                    }
                }
                .frame(width: 210, height: 210)
                VStack(alignment: .leading, spacing: 10) {
                    Text(catalog.title(c.outfit, female: c.female)).font(.system(size: 18, weight: .bold))
                    if c.mount > 0 { Text("Sobre \(catalog.mountTitle(c.mount))").font(.system(size: 12)).foregroundStyle(Color.muted) }
                    HStack(spacing: 6) {
                        CircleButton(icon: "arrow.counterclockwise", fill: Color.white.opacity(0.1), help: "Girar") {
                            outfit.config.facing = (c.facing + 3) % 4
                        }
                        CircleButton(icon: "arrow.clockwise", fill: Color.white.opacity(0.1), help: "Girar") {
                            outfit.config.facing = (c.facing + 1) % 4
                        }
                        Text(["Norte", "Este", "Sur", "Oeste"][c.facing]).font(.system(size: 12)).foregroundStyle(Color.muted)
                    }
                    Toggle("Mujer", isOn: $outfit.config.female).disabled(!catalog.canBeFemale(c.outfit))
                    Toggle("Addon 1", isOn: Binding(get: { c.addon1 }, set: { on in
                        outfit.config.addon1 = on
                        if on && catalog.onlyOneAddon(c.outfit) { outfit.config.addon2 = false }
                    })).disabled(!catalog.hasAddons(c.outfit))
                    Toggle("Addon 2", isOn: Binding(get: { c.addon2 }, set: { on in
                        outfit.config.addon2 = on
                        if on && catalog.onlyOneAddon(c.outfit) { outfit.config.addon1 = false }
                    })).disabled(!catalog.hasAddons(c.outfit))
                }
                .toggleStyle(.checkbox)
            }

            // Colores
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("Colores").font(.system(size: 13, weight: .semibold))
                    Spacer()
                    if catalog.mountColorable(c.mount) {
                        Picker("", selection: $mountColors) {
                            Text("Outfit").tag(false)
                            Text("Montura").tag(true)
                        }
                        .pickerStyle(.segmented).labelsHidden().fixedSize()
                    }
                }
                Picker("", selection: $part) {
                    Text("Cabeza").tag(0)
                    Text("Primario").tag(1)
                    Text("Secundario").tag(2)
                    Text("Detalle").tag(3)
                }
                .pickerStyle(.segmented).labelsHidden()
                ColorPalette(selected: colorBinding)
            }
            .padding(12)
            .background(RoundedRectangle(cornerRadius: 10).fill(Color.rowBackground))

            // Montura
            HStack {
                Text("Montura").font(.system(size: 13, weight: .semibold))
                Spacer()
                Picker("", selection: $outfit.config.mount) {
                    ForEach(catalog.sortedMounts, id: \.self) { Text(catalog.mountTitle($0)).tag($0) }
                }
                .labelsHidden()
                .frame(width: 240)
                .disabled(!catalog.canMount(c.outfit))
            }
            .padding(12)
            .background(RoundedRectangle(cornerRadius: 10).fill(Color.rowBackground))

            Text("Tu personaje se usa como ícono de Alfa Hub (en el Dock), en la barra lateral y en la barra de menús. Los sprites vienen del Outfiter de TibiaWiki: se descargan la primera vez que eliges cada outfit o montura y luego funcionan sin conexión. El ícono del Finder sigue siendo el original.")
                .font(.system(size: 11)).foregroundStyle(Color.muted)
        }
        .onChange(of: c.mount) { if !catalog.mountColorable(outfit.config.mount) { mountColors = false } }
    }

    private var colorBinding: Binding<Int> {
        let keys: [WritableKeyPath<OutfitConfig, Int>] = mountColors ? [\.mc1, \.mc2, \.mc3, \.mc4] : [\.c1, \.c2, \.c3, \.c4]
        let key = keys[part]
        return Binding(get: { outfit.config[keyPath: key] }, set: { outfit.config[keyPath: key] = $0 })
    }
}

/// Los 133 colores de Tibia (7 filas × 19), como en el Outfiter.
struct ColorPalette: View {
    @Binding var selected: Int
    private let colors = OutfitCatalog.shared.colors

    var body: some View {
        let columns = 19
        VStack(spacing: 2) {
            ForEach(0..<(colors.count / columns), id: \.self) { row in
                HStack(spacing: 2) {
                    ForEach(0..<columns, id: \.self) { col in
                        let i = row * columns + col
                        let rgb = colors[i]
                        Rectangle()
                            .fill(Color(red: Double(rgb[0]) / 255, green: Double(rgb[1]) / 255, blue: Double(rgb[2]) / 255))
                            .frame(width: 17, height: 17)
                            .overlay(Rectangle().stroke(i == selected ? Color.white : Color.black.opacity(0.4), lineWidth: i == selected ? 2 : 0.5))
                            .contentShape(Rectangle())
                            .onTapGesture { selected = i }
                    }
                }
            }
        }
    }
}

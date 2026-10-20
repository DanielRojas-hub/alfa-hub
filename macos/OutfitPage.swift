// Página Outfit del hub y el personaje en uso (ícono de la app, barra lateral y barra de menús).

import AppKit
import SwiftUI

/// Un outfit guardado en "Mis outfits".
struct OutfitPreset: Codable, Identifiable, Hashable {
    var id = UUID()
    var name: String
    var config: OutfitConfig
}

/// El personaje: lo que se edita en la página Outfit (borrador) y el que está en uso (guardado). El
/// borrador solo pasa al ícono de la app, la barra lateral y la barra de menús con "Guardar cambios".
@MainActor
final class OutfitManager: ObservableObject {
    @Published var draft: OutfitConfig {
        didSet { if draft != oldValue { renderDraft() } }
    }
    @Published private(set) var saved: OutfitConfig
    /// Dibujo del borrador (vista previa) y del guardado (barra lateral, íconos); nil = el original del .app.
    @Published private(set) var preview: OutfitSprite?
    @Published private(set) var current: OutfitSprite?
    @Published private(set) var loading = false
    @Published private(set) var failed = false
    @Published private(set) var presets: [OutfitPreset] {
        didSet { UserDefaults.standard.set(try? JSONEncoder().encode(presets), forKey: "outfitPresets") }
    }
    /// Miniaturas de "Mis outfits".
    @Published private(set) var thumbnails: [OutfitConfig: CGImage] = [:]
    /// Se llama con el sprite quieto del personaje guardado (nil = el personaje original del .app).
    var onChange: (CGImage?) -> Void = { _ in }
    private var draftTask: Task<Void, Never>?
    private var savedTask: Task<Void, Never>?

    init() {
        let saved = UserDefaults.standard.data(forKey: "outfit").flatMap { try? JSONDecoder().decode(OutfitConfig.self, from: $0) } ?? .original
        self.saved = saved
        draft = saved
        presets = UserDefaults.standard.data(forKey: "outfitPresets").flatMap { try? JSONDecoder().decode([OutfitPreset].self, from: $0) } ?? []
        renderDraft()
        renderSaved()
    }

    var isOriginal: Bool { saved == .original }
    var hasChanges: Bool { draft != saved }

    /// "Guardar cambios": el borrador pasa a ser el personaje.
    func save() {
        saved = draft
        UserDefaults.standard.set(try? JSONEncoder().encode(saved), forKey: "outfit")
        if let preview, !loading, !failed {
            current = saved == .original ? nil : preview
            onChange(current?.still)
        } else {
            renderSaved()
        }
    }

    func discard() { draft = saved }

    func addPreset(named name: String) {
        let title = name.trimmingCharacters(in: .whitespaces)
        presets.append(OutfitPreset(name: title.isEmpty ? OutfitCatalog.shared.title(draft.outfit, female: draft.female) : title, config: draft))
    }

    func removePreset(_ preset: OutfitPreset) { presets.removeAll { $0.id == preset.id } }

    private func renderDraft() {
        draftTask?.cancel()
        let config = draft
        if config == .original {
            // El original va dentro del .app: no hace falta descargarlo.
            preview = nil; loading = false; failed = false
            return
        }
        loading = true
        failed = false
        draftTask = Task { @MainActor in
            do {
                let sprite = try await OutfitRenderer.render(config)
                guard !Task.isCancelled else { return }
                preview = sprite
                loading = false
                failed = sprite == nil
            } catch {
                guard !Task.isCancelled else { return }
                loading = false
                failed = true
            }
        }
    }

    func renderSaved() {
        savedTask?.cancel()
        let config = saved
        if config == .original {
            current = nil
            onChange(nil)
            return
        }
        savedTask = Task { @MainActor in
            guard let sprite = try? await OutfitRenderer.render(config), !Task.isCancelled else { return }
            current = sprite
            onChange(sprite.still)
        }
    }

    /// Dibuja (una vez) la miniatura de un outfit de "Mis outfits".
    func loadThumbnail(_ config: OutfitConfig) async {
        var still = config
        still.animate = false
        guard thumbnails[still] == nil, still != .original,
              let sprite = try? await OutfitRenderer.render(still) else { return }
        thumbnails[still] = sprite.still
    }

    func thumbnail(_ config: OutfitConfig) -> CGImage? {
        var still = config
        still.animate = false
        return thumbnails[still]
    }
}

/// Ícono de la app (Dock y ⌘⇥) y de la barra de menús con el personaje elegido.
@MainActor
enum OutfitIcons {
    static func apply(_ sprite: CGImage?, statusItem: NSStatusItem?) {
        // nil: el ícono del .app (el personaje original).
        NSApp.applicationIconImage = sprite.map { OutfitRenderer.icon($0, size: 512, plate: true) }
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

/// El personaje (quieto o caminando) a una escala que cabe en maxSide; sin sprite, el original del .app.
struct CharacterView: View {
    let sprite: OutfitSprite?
    let maxScale: CGFloat
    let maxSide: CGFloat

    var body: some View {
        if let sprite {
            if sprite.frames.isEmpty {
                sized(sprite.still)
            } else {
                TimelineView(.periodic(from: .now, by: sprite.frameDuration)) { context in
                    sized(sprite.frame(at: context.date))
                }
            }
        } else if let characterImage {
            let s = min(maxScale, maxSide / max(characterImage.size.width, characterImage.size.height))
            Image(nsImage: characterImage).interpolation(.none).resizable()
                .frame(width: characterImage.size.width * s, height: characterImage.size.height * s)
        }
    }

    private func sized(_ image: CGImage) -> some View {
        // Escala calculada con el primer fotograma: todos miden lo mismo.
        let base = sprite?.frames.first ?? image
        return PixelSprite(image: image, scale: min(maxScale, maxSide / CGFloat(max(base.width, base.height))))
    }
}

// MARK: - Página

struct OutfitPage: View {
    @ObservedObject var outfit: OutfitManager
    @State private var outfitSearch = ""
    @State private var mountSearch = ""
    @State private var part = 0          // 0 cabeza, 1 primario, 2 secundario, 3 detalle
    @State private var mountColors = false
    @State private var presetName = ""
    private let catalog = OutfitCatalog.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            PageHeader(title: HubPage.outfit.title, subtitle: HubPage.outfit.subtitle) {
                HStack(spacing: 8) {
                    if outfit.hasChanges {
                        SmallButton(title: "Descartar", icon: "xmark") { outfit.discard() }
                            .help("Volver al personaje guardado")
                    }
                    SaveButton(enabled: outfit.hasChanges) { outfit.save() }
                }
            }
            HStack(alignment: .top, spacing: 16) {
                VStack(spacing: 10) {
                    PickerList(prompt: "Buscar outfit", search: $outfitSearch,
                               ids: catalog.sortedOutfits.filter { outfitSearch.isEmpty || catalog.title($0).localizedCaseInsensitiveContains(outfitSearch) },
                               selected: outfit.draft.outfit, title: { catalog.title($0) }, select: select)
                    PickerList(prompt: "Buscar montura", search: $mountSearch,
                               ids: catalog.sortedMounts.dropFirst().filter { mountSearch.isEmpty || catalog.mountTitle($0).localizedCaseInsensitiveContains(mountSearch) },
                               selected: outfit.draft.mount, title: { catalog.mountTitle($0) },
                               select: selectMount)
                }
                .frame(width: 210)
                ScrollView { editor.padding(.bottom, 16) }
            }
        }
        .padding(.horizontal, 28)
        .padding(.top, 22)
    }

    private func select(_ id: Int) {
        var c = outfit.draft
        c.outfit = id
        if !catalog.hasAddons(id) { c.addon1 = false; c.addon2 = false }
        if catalog.onlyOneAddon(id) && c.addon1 && c.addon2 { c.addon2 = false }
        if !catalog.canBeFemale(id) { c.female = false }
        if !catalog.canMount(id) { c.mounted = false }
        outfit.draft = c
    }

    /// Elegir una montura de la lista la monta (si el outfit puede ir montado).
    private func selectMount(_ id: Int) {
        var c = outfit.draft
        c.mount = id
        if catalog.canMount(c.outfit) { c.mounted = true }
        outfit.draft = c
    }

    private var editor: some View {
        let c = outfit.draft
        return VStack(alignment: .leading, spacing: 14) {
            // Vista previa
            HStack(alignment: .top, spacing: 18) {
                ZStack {
                    RoundedRectangle(cornerRadius: 12).fill(Color(red: 0.2, green: 0.24, blue: 0.16))
                    // Centrado (el sprite de Tibia está abajo a la derecha de su celda de 64 × 64).
                    if outfit.preview != nil || c == .original {
                        CharacterView(sprite: outfit.preview, maxScale: 3, maxSide: 170)
                    }
                    if outfit.loading { ProgressView().controlSize(.small) }
                    if outfit.failed {
                        Text("No se pudo descargar de TibiaWiki.\nRevisa la conexión.")
                            .font(.system(size: 11)).multilineTextAlignment(.center).foregroundStyle(Color.amber)
                    }
                }
                .frame(width: 190, height: 190)
                VStack(alignment: .leading, spacing: 10) {
                    Text(catalog.title(c.outfit, female: c.female)).font(.system(size: 18, weight: .bold)).lineLimit(1)
                    if c.ridden > 0 {
                        Text("Sobre \(catalog.mountTitle(c.ridden))").font(.system(size: 12)).foregroundStyle(Color.muted).lineLimit(1)
                    }
                    HStack(spacing: 6) {
                        CircleButton(icon: "arrow.counterclockwise", fill: Color.white.opacity(0.1), help: "Girar") {
                            outfit.draft.facing = (c.facing + 3) % 4
                        }
                        CircleButton(icon: "arrow.clockwise", fill: Color.white.opacity(0.1), help: "Girar") {
                            outfit.draft.facing = (c.facing + 1) % 4
                        }
                        Text(["Norte", "Este", "Sur", "Oeste"][c.facing]).font(.system(size: 12)).foregroundStyle(Color.muted)
                    }
                    Picker("", selection: $outfit.draft.female) {
                        Text("Hombre").tag(false)
                        Text("Mujer").tag(true)
                    }
                    .pickerStyle(.segmented).labelsHidden().fixedSize()
                    .disabled(!catalog.canBeFemale(c.outfit))
                    .help(catalog.canBeFemale(c.outfit) ? "" : "Este outfit no tiene versión de mujer")
                    Toggle("Addon 1", isOn: Binding(get: { c.addon1 }, set: { on in
                        outfit.draft.addon1 = on
                        if on && catalog.onlyOneAddon(c.outfit) { outfit.draft.addon2 = false }
                    })).disabled(!catalog.hasAddons(c.outfit))
                    Toggle("Addon 2", isOn: Binding(get: { c.addon2 }, set: { on in
                        outfit.draft.addon2 = on
                        if on && catalog.onlyOneAddon(c.outfit) { outfit.draft.addon1 = false }
                    })).disabled(!catalog.hasAddons(c.outfit))
                    Toggle("Montura", isOn: Binding(get: { c.ridden > 0 }, set: { on in
                        // Sin montura elegida todavía: la primera de la lista.
                        if on && outfit.draft.mount == 0 { outfit.draft.mount = catalog.sortedMounts.dropFirst().first ?? 0 }
                        outfit.draft.mounted = on
                    }))
                    .disabled(!catalog.canMount(c.outfit))
                    .help(catalog.canMount(c.outfit) ? "Montar en la montura elegida en la lista" : "Este outfit no puede ir montado")
                    Toggle("Animar caminata", isOn: $outfit.draft.animate)
                }
                .toggleStyle(.checkbox)
            }
            .onChange(of: c.ridden) { if !catalog.mountColorable(outfit.draft.ridden) { mountColors = false } }

            // Colores
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("Colores").font(.system(size: 13, weight: .semibold))
                    Spacer()
                    Picker("", selection: $mountColors) {
                        Text("Outfit").tag(false)
                        Text("Montura").tag(true)
                    }
                    .pickerStyle(.segmented).labelsHidden().fixedSize()
                    .disabled(!catalog.mountColorable(c.ridden))
                    .help(catalog.mountColorable(c.ridden) ? "Qué colores cambia la paleta"
                          : "Solo se pueden colorear algunas monturas (\(colorableMounts)), y hay que ir montado")
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

            presetsCard

            Text("Pulsa Guardar cambios para que tu personaje pase al ícono de Alfa Hub (en el Dock), a la barra lateral y a la barra de menús. Los sprites vienen del Outfiter de TibiaWiki: se descargan la primera vez que eliges cada outfit o montura y luego funcionan sin conexión. El ícono del Finder sigue siendo el original.")
                .font(.system(size: 11)).foregroundStyle(Color.muted)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var colorableMounts: String {
        catalog.mountColor.map { $0.replacingOccurrences(of: "_", with: " ") }.sorted().joined(separator: ", ")
    }

    private var colorBinding: Binding<Int> {
        let keys: [WritableKeyPath<OutfitConfig, Int>] = mountColors ? [\.mc1, \.mc2, \.mc3, \.mc4] : [\.c1, \.c2, \.c3, \.c4]
        let key = keys[part]
        return Binding(get: { outfit.draft[keyPath: key] }, set: { outfit.draft[keyPath: key] = $0 })
    }

    // MARK: Mis outfits

    private var presetsCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                Text("Mis outfits").font(.system(size: 13, weight: .semibold))
                Spacer()
                TextField("Nombre", text: $presetName)
                    .frame(width: 130)
                    .onSubmit(addPreset)
                SmallButton(title: "Guardar aquí", icon: "plus", action: addPreset)
                    .help("Guarda el outfit de la vista previa en Mis outfits")
            }
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 88), spacing: 8)], spacing: 8) {
                PresetTile(name: "Original", image: nil, selected: outfit.draft == .original, original: true) { outfit.draft = .original }
                    .help("El personaje original de Alfa Hub")
                ForEach(outfit.presets) { preset in
                    PresetTile(name: preset.name, image: outfit.thumbnail(preset.config), selected: outfit.draft == preset.config,
                               original: preset.config == .original, remove: { outfit.removePreset(preset) }) { outfit.draft = preset.config }
                        .help(catalog.summary(preset.config))
                        .task(id: preset.config) { await outfit.loadThumbnail(preset.config) }
                }
            }
            if outfit.presets.isEmpty {
                Text("Guarda aquí tus combinaciones para volver a ellas con un clic.")
                    .font(.system(size: 11)).foregroundStyle(Color.muted)
            }
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color.rowBackground))
    }

    private func addPreset() {
        outfit.addPreset(named: presetName)
        presetName = ""
    }
}

/// Botón principal "Guardar cambios".
struct SaveButton: View {
    let enabled: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Label("Guardar cambios", systemImage: "checkmark")
                .font(.system(size: 13, weight: .bold))
                .padding(.horizontal, 14).padding(.vertical, 8)
                .background(Capsule().fill(enabled ? Color.accentBlue : Color.white.opacity(0.08)))
                .foregroundStyle(enabled ? .white : Color.muted)
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
        .help("Usa este personaje en el ícono de la app, la barra lateral y la barra de menús")
    }
}

/// Lista con buscador (outfits o monturas), siempre desplegada.
struct PickerList: View {
    let prompt: String
    @Binding var search: String
    let ids: [Int]
    let selected: Int
    let title: (Int) -> String
    let select: (Int) -> Void

    var body: some View {
        VStack(spacing: 6) {
            TextField(prompt, text: $search)
                .onSubmit {
                    // Enter elige el que se llama exactamente así (o el primero de la lista).
                    if let id = ids.first(where: { title($0).caseInsensitiveCompare(search) == .orderedSame }) ?? ids.first { select(id) }
                }
            ScrollViewReader { proxy in
                List(ids, id: \.self) { id in
                    Button { select(id) } label: {
                        HStack {
                            Text(title(id)).lineLimit(1)
                            Spacer()
                            if id == selected { Image(systemName: "checkmark").foregroundStyle(Color.accentBlue) }
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .id(id)
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
                .onAppear { proxy.scrollTo(selected, anchor: .center) }
            }
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color.rowBackground))
    }
}

/// Una casilla de "Mis outfits": miniatura y nombre; clic para cargarlo en la vista previa.
struct PresetTile: View {
    let name: String
    let image: CGImage?
    let selected: Bool
    var original = false
    var remove: (() -> Void)?
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            VStack(spacing: 4) {
                ZStack {
                    RoundedRectangle(cornerRadius: 8).fill(Color(red: 0.2, green: 0.24, blue: 0.16))
                    if let image {
                        PixelSprite(image: image, scale: min(1.5, 56 / CGFloat(max(image.width, image.height))))
                    } else if original, let characterImage {
                        let s = min(1.5, 56 / max(characterImage.size.width, characterImage.size.height))
                        Image(nsImage: characterImage).interpolation(.none).resizable()
                            .frame(width: characterImage.size.width * s, height: characterImage.size.height * s)
                    } else {
                        ProgressView().controlSize(.small)
                    }
                }
                .frame(height: 64)
                Text(name).font(.system(size: 11)).lineLimit(1)
            }
            .padding(6)
            .background(RoundedRectangle(cornerRadius: 10).fill(selected ? Color.accentBlue.opacity(0.25) : Color.white.opacity(0.03)))
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(selected ? Color.accentBlue : Color.clear, lineWidth: 1.5))
            .overlay(alignment: .topTrailing) {
                if let remove, hovering {
                    Button(action: remove) {
                        Image(systemName: "xmark.circle.fill").font(.system(size: 14))
                            .foregroundStyle(.white, Color.black.opacity(0.6))
                    }
                    .buttonStyle(.plain)
                    .padding(3)
                    .help("Quitar de Mis outfits")
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .contextMenu {
            if let remove { Button("Quitar de Mis outfits", role: .destructive, action: remove) }
        }
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

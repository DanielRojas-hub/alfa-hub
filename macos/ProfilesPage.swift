// Página Perfiles: la lista de perfiles, cada perfil con sus chars y cada char con su outfit y tres
// pestañas: General (lo que dice tibia.com, al estilo de guildstats.eu), Inventario (cuadrículas como en
// Tibia: lo equipado y lo guardado en mochila, depot, casa…) y Bestiary (con recomendaciones single y
// group, como el Bestiary Tracker de tibiaroute.com).

import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct ProfilesPage: View {
    @ObservedObject var store: ProfileStore
    @ObservedObject var outfit: OutfitManager

    var body: some View {
        Group {
            if let profile = store.profiles.first(where: { $0.id == store.openProfile }) {
                if let char = profile.chars.first(where: { $0.id == store.openChar }) {
                    CharView(store: store, outfit: outfit, profileID: profile.id, charID: char.id)
                        .id(char.id)  // otro char: su propio estado (búsqueda en tibia.com…)
                } else {
                    ProfileView(store: store, outfit: outfit, profileID: profile.id)
                }
            } else {
                ProfileList(store: store, outfit: outfit)
            }
        }
        .overlay(alignment: .bottom) {
            if let notice = store.notice {
                HStack(spacing: 10) {
                    Image(systemName: "checkmark.circle.fill").foregroundStyle(Color.green)
                    Text(notice).font(.system(size: 12, weight: .medium)).fixedSize(horizontal: false, vertical: true)
                    Button { store.notice = nil } label: { Image(systemName: "xmark").font(.system(size: 10, weight: .bold)) }
                        .buttonStyle(.plain).foregroundStyle(Color.muted)
                }
                .padding(.horizontal, 14).padding(.vertical, 10)
                .background(RoundedRectangle(cornerRadius: 10).fill(Color(white: 0.2)).shadow(radius: 8))
                .padding(.horizontal, 28).padding(.bottom, 16)
                .task(id: notice) {
                    try? await Task.sleep(for: .seconds(6))
                    if store.notice == notice { store.notice = nil }
                }
            }
        }
    }
}

// MARK: - Acciones (compartir, importar, confirmar)

@MainActor
enum ProfileActions {
    static let fileType = UTType(filenameExtension: ProfileShare.fileExtension) ?? .json

    static func copyCode(_ profile: Profile, store: ProfileStore) {
        guard let code = try? ProfileShare.code(profile) else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(code, forType: .string)
        store.notice = "Código de “\(profile.name)” copiado. Pégalo en un chat: quien lo reciba lo abre en Perfiles → Importar."
    }

    static func exportFile(_ profile: Profile, store: ProfileStore) {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [fileType]
        panel.nameFieldStringValue = "\(profile.name).\(ProfileShare.fileExtension)"
        guard panel.runModal() == .OK, let url = panel.url, let data = try? ProfileShare.json(profile) else { return }
        do {
            try data.write(to: url, options: .atomic)
            store.notice = "Guardado “\(url.lastPathComponent)”. Envía el archivo a quien quieras."
        } catch {
            store.notice = "No se pudo guardar: \(error.localizedDescription)"
        }
    }

    static func importFile(store: ProfileStore) {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [fileType, .json, .plainText]
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url, let data = try? Data(contentsOf: url) else { return }
        do {
            finishImport(try ProfileShare.decode(data), store: store)
        } catch {
            alert("No se pudo importar “\(url.lastPathComponent)”", detail: error.localizedDescription)
        }
    }

    static func finishImport(_ shared: Profile, store: ProfileStore) {
        let (profile, updated) = store.importProfile(shared)
        store.openProfile = profile.id
        store.openChar = nil
        store.notice = updated ? "Se actualizó “\(profile.name)” con la versión nueva." : "Perfil “\(profile.name)” importado."
    }

    static func confirm(_ message: String, detail: String, button: String) -> Bool {
        let alert = NSAlert()
        alert.messageText = message
        alert.informativeText = detail
        alert.alertStyle = .warning
        alert.addButton(withTitle: button).hasDestructiveAction = true
        alert.addButton(withTitle: "Cancelar")
        return alert.runModal() == .alertFirstButtonReturn
    }

    static func alert(_ message: String, detail: String) {
        let alert = NSAlert()
        alert.messageText = message
        alert.informativeText = detail
        alert.runModal()
    }

    static func shareItems(_ profile: Profile, store: ProfileStore) -> [MoreButton.Item] {
        [.init(title: "Copiar código para compartir") { copyCode(profile, store: store) },
         .init(title: "Guardar archivo .\(ProfileShare.fileExtension)…") { exportFile(profile, store: store) }]
    }
}

// MARK: - Piezas comunes

/// Botón azul en cápsula (como "Añadir timer").
struct AccentButton: View {
    let title: String
    let icon: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Label(title, systemImage: icon)
                .font(.system(size: 13, weight: .bold))
                .padding(.horizontal, 14).padding(.vertical, 8)
                .background(Capsule().fill(Color.accentBlue))
                .foregroundStyle(.white)
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
    }
}

struct BackLink: View {
    let title: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Label(title, systemImage: "chevron.left")
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(Color.accentBlue)
                .lineLimit(1)
        }
        .buttonStyle(.plain)
    }
}

/// Imagen de TibiaWiki (criatura o item), descargada al aparecer.
struct WikiSprite: View {
    let name: String
    var size: CGFloat = 32
    @State private var data: Data?

    var body: some View {
        AnimatedImage(data: data)
            .frame(width: size, height: size)
            .task(id: name) {
                data = nil
                data = await WikiImages.shared.image(name)
            }
    }
}

/// El personaje de un char: uno de "Mis outfits" o, sin elegir, el de Alfa Hub.
struct CharAvatar: View {
    @ObservedObject var outfit: OutfitManager
    let config: OutfitConfig?
    var side: CGFloat = 64

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 10).fill(Color(red: 0.2, green: 0.24, blue: 0.16))
            if let config, config != .original {
                if let image = outfit.thumbnail(config) {
                    PixelSprite(image: image, scale: min(2, (side - 6) / CGFloat(max(image.width, image.height))))
                } else {
                    ProgressView().controlSize(.small)
                }
            } else {
                CharacterView(sprite: nil, maxScale: 2, maxSide: side - 6)
            }
        }
        .frame(width: side, height: side)
        .task(id: config) {
            if let config { await outfit.loadThumbnail(config) }
        }
    }
}

struct PlaceChip: View {
    let piece: OwnedItem

    var body: some View {
        Label(piece.placeText, systemImage: piece.place.icon)
            .font(.system(size: 11, weight: .semibold))
            .lineLimit(1)
            .padding(.horizontal, 8).padding(.vertical, 3)
            .background(Capsule().fill(piece.place.tint.opacity(0.15)))
            .foregroundStyle(piece.place.tint)
    }
}

struct EmptyCard: View {
    let icon: String
    let title: String
    let text: String

    var body: some View {
        VStack(spacing: 8) {
            Image(systemName: icon).font(.system(size: 26)).foregroundStyle(Color.muted)
            Text(title).font(.system(size: 14, weight: .semibold))
            Text(text).font(.system(size: 12)).foregroundStyle(Color.muted).multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(28)
        .frame(maxWidth: .infinity)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }
}

// MARK: - Lista de perfiles

struct ProfileList: View {
    @ObservedObject var store: ProfileStore
    @ObservedObject var outfit: OutfitManager
    @State private var pasting = false

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            PageHeader(title: HubPage.profiles.title, subtitle: HubPage.profiles.subtitle) {
                HStack(spacing: 8) {
                    SmallButton(title: "Importar", icon: "square.and.arrow.down") {
                        MenuPresenter.show([
                            .init(title: "Pegar código…") { pasting = true },
                            .init(title: "Abrir archivo .\(ProfileShare.fileExtension)…") { ProfileActions.importFile(store: store) },
                        ])
                    }
                    .help("Añade un perfil que te compartieron")
                    AccentButton(title: "Nuevo perfil", icon: "plus") {
                        let profile = store.addProfile()
                        store.openProfile = profile.id
                    }
                }
            }
            ScrollView {
                VStack(spacing: 8) {
                    if store.profiles.isEmpty {
                        EmptyCard(icon: "person.2", title: "Todavía no hay perfiles",
                                  text: "Un perfil junta tus chars: sus datos de tibia.com, qué items tiene cada uno y dónde están, y cómo va su Bestiary. Puedes compartirlo con un código o un archivo.")
                    }
                    ForEach(store.profiles) { profile in
                        ProfileRow(store: store, outfit: outfit, profile: profile)
                    }
                }
                .padding(.bottom, 16)
            }
        }
        .padding(.horizontal, 28)
        .padding(.top, 22)
        .sheet(isPresented: $pasting) {
            PasteCodeSheet { profile in
                pasting = false
                if let profile { ProfileActions.finishImport(profile, store: store) }
            }
        }
    }
}

struct ProfileRow: View {
    @ObservedObject var store: ProfileStore
    @ObservedObject var outfit: OutfitManager
    let profile: Profile

    var body: some View {
        HStack(spacing: 14) {
            HStack(spacing: -14) {
                ForEach(profile.chars.prefix(3)) { char in
                    CharAvatar(outfit: outfit, config: char.outfit, side: 44)
                        .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.windowBackground, lineWidth: 2))
                }
                if profile.chars.isEmpty { CharAvatar(outfit: outfit, config: nil, side: 44) }
            }
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 8) {
                    Text(profile.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                    if profile.importedAt != nil {
                        Text("COMPARTIDO").font(.system(size: 9, weight: .bold)).tracking(1)
                            .padding(.horizontal, 6).padding(.vertical, 2)
                            .background(Capsule().fill(Color.accentBlue.opacity(0.2)))
                            .foregroundStyle(Color.accentBlue)
                    }
                }
                Text(([profile.summary] + profile.chars.prefix(4).map(\.name)).joined(separator: " · "))
                    .font(.system(size: 11)).foregroundStyle(Color.muted).lineLimit(1)
            }
            Spacer(minLength: 8)
            SmallButton(title: "Compartir", icon: "square.and.arrow.up") {
                MenuPresenter.show(ProfileActions.shareItems(profile, store: store))
            }
            MoreButton {
                [.init(title: "Duplicar") { store.duplicate(profile) },
                 .init(title: "Eliminar", destructive: true) {
                     if ProfileActions.confirm("¿Eliminar “\(profile.name)”?", detail: "Se borran sus chars, items y Bestiary. No se puede deshacer.", button: "Eliminar") {
                         store.remove(profile)
                     }
                 }]
            }
        }
        .padding(.vertical, 10)
        .padding(.horizontal, 12)
        .background(
            RoundedRectangle(cornerRadius: 10).fill(Color.rowBackground)
                .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.white.opacity(0.06)))
        )
        .contentShape(Rectangle())
        .onTapGesture(perform: open)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Abrir \(profile.name)")
        .accessibilityAction(named: "Abrir", open)
    }

    private func open() {
        store.openProfile = profile.id
        store.openChar = nil
    }
}

struct PasteCodeSheet: View {
    let done: (Profile?) -> Void
    @State private var text = ""
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Importar perfil").font(.system(size: 18, weight: .bold))
            Text("Pega el código que te compartieron (empieza por \(ProfileShare.prefix)). Si ya tenías ese perfil, se actualiza.")
                .font(.system(size: 12)).foregroundStyle(Color.muted).fixedSize(horizontal: false, vertical: true)
            TextEditor(text: $text)
                .font(.system(size: 11, design: .monospaced))
                .scrollContentBackground(.hidden)
                .padding(6)
                .frame(height: 140)
                .background(RoundedRectangle(cornerRadius: 8).fill(Color.white.opacity(0.06)))
            if let error {
                Text(error).font(.system(size: 12)).foregroundStyle(Color.red)
            }
            HStack {
                Spacer()
                Button("Cancelar") { done(nil) }.keyboardShortcut(.cancelAction)
                Button("Importar") {
                    do { done(try ProfileShare.decode(Data(text.utf8))) } catch { self.error = error.localizedDescription }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
        }
        .padding(20)
        .frame(width: 460)
        .onAppear {
            if let clip = NSPasteboard.general.string(forType: .string), clip.hasPrefix(ProfileShare.prefix) { text = clip }
        }
    }
}

// MARK: - Un perfil

struct ProfileView: View {
    @ObservedObject var store: ProfileStore
    @ObservedObject var outfit: OutfitManager
    let profileID: UUID

    var body: some View {
        let profile = store.binding(profileID)
        VStack(alignment: .leading, spacing: 14) {
            BackLink(title: "Volver a Perfiles") { store.openProfile = nil }
            HStack(spacing: 10) {
                TextField("Nombre del perfil", text: profile.name)
                    .textFieldStyle(.plain)
                    .font(.system(size: 24, weight: .bold))
                SmallButton(title: "Compartir", icon: "square.and.arrow.up") {
                    MenuPresenter.show(ProfileActions.shareItems(profile.wrappedValue, store: store))
                }
                AccentButton(title: "Añadir char", icon: "plus") {
                    let char = GameChar()
                    profile.wrappedValue.chars.append(char)
                    store.openChar = char.id
                }
            }
            if let date = profile.wrappedValue.importedAt {
                Label("Perfil compartido, importado el \(date.formatted(.dateTime.day().month(.wide).hour().minute())). Si te pasan una versión nueva, impórtala y se actualiza esta copia.",
                      systemImage: "info.circle")
                    .font(.system(size: 12)).foregroundStyle(Color.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
            ScrollView {
                if profile.wrappedValue.chars.isEmpty {
                    EmptyCard(icon: "person.crop.circle.badge.plus", title: "Sin chars",
                              text: "Añade los chars de este perfil para guardar sus items y su Bestiary.")
                }
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 290), spacing: 12)], spacing: 12) {
                    ForEach(profile.wrappedValue.chars) { char in
                        CharCard(outfit: outfit, char: char) {
                            store.openChar = char.id
                        } duplicate: {
                            var copy = char
                            copy.id = UUID()
                            copy.name += " (copia)"
                            profile.wrappedValue.chars.append(copy)
                        } remove: {
                            if ProfileActions.confirm("¿Eliminar “\(char.name)”?", detail: "Se borran sus items y su Bestiary.", button: "Eliminar") {
                                profile.wrappedValue.chars.removeAll { $0.id == char.id }
                            }
                        }
                    }
                }
                .padding(.bottom, 16)
            }
        }
        .padding(.horizontal, 28)
        .padding(.top, 18)
    }
}

struct CharCard: View {
    @ObservedObject var outfit: OutfitManager
    let char: GameChar
    let open: () -> Void
    let duplicate: () -> Void
    let remove: () -> Void

    var body: some View {
        let done = char.completedCount
        let total = BestiaryCatalog.creatures.count
        HStack(alignment: .top, spacing: 12) {
            CharAvatar(outfit: outfit, config: char.outfit, side: 64)
            VStack(alignment: .leading, spacing: 4) {
                Text(char.name.isEmpty ? "Sin nombre" : char.name).font(.system(size: 15, weight: .bold)).lineLimit(1)
                Text(char.summary).font(.system(size: 11)).foregroundStyle(Color.muted).lineLimit(1)
                Text("\(char.items.count) \(char.items.count == 1 ? "item" : "items") · Bestiary \(done)/\(total)")
                    .font(.system(size: 11, weight: .medium)).foregroundStyle(Color.white.opacity(0.75))
                    .padding(.top, 4)
                ProgressBar(value: total == 0 ? 0 : Double(done) / Double(total), color: .green)
            }
            MoreButton {
                [.init(title: "Duplicar", action: duplicate), .init(title: "Eliminar", destructive: true, action: remove)]
            }
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
        .contentShape(Rectangle())
        .onTapGesture(perform: open)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Abrir \(char.name)")
        .accessibilityAction(named: "Abrir", open)
    }
}


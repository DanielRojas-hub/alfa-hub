// Un char de un perfil: cabecera (outfit, nombre, sincronización con tibia.com), barra de pestañas
// (General, Experiencia, Bestiary, Charms, Inventario) y la pestaña General.

import AppKit
import SwiftUI

enum CharTab: Int, CaseIterable, Identifiable {
    case general, experience, bestiary, charms, inventory
    var id: Int { rawValue }
    var title: String {
        switch self {
        case .general: "General"
        case .experience: "Experiencia"
        case .bestiary: "Bestiary"
        case .charms: "Charms"
        case .inventory: "Inventario"
        }
    }
    var icon: String {
        switch self {
        case .general: "person.text.rectangle"
        case .experience: "chart.line.uptrend.xyaxis"
        case .bestiary: "pawprint"
        case .charms: "sparkles"
        case .inventory: "shippingbox"
        }
    }
}

struct CharView: View {
    @ObservedObject var store: ProfileStore
    @ObservedObject var outfit: OutfitManager
    let profileID: UUID
    let charID: UUID
    @AppStorage("charTab") private var tab = CharTab.general.rawValue
    @State private var looking = false
    @State private var lookupError: String?
    @State private var editingOutfit = false

    var body: some View {
        let profile = store.binding(profileID)
        let char = store.binding(char: charID, in: profileID)
        Group {
            if editingOutfit {
                OutfitEditor(outfit: outfit, store: store, char: char) { editingOutfit = false }
            } else {
                VStack(alignment: .leading, spacing: 0) {
                    BackLink(title: "Volver a \(profile.wrappedValue.name)") { store.openChar = nil }
                        .padding(.bottom, 10)
                    header(char)
                    TabBar(selection: $tab)
                        .padding(.top, 14)
                    Group {
                        switch CharTab(rawValue: tab) ?? .general {
                        case .general:
                            GeneralView(char: char.wrappedValue, profile: profile.wrappedValue) { other in
                                profile.wrappedValue.chars.append(GameChar(name: other.name, world: other.world))
                                store.notice = "“\(other.name)” añadido a \(profile.wrappedValue.name)."
                            }
                        case .experience: ExperienceView(char: char.wrappedValue)
                        case .bestiary: BestiaryView(char: char)
                        case .charms: CharmsView(char: char)
                        case .inventory: InventoryView(char: char, profile: profile.wrappedValue)
                        }
                    }
                    .padding(.top, 14)
                }
                .padding(.horizontal, 28)
                .padding(.top, 18)
            }
        }
        // Al escribir el nombre (cuando se deja de teclear) y al abrir el char si hace más de una hora.
        .task(id: char.wrappedValue.name) {
            let c = char.wrappedValue
            guard !c.name.trimmingCharacters(in: .whitespaces).isEmpty else { lookupError = nil; return }
            if c.isSynced, let synced = c.tibia?.syncedAt, Date().timeIntervalSince(synced) < 3600 { return }
            try? await Task.sleep(for: .seconds(c.isSynced ? 0 : 1.2))
            guard !Task.isCancelled else { return }
            await lookup(char)
        }
    }

    /// Una línea: outfit, nombre y estado de la sincronización; a la derecha, nivel y Bestiary.
    private func header(_ char: Binding<GameChar>) -> some View {
        let c = char.wrappedValue
        let isIcon = store.iconChar == c.id
        return HStack(alignment: .center, spacing: 14) {
            OutfitButton(outfit: outfit, config: c.outfit, isIcon: isIcon) {
                // Antes de abrirlo, para que las listas aparezcan en el outfit y la montura del char.
                outfit.draft = c.outfit ?? .original
                editingOutfit = true
            } toggleIcon: {
                store.iconChar = isIcon ? nil : c.id
            }
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 8) {
                    TextField("Nombre del char en Tibia", text: char.name)
                        .textFieldStyle(.plain)
                        .font(.system(size: 22, weight: .bold))
                        .fixedSize()
                    if isIcon {
                        Chip(text: "ÍCONO", icon: "app.fill", tint: Color.accentBlue)
                            .help("Este char sale en el ícono de Alfa Hub")
                    }
                    Spacer(minLength: 0)
                }
                if c.isSynced, let info = c.tibia {
                    HStack(spacing: 8) {
                        Text(([info.vocation, info.world] + (info.guild.isEmpty ? [] : [info.guild])).filter { !$0.isEmpty }.joined(separator: " · "))
                            .font(.system(size: 13, weight: .medium)).foregroundStyle(Color.white.opacity(0.85)).lineLimit(1)
                        Button { Task { await lookup(char) } } label: {
                            HStack(spacing: 4) {
                                if looking { ProgressView().controlSize(.mini) } else { Image(systemName: "checkmark.circle.fill").foregroundStyle(Color.green) }
                                Text("tibia.com \(relative(info.syncedAt))")
                            }
                            .font(.system(size: 11)).foregroundStyle(Color.muted)
                        }
                        .buttonStyle(.plain).disabled(looking).help("Sincronizado con tibia.com; clic para volver a sincronizar")
                    }
                } else {
                    HStack(spacing: 8) {
                        Picker("", selection: char.vocation) {
                            ForEach(Vocation.allCases) { Text($0.title).tag($0) }
                        }
                        .labelsHidden().fixedSize()
                        TextField("Nivel", value: char.level, format: .number.grouping(.never)).frame(width: 56)
                        TextField("Mundo", text: char.world).frame(width: 100)
                        if looking {
                            ProgressView().controlSize(.mini)
                        } else if lookupError != nil {
                            Button("Reintentar") { Task { await lookup(char) } }.buttonStyle(.plain)
                                .font(.system(size: 11, weight: .semibold)).foregroundStyle(Color.accentBlue)
                        }
                    }
                    Text(lookupError ?? "Escribe el nombre del char y se sincroniza con tibia.com")
                        .font(.system(size: 11)).foregroundStyle(lookupError == nil ? Color.muted : Color.amber).lineLimit(1)
                }
            }
            Spacer(minLength: 8)
            HStack(spacing: 8) {
                HeaderStat(label: "Nivel", value: c.level.formatted(), tint: .green)
                HeaderStat(label: "Bestiary", value: "\(c.completedCount)/\(BestiaryCatalog.creatures.count)", tint: Color.amber)
                HeaderStat(label: "Charm points", value: c.charmPoints.formatted(), tint: Color(red: 0.69, green: 0.45, blue: 0.98))
            }
        }
    }

    private func lookup(_ char: Binding<GameChar>) async {
        let name = char.wrappedValue.name.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return }
        looking = true
        lookupError = nil
        defer { looking = false }
        do {
            let info = try await CharLookup.fetch(name)
            // Si mientras tanto se cambió el nombre, esta respuesta ya no vale.
            guard char.wrappedValue.name.trimmingCharacters(in: .whitespaces).caseInsensitiveCompare(name) == .orderedSame else { return }
            char.wrappedValue.apply(info)
        } catch {
            guard char.wrappedValue.name.trimmingCharacters(in: .whitespaces) == name else { return }
            lookupError = "“\(name)” no existe en tibia.com (o TibiaData no responde)"
        }
    }
}

/// Dato de la cabecera: valor grande y etiqueta pequeña.
struct HeaderStat: View {
    let label: String
    let value: String
    let tint: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(label.uppercased()).font(.system(size: 9, weight: .semibold)).tracking(0.8).foregroundStyle(Color.muted)
            Text(value).font(.system(size: 16, weight: .bold)).monospacedDigit().foregroundStyle(tint)
        }
        .padding(.horizontal, 12).padding(.vertical, 7)
        .background(RoundedRectangle(cornerRadius: 9).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 9).stroke(Color.white.opacity(0.06)))
    }
}

/// Pestañas con ícono y una raya azul debajo de la elegida (como guildstats.eu).
struct TabBar: View {
    @Binding var selection: Int

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 2) {
                ForEach(CharTab.allCases) { tab in
                    let selected = selection == tab.rawValue
                    Button { selection = tab.rawValue } label: {
                        Label(tab.title, systemImage: tab.icon)
                            .font(.system(size: 13, weight: selected ? .semibold : .regular))
                            .foregroundStyle(selected ? Color.white : Color.white.opacity(0.6))
                            .padding(.horizontal, 12).padding(.vertical, 8)
                            .overlay(alignment: .bottom) {
                                Capsule().fill(selected ? Color.accentBlue : .clear).frame(height: 2)
                            }
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
                Spacer()
            }
            Rectangle().fill(Color.white.opacity(0.08)).frame(height: 1)
        }
    }
}

/// Etiqueta pequeña en cápsula ("ÍCONO", "MUY RARA"…).
struct Chip: View {
    let text: String
    var icon: String? = nil
    let tint: Color

    var body: some View {
        HStack(spacing: 3) {
            if let icon { Image(systemName: icon) }
            Text(text)
        }
        .font(.system(size: 9, weight: .bold)).tracking(0.5)
        .padding(.horizontal, 6).padding(.vertical, 2)
        .background(Capsule().fill(tint.opacity(0.18)))
        .foregroundStyle(tint)
        .fixedSize()
    }
}

/// Opciones en cápsulas (Todas / Single / Group…), distintas de las pestañas.
struct PillPicker: View {
    let options: [String]
    @Binding var selection: Int

    var body: some View {
        HStack(spacing: 4) {
            ForEach(options.indices, id: \.self) { i in
                Button { selection = i } label: {
                    Text(options[i])
                        .font(.system(size: 12, weight: selection == i ? .semibold : .medium))
                        .padding(.horizontal, 12).padding(.vertical, 5)
                        .background(Capsule().fill(selection == i ? Color.accentBlue : Color.white.opacity(0.06)))
                        .foregroundStyle(selection == i ? .white : Color.white.opacity(0.75))
                        .contentShape(Capsule())
                }
                .buttonStyle(.plain)
            }
        }
    }
}

/// Botón ⓘ que abre una explicación.
struct InfoButton: View {
    let text: String
    @State private var shown = false

    var body: some View {
        Button { shown = true } label: {
            Image(systemName: "info.circle").font(.system(size: 13)).foregroundStyle(Color.muted)
        }
        .buttonStyle(.plain)
        .help("Cómo se calcula")
        .popover(isPresented: $shown) {
            Text(text).font(.system(size: 12)).fixedSize(horizontal: false, vertical: true)
                .padding(14).frame(width: 300)
        }
    }
}

/// Hace cuánto ("hace 5 minutos").
func relative(_ date: Date) -> String {
    let formatter = RelativeDateTimeFormatter()
    formatter.locale = Locale(identifier: "es")
    return date.timeIntervalSinceNow > -60 ? "ahora mismo" : formatter.localizedString(for: date, relativeTo: Date())
}

func longDate(_ date: Date, time: Bool = false) -> String {
    let formatter = DateFormatter()
    formatter.locale = Locale(identifier: "es")
    formatter.dateFormat = time ? "d 'de' MMMM yyyy, HH:mm" : "d 'de' MMMM yyyy"
    return formatter.string(from: date)
}

/// "54,5 mil M" (experiencia abreviada).
func compactNumber(_ value: Int64) -> String {
    Double(value).formatted(.number.notation(.compactName).precision(.fractionLength(0...1)))
}

/// El outfit del char: clic para editarlo (pincel en la esquina de abajo, sin tapar el personaje) y, en
/// la esquina de arriba, el botón para mostrarlo en el ícono de Alfa Hub.
struct OutfitButton: View {
    @ObservedObject var outfit: OutfitManager
    let config: OutfitConfig?
    let isIcon: Bool
    let edit: () -> Void
    let toggleIcon: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: edit) {
            CharAvatar(outfit: outfit, config: config, side: 68)
                .clipShape(RoundedRectangle(cornerRadius: 10))
                .overlay(RoundedRectangle(cornerRadius: 10).stroke(hover ? Color.accentBlue : Color.white.opacity(0.1), lineWidth: hover ? 2 : 1))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
        .help("Editar el outfit de este char")
        .overlay(alignment: .bottomTrailing) {
            Button(action: edit) {
                Image(systemName: "paintbrush.pointed.fill")
                    .font(.system(size: 10, weight: .semibold))
                    .frame(width: 22, height: 22)
                    .background(Circle().fill(hover ? Color.accentBlue : Color(white: 0.18)))
                    .overlay(Circle().stroke(Color.windowBackground, lineWidth: 2))
                    .foregroundStyle(.white)
            }
            .buttonStyle(.plain)
            .onHover { hover = $0 }
            .offset(x: 7, y: 7)
            .help("Editar el outfit de este char")
            .accessibilityLabel("Editar outfit")
        }
        .overlay(alignment: .topTrailing) {
            Button(action: toggleIcon) {
                Image(systemName: isIcon ? "app.badge.checkmark.fill" : "app.badge")
                    .font(.system(size: 11, weight: .semibold))
                    .frame(width: 22, height: 22)
                    .background(Circle().fill(isIcon ? Color.accentBlue : Color(white: 0.18)))
                    .overlay(Circle().stroke(Color.windowBackground, lineWidth: 2))
                    .foregroundStyle(.white)
            }
            .buttonStyle(.plain)
            .offset(x: 7, y: -7)
            .help(isIcon ? "Sale en el ícono de Alfa Hub (Dock, barra de menús y barra lateral, con su nombre). Clic para quitarlo."
                         : "Mostrar este char en el ícono de Alfa Hub (Dock, barra de menús y barra lateral, con su nombre)")
        }
    }
}

// MARK: - General

/// Lo que dice tibia.com del char, al estilo de guildstats.eu: arriba el nivel con su progreso y HP,
/// maná, capacidad y rango de party (calculados); debajo la ficha en dos columnas y, al lado, gremio,
/// casas, otros chars de la cuenta, muertes y comentario.
struct GeneralView: View {
    let char: GameChar
    let profile: Profile
    let add: (TibiaInfo.OtherChar) -> Void

    var body: some View {
        let info = char.isSynced ? char.tibia : nil
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                statsStrip(info)
                if let info {
                    HStack(alignment: .top, spacing: 12) {
                        VStack(spacing: 12) {
                            sheet(info)
                            deaths(info)
                        }
                        .frame(maxWidth: .infinity)
                        VStack(spacing: 12) {
                            if !info.guild.isEmpty { guild(info) }
                            if !info.houses.isEmpty { houses(info) }
                            if !info.otherChars.isEmpty { otherChars(info) }
                            if !info.formerNames.isEmpty || !info.formerWorlds.isEmpty { former(info) }
                            if !info.comment.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { comment(info) }
                        }
                        .frame(maxWidth: .infinity)
                    }
                    source(info)
                } else {
                    EmptyCard(icon: "globe", title: "Sin datos de tibia.com",
                              text: "Escribe arriba el nombre exacto del char: en cuanto lo encuentre en tibia.com se rellenan nivel, vocación, mundo, gremio, cuenta, casas, muertes y los otros chars de la cuenta.")
                }
            }
            .padding(.bottom, 16)
        }
    }

    // MARK: Franja de arriba

    /// Nivel (con la barra hasta el siguiente si se sabe la experiencia), HP, maná, capacidad y party.
    private func statsStrip(_ info: TibiaInfo?) -> some View {
        let level = char.level
        let vocation = char.vocation
        let range = Experience.shareRange(level)
        let start = Experience.forLevel(level), next = Experience.forLevel(level + 1)
        let progress = info?.experience.map { Double($0 - start) / Double(next - start) }
        return HStack(alignment: .center, spacing: 20) {
            VStack(alignment: .leading, spacing: 6) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text("NIVEL").font(.system(size: 10, weight: .semibold)).tracking(1).foregroundStyle(Color.muted)
                    Text(level.formatted()).font(.system(size: 30, weight: .bold)).monospacedDigit()
                    if let rank = info?.worldRank {
                        Chip(text: "#\(rank) EN \((info?.world ?? "").uppercased())", icon: "trophy.fill", tint: Color.amber)
                    }
                }
                ProgressBar(value: progress ?? 0, color: .green).frame(maxWidth: 260)
                Group {
                    if let progress, let exp = info?.experience {
                        Text("\(Int(progress * 100)) % · faltan \(compactNumber(next - exp)) exp para el \((level + 1).formatted())")
                    } else {
                        Text("\(compactNumber(next - start)) exp de este nivel al siguiente")
                    }
                }
                .font(.system(size: 11)).foregroundStyle(Color.muted)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Rectangle().fill(Color.white.opacity(0.08)).frame(width: 1, height: 64)
            Grid(alignment: .leading, horizontalSpacing: 18, verticalSpacing: 10) {
                GridRow {
                    meter("heart.fill", .red, "HP", vocation.hitPoints(level).formatted())
                    meter("bolt.fill", Color.accentBlue, "Maná", vocation.mana(level).formatted())
                }
                GridRow {
                    meter("scalemass.fill", Color(red: 0.95, green: 0.70, blue: 0.25), "Capacidad", vocation.capacity(level).formatted())
                    meter("person.3.fill", Color(red: 0.3, green: 0.8, blue: 0.85), "Party", "\(range.lowerBound.formatted())–\(range.upperBound.formatted())")
                        .help("Niveles con los que comparte experiencia en party")
                }
            }
            .fixedSize()
        }
        .padding(16)
        .background(
            RoundedRectangle(cornerRadius: 12).fill(LinearGradient(colors: [Color.green.opacity(0.08), Color.rowBackground],
                                                                   startPoint: .leading, endPoint: .trailing))
        )
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }

    /// Un dato con su barra de color (como las de HP y maná de guildstats).
    private func meter(_ icon: String, _ tint: Color, _ label: String, _ value: String) -> some View {
        HStack(spacing: 8) {
            Image(systemName: icon).font(.system(size: 12)).foregroundStyle(tint)
                .frame(width: 26, height: 26).background(Circle().fill(tint.opacity(0.15)))
            VStack(alignment: .leading, spacing: 2) {
                Text(label).font(.system(size: 10)).foregroundStyle(Color.muted)
                Text(value).font(.system(size: 14, weight: .semibold)).monospacedDigit()
                Capsule().fill(tint.opacity(0.7)).frame(width: 60, height: 3)
            }
        }
    }

    // MARK: Ficha

    private func sheet(_ info: TibiaInfo) -> some View {
        let rows: [(String, String, String)] = [
            ("person.fill", "Vocación", info.vocation),
            ("figure.stand", "Sexo", info.sex == "female" ? "Mujer" : info.sex == "male" ? "Hombre" : info.sex),
            ("globe.europe.africa.fill", "Mundo", info.world),
            ("house.fill", "Residencia", info.residence),
            ("rosette", "Título", info.title.isEmpty ? "Ninguno (\(info.unlockedTitles) desbloqueados)" : "\(info.title) (\(info.unlockedTitles))"),
            ("star.fill", "Puntos de logro", info.achievementPoints.formatted()),
            ("crown.fill", "Cuenta", info.accountStatus),
            ("clock.fill", "Última conexión", info.lastLogin.map { "\(longDate($0, time: true)) · \(relative($0))" } ?? "—"),
            ("calendar", "Cuenta creada", info.accountCreated.map { "\(longDate($0)) · \(relative($0))" } ?? "—"),
            ("medal.fill", "Lealtad", info.loyaltyTitle.isEmpty ? "—" : info.loyaltyTitle),
        ] + (info.marriedTo.isEmpty ? [] : [("heart.fill", "Casado con", info.marriedTo)])
        return SectionCard(title: "Personaje", icon: "person.text.rectangle") {
            VStack(spacing: 0) {
                ForEach(Array(rows.enumerated()), id: \.offset) { index, row in
                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        Image(systemName: row.0).font(.system(size: 10)).foregroundStyle(Color.muted).frame(width: 14)
                        Text(row.1).font(.system(size: 12)).foregroundStyle(Color.muted)
                        Spacer(minLength: 8)
                        Text(row.2).font(.system(size: 12, weight: .medium)).multilineTextAlignment(.trailing)
                            .fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
                    }
                    .padding(.vertical, 7)
                    .overlay(alignment: .bottom) {
                        if index < rows.count - 1 { Rectangle().fill(Color.white.opacity(0.05)).frame(height: 1) }
                    }
                }
            }
        }
    }

    private func guild(_ info: TibiaInfo) -> some View {
        SectionCard(title: "Gremio", icon: "shield.lefthalf.filled") {
            HStack(spacing: 12) {
                Image(systemName: "shield.fill").font(.system(size: 18)).foregroundStyle(.green)
                    .frame(width: 38, height: 38).background(RoundedRectangle(cornerRadius: 9).fill(Color.green.opacity(0.12)))
                VStack(alignment: .leading, spacing: 2) {
                    Text(info.guild).font(.system(size: 14, weight: .semibold))
                    Text(info.guildRank.isEmpty ? "Miembro" : info.guildRank).font(.system(size: 11)).foregroundStyle(Color.muted)
                }
                Spacer()
            }
        }
    }

    private func houses(_ info: TibiaInfo) -> some View {
        SectionCard(title: "Casas", icon: "house.fill") {
            ForEach(info.houses, id: \.self) { house in
                HStack(spacing: 10) {
                    Image(systemName: "house.fill").foregroundStyle(Color(red: 1.0, green: 0.45, blue: 0.25))
                    VStack(alignment: .leading, spacing: 1) {
                        Text(house.name).font(.system(size: 12, weight: .semibold))
                        Text([house.town, house.paid.isEmpty ? "" : "pagada hasta \(house.paid)"].filter { !$0.isEmpty }.joined(separator: " · "))
                            .font(.system(size: 11)).foregroundStyle(Color.muted)
                    }
                    Spacer()
                }
            }
        }
    }

    private func former(_ info: TibiaInfo) -> some View {
        SectionCard(title: "Antes", icon: "clock.arrow.circlepath") {
            if !info.formerNames.isEmpty { InfoRow("Nombres", info.formerNames.joined(separator: ", ")) }
            if !info.formerWorlds.isEmpty { InfoRow("Mundos", info.formerWorlds.joined(separator: ", ")) }
        }
    }

    private func deaths(_ info: TibiaInfo) -> some View {
        SectionCard(title: "Muertes recientes", icon: "cross.fill", count: info.deaths.count) {
            if info.deaths.isEmpty {
                Label("Ninguna en los últimos 30 días", systemImage: "checkmark.shield.fill")
                    .font(.system(size: 12)).foregroundStyle(Color.green)
            }
            ForEach(Array(info.deaths.prefix(10).enumerated()), id: \.offset) { _, death in
                HStack(alignment: .top, spacing: 10) {
                    Text("\(death.level)").font(.system(size: 11, weight: .bold)).monospacedDigit().foregroundStyle(.red)
                        .frame(width: 44).padding(.vertical, 3).background(RoundedRectangle(cornerRadius: 6).fill(Color.red.opacity(0.12)))
                    VStack(alignment: .leading, spacing: 1) {
                        Text(death.reason).font(.system(size: 12)).fixedSize(horizontal: false, vertical: true)
                        if let time = death.time {
                            Text("\(longDate(time, time: true)) · \(relative(time))").font(.system(size: 10)).foregroundStyle(Color.muted)
                        }
                    }
                }
            }
        }
    }

    private func otherChars(_ info: TibiaInfo) -> some View {
        SectionCard(title: "Otros chars de la cuenta", icon: "person.2.fill", count: info.otherChars.count) {
            ForEach(info.otherChars, id: \.self) { other in
                let added = profile.chars.contains { $0.name.caseInsensitiveCompare(other.name) == .orderedSame }
                HStack(spacing: 8) {
                    Circle().fill(other.online ? Color.green : Color.white.opacity(0.2)).frame(width: 6, height: 6)
                        .help(other.online ? "Conectado" : "Desconectado")
                    Text(other.name).font(.system(size: 12, weight: .medium)).lineLimit(1)
                    if other.main { Chip(text: "MAIN", tint: Color.accentBlue) }
                    Spacer(minLength: 4)
                    Text(other.world).font(.system(size: 11)).foregroundStyle(Color.muted).lineLimit(1)
                    if added {
                        Image(systemName: "checkmark").font(.system(size: 10, weight: .bold)).foregroundStyle(Color.muted)
                            .frame(width: 22).help("Ya está en el perfil")
                    } else {
                        Button { add(other) } label: {
                            Image(systemName: "plus").font(.system(size: 10, weight: .bold))
                                .frame(width: 22, height: 20).background(RoundedRectangle(cornerRadius: 5).fill(Color.accentBlue))
                                .foregroundStyle(.white)
                        }
                        .buttonStyle(.plain).help("Añadir \(other.name) al perfil")
                    }
                }
                .padding(.vertical, 2)
            }
        }
    }

    private func comment(_ info: TibiaInfo) -> some View {
        SectionCard(title: "Comentario", icon: "text.quote") {
            Text(info.comment.trimmingCharacters(in: .whitespacesAndNewlines)).font(.system(size: 12))
                .foregroundStyle(Color.white.opacity(0.85))
                .fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
        }
    }

    private func source(_ info: TibiaInfo) -> some View {
        Text("Datos de tibia.com (TibiaData), sincronizados \(relative(info.syncedAt)). HP, maná, capacidad y party se calculan con el nivel y la vocación.")
            .font(.system(size: 11)).foregroundStyle(Color.muted)
    }
}

/// Tarjeta con título (y un número a la derecha) y su contenido.
struct SectionCard<Content: View>: View {
    let title: String
    let icon: String
    var count: Int? = nil
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 6) {
                Image(systemName: icon).font(.system(size: 11)).foregroundStyle(Color.accentBlue)
                Text(title).font(.system(size: 13, weight: .semibold))
                Spacer()
                if let count, count > 0 {
                    Text("\(count)").font(.system(size: 11, weight: .semibold)).monospacedDigit()
                        .padding(.horizontal, 7).padding(.vertical, 1)
                        .background(Capsule().fill(Color.white.opacity(0.08)))
                }
            }
            content
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }
}

struct InfoCard<Content: View>: View {
    let title: String
    let icon: String
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label(title.uppercased(), systemImage: icon)
                .font(.system(size: 10, weight: .semibold)).tracking(1).foregroundStyle(Color.muted)
                .padding(.bottom, 2)
            content
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }
}

struct InfoRow: View {
    let label: String
    let value: String
    init(_ label: String, _ value: String) { self.label = label; self.value = value }

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(label).font(.system(size: 12)).foregroundStyle(Color.muted)
            Spacer(minLength: 8)
            Text(value.isEmpty ? "—" : value).font(.system(size: 12, weight: .medium)).multilineTextAlignment(.trailing)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
        }
    }
}

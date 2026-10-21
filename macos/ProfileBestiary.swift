// Pestaña Bestiary de un char, como el Bestiary Tracker de tibiaroute.com: resumen, todas las criaturas
// en cuadrícula por clase (con filtros de clase, dificultad y estado, y orden), o las recomendaciones para
// terminar solo (Single) o en grupo por zona (Group). Clic en una criatura para ver su ficha y apuntar
// las kills. Las recomendaciones se calculan con los datos de TibiaWiki (HP, kills que faltan, charm
// points y los sitios de cada criatura) y el nivel del char; son orientativas.

import SwiftUI

struct BestiaryView: View {
    @Binding var char: GameChar
    @AppStorage("bestiaryMode") private var mode = 0       // 0 todas, 1 single, 2 group
    @AppStorage("bestiaryClass") private var klass = ""   // "" = todas
    @AppStorage("bestiaryLevel") private var difficulty = ""
    @AppStorage("bestiaryFilter") private var filter = 0  // 0 todas, 1 en curso, 2 completas, 3 sin empezar
    @AppStorage("bestiarySort") private var sort = 0
    @State private var search = ""
    @State private var openPlace: String?
    private static let filters = ["Todas", "En curso", "Completas", "Sin empezar"]
    private static let sorts = ["Clase", "Nombre", "Charm points", "Progreso", "HP", "Kills que faltan"]

    /// Las que pasan la búsqueda, la clase y la dificultad.
    private var base: [BestiaryCreature] {
        let text = search.trimmingCharacters(in: .whitespaces)
        return BestiaryCatalog.creatures.filter { c in
            (klass.isEmpty || c.class == klass) && (difficulty.isEmpty || c.level == difficulty)
                && (text.isEmpty || c.name.localizedCaseInsensitiveContains(text))
        }
    }

    private var creatures: [BestiaryCreature] {
        let list = base.filter { c in
            let kills = char.kills(c)
            switch filter {
            case 1: return kills > 0 && kills < c.total
            case 2: return kills >= c.total
            case 3: return kills == 0
            default: return true
            }
        }
        func progress(_ c: BestiaryCreature) -> Double { Double(min(char.kills(c), c.total)) / Double(c.total) }
        switch sort {
        case 2: return list.sorted { ($0.charms, $1.name) > ($1.charms, $0.name) }
        case 3: return list.sorted { (progress($0), $1.name) > (progress($1), $0.name) }
        case 4: return list.sorted { ($0.hp, $0.name) < ($1.hp, $1.name) }
        case 5: return list.sorted { (remaining($0), $0.name) < (remaining($1), $1.name) }
        default: return list
        }
    }

    private func remaining(_ c: BestiaryCreature) -> Int { max(0, c.total - char.kills(c)) }

    private func kills(_ c: BestiaryCreature) -> Binding<Int> {
        Binding(get: { char.kills(c) }, set: { char.kills[c.name] = $0 > 0 ? $0 : nil })
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                PillPicker(options: ["Todas", "Single", "Group"], selection: $mode)
                TextField("Buscar criatura", text: $search).frame(minWidth: 120, maxWidth: 200)
                Spacer(minLength: 0)
                switch mode {
                case 1: InfoButton(text: singleHelp)
                case 2: InfoButton(text: groupHelp)
                default:
                    MoreButton {
                        let list = creatures
                        return [.init(title: "Marcar las \(list.count) de la lista como completas") {
                                    for c in list { char.kills[c.name] = max(char.kills(c), c.total) }
                                },
                                .init(title: "Poner a 0 las \(list.count) de la lista", destructive: true) {
                                    if ProfileActions.confirm("¿Poner a 0 las kills de \(list.count) criaturas?", detail: "Se borran las kills apuntadas de las criaturas que se ven en la lista.", button: "Poner a 0") {
                                        for c in list { char.kills[c.name] = nil }
                                    }
                                }]
                    }
                }
            }
            HStack(spacing: 8) {
                FilterMenu(title: klass.isEmpty ? "Todas las clases" : klass, icon: "line.3.horizontal.decrease") {
                    Button("Todas las clases · \(progress { _ in true })") { klass = "" }
                    Divider()
                    ForEach(BestiaryCatalog.classes, id: \.self) { name in
                        Button("\(name) · \(progress { $0.class == name })") { klass = name }
                    }
                }
                FilterMenu(title: difficulty.isEmpty ? "Todas las dificultades" : difficulty, icon: "gauge.with.dots.needle.33percent") {
                    Button("Todas las dificultades") { difficulty = "" }
                    Divider()
                    ForEach(BestiaryCatalog.levels, id: \.self) { level in
                        Button("\(level) · \(progress { $0.level == level })") { difficulty = level }
                    }
                }
                if mode == 0 {
                    FilterMenu(title: Self.filters[filter], icon: "checklist") {
                        ForEach(Self.filters.indices, id: \.self) { i in Button(Self.filters[i]) { filter = i } }
                    }
                    FilterMenu(title: "Orden: \(Self.sorts[sort])", icon: "arrow.up.arrow.down") {
                        ForEach(Self.sorts.indices, id: \.self) { i in Button(Self.sorts[i]) { sort = i } }
                    }
                }
                Spacer(minLength: 0)
            }
            ScrollView {
                switch mode {
                case 1: singleList
                case 2: groupList
                default: allGrid
                }
            }
        }
    }

    /// "12/43" (completas de las criaturas que cumplen la condición).
    private func progress(_ include: (BestiaryCreature) -> Bool) -> String {
        let all = BestiaryCatalog.creatures.filter(include)
        return "\(all.filter(char.completed).count)/\(all.count)"
    }

    // MARK: Todas

    private var summary: some View {
        let all = BestiaryCatalog.creatures
        let done = all.filter(char.completed).count
        let started = all.filter { char.kills($0) > 0 && !char.completed($0) }.count
        return HStack(spacing: 18) {
            VStack(alignment: .leading, spacing: 5) {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    Text("Completas").font(.system(size: 12)).foregroundStyle(Color.green)
                    Text("\(done)/\(all.count)").font(.system(size: 18, weight: .bold)).monospacedDigit()
                    Text("\(Int(Double(done) / Double(max(1, all.count)) * 100)) %").font(.system(size: 12, weight: .semibold)).foregroundStyle(Color.amber)
                }
                ProgressBar(value: Double(done) / Double(max(1, all.count)), color: .green)
            }
            .frame(maxWidth: 280)
            Spacer()
            legend(Color.accentBlue, "En curso", started)
            legend(Color.white.opacity(0.3), "Sin empezar", all.count - done - started)
            Label("\(char.charmPoints.formatted()) / \(BestiaryCatalog.totalCharms.formatted())", systemImage: "sparkles")
                .font(.system(size: 12, weight: .semibold)).foregroundStyle(Color(red: 0.69, green: 0.45, blue: 0.98))
                .help("Charm points conseguidos de los que da el Bestiary completo")
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }

    private func legend(_ color: Color, _ title: String, _ count: Int) -> some View {
        HStack(spacing: 5) {
            Circle().fill(color).frame(width: 7, height: 7)
            Text(title).font(.system(size: 11)).foregroundStyle(Color.muted)
            Text("\(count)").font(.system(size: 12, weight: .semibold)).monospacedDigit()
        }
    }

    private var tileColumns: [GridItem] { [GridItem(.adaptive(minimum: 96, maximum: 120), spacing: 8)] }

    @ViewBuilder private var allGrid: some View {
        let list = creatures
        LazyVStack(alignment: .leading, spacing: 14) {
            summary
            if list.isEmpty {
                EmptyCard(icon: "magnifyingglass", title: "Ninguna criatura", text: "No hay criaturas con estos filtros.")
            } else if sort == 0 {
                // Por clase, como en el juego.
                ForEach(BestiaryCatalog.classes, id: \.self) { name in
                    let group = list.filter { $0.class == name }
                    if !group.isEmpty {
                        let all = BestiaryCatalog.creatures.filter { $0.class == name }
                        let earned = all.filter(char.completed).reduce(0) { $0 + $1.charms }
                        VStack(alignment: .leading, spacing: 8) {
                            HStack(spacing: 8) {
                                Text(name).font(.system(size: 14, weight: .bold)).foregroundStyle(Color.amber)
                                Text("\(all.filter(char.completed).count)/\(all.count)").font(.system(size: 12, weight: .semibold)).monospacedDigit()
                                Label("\(earned.formatted()) / \(all.reduce(0) { $0 + $1.charms }.formatted())", systemImage: "sparkles")
                                    .font(.system(size: 11)).foregroundStyle(Color.muted)
                                Rectangle().fill(Color.white.opacity(0.08)).frame(height: 1)
                            }
                            LazyVGrid(columns: tileColumns, alignment: .leading, spacing: 8) {
                                ForEach(group) { c in CreatureTile(creature: c, kills: kills(c), level: char.level) }
                            }
                        }
                    }
                }
            } else {
                LazyVGrid(columns: tileColumns, alignment: .leading, spacing: 8) {
                    ForEach(list) { c in CreatureTile(creature: c, kills: kills(c), level: char.level) }
                }
            }
        }
        .padding(.bottom, 16)
    }

    // MARK: Single

    private var singleHelp: String {
        "Criaturas que conviene terminar yendo solo con nivel \(char.level.formatted()): las que dan más charm points según lo fácil que se matan a tu nivel (su vida comparada con tu nivel), las kills que faltan y en cuántos sitios viven según TibiaWiki (las de un solo sitio suelen ser de evento). Sin las muy raras, que no se pueden buscar, ni las que tienen más de 60 HP por cada nivel tuyo. Es orientativo: no se sabe cuántas hay en cada spawn."
    }

    private var groupHelp: String {
        "Zonas donde terminar varias criaturas a la vez (los sitios que enlaza TibiaWiki en cada criatura), ordenadas por los charm points que faltan. La dificultad (1–10) compara la vida de la criatura más dura con el nivel \(char.level.formatted()). Es orientativo."
    }

    /// Golpes (aprox.) para matar una criatura según el nivel del char: cuanto más HP, más tarda.
    private func effort(_ c: BestiaryCreature) -> Double { max(1, Double(c.hp) / Double(char.level * 4 + 100)) }

    /// Qué tal se hace solo: "Fácil", "Normal" o "Difícil".
    private func ease(_ c: BestiaryCreature) -> (String, Color) {
        let ratio = Double(c.hp) / Double(max(1, char.level))
        return ratio < 5 ? ("FÁCIL", .green) : ratio < 20 ? ("NORMAL", Color.amber) : ("DIFÍCIL", .red)
    }

    /// Charm points de la criatura, por lo fácil que es matarla, por las kills que faltan (raíz: una a
    /// medias sube, pero no tapa a las que dan más) y por en cuántos sitios vive.
    private func singleScore(_ c: BestiaryCreature) -> Double {
        let availability = Double(min(c.places.count, 6)) / 6
        return Double(c.charms) * availability * 100 / (effort(c) * Double(remaining(c)).squareRoot())
    }

    private var singles: [BestiaryCreature] {
        base.filter { !$0.rare && remaining($0) > 0 && $0.hp <= char.level * 60 && !$0.places.isEmpty }
            .sorted { singleScore($0) > singleScore($1) }
            .prefix(40).map { $0 }
    }

    @ViewBuilder private var singleList: some View {
        let list = singles
        LazyVStack(alignment: .leading, spacing: 6) {
            if list.isEmpty {
                EmptyCard(icon: "checkmark.seal", title: "Nada que recomendar",
                          text: "No queda ninguna criatura sin terminar con estos filtros (o todas tienen demasiada vida para el nivel del char).")
            }
            ForEach(Array(list.enumerated()), id: \.element.id) { index, c in
                let (word, tint) = ease(c)
                RecommendationRow(rank: index + 1, creature: c, kills: kills(c), level: char.level) {
                    Chip(text: "FALTAN \(remaining(c).formatted())", tint: Color.accentBlue)
                    Chip(text: word, tint: tint)
                    Chip(text: "\(c.hp.formatted()) HP", tint: Color.white.opacity(0.7))
                    Chip(text: "+\(c.charms) CP", icon: "sparkles", tint: Color(red: 0.69, green: 0.45, blue: 0.98))
                }
            }
        }
        .padding(.bottom, 16)
    }

    // MARK: Group

    /// Zonas con 2 o más criaturas sin terminar, por charm points por conseguir.
    private var places: [(name: String, creatures: [BestiaryCreature], charms: Int, difficulty: Int)] {
        var byPlace: [String: [BestiaryCreature]] = [:]
        for c in base where !c.rare && remaining(c) > 0 {
            for place in c.places { byPlace[place, default: []].append(c) }
        }
        return byPlace.filter { $0.value.count >= 2 }.map { name, list in
            let maxHP = list.map(\.hp).max() ?? 0
            let difficulty = min(10, max(1, Int(Double(maxHP) / Double(max(1, char.level)) / 4) + 1))
            return (name, list.sorted { remaining($0) < remaining($1) }, list.reduce(0) { $0 + $1.charms }, difficulty)
        }
        .sorted { ($0.charms, $0.creatures.count) > ($1.charms, $1.creatures.count) }
        .prefix(30).map { $0 }
    }

    @ViewBuilder private var groupList: some View {
        let list = places
        LazyVStack(alignment: .leading, spacing: 8) {
            if list.isEmpty {
                EmptyCard(icon: "checkmark.seal", title: "Nada que recomendar", text: "No hay zonas con varias criaturas sin terminar con estos filtros.")
            }
            ForEach(Array(list.enumerated()), id: \.element.name) { index, place in
                let open = openPlace == place.name
                VStack(alignment: .leading, spacing: 10) {
                    Button { openPlace = open ? nil : place.name } label: {
                        HStack(alignment: .center, spacing: 12) {
                            RankBadge(rank: index + 1)
                            VStack(alignment: .leading, spacing: 6) {
                                HStack(spacing: 8) {
                                    Text(place.name).font(.system(size: 14, weight: .semibold))
                                    Chip(text: "\(place.creatures.count) POR TERMINAR", tint: Color.accentBlue)
                                    Chip(text: "+\(place.charms.formatted()) CP", icon: "sparkles", tint: Color(red: 0.69, green: 0.45, blue: 0.98))
                                    Chip(text: "DIFICULTAD \(place.difficulty)/10", tint: place.difficulty <= 3 ? .green : place.difficulty <= 6 ? Color.amber : .red)
                                }
                                HStack(spacing: 6) {
                                    ForEach(place.creatures.prefix(10)) { c in
                                        VStack(spacing: 1) {
                                            WikiSprite(name: c.name, size: 30)
                                            Text(compactNumber(Int64(remaining(c)))).font(.system(size: 9, weight: .semibold)).foregroundStyle(Color.muted)
                                        }
                                        .help("\(c.name): faltan \(remaining(c).formatted()) kills")
                                    }
                                    if place.creatures.count > 10 {
                                        Text("+\(place.creatures.count - 10)").font(.system(size: 11, weight: .semibold)).foregroundStyle(Color.muted)
                                    }
                                }
                            }
                            Spacer()
                            Image(systemName: open ? "chevron.up" : "chevron.down").foregroundStyle(Color.muted)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    if open {
                        LazyVGrid(columns: tileColumns, alignment: .leading, spacing: 8) {
                            ForEach(place.creatures) { c in CreatureTile(creature: c, kills: kills(c), level: char.level) }
                        }
                    }
                }
                .padding(12)
                .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
            }
        }
        .padding(.bottom, 16)
    }
}

/// Número de puesto en un círculo ámbar.
struct RankBadge: View {
    let rank: Int
    var body: some View {
        Text("\(rank)").font(.system(size: 12, weight: .bold)).foregroundStyle(Color.amber)
            .frame(width: 26, height: 26).background(Circle().fill(Color.amber.opacity(0.15)))
    }
}

/// Una criatura en la cuadrícula: GIF, nombre, barra y kills. Clic: su ficha.
struct CreatureTile: View {
    let creature: BestiaryCreature
    @Binding var kills: Int
    let level: Int
    @State private var open = false

    var body: some View {
        let done = kills >= creature.total
        let started = kills > 0 && !done
        Button { open = true } label: {
            VStack(spacing: 4) {
                WikiSprite(name: creature.name, size: 48)
                Text(creature.name).font(.system(size: 11, weight: .medium)).lineLimit(1).truncationMode(.tail)
                ProgressBar(value: Double(kills) / Double(creature.total), color: done ? .green : Color.accentBlue)
                Text(done ? "Completa" : "\(compactNumber(Int64(kills)))/\(compactNumber(Int64(creature.total)))")
                    .font(.system(size: 10, weight: done ? .semibold : .regular)).monospacedDigit()
                    .foregroundStyle(done ? Color.green : Color.muted)
            }
            .padding(8)
            .frame(maxWidth: .infinity)
            .background(RoundedRectangle(cornerRadius: 10).fill(done ? Color.green.opacity(0.08) : Color.rowBackground))
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(open ? Color.accentBlue
                : done ? Color.green.opacity(0.45) : started ? Color.accentBlue.opacity(0.4) : Color.white.opacity(0.06)))
            .overlay(alignment: .topTrailing) {
                if creature.rare { Circle().fill(Color.amber).frame(width: 6, height: 6).padding(6).help("Muy rara") }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("\(creature.name) · \(creature.level) · \(creature.class)")
        .popover(isPresented: $open, arrowEdge: .trailing) {
            CreatureDetail(creature: creature, kills: $kills, level: level)
        }
    }
}

/// Una recomendación Single: puesto, criatura, etiquetas y sus kills. Clic: su ficha.
struct RecommendationRow<Chips: View>: View {
    let rank: Int
    let creature: BestiaryCreature
    @Binding var kills: Int
    let level: Int
    @ViewBuilder var chips: Chips
    @State private var open = false

    var body: some View {
        Button { open = true } label: {
            HStack(spacing: 12) {
                RankBadge(rank: rank)
                WikiSprite(name: creature.name, size: 36)
                VStack(alignment: .leading, spacing: 4) {
                    HStack(spacing: 6) {
                        Text(creature.name).font(.system(size: 13, weight: .semibold)).lineLimit(1)
                        Text("\(creature.class) · \(creature.level)").font(.system(size: 11)).foregroundStyle(Color.muted).lineLimit(1)
                    }
                    HStack(spacing: 5) { chips }
                }
                Spacer(minLength: 8)
                VStack(alignment: .trailing, spacing: 4) {
                    Text("\(kills.formatted()) / \(creature.total.formatted())").font(.system(size: 11)).monospacedDigit().foregroundStyle(Color.muted)
                    ProgressBar(value: Double(kills) / Double(creature.total), color: Color.accentBlue).frame(width: 110)
                }
            }
            .padding(.vertical, 8).padding(.horizontal, 12)
            .background(RoundedRectangle(cornerRadius: 10).fill(Color.rowBackground))
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(open ? Color.accentBlue : Color.white.opacity(0.06)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .popover(isPresented: $open, arrowEdge: .trailing) {
            CreatureDetail(creature: creature, kills: $kills, level: level)
        }
    }
}

/// Ficha de una criatura: datos, etapas, debilidades, sitios y las kills.
struct CreatureDetail: View {
    let creature: BestiaryCreature
    @Binding var kills: Int
    let level: Int

    private static let elements: [(String, String, Color)] = [
        ("physical", "Físico", Color(white: 0.75)), ("fire", "Fuego", .orange), ("ice", "Hielo", .cyan),
        ("energy", "Energía", .purple), ("earth", "Tierra", .green), ("holy", "Sagrado", .yellow), ("death", "Muerte", .red),
    ]

    var body: some View {
        let done = kills >= creature.total
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 12) {
                WikiSprite(name: creature.name, size: 64)
                VStack(alignment: .leading, spacing: 4) {
                    Text(creature.name).font(.system(size: 16, weight: .bold))
                    HStack(spacing: 4) {
                        Chip(text: creature.class.uppercased(), tint: Color.amber)
                        Chip(text: creature.level.uppercased(), tint: Color.accentBlue)
                        if creature.rare { Chip(text: "MUY RARA", tint: .red) }
                    }
                    Text("\(creature.hp.formatted()) HP · \(creature.exp.formatted()) exp · \(creature.charms) charm points")
                        .font(.system(size: 11)).foregroundStyle(Color.muted)
                }
            }
            // Etapas
            HStack(spacing: 6) {
                ForEach(Array(creature.stages.enumerated()), id: \.offset) { index, need in
                    let reached = kills >= need
                    VStack(spacing: 2) {
                        Image(systemName: reached ? "checkmark.circle.fill" : "circle").foregroundStyle(reached ? Color.green : Color.muted)
                        Text("Etapa \(index + 1)").font(.system(size: 10, weight: .semibold))
                        Text(need.formatted()).font(.system(size: 10)).monospacedDigit().foregroundStyle(Color.muted)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 6)
                    .background(RoundedRectangle(cornerRadius: 8).fill(reached ? Color.green.opacity(0.1) : Color.white.opacity(0.04)))
                }
            }
            // Kills
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 6) {
                    Text("Kills").font(.system(size: 12)).foregroundStyle(Color.muted)
                    TextField("", value: $kills, format: .number).frame(width: 80).multilineTextAlignment(.trailing)
                    Text("/ \(creature.total.formatted())").font(.system(size: 12)).foregroundStyle(Color.muted)
                    Spacer()
                    ForEach([1, 10, 100], id: \.self) { step in
                        Button("+\(step)") { kills = min(creature.total, kills + step) }
                            .buttonStyle(.plain).font(.system(size: 11, weight: .semibold))
                            .padding(.horizontal, 7).padding(.vertical, 3)
                            .background(Capsule().fill(Color.white.opacity(0.08)))
                    }
                }
                ProgressBar(value: Double(kills) / Double(creature.total), color: done ? .green : Color.accentBlue)
                Button { kills = done ? 0 : creature.total } label: {
                    Label(done ? "Quitar completa" : "Marcar completa", systemImage: done ? "xmark.circle" : "checkmark.circle.fill")
                        .font(.system(size: 12, weight: .semibold))
                        .frame(maxWidth: .infinity).padding(.vertical, 6)
                        .background(RoundedRectangle(cornerRadius: 8).fill(done ? Color.white.opacity(0.08) : Color.green.opacity(0.8)))
                        .foregroundStyle(.white)
                }
                .buttonStyle(.plain)
            }
            // Debilidades
            VStack(alignment: .leading, spacing: 6) {
                Text("DAÑO QUE RECIBE").font(.system(size: 10, weight: .semibold)).tracking(1).foregroundStyle(Color.muted)
                HStack(spacing: 4) {
                    ForEach(Self.elements, id: \.0) { key, title, tint in
                        let value = creature.mod(key)
                        VStack(spacing: 1) {
                            Text(title).font(.system(size: 9)).foregroundStyle(tint)
                            Text("\(value) %").font(.system(size: 10, weight: .semibold)).monospacedDigit()
                                .foregroundStyle(value > 100 ? Color.green : value < 100 ? Color.muted : .white)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 4)
                        .background(RoundedRectangle(cornerRadius: 6).fill(value > 100 ? Color.green.opacity(0.1) : Color.white.opacity(0.04)))
                    }
                }
            }
            if !creature.places.isEmpty {
                VStack(alignment: .leading, spacing: 4) {
                    Text("DÓNDE VIVE (TIBIAWIKI)").font(.system(size: 10, weight: .semibold)).tracking(1).foregroundStyle(Color.muted)
                    Text(creature.places.prefix(8).joined(separator: " · ")).font(.system(size: 11)).fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .padding(16)
        .frame(width: 360)
    }
}

/// Menú con el aspecto de SmallButton.
struct FilterMenu<Items: View>: View {
    let title: String
    let icon: String
    @ViewBuilder var items: Items

    var body: some View {
        Menu { items } label: {
            Label(title, systemImage: icon)
                .font(.system(size: 12, weight: .medium))
                .lineLimit(1)
                .padding(.horizontal, 10).padding(.vertical, 5)
                .background(RoundedRectangle(cornerRadius: 7).fill(Color.white.opacity(0.07)))
                .overlay(RoundedRectangle(cornerRadius: 7).stroke(Color.white.opacity(0.08)))
                .foregroundStyle(.white)
        }
        .menuStyle(.button).menuIndicator(.hidden).buttonStyle(.plain).fixedSize()
    }
}

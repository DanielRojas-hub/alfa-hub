// Pestaña Charms de un char, como el Charm Optimizer de hakaimarket.com: los charm points (los que da su
// Bestiary y los gastados), qué charms tiene desbloqueados y a qué nivel, y el optimizador: con las
// criaturas de su hunt (a mano o de un preset de hunt), qué charm de daño poner en cada una y qué charm
// menor. Fórmulas de TibiaWiki (CharmCatalog.damage).

import SwiftUI

struct CharmsView: View {
    @Binding var char: GameChar
    @AppStorage("charmsMode") private var mode = 0   // 0 optimizador, 1 mis charms
    @State private var choosingPreset = false
    @State private var search = ""
    @State private var showTable = false

    private static let purple = Color(red: 0.69, green: 0.45, blue: 0.98)

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 10) {
                PillPicker(options: ["Optimizador", "Mis charms"], selection: $mode)
                Spacer()
                if mode == 0 { InfoButton(text: optimizerHelp) }
            }
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    if mode == 0 {
                        setup
                        if !hunt.isEmpty { results }
                    } else {
                        summary
                        charmGrid(title: "Charms mayores", charms: CharmCatalog.major)
                        charmGrid(title: "Charms menores", charms: CharmCatalog.minor)
                    }
                }
                .padding(.bottom, 16)
            }
        }
    }

    // MARK: Charm points

    /// Lo gastado en cada charm: la suma de sus niveles desbloqueados.
    private func spent(_ charms: [CharmInfo]) -> Int {
        charms.reduce(0) { total, charm in total + charm.costs.prefix(char.charms[charm.name] ?? 0).reduce(0, +) }
    }

    private var summary: some View {
        let earned = char.charmPoints
        let spentMajor = spent(CharmCatalog.major)
        let spentMinor = spent(CharmCatalog.minor)
        return HStack(spacing: 10) {
            StatTile(icon: "sparkles", tint: Self.purple, title: "Charm points del Bestiary",
                     value: earned.formatted(), detail: "de \(BestiaryCatalog.totalCharms.formatted()) con todo completo")
            StatTile(icon: "cart.fill", tint: Color.amber, title: "Gastados en mayores",
                     value: spentMajor.formatted(), detail: earned >= spentMajor ? "quedan \((earned - spentMajor).formatted())" : "más de los que da su Bestiary")
            StatTile(icon: "circle.hexagongrid.fill", tint: Color.accentBlue, title: "Gastados en menores",
                     value: spentMinor.formatted(), detail: "\(CharmCatalog.minor.filter { (char.charms[$0.name] ?? 0) > 0 }.count) de \(CharmCatalog.minor.count) desbloqueados")
        }
    }

    // MARK: Charms

    private func charmGrid(title: String, charms: [CharmInfo]) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Text(title).font(.system(size: 14, weight: .bold))
                Text("\(charms.filter { (char.charms[$0.name] ?? 0) > 0 }.count)/\(charms.count)")
                    .font(.system(size: 12, weight: .semibold)).foregroundStyle(Color.muted).monospacedDigit()
                Text("Clic para marcar el nivel que tienes").font(.system(size: 11)).foregroundStyle(Color.muted)
                Spacer()
            }
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 104, maximum: 130), spacing: 8)], alignment: .leading, spacing: 8) {
                ForEach(charms) { charm in
                    CharmTile(charm: charm, tier: Binding(get: { char.charms[charm.name] ?? 0 },
                                                          set: { char.charms[charm.name] = $0 > 0 ? $0 : nil }))
                }
            }
        }
    }

    // MARK: Optimizador

    /// Charms de daño con los que se calcula: los desbloqueados o, si no hay ninguno, todos (nivel 1).
    private var usable: [(CharmInfo, Int)] {
        let owned = CharmCatalog.damage.compactMap { c in (char.charms[c.name] ?? 0) > 0 ? (c, char.charms[c.name]!) : nil }
        return owned.isEmpty ? CharmCatalog.damage.map { ($0, 1) } : owned
    }

    private var hunt: [(HuntCreature, BestiaryCreature)] {
        char.hunt.compactMap { entry in BestiaryCatalog.creatures.first { $0.name == entry.name }.map { (entry, $0) } }
    }

    private func damage(_ charm: CharmInfo, _ creature: BestiaryCreature) -> Int {
        CharmCatalog.damage(charm, on: creature, level: char.level, vocation: char.vocation)
    }

    /// Daño extra por hora (en proporción): los ataques que van a esa criatura (sus kills por hora por su
    /// vida) × la probabilidad del charm × su daño.
    private func score(_ entry: HuntCreature, _ creature: BestiaryCreature, _ charm: CharmInfo, tier: Int) -> Double {
        Double(entry.perHour) * Double(creature.hp) * CharmInfo.chances[tier - 1] * Double(damage(charm, creature))
    }

    /// El mejor reparto: cada charm en una sola criatura y cada criatura con un solo charm (programación
    /// dinámica sobre qué charms ya se usaron).
    private var assignment: [(creature: BestiaryCreature, charm: CharmInfo, tier: Int)] {
        let creatures = hunt
        let charms = usable
        guard !creatures.isEmpty, charms.count <= 16 else { return [] }
        var memo: [Int: [Int: (Double, [Int])]] = [:]
        func best(_ i: Int, _ used: Int) -> (Double, [Int]) {
            if i == creatures.count { return (0, []) }
            if let hit = memo[i]?[used] { return hit }
            var result = best(i + 1, used)
            result.1.insert(-1, at: 0)
            for (j, (charm, tier)) in charms.enumerated() where used & (1 << j) == 0 {
                let (entry, creature) = creatures[i]
                let gain = score(entry, creature, charm, tier: tier)
                guard gain > 0 else { continue }
                let rest = best(i + 1, used | (1 << j))
                if rest.0 + gain > result.0 { result = (rest.0 + gain, [j] + rest.1) }
            }
            memo[i, default: [:]][used] = result
            return result
        }
        let (_, picks) = best(0, 0)
        return zip(creatures, picks).compactMap { pair, j in j >= 0 ? (pair.1, charms[j].0, charms[j].1) : nil }
    }

    /// Charms menores recomendados: cada criatura admite uno además del mayor. Los de leech dan más
    /// cuantos más ataques van a la criatura, así que van en las más atacadas (kills por hora × vida):
    /// para knights primero Vampiric Embrace (life leech); para el resto, Void's Call (mana leech).
    private var minorPicks: [(creature: BestiaryCreature, charm: CharmInfo, tier: Int)] {
        let order = char.vocation == .knight ? ["Vampiric Embrace", "Void's Call"] : ["Void's Call", "Vampiric Embrace"]
        let charms = order.compactMap { name in CharmCatalog.minor.first { $0.name == name } }
        let ranked = hunt.sorted { Double($0.0.perHour) * Double($0.1.hp) > Double($1.0.perHour) * Double($1.1.hp) }
        return zip(ranked, charms).map { pair, charm in (pair.1, charm, char.charms[charm.name] ?? 0) }
    }

    /// El leech que añade cada charm menor de leech en cada nivel.
    private static let leech: [String: [Double]] = ["Void's Call": [0.8, 1.2, 1.6], "Vampiric Embrace": [1.6, 2.4, 3.2]]

    private var minorSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Image(systemName: "circle.hexagongrid.fill").foregroundStyle(Self.purple)
                Text("Charm menor recomendado").font(.system(size: 13, weight: .bold))
                Text("uno por criatura, además del mayor").font(.system(size: 11)).foregroundStyle(Color.muted)
            }
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 10)], alignment: .leading, spacing: 10) {
                ForEach(minorPicks, id: \.creature.name) { pick in minorTile(pick.creature, pick.charm, tier: pick.tier) }
            }
            Text("Solo suman si llevas equipo con mana o life leech. Para las demás criaturas: Gut si vendes sus productos, Scavenge si se pueden despellejar o Bless donde más mueres.")
                .font(.system(size: 11)).foregroundStyle(Color.muted).fixedSize(horizontal: false, vertical: true)
        }
    }

    private func minorTile(_ creature: BestiaryCreature, _ charm: CharmInfo, tier: Int) -> some View {
        let values = Self.leech[charm.name] ?? []
        let kind = charm.name == "Void's Call" ? "mana leech" : "life leech"
        let owned = tier > 0 && values.indices.contains(tier - 1)
        let detail = owned
            ? "+\(values[tier - 1].formatted()) % \(kind) · nivel \(["I", "II", "III"][tier - 1])"
            : "+\(values.map { $0.formatted() }.joined(separator: " / ")) % \(kind) · sin desbloquear"
        return HStack(spacing: 10) {
            WikiSprite(name: charm.image, size: 34)
            Image(systemName: "arrow.right").font(.system(size: 10, weight: .bold)).foregroundStyle(Color.muted)
            WikiSprite(name: creature.name, size: 34)
            VStack(alignment: .leading, spacing: 2) {
                Text(charm.name).font(.system(size: 12, weight: .bold))
                Text(creature.name).font(.system(size: 11)).foregroundStyle(Color.muted).lineLimit(1)
                Text(detail).font(.system(size: 10, weight: owned ? .semibold : .regular))
                    .foregroundStyle(owned ? Color.green : Color.amber)
            }
            Spacer(minLength: 0)
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 10).fill(Self.purple.opacity(0.08)))
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(Self.purple.opacity(0.3)))
        .help(charm.effect)
    }

    private var optimizerHelp: String {
        "Pon las criaturas de tu hunt y cuántas matas por hora (o elige un preset de hunt, con las kills por hora del Charm Optimizer de hakaimarket.com): el optimizador reparte tus charms de daño (los elementales, Overpower y Overflux) para sacar el máximo daño extra. Los elementales hacen el 5 % de la vida de la criatura (como mucho el doble de tu nivel) por su debilidad a ese elemento; Overpower, el 5 % de tu vida, y Overflux, el 2,5 % de tu maná (los dos hasta el 8 % de la vida de la criatura). Se activan con un 5 / 10 / 11 % de probabilidad según el nivel del charm. Low Blow y Savage Blow dependen de los críticos de tu arma y no se calculan. Además, cada criatura admite un charm menor: se recomiendan Void's Call (mana leech) y Vampiric Embrace (life leech) en las criaturas a las que más atacas. En el juego solo se puede poner un charm en criaturas con el Bestiary completo."
    }

    // MARK: Configuración

    private var setup: some View {
        VStack(alignment: .leading, spacing: 14) {
            // Personaje
            HStack(spacing: 8) {
                Text("Personaje").font(.system(size: 12, weight: .semibold)).foregroundStyle(Color.muted).frame(width: 90, alignment: .leading)
                Chip(text: "NIVEL \(char.level.formatted())", tint: .green)
                Chip(text: char.vocation.title.uppercased(), tint: Color.accentBlue)
                Spacer()
                Label(char.vocation.hitPoints(char.level).formatted(), systemImage: "heart.fill").foregroundStyle(.red)
                Label(char.vocation.mana(char.level).formatted(), systemImage: "bolt.fill").foregroundStyle(Color.accentBlue)
            }
            .font(.system(size: 12, weight: .semibold)).monospacedDigit()
            // Charms de daño
            HStack(alignment: .top, spacing: 8) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Tus charms").font(.system(size: 12, weight: .semibold)).foregroundStyle(Color.muted)
                    Text("clic: nivel").font(.system(size: 10)).foregroundStyle(Color.muted)
                }
                .frame(width: 90, alignment: .leading)
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 62), spacing: 6)], alignment: .leading, spacing: 6) {
                    ForEach(CharmCatalog.damage) { charm in
                        CharmToggle(charm: charm, tier: Binding(get: { char.charms[charm.name] ?? 0 },
                                                                set: { char.charms[charm.name] = $0 > 0 ? $0 : nil }))
                    }
                }
            }
            Divider().overlay(Color.white.opacity(0.06))
            // Hunt
            HStack(spacing: 8) {
                Text("Tu hunt").font(.system(size: 12, weight: .semibold)).foregroundStyle(Color.muted).frame(width: 90, alignment: .leading)
                TextField("Añadir criatura…", text: $search)
                    .onSubmit { if let first = matches.first { addCreature(first.name) } }
                SmallButton(title: "Presets de hunt", icon: "scope") { choosingPreset = true }
                    .help("Elige una hunt conocida y se ponen sus criaturas con las kills por hora (de hakaimarket.com)")
                    .popover(isPresented: $choosingPreset, arrowEdge: .bottom) {
                        HuntPresetPicker(level: char.level) { preset in
                            char.hunt = preset.creatures
                            search = ""
                            choosingPreset = false
                        }
                    }
                if !char.hunt.isEmpty {
                    SmallButton(title: "Vaciar", icon: "trash") { char.hunt = [] }
                }
            }
            if !matches.isEmpty {
                VStack(spacing: 0) {
                    ForEach(matches.prefix(6)) { c in
                        Button { addCreature(c.name) } label: {
                            HStack(spacing: 8) {
                                WikiSprite(name: c.name, size: 24)
                                Text(c.name).font(.system(size: 12))
                                Spacer()
                                Text("\(c.hp.formatted()) HP").font(.system(size: 11)).foregroundStyle(Color.muted)
                                Image(systemName: "plus.circle.fill").foregroundStyle(Color.accentBlue)
                            }
                            .padding(.horizontal, 10).padding(.vertical, 4)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
                .background(RoundedRectangle(cornerRadius: 8).fill(Color.white.opacity(0.04)))
                .padding(.leading, 98)
            }
            if hunt.isEmpty {
                HStack(spacing: 10) {
                    Image(systemName: "scope").font(.system(size: 20)).foregroundStyle(Color.muted)
                    Text("Añade las criaturas que cazas (o elige un preset de hunt) y verás qué charm conviene en cada una.")
                        .font(.system(size: 12)).foregroundStyle(Color.muted)
                }
                .padding(.leading, 98)
            } else {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 8)], alignment: .leading, spacing: 8) {
                    ForEach(hunt, id: \.1.name) { entry, creature in huntCard(entry, creature) }
                }
            }
        }
        .padding(16)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }

    private var matches: [BestiaryCreature] {
        let text = search.trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty else { return [] }
        return BestiaryCatalog.creatures.filter { c in
            c.name.localizedCaseInsensitiveContains(text) && !char.hunt.contains { $0.name == c.name }
        }
    }

    private func addCreature(_ name: String) {
        guard !char.hunt.contains(where: { $0.name == name }) else { return }
        char.hunt.append(HuntCreature(name: name))
        search = ""
    }

    /// El elemento al que más daño recibe (si es más del 100 %).
    private func weakness(_ c: BestiaryCreature) -> (String, Int)? {
        let names = ["physical": "físico", "fire": "fuego", "ice": "hielo", "energy": "energía", "earth": "tierra", "holy": "sagrado", "death": "muerte"]
        let order = ["physical", "fire", "ice", "energy", "earth", "holy", "death"]
        let weak = order.filter { c.mod($0) > 100 }
        guard let top = weak.map({ c.mod($0) }).max() else { return nil }
        // Si empatan varios elementos, todos ("fuego y tierra").
        let names2 = weak.filter { c.mod($0) == top }.map { names[$0] ?? $0 }
        return (names2.joined(separator: " y "), top)
    }

    private func huntCard(_ entry: HuntCreature, _ creature: BestiaryCreature) -> some View {
        let binding = Binding(get: { char.hunt.first { $0.name == entry.name }?.perHour ?? 100 },
                              set: { value in if let i = char.hunt.firstIndex(where: { $0.name == entry.name }) { char.hunt[i].perHour = max(1, value) } })
        return HStack(spacing: 10) {
            WikiSprite(name: creature.name, size: 40)
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 4) {
                    Text(creature.name).font(.system(size: 12, weight: .semibold)).lineLimit(1)
                    if !char.completed(creature) {
                        Image(systemName: "exclamationmark.triangle.fill").font(.system(size: 9)).foregroundStyle(Color.amber)
                            .help("Bestiary sin completar: en el juego todavía no se le puede poner un charm")
                    }
                }
                Text("\(creature.hp.formatted()) HP\(weakness(creature).map { " · débil a \($0.0) (\($0.1) %)" } ?? "")")
                    .font(.system(size: 10)).foregroundStyle(Color.muted).lineLimit(1)
                HStack(spacing: 4) {
                    stepButton("minus") { binding.wrappedValue -= 50 }
                    TextField("", value: binding, format: .number).frame(width: 52).multilineTextAlignment(.center)
                    stepButton("plus") { binding.wrappedValue += 50 }
                    Text("/ hora").font(.system(size: 10)).foregroundStyle(Color.muted)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color.white.opacity(0.04)))
        .overlay(alignment: .topTrailing) {
            Button { char.hunt.removeAll { $0.name == entry.name } } label: {
                Image(systemName: "xmark").font(.system(size: 9, weight: .bold)).foregroundStyle(Color.muted)
                    .frame(width: 20, height: 20).contentShape(Rectangle())
            }
            .buttonStyle(.plain).padding(4).help("Quitar de la hunt")
        }
    }

    private func stepButton(_ icon: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon).font(.system(size: 9, weight: .bold))
                .frame(width: 20, height: 20).background(Circle().fill(Color.white.opacity(0.08)))
        }
        .buttonStyle(.plain)
    }

    // MARK: Resultado

    @ViewBuilder private var results: some View {
        let picks = assignment
        let ownsNone = CharmCatalog.damage.allSatisfy { (char.charms[$0.name] ?? 0) == 0 }
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                Image(systemName: "trophy.fill").foregroundStyle(Color.amber)
                Text("Mejor combinación").font(.system(size: 15, weight: .bold))
                Chip(text: "BEST", tint: Color.amber)
                Spacer()
                if ownsNone {
                    Text("Con todos los charms de daño: marca arriba los que tienes").font(.system(size: 11)).foregroundStyle(Color.amber)
                }
            }
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 160), spacing: 10)], alignment: .leading, spacing: 10) {
                ForEach(picks, id: \.creature.name) { pick in
                    let dmg = damage(pick.charm, pick.creature)
                    let chance = CharmInfo.chances[pick.tier - 1]
                    VStack(spacing: 6) {
                        HStack(alignment: .center, spacing: 8) {
                            WikiSprite(name: pick.charm.image, size: 38)
                            Image(systemName: "arrow.right").font(.system(size: 11, weight: .bold)).foregroundStyle(Color.muted)
                            WikiSprite(name: pick.creature.name, size: 38)
                        }
                        Text(pick.charm.name).font(.system(size: 13, weight: .bold))
                        Text(pick.creature.name).font(.system(size: 11)).foregroundStyle(Color.muted).lineLimit(1)
                        Text(dmg.formatted()).font(.system(size: 20, weight: .bold)).monospacedDigit().foregroundStyle(Color.green)
                        Text("por activación · \(Int(chance * 100)) % (nivel \(["I", "II", "III"][pick.tier - 1]))")
                            .font(.system(size: 10)).foregroundStyle(Color.muted)
                    }
                    .padding(12)
                    .frame(maxWidth: .infinity)
                    .background(RoundedRectangle(cornerRadius: 10).fill(Color.black.opacity(0.25)))
                    .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.white.opacity(0.06)))
                }
            }
            if picks.count < hunt.count {
                Text("Hay más criaturas que charms de daño: las que quedan sin charm son en las que menos aporta.")
                    .font(.system(size: 11)).foregroundStyle(Color.muted)
            }
            if !minorPicks.isEmpty { minorSection }
            Button { withAnimation(.snappy) { showTable.toggle() } } label: {
                Label(showTable ? "Ocultar el daño de cada charm" : "Ver el daño de cada charm en cada criatura",
                      systemImage: showTable ? "chevron.up" : "tablecells")
                    .font(.system(size: 12, weight: .medium)).foregroundStyle(Color.accentBlue)
            }
            .buttonStyle(.plain)
            if showTable { damageTable }
        }
        .padding(16)
        .background(RoundedRectangle(cornerRadius: 12).fill(LinearGradient(colors: [Color.amber.opacity(0.10), Color.rowBackground],
                                                                           startPoint: .top, endPoint: .bottom)))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.amber.opacity(0.45), lineWidth: 1.5))
    }

    /// Daño por activación de cada charm en cada criatura (el mejor de cada una, resaltado).
    private var damageTable: some View {
        let charms = usable.map(\.0)
        return ScrollView(.horizontal) {
            Grid(alignment: .trailing, horizontalSpacing: 0, verticalSpacing: 0) {
                GridRow {
                    Text("").frame(width: 150)
                    ForEach(charms) { charm in
                        VStack(spacing: 2) {
                            WikiSprite(name: charm.image, size: 20)
                            Text(charm.name).font(.system(size: 9)).lineLimit(1)
                        }
                        .frame(width: 72)
                    }
                }
                .padding(.bottom, 4)
                ForEach(Array(hunt.enumerated()), id: \.element.1.name) { index, pair in
                    let creature = pair.1
                    let values = charms.map { damage($0, creature) }
                    let top = values.max() ?? 0
                    GridRow {
                        HStack(spacing: 6) {
                            WikiSprite(name: creature.name, size: 20)
                            Text(creature.name).font(.system(size: 11)).lineLimit(1)
                            Spacer(minLength: 0)
                        }
                        .frame(width: 150)
                        ForEach(Array(values.enumerated()), id: \.offset) { _, value in
                            Text(value.formatted()).font(.system(size: 11, weight: value == top ? .bold : .regular)).monospacedDigit()
                                .foregroundStyle(value == top && value > 0 ? Color.green : Color.white.opacity(0.75))
                                .frame(width: 72, alignment: .center)
                        }
                    }
                    .padding(.vertical, 5)
                    .background(index % 2 == 0 ? Color.white.opacity(0.03) : .clear)
                }
            }
        }
    }
}

/// Un charm de daño en la configuración del optimizador: clic para cambiar su nivel (No, I, II, III).
struct CharmToggle: View {
    let charm: CharmInfo
    @Binding var tier: Int

    var body: some View {
        Button { tier = (tier + 1) % 4 } label: {
            VStack(spacing: 3) {
                WikiSprite(name: charm.image, size: 28).opacity(tier > 0 ? 1 : 0.35)
                Text(charm.name).font(.system(size: 10, weight: .medium)).lineLimit(1).minimumScaleFactor(0.8)
                Text(tier > 0 ? ["I", "II", "III"][tier - 1] : "—").font(.system(size: 9, weight: .bold))
                    .foregroundStyle(tier > 0 ? Color(red: 0.69, green: 0.45, blue: 0.98) : Color.muted)
            }
            .padding(.vertical, 6).padding(.horizontal, 4)
            .frame(maxWidth: .infinity)
            .background(RoundedRectangle(cornerRadius: 8).fill(tier > 0 ? Color(red: 0.69, green: 0.45, blue: 0.98).opacity(0.14) : Color.white.opacity(0.03)))
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(tier > 0 ? Color(red: 0.69, green: 0.45, blue: 0.98).opacity(0.5) : Color.white.opacity(0.06)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(tier > 0 ? "\(charm.name) nivel \(tier). Clic para cambiarlo." : "No tienes \(charm.name). Clic para marcarlo.")
    }
}

/// Un charm: imagen, nombre y el nivel que tiene (I, II, III). Clic: su efecto y elegir el nivel.
struct CharmTile: View {
    let charm: CharmInfo
    @Binding var tier: Int
    @State private var open = false

    var body: some View {
        Button { open = true } label: {
            VStack(spacing: 4) {
                WikiSprite(name: charm.image, size: 36).opacity(tier > 0 ? 1 : 0.4)
                Text(charm.name).font(.system(size: 11, weight: .medium)).lineLimit(1).minimumScaleFactor(0.8)
                HStack(spacing: 3) {
                    ForEach(1...3, id: \.self) { level in
                        Capsule().fill(level <= tier ? Color(red: 0.69, green: 0.45, blue: 0.98) : Color.white.opacity(0.12))
                            .frame(width: 14, height: 4)
                    }
                }
            }
            .padding(8)
            .frame(maxWidth: .infinity)
            .background(RoundedRectangle(cornerRadius: 10).fill(tier > 0 ? Color(red: 0.69, green: 0.45, blue: 0.98).opacity(0.1) : Color.rowBackground))
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(open ? Color.accentBlue : tier > 0 ? Color(red: 0.69, green: 0.45, blue: 0.98).opacity(0.4) : Color.white.opacity(0.06)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(charm.effect)
        .popover(isPresented: $open, arrowEdge: .bottom) {
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 10) {
                    WikiSprite(name: charm.image, size: 40)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(charm.name).font(.system(size: 15, weight: .bold))
                        Text(charm.major ? "Charm mayor" : "Charm menor").font(.system(size: 11)).foregroundStyle(Color.muted)
                    }
                }
                Text(charm.effect).font(.system(size: 12)).fixedSize(horizontal: false, vertical: true)
                Text("NIVEL QUE TIENES").font(.system(size: 10, weight: .semibold)).tracking(1).foregroundStyle(Color.muted)
                HStack(spacing: 6) {
                    tierButton(0, "No", cost: nil)
                    ForEach(1...3, id: \.self) { level in
                        tierButton(level, ["I", "II", "III"][level - 1], cost: charm.costs.indices.contains(level - 1) ? charm.costs[level - 1] : nil)
                    }
                }
            }
            .padding(16)
            .frame(width: 320)
        }
    }

    private func tierButton(_ level: Int, _ title: String, cost: Int?) -> some View {
        Button { tier = level } label: {
            VStack(spacing: 1) {
                Text(title).font(.system(size: 13, weight: .bold))
                Text(cost.map { $0.formatted() } ?? "—").font(.system(size: 9)).foregroundStyle(tier == level ? .white : Color.muted)
            }
            .frame(maxWidth: .infinity).padding(.vertical, 6)
            .background(RoundedRectangle(cornerRadius: 8).fill(tier == level ? Color.accentBlue : Color.white.opacity(0.06)))
        }
        .buttonStyle(.plain)
        .help(cost.map { "Cuesta \($0.formatted()) charm points" } ?? "Sin desbloquear")
    }
}

/// Buscador de presets de hunt (hakaimarket.com), por ciudad: al elegir uno se ponen sus criaturas.
struct HuntPresetPicker: View {
    let level: Int
    let pick: (HuntPreset) -> Void
    @State private var search = ""

    var body: some View {
        let text = search.trimmingCharacters(in: .whitespaces)
        let list = HuntCatalog.presets.filter { p in
            text.isEmpty || p.name.localizedCaseInsensitiveContains(text) || p.city.localizedCaseInsensitiveContains(text)
                || p.creatures.contains { $0.name.localizedCaseInsensitiveContains(text) }
        }
        let cities = list.reduce(into: [String]()) { if $0.last != $1.city { $0.append($1.city) } }
        VStack(alignment: .leading, spacing: 8) {
            TextField("Buscar hunt, ciudad o criatura…", text: $search)
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0) {
                    ForEach(cities, id: \.self) { city in
                        Label(city, systemImage: "mappin.and.ellipse")
                            .font(.system(size: 10, weight: .bold)).foregroundStyle(Color.accentBlue)
                            .padding(.horizontal, 8).padding(.top, 8).padding(.bottom, 2)
                        ForEach(list.filter { $0.city == city }) { preset in row(preset) }
                    }
                    if list.isEmpty {
                        Text("Ninguna hunt coincide").font(.system(size: 11)).foregroundStyle(Color.muted).padding(8)
                    }
                }
            }
            .frame(height: 300)
            Text("Hunts y kills por hora del Charm Optimizer de hakaimarket.com").font(.system(size: 9)).foregroundStyle(Color.muted)
        }
        .padding(12)
        .frame(width: 340)
    }

    private func row(_ preset: HuntPreset) -> some View {
        Button { pick(preset) } label: {
            VStack(alignment: .leading, spacing: 2) {
                HStack {
                    Text(preset.name).font(.system(size: 12, weight: .medium)).lineLimit(1)
                    Spacer()
                    Text("Nv \(preset.minLevel.formatted())\(preset.premium ? " · Premium" : " · Free")")
                        .font(.system(size: 10)).monospacedDigit()
                        .foregroundStyle(preset.minLevel > level ? Color.amber : Color.muted)
                }
                Text(preset.creatures.map(\.name).joined(separator: ", "))
                    .font(.system(size: 10)).foregroundStyle(Color.muted).lineLimit(1)
            }
            .padding(.horizontal, 8).padding(.vertical, 4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(preset.minLevel > level ? "Pide nivel \(preset.minLevel.formatted()): más que el de este char" : preset.name)
    }
}

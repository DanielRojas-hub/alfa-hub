// Pestaña Inventario de un char: cuadrículas como en Tibia con lo equipado y lo guardado en cada sitio.

import AppKit
import SwiftUI

/// Los items del char en cuadrículas como en Tibia: el equipo en sus huecos y, al lado, un contenedor
/// por sitio (mochila, depot, casa…). Clic en una casilla para cambiar el item, cuántos hay, moverlo o
/// quitarlo; en "+" para añadir uno.
struct InventoryView: View {
    @Binding var char: GameChar
    let profile: Profile
    @State private var search = ""

    /// Los huecos del equipo como en Tibia (nil = vacío).
    private let layout: [[ItemSlot?]] = [
        [.amulet, .helmet, nil],
        [.weapon, .armor, .shield],
        [.ring, .legs, nil],
        [nil, .boots, nil],
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                TextField("Buscar un item en todos los chars del perfil", text: $search).frame(maxWidth: 320)
                Spacer()
                Text("\(char.items.count) \(char.items.count == 1 ? "item" : "items")")
                    .font(.system(size: 12)).foregroundStyle(Color.muted)
            }
            ScrollView {
                if !search.trimmingCharacters(in: .whitespaces).isEmpty {
                    results.padding(.bottom, 16)
                } else {
                    HStack(alignment: .top, spacing: 14) {
                        equipment
                        // Dos columnas que se rellenan una y otra (sin huecos cuando un sitio tiene más items).
                        let places = ItemPlace.allCases.filter { $0 != .equipped }
                        HStack(alignment: .top, spacing: 10) {
                            ForEach(0..<2, id: \.self) { column in
                                VStack(spacing: 10) {
                                    ForEach(places.indices.filter { $0 % 2 == column }, id: \.self) { container(places[$0]) }
                                }
                            }
                        }
                    }
                    .padding(.bottom, 16)
                }
            }
        }
    }

    private var otherChars: [String] { profile.chars.filter { $0.id != char.id }.map(\.name).filter { !$0.isEmpty } }

    private var equipment: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label(ItemPlace.equipped.title, systemImage: ItemPlace.equipped.icon)
                .font(.system(size: 12, weight: .semibold)).foregroundStyle(ItemPlace.equipped.tint)
            Grid(horizontalSpacing: 4, verticalSpacing: 4) {
                ForEach(0..<layout.count, id: \.self) { row in
                    GridRow {
                        ForEach(0..<3, id: \.self) { col in
                            if let slot = layout[row][col] {
                                if let item = char.equipped(slot) {
                                    ItemSquare(char: $char, id: item.id, otherChars: otherChars)
                                } else {
                                    AddSquare(char: $char, place: .equipped, slot: slot)
                                }
                            } else {
                                Color.clear.frame(width: 44, height: 44)
                            }
                        }
                    }
                }
            }
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }

    /// Un sitio: sus items agrupados por detalle ("Thais", "Edron"…) y la casilla "+".
    private func container(_ place: ItemPlace) -> some View {
        let items = char.items.filter { $0.place == place }
        let groups = Dictionary(grouping: items, by: \.detail).sorted { $0.key.localizedStandardCompare($1.key) == .orderedAscending }
        let columns = [GridItem(.adaptive(minimum: 44, maximum: 44), spacing: 4)]
        return VStack(alignment: .leading, spacing: 8) {
            HStack {
                Label(place.title, systemImage: place.icon).font(.system(size: 12, weight: .semibold)).foregroundStyle(place.tint)
                Spacer()
                if !items.isEmpty {
                    Text("\(items.count)").font(.system(size: 11)).foregroundStyle(Color.muted)
                }
            }
            ForEach(groups, id: \.key) { detail, group in
                if !detail.isEmpty {
                    Text(detail).font(.system(size: 11, weight: .medium)).foregroundStyle(Color.muted)
                }
                LazyVGrid(columns: columns, alignment: .leading, spacing: 4) {
                    ForEach(group) { item in ItemSquare(char: $char, id: item.id, otherChars: otherChars) }
                    if detail == groups.last?.key { AddSquare(char: $char, place: place, slot: nil, detail: detail) }
                }
            }
            if items.isEmpty {
                LazyVGrid(columns: columns, alignment: .leading, spacing: 4) {
                    AddSquare(char: $char, place: place, slot: nil)
                }
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }

    /// Dónde está cada item que coincide con la búsqueda, en cualquier char del perfil.
    @ViewBuilder private var results: some View {
        let text = search.trimmingCharacters(in: .whitespaces)
        // Lo del char abierto se toma de char (el perfil puede ir un paso por detrás mientras se edita).
        let chars = profile.chars.map { $0.id == char.id ? char : $0 }
        let found = chars.flatMap { c in c.items.filter { $0.item.localizedCaseInsensitiveContains(text) }.map { (c.name, $0) } }
        VStack(spacing: 8) {
            if found.isEmpty {
                Text("Ningún char del perfil tiene “\(text)”").font(.system(size: 12)).foregroundStyle(Color.muted)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            ForEach(found, id: \.1.id) { charName, item in
                HStack(spacing: 12) {
                    WikiSprite(name: item.item)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(item.count > 1 ? "\(item.count) × \(item.item)" : item.item).font(.system(size: 13, weight: .semibold))
                        Text(charName.isEmpty ? "Sin nombre" : charName).font(.system(size: 11)).foregroundStyle(Color.muted)
                    }
                    Spacer()
                    PlaceChip(piece: item)
                }
                .padding(.vertical, 8).padding(.horizontal, 12)
                .background(RoundedRectangle(cornerRadius: 10).fill(Color.rowBackground))
            }
        }
    }
}

/// Casilla con un item (sprite y cantidad); clic para editarlo.
struct ItemSquare: View {
    @Binding var char: GameChar
    let id: UUID
    let otherChars: [String]
    @State private var editing = false

    var body: some View {
        let item = char.items.first { $0.id == id }
        Button { editing = true } label: {
            ZStack {
                SquareBackground(highlighted: editing)
                if let item { WikiSprite(name: item.item) }
            }
            .frame(width: 44, height: 44)
            .overlay(alignment: .bottomTrailing) {
                if let item, item.count > 1 {
                    Text("\(item.count)").font(.system(size: 9, weight: .bold)).monospacedDigit()
                        .shadow(color: .black, radius: 1).padding(3)
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(item.map { "\($0.count > 1 ? "\($0.count) × " : "")\($0.item) — \($0.placeText)" } ?? "")
        .popover(isPresented: $editing, arrowEdge: .trailing) {
            ItemEditor(char: $char, id: id, otherChars: otherChars) { editing = false }
        }
    }
}

/// Casilla vacía: un hueco del equipo sin nada o el "+" de un contenedor.
struct AddSquare: View {
    @Binding var char: GameChar
    let place: ItemPlace
    let slot: ItemSlot?
    var detail = ""
    @State private var adding = false

    var body: some View {
        Button { adding = true } label: {
            ZStack {
                SquareBackground(highlighted: adding)
                if let slot {
                    Text(slot.title).font(.system(size: 8, weight: .medium)).foregroundStyle(Color.white.opacity(0.3))
                } else {
                    Image(systemName: "plus").font(.system(size: 12, weight: .semibold)).foregroundStyle(Color.white.opacity(0.35))
                }
            }
            .frame(width: 44, height: 44)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(slot.map { "Equipar \($0.title.lowercased())" } ?? "Añadir un item en \(place.title.lowercased())")
        .popover(isPresented: $adding, arrowEdge: .trailing) {
            VStack(alignment: .leading, spacing: 10) {
                Text(slot.map { "Equipar \($0.title.lowercased())" } ?? "Añadir en \(place.title.lowercased())")
                    .font(.system(size: 14, weight: .bold))
                ItemSearchList(slot: slot, selected: nil) { name in
                    char.items.append(OwnedItem(item: name, place: place, slot: slot, detail: detail))
                    adding = false
                }
            }
            .padding(14)
            .frame(width: 300)
        }
    }
}

struct SquareBackground: View {
    let highlighted: Bool
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 5).fill(Color.black.opacity(0.4))
            RoundedRectangle(cornerRadius: 5).stroke(highlighted ? Color.accentBlue : Color.white.opacity(0.1))
        }
    }
}

/// Buscador de items (de un hueco o de todos) con sus sprites; "Usar “…”" para uno que no está.
struct ItemSearchList: View {
    let slot: ItemSlot?
    let selected: String?
    let pick: (String) -> Void
    @State private var search = ""

    var body: some View {
        let text = search.trimmingCharacters(in: .whitespaces)
        let items = ItemCatalog.items(for: slot).filter { text.isEmpty || $0.name.localizedCaseInsensitiveContains(text) }
        VStack(alignment: .leading, spacing: 8) {
            TextField(slot.map { "Buscar \($0.title.lowercased())" } ?? "Buscar item", text: $search)
                .onSubmit { if let first = items.first { pick(first.name) } else if !text.isEmpty { pick(text) } }
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0) {
                    if !text.isEmpty && !items.contains(where: { $0.name.caseInsensitiveCompare(text) == .orderedSame }) {
                        row(text, label: "Usar “\(text)”")
                    }
                    ForEach(items, id: \.name) { row($0.name, label: $0.name) }
                }
            }
            .frame(height: 210)
            .background(RoundedRectangle(cornerRadius: 8).fill(Color.white.opacity(0.04)))
        }
    }

    private func row(_ name: String, label: String) -> some View {
        let isSelected = selected == name
        return Button { pick(name) } label: {
            HStack(spacing: 8) {
                WikiSprite(name: name, size: 26)
                Text(label).font(.system(size: 12, weight: isSelected ? .semibold : .regular)).lineLimit(1)
                Spacer()
                if isSelected { Image(systemName: "checkmark").font(.system(size: 11, weight: .bold)).foregroundStyle(Color.accentBlue) }
            }
            .padding(.horizontal, 8).padding(.vertical, 3)
            .background(isSelected ? Color.accentBlue.opacity(0.15) : .clear)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// Un item: cambiarlo por otro, cuántos hay, dónde está (moverlo) y quitarlo.
struct ItemEditor: View {
    @Binding var char: GameChar
    let id: UUID
    let otherChars: [String]
    let close: () -> Void

    var body: some View {
        if let index = char.items.firstIndex(where: { $0.id == id }) {
            let item = char.items[index]
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 10) {
                    WikiSprite(name: item.item)
                    Text(item.item).font(.system(size: 14, weight: .bold)).lineLimit(2)
                }
                ItemSearchList(slot: item.place == .equipped ? item.slot : nil, selected: item.item) { name in
                    char.items[index].item = name
                }
                Divider()
                HStack(spacing: 8) {
                    Text("Cantidad").font(.system(size: 12)).foregroundStyle(Color.muted)
                    TextField("", value: $char.items[index].count, format: .number).frame(width: 60)
                    Stepper("", value: $char.items[index].count, in: 1...100_000).labelsHidden()
                }
                Text("Dónde está").font(.system(size: 11, weight: .semibold)).foregroundStyle(Color.muted)
                Picker("", selection: Binding(get: { item.place }, set: { move(index, to: $0) })) {
                    ForEach(ItemPlace.allCases) { Label($0.title, systemImage: $0.icon).tag($0) }
                }
                .labelsHidden()
                if item.place == .equipped {
                    Picker("", selection: Binding(get: { item.slot ?? .weapon }, set: { equip(index, in: $0) })) {
                        ForEach(ItemSlot.allCases) { Text($0.title).tag($0) }
                    }
                    .labelsHidden()
                } else {
                    HStack(spacing: 6) {
                        TextField(item.place.detailPrompt, text: $char.items[index].detail)
                        if item.place == .otherChar && !otherChars.isEmpty {
                            Menu {
                                ForEach(otherChars, id: \.self) { name in Button(name) { char.items[index].detail = name } }
                            } label: { Image(systemName: "person.2") }
                            .menuIndicator(.hidden).fixedSize()
                        }
                    }
                }
                Button(role: .destructive) {
                    close()
                    char.items.removeAll { $0.id == id }
                } label: { Label("Quitar item", systemImage: "trash") }
                    .buttonStyle(.plain).foregroundStyle(Color.red).font(.system(size: 12, weight: .medium))
            }
            .padding(14)
            .frame(width: 300)
        }
    }

    /// Moverlo de sitio; al equiparlo va a su hueco (y lo que hubiera ahí pasa a la mochila).
    private func move(_ index: Int, to place: ItemPlace) {
        if place == .equipped {
            equip(index, in: char.items[index].slot ?? ItemCatalog.slot(of: char.items[index].item) ?? .weapon)
        } else {
            char.items[index].place = place
            char.items[index].slot = nil
            char.items[index].detail = ""
        }
    }

    private func equip(_ index: Int, in slot: ItemSlot) {
        let id = char.items[index].id
        for i in char.items.indices where char.items[i].place == .equipped && char.items[i].slot == slot && char.items[i].id != id {
            char.items[i].place = .backpack
            char.items[i].slot = nil
        }
        char.items[index].place = .equipped
        char.items[index].slot = slot
        char.items[index].detail = ""
    }
}


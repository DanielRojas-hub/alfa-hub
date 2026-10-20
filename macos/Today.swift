// Página Hoy: dónde está Rashid, cuándo es el server save, cuándo termina el Tibiadrome y las
// criaturas boosteadas del día.
//
// Rashid, el server save y el Tibiadrome se calculan aquí: el server save es todos los días a las
// 10:00 de Alemania (CET/CEST), Rashid cambia de ciudad en cada server save según el día de la semana
// y el Tibiadrome rota cada dos semanas, también en el server save (una rotación terminó el
// 14 oct 2026 y la siguiente termina el 28 oct 2026). La criatura y el boss boosteados se piden a la
// API de TibiaData (api.tibiadata.com), que los saca de tibia.com.

import AppKit
import SwiftUI

enum TibiaCalendar {
    static let berlin: Calendar = {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
        return calendar
    }()

    /// El último server save (el día de Tibia empieza en él).
    static func lastServerSave(_ now: Date = Date()) -> Date {
        let today = berlin.date(bySettingHour: 10, minute: 0, second: 0, of: now)!
        return today <= now ? today : berlin.date(byAdding: .day, value: -1, to: today)!
    }

    static func nextServerSave(_ now: Date = Date()) -> Date {
        berlin.date(byAdding: .day, value: 1, to: lastServerSave(now))!
    }

    /// Fin de una rotación conocida del Tibiadrome; las demás van de dos en dos semanas.
    static let dromeAnchor = berlin.date(from: DateComponents(year: 2026, month: 10, day: 28, hour: 10))!

    static func dromeEnd(_ now: Date = Date()) -> Date {
        let days = berlin.dateComponents([.day], from: dromeAnchor, to: lastServerSave(now)).day ?? 0
        let rotations = Int((Double(days) / 14).rounded(.down)) + 1
        return berlin.date(byAdding: .day, value: rotations * 14, to: dromeAnchor)!
    }

    /// Ciudad de Rashid y dónde encontrarlo, según el día de Tibia (1 = domingo … 7 = sábado).
    static let rashid: [Int: (city: String, place: String)] = [
        1: ("Carlin", "En el depot, un piso arriba"),
        2: ("Svargrond", "En la taberna de Dankwart, al sur del templo"),
        3: ("Liberty Bay", "En la taberna de Lyonel, al oeste del depot"),
        4: ("Port Hope", "En la taberna de Clyde, al oeste del depot"),
        5: ("Ankrahmun", "En la taberna de Arito, encima de la oficina de correos"),
        6: ("Darashia", "En la taberna de Miraia, al sur de los guildhalls"),
        7: ("Edron", "En la taberna de Mirabell, encima del depot"),
    ]

    static func rashid(on day: Date) -> (city: String, place: String) {
        rashid[berlin.component(.weekday, from: day)]!
    }
}

/// Criatura y boss boosteados (TibiaData). Se guardan para verlos aunque no haya conexión.
struct Boosted: Codable, Equatable {
    var creature: String
    var creatureImage: String
    var boss: String
    var bossImage: String
    var day: Date   // server save en el que empezaron
}

@MainActor
final class TodayModel: ObservableObject {
    @Published private(set) var boosted: Boosted?
    @Published private(set) var images: [String: Data] = [:]
    @Published private(set) var failed = false
    private var loading = false
    private var lastTry = Date.distantPast

    init() {
        boosted = UserDefaults.standard.data(forKey: "boosted").flatMap { try? JSONDecoder().decode(Boosted.self, from: $0) }
    }

    /// Pide los boosteados si los guardados son de otro día. Justo después del server save la API
    /// puede seguir dando los de ayer, así que en esos minutos se vuelve a preguntar.
    func refreshIfNeeded(now: Date = Date()) {
        let day = TibiaCalendar.lastServerSave(now)
        if let boosted, boosted.day == day {
            loadImages(boosted)
            return
        }
        guard !loading, now.timeIntervalSince(lastTry) > 60 else { return }
        loading = true
        lastTry = now
        Task {
            defer { loading = false }
            do {
                let fresh = try await Self.fetch()
                // En los primeros 15 min tras el server save no se da por buena si no ha cambiado.
                let settled = now.timeIntervalSince(day) > 15 * 60 || fresh.creature != boosted?.creature
                var result = fresh
                result.day = settled ? day : (boosted?.day ?? .distantPast)
                boosted = result
                failed = false
                UserDefaults.standard.set(try? JSONEncoder().encode(result), forKey: "boosted")
                loadImages(result)
            } catch {
                failed = true
            }
        }
    }

    private func loadImages(_ boosted: Boosted) {
        for url in [boosted.creatureImage, boosted.bossImage] where images[url] == nil {
            guard let link = URL(string: url) else { continue }
            images[url] = Data()  // pedida
            Task {
                if let (data, _) = try? await URLSession.shared.data(from: link) { images[url] = data } else { images[url] = nil }
            }
        }
    }

    private static func fetch() async throws -> Boosted {
        struct Entry: Decodable { let name: String; let image_url: String }
        struct Creatures: Decodable { struct Body: Decodable { let boosted: Entry }; let creatures: Body }
        struct Bosses: Decodable { struct Body: Decodable { let boosted: Entry }; let boostable_bosses: Body }
        async let creatureData = get("https://api.tibiadata.com/v4/creatures")
        async let bossData = get("https://api.tibiadata.com/v4/boostablebosses")
        let creature = try JSONDecoder().decode(Creatures.self, from: try await creatureData).creatures.boosted
        let boss = try JSONDecoder().decode(Bosses.self, from: try await bossData).boostable_bosses.boosted
        return Boosted(creature: creature.name, creatureImage: creature.image_url,
                       boss: boss.name, bossImage: boss.image_url, day: .distantPast)
    }

    private static func get(_ url: String) async throws -> Data {
        var request = URLRequest(url: URL(string: url)!)
        request.setValue("AlfaHub", forHTTPHeaderField: "User-Agent")
        request.cachePolicy = .reloadIgnoringLocalCacheData
        let (data, response) = try await URLSession.shared.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw URLError(.badServerResponse) }
        return data
    }
}

// MARK: - Página

struct TodayPage: View {
    @ObservedObject var today: TodayModel

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            PageHeader(title: HubPage.today.title, subtitle: HubPage.today.subtitle)
            TimelineView(.periodic(from: .now, by: 1)) { context in
                let now = context.date
                ScrollView {
                    Grid(horizontalSpacing: 12, verticalSpacing: 12) {
                        GridRow {
                            rashidCard(now)
                            dromeCard(now)
                        }
                        GridRow {
                            serverSaveCard(now)
                            boostedCard(now)
                        }
                    }
                    .padding(.bottom, 16)
                }
                .onAppear { today.refreshIfNeeded(now: now) }
                .onChange(of: TibiaCalendar.lastServerSave(now)) { today.refreshIfNeeded(now: now) }
                .onChange(of: Int(now.timeIntervalSince1970) / 60) { today.refreshIfNeeded(now: now) }
            }
        }
        .padding(.horizontal, 28)
        .padding(.top, 22)
    }

    private func rashidCard(_ now: Date) -> some View {
        let day = TibiaCalendar.lastServerSave(now)
        let rashid = TibiaCalendar.rashid(on: day)
        let tomorrow = TibiaCalendar.rashid(on: TibiaCalendar.nextServerSave(now))
        return TodayCard(title: "Rashid", icon: "bag.fill", tint: Color(red: 0.95, green: 0.70, blue: 0.25), sprite: "rashid") {
            Text(rashid.city).font(.system(size: 28, weight: .bold))
            Text(rashid.place).font(.system(size: 12)).foregroundStyle(Color.muted)
                .fixedSize(horizontal: false, vertical: true)
        } footer: {
            Text("Mañana: \(tomorrow.city)")
        }
    }

    private func dromeCard(_ now: Date) -> some View {
        let end = TibiaCalendar.dromeEnd(now)
        let progress = 1 - end.timeIntervalSince(now) / (14 * 86400)
        return TodayCard(title: "Tibiadrome", icon: "trophy.fill", tint: Color(red: 0.69, green: 0.45, blue: 0.98), sprite: "tibiadrome") {
            Text(countdown(end.timeIntervalSince(now))).font(.system(size: 28, weight: .bold)).monospacedDigit()
            ProgressBar(value: progress, color: Color(red: 0.69, green: 0.45, blue: 0.98)).padding(.top, 4)
            Text("Cada rotación dura 2 semanas").font(.system(size: 12)).foregroundStyle(Color.muted).padding(.top, 4)
        } footer: {
            Text("Termina el \(localDate(end))")
        }
    }

    private func serverSaveCard(_ now: Date) -> some View {
        let next = TibiaCalendar.nextServerSave(now)
        let left = next.timeIntervalSince(now)
        return TodayCard(title: "Server save", icon: "arrow.triangle.2.circlepath", tint: left < 15 * 60 ? .red : Color.accentBlue,
                         sprite: "server-save") {
            Text(hms(left)).font(.system(size: 28, weight: .bold)).monospacedDigit()
            ProgressBar(value: 1 - left / 86400, color: left < 15 * 60 ? .red : Color.accentBlue).padding(.top, 4)
            Text("Todos los días a las 10:00 de Alemania. Rashid y los boosteados cambian con él.")
                .font(.system(size: 12)).foregroundStyle(Color.muted).padding(.top, 4)
                .fixedSize(horizontal: false, vertical: true)
        } footer: {
            Text("\(sameDay(next, now) ? "Hoy" : "Mañana") a las \(localTime(next)) (tu hora)")
        }
    }

    private func boostedCard(_ now: Date) -> some View {
        TodayCard(title: "Boosteados", icon: "flame.fill", tint: Color(red: 1.0, green: 0.45, blue: 0.25)) {
            if let boosted = today.boosted {
                HStack(alignment: .bottom, spacing: 8) {
                    creature(boosted.creature, image: boosted.creatureImage, label: "Criatura")
                    creature(boosted.boss, image: boosted.bossImage, label: "Boss")
                }
            } else {
                Text(today.failed ? "Sin conexión con TibiaData" : "Cargando…")
                    .font(.system(size: 13)).foregroundStyle(Color.muted)
                    .frame(maxWidth: .infinity, minHeight: 90)
            }
        } footer: {
            if let boosted = today.boosted, boosted.day != TibiaCalendar.lastServerSave(now) {
                Text("Pueden ser los de ayer: actualizando…")
            } else {
                Text("Más experiencia y loot hasta el server save")
            }
        }
    }

    private func creature(_ name: String, image: String, label: String) -> some View {
        VStack(spacing: 4) {
            AnimatedImage(data: today.images[image])
                .frame(width: 64, height: 64)
            Text(name).font(.system(size: 13, weight: .semibold)).lineLimit(1).minimumScaleFactor(0.7)
            Text(label.uppercased()).font(.system(size: 9, weight: .semibold)).tracking(1).foregroundStyle(Color.muted)
        }
        .frame(maxWidth: .infinity)
    }

    private func hms(_ seconds: TimeInterval) -> String {
        let s = max(0, Int(seconds))
        return String(format: "%02d:%02d:%02d", s / 3600, s / 60 % 60, s % 60)
    }

    private func countdown(_ seconds: TimeInterval) -> String {
        let s = max(0, Int(seconds))
        return s < 86400 ? hms(seconds) : "\(s / 86400) d \(s / 3600 % 24) h"
    }

    private func sameDay(_ a: Date, _ b: Date) -> Bool { Calendar.current.isDate(a, inSameDayAs: b) }

    private func localTime(_ date: Date) -> String {
        date.formatted(.dateTime.hour(.twoDigits(amPM: .omitted)).minute(.twoDigits))
    }

    private func localDate(_ date: Date) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "es")
        formatter.dateFormat = "EEEE d 'de' MMMM 'a las' HH:mm"
        return formatter.string(from: date)
    }
}

/// Tarjeta de la página Hoy: ícono y título arriba, el dato grande y una línea al pie.
struct TodayCard<Content: View, Footer: View>: View {
    let title: String
    let icon: String
    let tint: Color
    /// Sprite de Tibia (Resources/<nombre>.gif, sacado de TibiaWiki) arriba a la derecha.
    var sprite: String? = nil
    @ViewBuilder var content: Content
    @ViewBuilder var footer: Footer

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Image(systemName: icon)
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(tint)
                    .frame(width: 26, height: 26)
                    .background(Circle().fill(tint.opacity(0.18)))
                Text(title.uppercased())
                    .font(.system(size: 11, weight: .semibold)).tracking(1)
                    .foregroundStyle(Color.muted)
            }
            VStack(alignment: .leading, spacing: 4) { content }
                .frame(maxWidth: .infinity, alignment: .leading)
            Spacer(minLength: 0)
            footer
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(Color.white.opacity(0.75))
        }
        .padding(16)
        .frame(maxWidth: .infinity, minHeight: 170, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(alignment: .top) {
            UnevenRoundedRectangle(topLeadingRadius: 12, topTrailingRadius: 12).fill(tint).frame(height: 3)
        }
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
        .overlay(alignment: .topTrailing) {
            if let sprite {
                AnimatedImage(data: todaySprites[sprite]).frame(width: 64, height: 64).padding(10)
            }
        }
    }
}

/// Sprites de las tarjetas (Resources/*.gif).
let todaySprites: [String: Data] = Dictionary(uniqueKeysWithValues: ["rashid", "server-save", "tibiadrome"].compactMap { name in
    Bundle.main.url(forResource: name, withExtension: "gif").flatMap { try? Data(contentsOf: $0) }.map { (name, $0) }
})

/// GIF animado (las criaturas de tibia.com).
struct AnimatedImage: NSViewRepresentable {
    let data: Data?

    func makeNSView(context: Context) -> NSImageView {
        let view = NSImageView()
        view.animates = true
        view.imageScaling = .scaleProportionallyDown
        view.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        view.setContentCompressionResistancePriority(.defaultLow, for: .vertical)
        return view
    }

    func updateNSView(_ view: NSImageView, context: Context) {
        guard context.coordinator.data != data else { return }
        context.coordinator.data = data
        view.image = data.flatMap { $0.isEmpty ? nil : NSImage(data: $0) }
    }

    func makeCoordinator() -> Coordinator { Coordinator() }
    final class Coordinator { var data: Data? }
}

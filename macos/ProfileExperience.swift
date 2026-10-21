// Pestaña Experiencia de un char, al estilo de guildstats.eu: mejor día, media diaria, puesto en el
// mundo, evolución de la experiencia o el nivel, experiencia ganada cada día, predicción de nivel y una
// tabla por día. Los datos son los que guarda Alfa Hub en cada sincronización (una por día de Tibia,
// también sin abrir el char); la experiencia sale de los highscores, así que solo se sabe si el char está
// entre los 1000 primeros de su mundo. Los días de antes de empezar a guardarlos no se pueden traer
// (guildstats.eu protege su historial con una comprobación de Cloudflare contra programas): hasta el
// segundo día se muestra solo el primer registro.

import Charts
import SwiftUI

struct ExperienceView: View {
    let char: GameChar
    @State private var chartMode = 0          // 0 experiencia, 1 nivel
    @State private var target: Int?

    /// Lo ganado en cada día de Tibia: entre dos sincronizaciones seguidas.
    struct Day: Identifiable {
        let date: Date          // el día de Tibia en que se ganó
        let gained: Int64?
        let level: Int
        let levels: Int         // niveles subidos ese día
        let experience: Int64?
        let rank: Int?
        var id: Date { date }
    }

    private var days: [Day] {
        let points = char.history
        return zip(points, points.dropFirst()).map { a, b in
            let gained: Int64? = a.experience.flatMap { ea in b.experience.map { $0 - ea } }
            let day = TibiaCalendar.berlin.date(byAdding: .day, value: -1, to: TibiaCalendar.lastServerSave(b.date)) ?? b.date
            return Day(date: day, gained: gained, level: b.level, levels: b.level - a.level, experience: b.experience, rank: b.rank)
        }
    }

    var body: some View {
        let days = days
        let gains = days.compactMap(\.gained)
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                if !char.isSynced {
                    EmptyCard(icon: "chart.line.uptrend.xyaxis", title: "Sin datos de tibia.com",
                              text: "Sincroniza el char con tibia.com (escribe su nombre arriba) y Alfa Hub empezará a guardar su experiencia cada día.")
                } else if days.isEmpty {
                    // Primer día: lo que se sabe hoy y cuándo empieza el historial.
                    summary(days, gains: gains)
                    firstDay
                } else {
                    summary(days, gains: gains)
                    evolution
                    if !gains.isEmpty { dailyChart(days) }
                    prediction(gains: gains)
                    table(days)
                }
                if char.isSynced && char.tibia?.experience == nil {
                    Label("Este char no sale entre los 1000 primeros de los highscores de su mundo: se guarda su nivel, pero no la experiencia exacta.",
                          systemImage: "info.circle")
                        .font(.system(size: 11)).foregroundStyle(Color.muted).fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(.bottom, 16)
        }
    }

    // MARK: Primer día

    private var firstDay: some View {
        let first = char.history.first
        return HStack(alignment: .top, spacing: 14) {
            Image(systemName: "calendar.badge.clock").font(.system(size: 26)).foregroundStyle(Color.accentBlue)
                .frame(width: 44, height: 44).background(Circle().fill(Color.accentBlue.opacity(0.12)))
            VStack(alignment: .leading, spacing: 6) {
                Text("Primer registro: \(first.map { longDate($0.date, time: true) } ?? "hoy")")
                    .font(.system(size: 14, weight: .semibold))
                if let first {
                    Text("Nivel \(first.level.formatted())\(first.experience.map { " · \($0.formatted()) exp" } ?? "")\(first.rank.map { " · #\($0) en \(char.world)" } ?? "")")
                        .font(.system(size: 12)).monospacedDigit().foregroundStyle(Color.white.opacity(0.85))
                }
                Text("Alfa Hub guarda el nivel y la experiencia de este char una vez por día de Tibia (después del server save), aunque no lo abras. Mañana, tras el server save, aparece lo que ganó hoy y se empiezan a llenar los gráficos, la media y la predicción.")
                    .font(.system(size: 12)).foregroundStyle(Color.muted).fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }

    // MARK: Resumen

    private func summary(_ days: [Day], gains: [Int64]) -> some View {
        let best = days.filter { $0.gained != nil }.max { ($0.gained ?? 0) < ($1.gained ?? 0) }
        let recent = Array(gains.suffix(7))
        let average = recent.isEmpty ? nil : recent.reduce(0, +) / Int64(recent.count)
        let level = char.level
        let start = Experience.forLevel(level), next = Experience.forLevel(level + 1)
        let exp = char.tibia?.experience
        return LazyVGrid(columns: [GridItem(.adaptive(minimum: 170), spacing: 10)], spacing: 10) {
            StatTile(icon: "crown.fill", tint: Color.amber, title: "Mejor día",
                     value: best?.gained.map { "+\(compactNumber($0))" } ?? "—",
                     detail: best.map { "\($0.date.formatted(.dateTime.day().month(.abbreviated))) · nivel \($0.level.formatted())" } ?? "")
            StatTile(icon: "calendar", tint: .green, title: "Media diaria",
                     value: average.map { "+\(compactNumber($0))" } ?? "—",
                     detail: recent.isEmpty ? "" : "últimos \(recent.count) \(recent.count == 1 ? "día" : "días")")
            StatTile(icon: "trophy.fill", tint: Color(red: 0.69, green: 0.45, blue: 0.98), title: "Puesto en \(char.world)",
                     value: char.tibia?.worldRank.map { "#\($0)" } ?? "—",
                     detail: rankChange(days))
            StatTile(icon: "arrow.up.forward", tint: Color.accentBlue, title: "Al nivel \((level + 1).formatted())",
                     value: exp.map { "\(Int(Double($0 - start) / Double(next - start) * 100)) %" } ?? "—",
                     detail: exp.map { "faltan \(compactNumber(next - $0)) exp" } ?? "")
        }
    }

    private func rankChange(_ days: [Day]) -> String {
        let ranks = days.compactMap(\.rank)
        guard let first = ranks.first, let last = ranks.last, ranks.count >= 2 else { return "" }
        let change = first - last
        return change == 0 ? "igual que hace \(ranks.count - 1) días" : "\(change > 0 ? "▲" : "▼") \(abs(change)) en \(ranks.count - 1) días"
    }

    // MARK: Gráficos

    private var evolution: some View {
        let points = char.history
        let byExperience = chartMode == 0 && points.contains { $0.experience != nil }
        let values = points.map { byExperience ? Double($0.experience ?? 0) : Double($0.level) }.filter { $0 > 0 }
        // Escala de los datos (con un margen), no desde 0: si no, la línea sale plana.
        let low = values.min() ?? 0, high = values.max() ?? 1
        let margin = max((high - low) * 0.15, byExperience ? 1 : 1)
        let domain = (low - margin)...(high + margin)
        return ChartCard(title: "Evolución", icon: "chart.xyaxis.line") {
            PillPicker(options: ["Experiencia", "Nivel"], selection: $chartMode)
        } content: {
            Chart(Array(points.enumerated()), id: \.offset) { _, point in
                let value = byExperience ? Double(point.experience ?? 0) : Double(point.level)
                AreaMark(x: .value("Día", point.date), yStart: .value("Base", domain.lowerBound), yEnd: .value("Valor", value))
                    .foregroundStyle(LinearGradient(colors: [Color.green.opacity(0.35), Color.green.opacity(0.02)], startPoint: .top, endPoint: .bottom))
                LineMark(x: .value("Día", point.date), y: .value("Valor", value))
                    .foregroundStyle(Color.green)
                    .lineStyle(StrokeStyle(lineWidth: 2))
                PointMark(x: .value("Día", point.date), y: .value("Valor", value))
                    .foregroundStyle(Color.green).symbolSize(18)
            }
            .chartYScale(domain: domain)
            .chartYAxis {
                AxisMarks(position: .leading, values: .automatic(desiredCount: 4)) { value in
                    AxisGridLine().foregroundStyle(Color.white.opacity(0.08))
                    AxisValueLabel { Text(byExperience ? compactNumber(Int64(value.as(Double.self) ?? 0)) : "\(Int(value.as(Double.self) ?? 0))") }
                }
            }
            .chartXAxis {
                AxisMarks(values: .automatic(desiredCount: 6)) { _ in
                    AxisGridLine().foregroundStyle(Color.white.opacity(0.05))
                    AxisValueLabel(format: .dateTime.day().month(.abbreviated))
                }
            }
            .frame(height: 170)
        }
    }

    private func dailyChart(_ days: [Day]) -> some View {
        let best = days.compactMap(\.gained).max()
        return ChartCard(title: "Experiencia por día", icon: "chart.bar.fill") {
            EmptyView()
        } content: {
            Chart(days.filter { $0.gained != nil }) { day in
                BarMark(x: .value("Día", day.date, unit: .day), y: .value("Experiencia", day.gained ?? 0))
                    .foregroundStyle(day.gained == best ? Color.amber : Color.green.opacity(0.75))
                    .cornerRadius(3)
            }
            .chartYAxis {
                AxisMarks(position: .leading, values: .automatic(desiredCount: 4)) { value in
                    AxisGridLine().foregroundStyle(Color.white.opacity(0.08))
                    AxisValueLabel { Text(compactNumber(Int64(value.as(Double.self) ?? 0))) }
                }
            }
            .chartXAxis {
                AxisMarks(values: .stride(by: .day, count: max(1, days.count / 8))) { _ in
                    AxisValueLabel(format: .dateTime.day().month(.abbreviated))
                }
            }
            .frame(height: 150)
        }
    }

    // MARK: Predicción

    private func prediction(gains: [Int64]) -> some View {
        let recent = Array(gains.suffix(7))
        let average = recent.isEmpty ? 0 : recent.reduce(0, +) / Int64(recent.count)
        let level = char.level
        let goal = max(level + 1, target ?? level + 10)
        let current = char.tibia?.experience ?? Experience.forLevel(level)
        let missing = Experience.forLevel(goal) - current
        let daysNeeded = average > 0 ? Int((Double(missing) / Double(average)).rounded(.up)) : nil
        return InfoCard(title: "Predicción de nivel", icon: "wand.and.stars") {
            HStack(spacing: 8) {
                Text("Nivel objetivo").font(.system(size: 12)).foregroundStyle(Color.muted)
                TextField("", value: Binding(get: { goal }, set: { target = $0 }), format: .number.grouping(.never))
                    .frame(width: 70)
                ForEach([10, 50, 100], id: \.self) { step in
                    Button("+\(step)") { target = level + step }
                        .buttonStyle(.plain)
                        .font(.system(size: 11, weight: .semibold))
                        .padding(.horizontal, 8).padding(.vertical, 3)
                        .background(Capsule().fill(Color.white.opacity(0.07)))
                }
                Spacer()
            }
            if let daysNeeded, let date = Calendar.current.date(byAdding: .day, value: daysNeeded, to: Date()) {
                (Text("Con una media de ") + Text("+\(compactNumber(average)) exp/día").bold()
                 + Text(" llegarías al nivel \(goal.formatted()) el ") + Text(longDate(date)).bold().foregroundColor(.green)
                 + Text(" (en \(daysNeeded) \(daysNeeded == 1 ? "día" : "días")). Faltan \(missing.formatted()) exp."))
                    .font(.system(size: 12)).fixedSize(horizontal: false, vertical: true)
            } else {
                Text("Hace falta al menos un día con experiencia ganada para calcularlo.")
                    .font(.system(size: 12)).foregroundStyle(Color.muted)
            }
        }
    }

    // MARK: Tabla

    private func table(_ days: [Day]) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                column("Día", width: 90, alignment: .leading)
                column("Exp ganada", width: 130)
                column("Puesto", width: 60)
                column("Nivel", width: 90)
                column("Experiencia", width: nil)
            }
            .font(.system(size: 10, weight: .semibold)).foregroundStyle(Color.muted)
            .padding(.horizontal, 12).padding(.vertical, 8)
            ForEach(Array(days.reversed().enumerated()), id: \.element.id) { index, day in
                HStack {
                    column(day.date.formatted(.dateTime.day().month(.abbreviated).year()), width: 90, alignment: .leading)
                    Text(day.gained.map { "\($0 >= 0 ? "+" : "")\($0.formatted())" } ?? "—")
                        .foregroundStyle((day.gained ?? 0) > 0 ? Color.green : Color.white.opacity(0.75))
                        .frame(width: 130, alignment: .trailing)
                    column(day.rank.map { "#\($0)" } ?? "—", width: 60)
                    Text("\(day.level.formatted())\(day.levels != 0 ? " (\(day.levels > 0 ? "+" : "")\(day.levels))" : "")")
                        .foregroundStyle(day.levels > 0 ? Color.green : day.levels < 0 ? Color.red : .white)
                        .frame(width: 90, alignment: .trailing)
                    column(day.experience.map { $0.formatted() } ?? "—", width: nil)
                }
                .font(.system(size: 12)).monospacedDigit()
                .padding(.horizontal, 12).padding(.vertical, 7)
                .background(index % 2 == 0 ? Color.white.opacity(0.03) : .clear)
            }
        }
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }

    private func column(_ text: String, width: CGFloat?, alignment: Alignment = .trailing) -> some View {
        Text(text).lineLimit(1).frame(width: width, alignment: alignment).frame(maxWidth: width == nil ? .infinity : nil, alignment: alignment)
    }
}

/// Recuadro con un dato: ícono, título, valor grande y detalle.
struct StatTile: View {
    let icon: String
    let tint: Color
    let title: String
    let value: String
    var detail = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Label(title, systemImage: icon).font(.system(size: 11, weight: .medium)).foregroundStyle(tint).lineLimit(1)
            Text(value).font(.system(size: 20, weight: .bold)).monospacedDigit().lineLimit(1).minimumScaleFactor(0.7)
            Text(detail.isEmpty ? " " : detail).font(.system(size: 11)).foregroundStyle(Color.muted).lineLimit(1)
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }
}

/// Tarjeta de un gráfico: título con controles a la derecha y el gráfico debajo.
struct ChartCard<Controls: View, Content: View>: View {
    let title: String
    let icon: String
    @ViewBuilder var controls: Controls
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Label(title.uppercased(), systemImage: icon)
                    .font(.system(size: 10, weight: .semibold)).tracking(1).foregroundStyle(Color.muted)
                Spacer()
                controls
            }
            content
        }
        .padding(14)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.rowBackground))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.06)))
    }
}

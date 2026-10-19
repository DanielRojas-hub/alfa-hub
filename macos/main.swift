// Alfa Hub — timer de ciclos para Tibia, basado en el "Peixe Timer" de cueistoreservices.com.br/timer.
//
// Dos ventanas:
//  • Overlay: solo el contador, encima de Tibia (también en pantalla completa) y solo
//    mientras el cliente de Tibia está al frente. Los clics lo atraviesan y llegan al juego.
//  • Hub: ventana normal con los controles y todos los ajustes (duración del ciclo, alertas,
//    sonido, colores, atajos y tamaño/opacidad/posición del overlay).
// Atajos globales editables (por defecto ⌃⌥P iniciar/pausar, ⌃⌥R reiniciar, ⌃⌥O overlay).

import AppKit
import AVFoundation
import Carbon.HIToolbox
import Combine
import SwiftUI

// MARK: - Sonido (beeps senoidales como los del sitio)

@MainActor
final class Beeper {
    static let shared = Beeper()
    var volume: Double = 0.5
    var muted = false
    private var cache: [String: Data] = [:]
    private var players: [AVAudioPlayer] = []

    func beep(_ freq: Double, _ duration: Double) {
        guard !muted, volume > 0 else { return }
        let key = "\(freq)-\(duration)"
        let data = cache[key] ?? Self.wav(freq: freq, duration: duration)
        cache[key] = data
        guard let player = try? AVAudioPlayer(data: data) else { return }
        player.volume = Float(volume)
        player.play()
        players.removeAll { !$0.isPlaying }
        players.append(player)
    }

    /// WAV mono 16-bit con caída exponencial 1 → 0.01 (igual que el gain ramp del sitio).
    private static func wav(freq: Double, duration: Double) -> Data {
        let rate = 44_100.0
        let count = Int(rate * duration)
        let decay = log(0.01) / duration
        var samples = [Int16](repeating: 0, count: count)
        for i in 0..<count {
            let t = Double(i) / rate
            let attack = min(1, t / 0.004)
            samples[i] = Int16(sin(2 * .pi * freq * t) * exp(decay * t) * attack * 32_000)
        }
        var d = Data()
        func u32(_ v: UInt32) { withUnsafeBytes(of: v.littleEndian) { d.append(contentsOf: $0) } }
        func u16(_ v: UInt16) { withUnsafeBytes(of: v.littleEndian) { d.append(contentsOf: $0) } }
        d.append(contentsOf: Array("RIFF".utf8)); u32(UInt32(36 + count * 2))
        d.append(contentsOf: Array("WAVEfmt ".utf8)); u32(16); u16(1); u16(1); u32(44_100); u32(88_200); u16(2); u16(16)
        d.append(contentsOf: Array("data".utf8)); u32(UInt32(count * 2))
        samples.withUnsafeBytes { d.append(contentsOf: $0) }
        return d
    }
}

// MARK: - Colores y atajos

extension Color {
    /// "RRGGBB" o "RRGGBBAA".
    init(hex: String) {
        var value: UInt64 = 0
        Scanner(string: hex.trimmingCharacters(in: CharacterSet(charactersIn: "#"))).scanHexInt64(&value)
        let hasAlpha = hex.trimmingCharacters(in: CharacterSet(charactersIn: "#")).count > 6
        let rgba = hasAlpha ? value : (value << 8) | 0xFF
        self.init(.sRGB,
                  red: Double((rgba >> 24) & 0xFF) / 255,
                  green: Double((rgba >> 16) & 0xFF) / 255,
                  blue: Double((rgba >> 8) & 0xFF) / 255,
                  opacity: Double(rgba & 0xFF) / 255)
    }

    var hex: String {
        let c = NSColor(self).usingColorSpace(.sRGB) ?? .white
        let parts = [c.redComponent, c.greenComponent, c.blueComponent, c.alphaComponent]
        return parts.map { String(format: "%02X", Int(($0 * 255).rounded())) }.joined()
    }

    var alpha: Double { Double(NSColor(self).usingColorSpace(.sRGB)?.alphaComponent ?? 1) }
}

struct Shortcut: Codable, Equatable {
    var keyCode: Int
    var modifiers: Int  // flags de Carbon: controlKey | optionKey | shiftKey | cmdKey
    var key: String     // nombre visible de la tecla

    var display: String {
        (modifiers & controlKey != 0 ? "⌃" : "") + (modifiers & optionKey != 0 ? "⌥" : "")
            + (modifiers & shiftKey != 0 ? "⇧" : "") + (modifiers & cmdKey != 0 ? "⌘" : "") + key
    }

    static let functionKeys: [Int: String] = [
        kVK_F1: "F1", kVK_F2: "F2", kVK_F3: "F3", kVK_F4: "F4", kVK_F5: "F5", kVK_F6: "F6",
        kVK_F7: "F7", kVK_F8: "F8", kVK_F9: "F9", kVK_F10: "F10", kVK_F11: "F11", kVK_F12: "F12",
        kVK_F13: "F13", kVK_F14: "F14", kVK_F15: "F15", kVK_F16: "F16", kVK_F17: "F17", kVK_F18: "F18",
    ]
    static let namedKeys: [Int: String] = [
        kVK_Space: "Espacio", kVK_Return: "↩", kVK_Tab: "⇥", kVK_Delete: "⌫", kVK_ForwardDelete: "⌦",
        kVK_LeftArrow: "←", kVK_RightArrow: "→", kVK_UpArrow: "↑", kVK_DownArrow: "↓",
        kVK_Home: "Inicio", kVK_End: "Fin", kVK_PageUp: "RePág", kVK_PageDown: "AvPág",
    ]

    /// nil si la combinación no sirve como atajo global (sin modificador y no es tecla F).
    init?(event: NSEvent) {
        let code = Int(event.keyCode)
        let flags = event.modifierFlags
        var mods = 0
        if flags.contains(.control) { mods |= controlKey }
        if flags.contains(.option) { mods |= optionKey }
        if flags.contains(.shift) { mods |= shiftKey }
        if flags.contains(.command) { mods |= cmdKey }
        let fKey = Self.functionKeys[code]
        guard mods != 0 || fKey != nil else { return nil }
        keyCode = code
        modifiers = mods
        key = fKey ?? Self.namedKeys[code]
            ?? (event.characters(byApplyingModifiers: [])?.uppercased()).flatMap { $0.isEmpty ? nil : $0 }
            ?? "#\(code)"
    }

    init(keyCode: Int, modifiers: Int, key: String) {
        self.keyCode = keyCode
        self.modifiers = modifiers
        self.key = key
    }
}

enum HotKeyAction: String, CaseIterable, Identifiable {
    case toggle, reset, overlay
    var id: String { rawValue }
    var title: String {
        switch self {
        case .toggle: "Iniciar / pausar"
        case .reset: "Reiniciar"
        case .overlay: "Mostrar / ocultar overlay"
        }
    }
    var defaultShortcut: Shortcut {
        let mods = controlKey | optionKey
        switch self {
        case .toggle: return Shortcut(keyCode: kVK_ANSI_P, modifiers: mods, key: "P")
        case .reset: return Shortcut(keyCode: kVK_ANSI_R, modifiers: mods, key: "R")
        case .overlay: return Shortcut(keyCode: kVK_ANSI_O, modifiers: mods, key: "O")
        }
    }
}

// MARK: - Modelo

/// Cronómetro basado en reloj real: no se desfasa aunque macOS frene el timer.
struct Stopwatch {
    private var accumulated: TimeInterval = 0
    private var startedAt: Date?
    var running: Bool { startedAt != nil }
    var elapsed: TimeInterval { accumulated + (startedAt.map { Date().timeIntervalSince($0) } ?? 0) }
    mutating func start() { if startedAt == nil { startedAt = Date() } }
    mutating func pause() { accumulated = elapsed; startedAt = nil }
    mutating func reset() { accumulated = 0; startedAt = nil }
}

@MainActor
final class HubModel: ObservableObject {
    static let defaultCycle = 120, defaultWarning = 25, defaultBeeps = 5
    static let defaultCounterColor = "A873FA", defaultAlertColor = "F54D4D", defaultBackgroundColor = "111827D9"

    // Estado del timer
    @Published private(set) var watch = Stopwatch()
    @Published private(set) var seconds = 0

    // Ajustes (se guardan entre aperturas)
    @Published var cycle: Int {
        didSet {
            let clamped = max(5, min(3600, cycle))
            if clamped != cycle { cycle = clamped; return }  // con @Published, reasignar vuelve a llamar didSet
            save(cycle, "cycle")
        }
    }
    @Published var warning: Int { didSet { save(warning, "warning") } }
    @Published var beepSeconds: Int { didSet { save(beepSeconds, "beepSeconds") } }
    @Published var cycleBeep: Bool { didSet { save(cycleBeep, "cycleBeep") } }
    @Published var volume: Double { didSet { Beeper.shared.volume = volume; save(volume, "volume") } }
    @Published var muted: Bool { didSet { Beeper.shared.muted = muted; save(muted, "muted") } }
    @Published var overlayVisible: Bool { didSet { save(overlayVisible, "overlayVisible") } }
    @Published var overlayScale: Double { didSet { save(overlayScale, "overlayScale") } }
    @Published var overlayOpacity: Double { didSet { save(overlayOpacity, "overlayOpacity") } }
    @Published var counterColor: Color { didSet { save(counterColor.hex, "counterColor") } }
    @Published var alertColor: Color { didSet { save(alertColor.hex, "alertColor") } }
    @Published var backgroundColor: Color { didSet { save(backgroundColor.hex, "backgroundColor") } }
    @Published private(set) var shortcuts: [HotKeyAction: Shortcut] {
        didSet {
            let raw = Dictionary(uniqueKeysWithValues: shortcuts.map { ($0.key.rawValue, $0.value) })
            save((try? JSONEncoder().encode(raw)) ?? Data(), "shortcuts")
        }
    }
    /// Solo mientras está activo el overlay recibe clics (para arrastrarlo). No se guarda.
    @Published var overlayUnlocked = false
    /// Mientras se graba un atajo, los globales se desactivan para poder capturar cualquier combinación.
    @Published var recordingShortcut = false

    private let defaults = UserDefaults.standard
    private func save(_ value: Any, _ key: String) { defaults.set(value, forKey: key) }

    init() {
        cycle = defaults.object(forKey: "cycle") as? Int ?? Self.defaultCycle
        warning = defaults.object(forKey: "warning") as? Int ?? Self.defaultWarning
        beepSeconds = defaults.object(forKey: "beepSeconds") as? Int ?? Self.defaultBeeps
        cycleBeep = defaults.object(forKey: "cycleBeep") as? Bool ?? true
        volume = defaults.object(forKey: "volume") as? Double ?? 0.5
        muted = defaults.object(forKey: "muted") as? Bool ?? false
        overlayVisible = defaults.object(forKey: "overlayVisible") as? Bool ?? true
        overlayScale = defaults.object(forKey: "overlayScale") as? Double ?? 1
        overlayOpacity = defaults.object(forKey: "overlayOpacity") as? Double ?? 1
        counterColor = Color(hex: defaults.string(forKey: "counterColor") ?? Self.defaultCounterColor)
        alertColor = Color(hex: defaults.string(forKey: "alertColor") ?? Self.defaultAlertColor)
        backgroundColor = Color(hex: defaults.string(forKey: "backgroundColor") ?? Self.defaultBackgroundColor)
        if let data = defaults.data(forKey: "shortcuts"),
           let raw = try? JSONDecoder().decode([String: Shortcut].self, from: data) {
            shortcuts = Dictionary(uniqueKeysWithValues: raw.compactMap { k, v in HotKeyAction(rawValue: k).map { ($0, v) } })
        } else {
            shortcuts = Dictionary(uniqueKeysWithValues: HotKeyAction.allCases.map { ($0, $0.defaultShortcut) })
        }
        Beeper.shared.volume = volume
        Beeper.shared.muted = muted
    }

    var running: Bool { watch.running }
    var remaining: Int { cycle - seconds % cycle }
    var progress: Double { Double(seconds % cycle) / Double(cycle) }
    var cycles: Int { seconds / cycle }
    var critical: Bool { remaining <= warning }

    func toggle() { if running { watch.pause() } else { watch.start() } }
    func reset() { watch.reset(); seconds = 0 }

    func restoreTimerDefaults() {
        cycle = Self.defaultCycle
        warning = Self.defaultWarning
        beepSeconds = Self.defaultBeeps
        cycleBeep = true
    }
    func restoreColors() {
        counterColor = Color(hex: Self.defaultCounterColor)
        alertColor = Color(hex: Self.defaultAlertColor)
        backgroundColor = Color(hex: Self.defaultBackgroundColor)
    }
    func restoreShortcuts() {
        shortcuts = Dictionary(uniqueKeysWithValues: HotKeyAction.allCases.map { ($0, $0.defaultShortcut) })
    }

    /// Asigna un atajo; si otra acción tenía la misma combinación, se la quita.
    func setShortcut(_ shortcut: Shortcut?, for action: HotKeyAction) {
        var updated = shortcuts
        if let shortcut {
            for (other, existing) in updated where other != action && existing == shortcut { updated[other] = nil }
        }
        updated[action] = shortcut
        shortcuts = updated
    }

    func perform(_ action: HotKeyAction) {
        switch action {
        case .toggle: toggle()
        case .reset: reset()
        case .overlay: overlayVisible.toggle()
        }
    }

    func tick() {
        guard running else { return }
        let e = Int(watch.elapsed)
        guard e != seconds else { return }
        seconds = e
        if cycleBeep && e > 0 && e % cycle == 0 { Beeper.shared.beep(880, 0.25) }
        if beepSeconds > 0 && (1...beepSeconds).contains(remaining) { Beeper.shared.beep(1200, 0.15) }
    }
}

// MARK: - Estilo

extension Color {
    /// Contador del hub en blanco hueso; botones y selección en el azul del sistema.
    static let accent = Color(white: 0.93)
    static let accentBlue = Color(red: 0.04, green: 0.52, blue: 1.0)
    static let amber = Color(red: 0.85, green: 0.47, blue: 0.02)
    static let card = Color.white.opacity(0.07)
    static let muted = Color.white.opacity(0.55)
}

func mmss(_ s: Int) -> String { String(format: "%02d:%02d", s / 60, s % 60) }

struct Caps: View {
    let text: String
    init(_ text: String) { self.text = text }
    var body: some View {
        Text(text.uppercased())
            .font(.system(size: 9, weight: .semibold))
            .tracking(1.2)
            .foregroundStyle(Color.muted)
    }
}

struct ProgressBar: View {
    let value: Double
    var color: Color = .accent
    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.white.opacity(0.12))
                Capsule().fill(color).frame(width: g.size.width * min(1, max(0, value)))
            }
        }
        .frame(height: 5)
    }
}

struct Pulsing: ViewModifier {
    let active: Bool
    func body(content: Content) -> some View {
        if active {
            TimelineView(.animation) { ctx in
                content.opacity(0.55 + 0.45 * abs(cos(ctx.date.timeIntervalSinceReferenceDate * .pi)))
            }
        } else {
            content
        }
    }
}

struct PillButton: View {
    let title: String
    let symbol: String
    var fill: Color = .accentBlue
    var outline = false
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 5) {
                Image(systemName: symbol).font(.system(size: 11, weight: .bold))
                Text(title).font(.system(size: 12, weight: .semibold))
            }
            .foregroundStyle(.white)
            .padding(.horizontal, 14)
            .padding(.vertical, 7)
            .background(RoundedRectangle(cornerRadius: 8).fill(outline ? Color.black.opacity(0.35) : fill))
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(outline ? Color.white.opacity(0.15) : .clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Overlay (solo el contador)

/// El contador tal como se ve sobre Tibia. Se reutiliza en la vista previa del hub.
struct CounterBadge: View {
    @ObservedObject var model: HubModel
    var preview = false
    var previewAlert = false

    var body: some View {
        let s = model.overlayScale
        let critical = preview ? previewAlert : model.critical
        let running = preview || model.running
        let alert = critical && running
        let unlocked = model.overlayUnlocked && !preview
        // Sin fondo (o casi), una sombra mantiene el número legible sobre el juego.
        let bare = model.backgroundColor.alpha < 0.3 && !unlocked
        Text(preview ? mmss(previewAlert ? min(model.warning, model.cycle) : model.cycle) : mmss(model.remaining))
            .font(.system(size: 28 * s, weight: .bold, design: .monospaced))
            .foregroundStyle(critical ? model.alertColor : model.counterColor)
            .shadow(color: .black.opacity(bare ? 0.9 : 0), radius: 2)
            .opacity(running ? 1 : 0.6)
            .modifier(Pulsing(active: alert))
            .padding(.horizontal, 12 * s)
            .padding(.vertical, 4 * s)
            .background(
                RoundedRectangle(cornerRadius: 9 * s)
                    .fill(unlocked && bare ? Color.black.opacity(0.6) : model.backgroundColor)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 9 * s)
                    .stroke(unlocked ? Color.amber : alert && !bare ? model.alertColor.opacity(0.6) : .clear,
                            style: StrokeStyle(lineWidth: 1.5, dash: unlocked ? [5, 3] : []))
            )
            .opacity(preview ? model.overlayOpacity : 1)
            .fixedSize()
    }
}

struct OverlayView: View {
    @ObservedObject var model: HubModel

    var body: some View {
        CounterBadge(model: model)
            .gesture(WindowDragGesture())  // solo actúa cuando está desbloqueado (si no, no recibe clics)
    }
}

final class OverlayPanel: NSPanel {
    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 120, height: 44),
                   styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .statusBar  // después de isFloatingPanel, que reinicia el nivel a .floating
        // Visible en todos los Spaces y encima de apps en pantalla completa (Tibia fullscreen).
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        hidesOnDeactivate = false
        becomesKeyOnlyIfNeeded = true
        ignoresMouseEvents = true  // overlay: los clics pasan al juego
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
    }
    override var canBecomeKey: Bool { false }
}

final class FirstMouseHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override var mouseDownCanMoveWindow: Bool { false }
}

// MARK: - Hub

/// Sprite del personaje (Outfiter de TibiaWiki), empaquetado en Resources/character.png.
let characterImage: NSImage? = Bundle.main.image(forResource: "character")

/// Nube de diálogo con la punta a la derecha (hacia el personaje).
struct BubbleShape: Shape {
    var tail: CGFloat = 9

    func path(in rect: CGRect) -> Path {
        let body = CGRect(x: rect.minX, y: rect.minY, width: rect.width - tail, height: rect.height)
        var path = Path(roundedRect: body, cornerRadius: min(12, body.height / 2))
        path.move(to: CGPoint(x: body.maxX - 1, y: body.midY - 6))
        path.addQuadCurve(to: CGPoint(x: rect.maxX, y: body.midY + 4), control: CGPoint(x: body.maxX + 3, y: body.midY))
        path.addQuadCurve(to: CGPoint(x: body.maxX - 1, y: body.midY + 6), control: CGPoint(x: body.maxX + 2, y: body.midY + 6))
        path.closeSubpath()
        return path
    }
}

struct SpeechBubble: View {
    let text: String
    var body: some View {
        Text(text)
            .font(.system(size: 12.5, weight: .bold, design: .rounded))
            .foregroundStyle(Color(white: 0.1))
            .padding(.leading, 12)
            .padding(.trailing, 21)  // 12 + punta
            .padding(.vertical, 7)
            .background(BubbleShape().fill(Color(white: 0.96)).shadow(color: .black.opacity(0.35), radius: 3, y: 1))
            .fixedSize()
    }
}

/// Franja con el personaje y su frase, encima de la tarjeta del timer, alineada a la derecha.
struct CharacterView: View {
    var body: some View {
        HStack(alignment: .center, spacing: 6) {
            Spacer(minLength: 0)
            SpeechBubble(text: "Slayer macaco e mielda")
                .offset(y: -8)  // a la altura de la cabeza
            if let characterImage {
                Image(nsImage: characterImage)
                    .interpolation(.none)
                    .resizable()
                    .frame(width: characterImage.size.width * 2, height: characterImage.size.height * 2)
            } else {
                Image(systemName: "person.fill").font(.system(size: 40)).foregroundStyle(Color.muted)
            }
        }
        .padding(.horizontal, 4)
    }
}

/// Tarjeta del timer: estado, contador, progreso y controles.
struct HeroView: View {
    @ObservedObject var model: HubModel

    var body: some View {
        // El hub usa el azul de la app; el color del contador solo aplica al overlay.
        let accent = model.critical ? model.alertColor : Color.accent
        VStack(spacing: 12) {
            HStack {
                StatusChip(running: model.running, critical: model.critical, alertColor: model.alertColor)
                Spacer()
                Text("Ciclo \(model.cycles + 1) · Total \(mmss(model.seconds))")
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(Color.muted)
            }
            Text(mmss(model.remaining))
                .font(.system(size: 56, weight: .bold, design: .monospaced))
                .foregroundStyle(accent)
                .modifier(Pulsing(active: model.critical && model.running))
                .contentTransition(.numericText(countsDown: true))
                .animation(.snappy, value: model.remaining)
            ProgressBar(value: model.progress, color: accent)
            HStack(spacing: 10) {
                Button { model.toggle() } label: {
                    Label(model.running ? "Pausar" : "Iniciar", systemImage: model.running ? "pause.fill" : "play.fill")
                        .font(.system(size: 14, weight: .semibold))
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 9)
                        .background(RoundedRectangle(cornerRadius: 10).fill(model.running ? Color.amber : Color.accentBlue))
                        .foregroundStyle(.white)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help(model.shortcuts[.toggle].map { "Atajo: \($0.display)" } ?? "")

                Button { model.reset() } label: {
                    Label("Reiniciar", systemImage: "arrow.counterclockwise")
                        .font(.system(size: 14, weight: .semibold))
                        .padding(.vertical, 9)
                        .padding(.horizontal, 16)
                        .background(RoundedRectangle(cornerRadius: 10).fill(Color.white.opacity(0.06)))
                        .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.white.opacity(0.1)))
                        .foregroundStyle(.white)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help(model.shortcuts[.reset].map { "Atajo: \($0.display)" } ?? "")
            }
        }
        .padding(16)
        .background(
            RoundedRectangle(cornerRadius: 14)
                .fill(Color.white.opacity(0.05))
                .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.white.opacity(0.08)))
        )
    }
}

struct StatusChip: View {
    let running: Bool
    let critical: Bool
    let alertColor: Color

    var body: some View {
        let (label, color): (String, Color) = !running ? ("En pausa", Color.muted)
            : critical ? ("¡Alerta!", alertColor) : ("En marcha", Color.green)
        HStack(spacing: 5) {
            Circle().fill(color).frame(width: 7, height: 7)
            Text(label).font(.system(size: 11, weight: .semibold))
        }
        .foregroundStyle(color)
        .padding(.horizontal, 8).padding(.vertical, 3)
        .background(Capsule().fill(color.opacity(0.15)))
    }
}

/// Botón que graba una combinación de teclas al pulsarlo. Esc cancela.
struct ShortcutRecorder: View {
    @ObservedObject var model: HubModel
    let action: HotKeyAction
    @State private var recording = false
    @State private var monitor: Any?

    var body: some View {
        HStack(spacing: 6) {
            Button {
                recording ? stop() : start()
            } label: {
                Text(recording ? "Pulsa la combinación…" : model.shortcuts[action]?.display ?? "Sin atajo")
                    .font(.system(size: 12, weight: .medium, design: recording ? .default : .rounded))
                    .foregroundStyle(recording ? Color.amber : model.shortcuts[action] == nil ? Color.muted : .white)
                    .frame(minWidth: 130)
            }
            if model.shortcuts[action] != nil && !recording {
                Button {
                    model.setShortcut(nil, for: action)
                } label: {
                    Image(systemName: "xmark.circle.fill").foregroundStyle(Color.muted)
                }
                .buttonStyle(.plain)
                .help("Quitar atajo")
            }
        }
        .onDisappear(perform: stop)
    }

    private func start() {
        recording = true
        model.recordingShortcut = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if Int(event.keyCode) == kVK_Escape {
                stop()
            } else if let shortcut = Shortcut(event: event) {
                model.setShortcut(shortcut, for: action)
                stop()
            } else {
                NSSound.beep()  // sin modificador: no sirve como atajo global
            }
            return nil
        }
    }

    private func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        if recording { model.recordingShortcut = false }
        recording = false
    }
}

enum HubTab: String, CaseIterable, Identifiable {
    case timer, overlay, shortcuts
    var id: String { rawValue }
    var title: String {
        switch self {
        case .timer: "Ciclo y sonido"
        case .overlay: "Overlay"
        case .shortcuts: "Atajos"
        }
    }
}

struct HubView: View {
    @ObservedObject var model: HubModel
    let resetOverlayPosition: () -> Void
    @AppStorage("hubTab") private var tab: HubTab = .timer

    var body: some View {
        VStack(spacing: 12) {
            CharacterView()
            HeroView(model: model)
            Picker("", selection: $tab) {
                ForEach(HubTab.allCases) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            Group {
                switch tab {
                case .timer: TimerSettings(model: model)
                case .overlay: OverlaySettings(model: model, resetOverlayPosition: resetOverlayPosition)
                case .shortcuts: ShortcutSettings(model: model)
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
            .padding(.horizontal, -20)  // el Form agrupado ya trae su propio margen
            .padding(.top, -16)
        }
        .padding(.horizontal, 16)
        .padding(.top, 12)
        .frame(width: 420)
        .frame(minHeight: 520, idealHeight: 760)
        .textFieldStyle(.roundedBorder)
    }
}

struct TimerSettings: View {
    @ObservedObject var model: HubModel
    private static let presets = [60, 90, 120, 150, 180]

    private var minutes: Binding<Int> {
        Binding(get: { model.cycle / 60 },
                set: { model.cycle = max(0, $0) * 60 + model.cycle % 60 })
    }
    private var secs: Binding<Int> {
        Binding(get: { model.cycle % 60 },
                set: { model.cycle = model.cycle / 60 * 60 + max(0, min(59, $0)) })
    }

    var body: some View {
        Form {
            Section {
                HStack(spacing: 6) {
                    ForEach(Self.presets, id: \.self) { preset in
                        Button(mmss(preset)) { model.cycle = preset }
                            .buttonStyle(.plain)
                            .font(.system(size: 12, weight: .semibold, design: .monospaced))
                            .padding(.horizontal, 9).padding(.vertical, 5)
                            .background(Capsule().fill(model.cycle == preset ? Color.accentBlue : Color.white.opacity(0.08)))
                            .foregroundStyle(.white)
                    }
                }
                .frame(maxWidth: .infinity)
                LabeledContent("Personalizado") {
                    HStack(spacing: 4) {
                        TextField("", value: minutes, format: .number)
                            .frame(width: 38).multilineTextAlignment(.trailing)
                        Text("min")
                        TextField("", value: secs, format: .number)
                            .frame(width: 38).multilineTextAlignment(.trailing)
                        Text("s")
                        Stepper("", value: $model.cycle, in: 5...3600, step: 5).labelsHidden()
                    }
                }
            } header: {
                Text("Tiempo entre ciclos")
            }

            Section("Avisos") {
                Stepper("Alerta en los últimos \(model.warning) s", value: $model.warning, in: 0...max(0, model.cycle))
                Stepper("Beeps en los últimos \(model.beepSeconds) s", value: $model.beepSeconds, in: 0...30)
                Toggle("Tono al empezar cada ciclo", isOn: $model.cycleBeep)
                Button("Restaurar valores del Pez (2:00 · 25 s · 5 s)") { model.restoreTimerDefaults() }
            }

            Section("Sonido") {
                Toggle("Silenciar", isOn: $model.muted)
                LabeledContent("Volumen") {
                    HStack {
                        Slider(value: $model.volume, in: 0...1).disabled(model.muted)
                        Button("Probar") { Beeper.shared.beep(1200, 0.15) }
                    }
                }
            }
        }
    }
}

struct OverlaySettings: View {
    @ObservedObject var model: HubModel
    let resetOverlayPosition: () -> Void
    @State private var previewAlert = false

    var body: some View {
        Form {
            Section {
                ZStack {
                    // Fondo que imita el mapa de Tibia, para juzgar colores y tamaño.
                    LinearGradient(colors: [Color(red: 0.29, green: 0.36, blue: 0.20), Color(red: 0.20, green: 0.16, blue: 0.12)],
                                   startPoint: .topLeading, endPoint: .bottomTrailing)
                    CounterBadge(model: model, preview: true, previewAlert: previewAlert)
                }
                .frame(height: max(90, 60 * model.overlayScale))
                .clipShape(RoundedRectangle(cornerRadius: 10))
                .listRowInsets(EdgeInsets(top: 8, leading: 8, bottom: 8, trailing: 8))
                Toggle("Ver en alerta", isOn: $previewAlert)
            } header: {
                Text("Vista previa")
            }

            Section {
                Toggle("Mostrar overlay", isOn: $model.overlayVisible)
                LabeledContent("Tamaño") { Slider(value: $model.overlayScale, in: 0.6...3) }
                LabeledContent("Opacidad") { Slider(value: $model.overlayOpacity, in: 0.2...1) }
                Toggle("Desbloquear para moverlo", isOn: $model.overlayUnlocked)
                Button("Volver a la esquina superior derecha", action: resetOverlayPosition)
            } header: {
                Text("En Tibia")
            } footer: {
                Text("Solo aparece con Tibia al frente y deja pasar los clics al juego. Mientras está desbloqueado se ve siempre, para que puedas arrastrarlo a su sitio; luego vuelve a bloquearlo.")
                    .foregroundStyle(.secondary)
            }

            Section {
                ColorPicker("Contador", selection: $model.counterColor, supportsOpacity: false)
                ColorPicker("Contador en alerta", selection: $model.alertColor, supportsOpacity: false)
                ColorPicker("Fondo", selection: $model.backgroundColor, supportsOpacity: true)
                Button("Restaurar colores") { model.restoreColors() }
            } header: {
                Text("Colores")
            } footer: {
                Text("Baja la opacidad del color de fondo a cero para ver solo el número.")
                    .foregroundStyle(.secondary)
            }
        }
    }
}

struct ShortcutSettings: View {
    @ObservedObject var model: HubModel

    var body: some View {
        Form {
            Section {
                ForEach(HotKeyAction.allCases) { action in
                    LabeledContent(action.title) { ShortcutRecorder(model: model, action: action) }
                }
                Button("Restaurar atajos") { model.restoreShortcuts() }
            } header: {
                Text("Funcionan con Tibia al frente")
            } footer: {
                Text("Haz clic en un atajo y pulsa la nueva combinación (Esc cancela). Usa al menos ⌃, ⌥, ⇧ o ⌘, o una tecla F sola.")
                    .foregroundStyle(.secondary)
            }
        }
    }
}

// MARK: - Atajos globales (Carbon: no requieren permisos de accesibilidad)

@MainActor
enum HotKeys {
    private static var actions: [UInt32: () -> Void] = [:]
    private static var refs: [EventHotKeyRef] = []
    private static var installed = false

    static func set(_ bindings: [(Shortcut, () -> Void)]) {
        if !installed { install() }
        refs.forEach { UnregisterEventHotKey($0) }
        refs = []
        actions = [:]
        for (index, (shortcut, action)) in bindings.enumerated() {
            let id = UInt32(index + 1)
            var ref: EventHotKeyRef?
            let status = RegisterEventHotKey(UInt32(shortcut.keyCode), UInt32(shortcut.modifiers),
                                             EventHotKeyID(signature: OSType(0x414C_4641), id: id),  // 'ALFA'
                                             GetApplicationEventTarget(), 0, &ref)
            if status == noErr, let ref {
                refs.append(ref)
                actions[id] = action
            }
        }
    }

    private static func install() {
        installed = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var hotKey = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                              nil, MemoryLayout<EventHotKeyID>.size, nil, &hotKey)
            let id = hotKey.id
            MainActor.assumeIsolated { HotKeys.actions[id]?() }
            return noErr
        }, 1, &spec, nil, nil)
    }
}

// MARK: - App

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let model = HubModel()
    private var overlay: OverlayPanel!
    private var hub: NSWindow!
    private var statusItem: NSStatusItem!
    private var ticker: Timer?
    private var activity: NSObjectProtocol?
    private var bag = Set<AnyCancellable>()
    /// El launcher (/Applications/Tibia.app) es com.tibia.launcher; el juego en sí es este.
    private static let tibiaClientID = "com.tibia.client"
    private let tibiaActive = CurrentValueSubject<Bool, Never>(false)

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Evita que App Nap ralentice el timer mientras Tibia está en primer plano.
        activity = ProcessInfo.processInfo.beginActivity(options: .userInitiated, reason: "Timer de Tibia en marcha")

        buildMainMenu()
        buildOverlay()
        buildHub()
        buildStatusItem()

        Publishers.CombineLatest(model.$shortcuts, model.$recordingShortcut)
            .sink { [weak self] shortcuts, recording in
                guard let self else { return }
                let bindings: [(Shortcut, () -> Void)] = recording ? [] : HotKeyAction.allCases.compactMap { action in
                    shortcuts[action].map { shortcut in (shortcut, { [weak self] in self?.model.perform(action) }) }
                }
                HotKeys.set(bindings)
            }
            .store(in: &bag)

        let ticker = Timer(timeInterval: 0.1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.model.tick()
                self?.refreshTibiaActive()
            }
        }
        RunLoop.main.add(ticker, forMode: .common)
        self.ticker = ticker

        showHub()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showHub()
        return true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    private func buildOverlay() {
        overlay = OverlayPanel()
        overlay.contentView = FirstMouseHostingView(rootView: OverlayView(model: model))
        if !overlay.setFrameUsingName("AlfaHubOverlay") { placeOverlayTopRight() }
        overlay.setFrameAutosaveName("AlfaHubOverlay")

        model.$overlayOpacity.sink { [weak self] in self?.overlay.alphaValue = $0 }.store(in: &bag)
        model.$overlayUnlocked.sink { [weak self] unlocked in
            guard let overlay = self?.overlay else { return }
            overlay.ignoresMouseEvents = !unlocked
            if unlocked { self?.model.overlayVisible = true }
        }.store(in: &bag)

        // Solo se ve con el cliente de Tibia al frente (o desbloqueado, para poder colocarlo).
        // (se actualiza en cada tick: el aviso de cambio de app no siempre llega al salir de pantalla completa)
        refreshTibiaActive()
        Publishers.CombineLatest3(model.$overlayVisible, model.$overlayUnlocked, tibiaActive)
            .map { visible, unlocked, tibia in visible && (tibia || unlocked) }
            .removeDuplicates()
            .sink { [weak self] show in
                guard let overlay = self?.overlay else { return }
                if show { overlay.orderFrontRegardless() } else { overlay.orderOut(nil) }
            }
            .store(in: &bag)
    }

    /// Tibia cuenta como activo si es la app al frente y además tiene una ventana visible en el
    /// Space actual (si está en pantalla completa en otro Space, macOS puede darlo por "al frente"
    /// mientras se ve otra cosa).
    private func refreshTibiaActive() {
        var active = false
        if let front = NSWorkspace.shared.frontmostApplication, front.bundleIdentifier == Self.tibiaClientID {
            let pid = front.processIdentifier
            let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
                as? [[String: Any]] ?? []
            active = windows.contains {
                ($0[kCGWindowOwnerPID as String] as? pid_t) == pid && ($0[kCGWindowLayer as String] as? Int) == 0
            }
        }
        if active != tibiaActive.value { tibiaActive.send(active) }
    }

    private func buildHub() {
        hub = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 420, height: 760),
                       styleMask: [.titled, .closable, .miniaturizable, .resizable],
                       backing: .buffered, defer: false)
        hub.title = "Alfa Hub"
        hub.isReleasedWhenClosed = false
        hub.isRestorable = false
        hub.appearance = NSAppearance(named: .darkAqua)
        hub.contentViewController = NSHostingController(
            rootView: HubView(model: model) { [weak self] in self?.placeOverlayTopRight() })
        hub.setContentSize(NSSize(width: 420, height: min(760, (NSScreen.main?.visibleFrame.height ?? 800) - 60)))
        if !hub.setFrameUsingName("AlfaHubWindow") { hub.center() }
        hub.setFrameAutosaveName("AlfaHubWindow")
    }

    private func buildStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "timer", accessibilityDescription: "Alfa Hub")
        let menu = NSMenu()
        menu.addItem(withTitle: "Abrir Alfa Hub", action: #selector(showHub), keyEquivalent: "").target = self
        menu.addItem(withTitle: "Iniciar / pausar", action: #selector(toggleTimer), keyEquivalent: "").target = self
        menu.addItem(withTitle: "Reiniciar", action: #selector(resetTimer), keyEquivalent: "").target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "Salir", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        statusItem.menu = menu
    }

    private func buildMainMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "Ocultar Alfa Hub", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Salir de Alfa Hub", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)

        // Necesario para que funcionen copiar/pegar en los campos del hub.
        let editItem = NSMenuItem()
        let editMenu = NSMenu(title: "Edición")
        editMenu.addItem(withTitle: "Cortar", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Copiar", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Pegar", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "Seleccionar todo", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = editMenu
        main.addItem(editItem)

        let windowItem = NSMenuItem()
        let windowMenu = NSMenu(title: "Ventana")
        windowMenu.addItem(withTitle: "Cerrar", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowMenu.addItem(withTitle: "Minimizar", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowItem.submenu = windowMenu
        main.addItem(windowItem)
        NSApp.mainMenu = main
    }

    @objc private func showHub() {
        hub.makeKeyAndOrderFront(nil)
        NSApp.activate()
    }

    @objc private func toggleTimer() { model.toggle() }
    @objc private func resetTimer() { model.reset() }

    private func placeOverlayTopRight() {
        guard let screen = NSScreen.main?.visibleFrame else { return }
        let size = overlay.frame.size
        overlay.setFrameOrigin(NSPoint(x: screen.maxX - size.width - 20, y: screen.maxY - size.height - 20))
    }
}

let app = NSApplication.shared
let delegate = MainActor.assumeIsolated { AppDelegate() }
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()

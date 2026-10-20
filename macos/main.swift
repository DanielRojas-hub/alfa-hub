// Alfa Hub — timers para Tibia con overlay, basado en los timers de cueistoreservices.com.br/timer.
//
// Dos ventanas:
//  • Overlay: solo los contadores de los timers en marcha, encima de Tibia (también en pantalla
//    completa) y solo mientras el cliente de Tibia está al frente. Los clics lo atraviesan.
//  • Hub: los timers (Pez, Bakragore, Pilar o personalizados), cada uno con su duración, avisos,
//    sonido propio y atajo; más el aspecto del overlay y los atajos globales.

import AppKit
import AVFoundation
import Carbon.HIToolbox
import Combine
import SwiftUI
import UniformTypeIdentifiers

// MARK: - Sonido

@MainActor
final class Beeper {
    static let shared = Beeper()
    var volume: Double = 0.5
    var muted = false
    private var cache: [String: Data] = [:]
    private var players: [AVAudioPlayer] = []

    /// Beep senoidal como los del sitio original (los de los últimos segundos).
    func beep(_ freq: Double, _ duration: Double) {
        guard !muted, volume > 0 else { return }
        let key = "\(freq)-\(duration)"
        let data = cache[key] ?? Synth.wav(Synth.render([Tone(start: 0, duration: duration, freq: freq, decay: -log(0.01) / duration)], peak: 0.98))
        cache[key] = data
        play(try? AVAudioPlayer(data: data), gain: 1)
    }

    /// Uno de los sonidos incluidos; gain (0–1) se multiplica por el volumen de la app.
    func play(_ sound: BuiltInSound, gain: Double = 1) {
        guard !muted, volume > 0 else { return }
        let data = cache[sound.rawValue] ?? sound.wav
        cache[sound.rawValue] = data
        play(try? AVAudioPlayer(data: data), gain: gain)
    }

    /// Sonido elegido por el usuario (wav, mp3, aiff, m4a…). Si no se puede abrir, suena el fallback.
    func play(file path: String, gain: Double = 1, fallback: () -> Void) {
        guard !muted, volume > 0 else { return }
        if let player = try? AVAudioPlayer(contentsOf: URL(fileURLWithPath: path)) { play(player, gain: gain) } else { fallback() }
    }

    private func play(_ player: AVAudioPlayer?, gain: Double) {
        guard let player else { return }
        player.volume = Float(volume * gain)
        player.play()
        players.removeAll { !$0.isPlaying }
        players.append(player)
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
    /// Con allowPlain vale cualquier tecla (las teclas de Tibia Audio no se registran como atajo).
    init?(event: NSEvent, allowPlain: Bool = false) {
        let code = Int(event.keyCode)
        let flags = event.modifierFlags
        var mods = 0
        if flags.contains(.control) { mods |= controlKey }
        if flags.contains(.option) { mods |= optionKey }
        if flags.contains(.shift) { mods |= shiftKey }
        if flags.contains(.command) { mods |= cmdKey }
        let fKey = Self.functionKeys[code]
        guard mods != 0 || fKey != nil || allowPlain else { return nil }
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

/// Atajos que no dependen de un timer concreto.
enum GlobalAction: String, CaseIterable, Identifiable {
    case reset, overlay
    var id: String { rawValue }
    var title: String {
        switch self {
        case .reset: "Reiniciar el último timer usado"
        case .overlay: "Mostrar / ocultar overlay"
        }
    }
    var defaultShortcut: Shortcut {
        let mods = controlKey | optionKey
        switch self {
        case .reset: return Shortcut(keyCode: kVK_ANSI_R, modifiers: mods, key: "R")
        case .overlay: return Shortcut(keyCode: kVK_ANSI_O, modifiers: mods, key: "O")
        }
    }
}

// MARK: - Timers

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

enum TimerKind: String, Codable, CaseIterable, Identifiable {
    /// Cuenta atrás que puede repetirse (Pez) y tener una duración total (Bakragore).
    case cycle
    /// Rotación de direcciones cada pocos segundos (Pilar del Rotten Blood).
    case rotation
    var id: String { rawValue }
    var title: String { self == .cycle ? "Cuenta atrás" : "Rotación" }
}

/// Direcciones de la rotación de los Totens: Norte → Derecha → Sur → Izquierda.
let directions = [("↑", "Norte"), ("→", "Derecha"), ("↓", "Sur"), ("←", "Izquierda")]

struct TimerConfig: Codable, Identifiable, Equatable {
    var id = UUID()
    var name: String
    var kind: TimerKind = .cycle
    // Cuenta atrás
    var cycle = 120          // segundos de cada ciclo
    var repeats = true       // al llegar a 0 vuelve a empezar
    var total = 0            // duración total en segundos (0 = sin límite)
    var warning = 25         // rojo en los últimos N s
    var cycleBeep = true     // sonido de aviso al empezar cada ciclo
    // Rotación
    var step = 6             // segundos por dirección
    var startDirection = 0
    // Ambos
    var beepSeconds = 5      // beeps en los últimos N s
    var sound: String?       // sonido incluido (BuiltInSound; nil = beep)
    var soundPath: String?   // sonido de aviso propio (tiene prioridad sobre el incluido)
    var shortcut: Shortcut?  // iniciar / pausar este timer
    var overlay = OverlayStyle()
}

/// Lectura tolerante: los campos que falten (de versiones anteriores) toman su valor por defecto.
extension TimerConfig {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func get<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T { (try? c.decodeIfPresent(T.self, forKey: key)) ?? fallback }
        self.init(name: get(.name, "Timer"))
        id = get(.id, id)
        kind = get(.kind, kind)
        cycle = get(.cycle, cycle)
        repeats = get(.repeats, repeats)
        total = get(.total, total)
        warning = get(.warning, warning)
        cycleBeep = get(.cycleBeep, cycleBeep)
        step = get(.step, step)
        startDirection = get(.startDirection, startDirection)
        beepSeconds = get(.beepSeconds, beepSeconds)
        sound = get(.sound, sound)
        soundPath = get(.soundPath, soundPath)
        shortcut = get(.shortcut, shortcut)
        overlay = get(.overlay, overlay)
    }
}

/// Aspecto y sitio del contador de un timer sobre Tibia (cada timer tiene el suyo).
struct OverlayStyle: Codable, Equatable {
    static let defaultCounter = "A873FA", defaultAlert = "F54D4D", defaultBackground = "111827D9"
    var show = true
    var scale = 1.0
    var opacity = 1.0
    var counterColor = Self.defaultCounter
    var alertColor = Self.defaultAlert
    var backgroundColor = Self.defaultBackground
    /// Esquina superior derecha en pantalla (coordenadas de Cocoa); nil = colocado automáticamente.
    var right: Double?
    var top: Double?

    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func get<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T { (try? c.decodeIfPresent(T.self, forKey: key)) ?? fallback }
        show = get(.show, show)
        scale = get(.scale, scale)
        opacity = get(.opacity, opacity)
        counterColor = get(.counterColor, counterColor)
        alertColor = get(.alertColor, alertColor)
        backgroundColor = get(.backgroundColor, backgroundColor)
        right = get(.right, right)
        top = get(.top, top)
    }

    var counter: Color { Color(hex: counterColor) }
    var alert: Color { Color(hex: alertColor) }
    var background: Color { Color(hex: backgroundColor) }
}

enum TimerPreset: String, CaseIterable, Identifiable {
    case pez, bakragore, pilar, custom
    var id: String { rawValue }
    var title: String {
        switch self {
        case .pez: "Pez"
        case .bakragore: "Bakragore"
        case .pilar: "Pilar"
        case .custom: "Personalizado"
        }
    }
    var detail: String {
        switch self {
        case .pez: "Cada 2:00, alerta en los últimos 25 s"
        case .bakragore: "Mecánica cada 1:30 durante 25 min"
        case .pilar: "Rotación de los Totens cada 6 s"
        case .custom: "Cuenta atrás de 1:00 que no se repite"
        }
    }
    var config: TimerConfig {
        switch self {
        case .pez:
            return TimerConfig(name: "Pez", cycle: 120, repeats: true, total: 0, warning: 25, cycleBeep: true, beepSeconds: 5)
        case .bakragore:
            return TimerConfig(name: "Bakragore", cycle: 90, repeats: true, total: 25 * 60, warning: 10, cycleBeep: false, beepSeconds: 5)
        case .pilar:
            return TimerConfig(name: "Pilar", kind: .rotation, step: 6, startDirection: 0, beepSeconds: 2)
        case .custom:
            return TimerConfig(name: "Timer", cycle: 60, repeats: false, total: 0, warning: 10, cycleBeep: true, beepSeconds: 3)
        }
    }
}

/// Lo que se muestra de un timer en un instante.
struct TimerDisplay: Equatable {
    var text = "00:00"        // contador ("01:30" o "↑ 6")
    var detail = ""           // "Ciclo 2 · Total 03:10", "Norte · luego Derecha"…
    var progress = 0.0
    var critical = false
    var finished = false
}

/// Un timer en marcha: su configuración, su cronómetro y lo que muestra.
@MainActor
final class TimerRun: ObservableObject, Identifiable {
    @Published var config: TimerConfig { didSet { refresh(sound: false); onConfigChange() } }
    @Published private(set) var watch = Stopwatch()
    @Published private(set) var display = TimerDisplay()
    var onConfigChange: () -> Void = {}
    private var lastSecond = -1, lastSlot = -1, lastCount = -1

    let id: UUID

    init(_ config: TimerConfig) {
        id = config.id
        self.config = config
        refresh(sound: false)
    }

    var running: Bool { watch.running }
    /// En marcha o pausado a medias: se muestra en el overlay.
    var started: Bool { watch.running || watch.elapsed > 0 }

    func toggle() {
        if running {
            watch.pause()
        } else {
            if display.finished { reset() }
            watch.start()
        }
        refresh(sound: true)
    }

    func reset() {
        watch.reset()
        lastSecond = -1; lastSlot = -1; lastCount = -1
        refresh(sound: false)
    }

    func tick() { if running { refresh(sound: true) } }

    /// Sonido de aviso del timer: el archivo elegido o el beep por defecto.
    func playAlert(long: Bool = false) {
        let builtIn = BuiltInSound.named(config.sound)
        let play = { builtIn == .beep && long ? Beeper.shared.beep(880, 0.6) : Beeper.shared.play(builtIn) }
        if let path = config.soundPath { Beeper.shared.play(file: path, fallback: play) } else { play() }
    }

    private func refresh(sound: Bool) {
        let next = config.kind == .cycle ? cycleDisplay(sound: sound) : rotationDisplay(sound: sound)
        if next != display { display = next }
    }

    private func cycleDisplay(sound: Bool) -> TimerDisplay {
        let cycle = max(5, config.cycle)
        let limit = config.repeats ? (config.total > 0 ? config.total : Int.max) : cycle
        let e = min(Int(watch.elapsed), limit)
        let finished = e >= limit
        let remaining = finished ? 0 : cycle - e % cycle

        if sound && e != lastSecond {
            lastSecond = e
            if finished {
                watch.pause()
                playAlert(long: true)
            } else {
                if config.cycleBeep && e > 0 && e % cycle == 0 { playAlert() }
                if config.beepSeconds > 0 && (1...config.beepSeconds).contains(remaining) { Beeper.shared.beep(1200, 0.15) }
            }
        }

        var d = TimerDisplay()
        d.text = mmss(remaining)
        d.finished = finished
        d.critical = !finished && remaining <= config.warning
        d.progress = finished ? 1 : Double(e % cycle) / Double(cycle)
        let cycleNumber = min(e, limit - 1) / cycle + 1
        if finished {
            d.detail = "Terminado · \(mmss(e))"
        } else if !config.repeats {
            d.detail = "Cuenta atrás · \(mmss(cycle))"
        } else if config.total > 0 {
            d.detail = "Ciclo \(cycleNumber) · Quedan \(mmss(limit - e))"
        } else {
            d.detail = "Ciclo \(cycleNumber) · Total \(mmss(e))"
        }
        return d
    }

    private func rotationDisplay(sound: Bool) -> TimerDisplay {
        let step = Double(max(1, config.step))
        let t = watch.elapsed
        let slot = Int(t / step)
        let dir = (config.startDirection + slot) % directions.count
        let count = max(1, Int((step - (t - Double(slot) * step)).rounded(.up)))

        if sound && running {
            if slot != lastSlot {
                lastSlot = slot; lastCount = count
                playAlert()
            } else if count != lastCount {
                lastCount = count
                if config.beepSeconds > 0 && (1...config.beepSeconds).contains(count) { Beeper.shared.beep(1200, 0.15) }
            }
        }

        var d = TimerDisplay()
        d.text = "\(directions[dir].0) \(count)"
        d.detail = "\(directions[dir].1) · luego \(directions[(dir + 1) % directions.count].1)"
        d.critical = running && count <= config.beepSeconds
        d.progress = (t - Double(slot) * step) / step
        return d
    }
}

// MARK: - Modelo

@MainActor
final class HubModel: ObservableObject {

    @Published private(set) var runs: [TimerRun] = []
    @Published var selectedID: UUID? { didSet { defaults.set(selectedID?.uuidString, forKey: "selectedTimer") } }
    /// El último timer iniciado o pausado: el atajo "Reiniciar" actúa sobre él.
    private(set) var lastUsedID: UUID?

    @Published var volume: Double { didSet { Beeper.shared.volume = volume; save(volume, "volume") } }
    @Published var muted: Bool { didSet { Beeper.shared.muted = muted; save(muted, "muted") } }
    @Published var overlayVisible: Bool { didSet { save(overlayVisible, "overlayVisible") } }
    /// Esquina superior derecha desde la que se apilan los contadores sin sitio propio (nil = la de la pantalla).
    var overlayAnchor: CGPoint? {
        didSet { save(overlayAnchor.map { [$0.x, $0.y] } ?? [], "overlayAnchor") }
    }
    @Published private(set) var globalShortcuts: [GlobalAction: Shortcut] {
        didSet {
            let raw = Dictionary(uniqueKeysWithValues: globalShortcuts.map { ($0.key.rawValue, $0.value) })
            save((try? JSONEncoder().encode(raw)) ?? Data(), "globalShortcuts")
        }
    }
    /// Cambia cuando cambia cualquier atajo (globales o de un timer), para volver a registrarlos.
    @Published private(set) var shortcutsVersion = 0
    /// Cuántos timers están en marcha (para la barra lateral y la lista).
    @Published private(set) var runningCount = 0
    // Tibia Audio
    @Published private(set) var audios: [AudioRun] = []
    /// Interruptor general de Tibia Audio: solo entonces se escuchan las teclas.
    @Published var audioEnabled: Bool { didSet { save(audioEnabled, "audioEnabled") } }
    /// Permiso de Monitorización de entrada (para oír las teclas sin bloquearlas).
    @Published var listenAllowed = false
    /// La escucha de teclas está en marcha.
    @Published var listening = false
    // Espejos
    @Published var mirrors: [MirrorConfig] { didSet { save((try? JSONEncoder().encode(mirrors)) ?? Data(), "mirrors") } }
    @Published var mirrorsVisible: Bool { didSet { save(mirrorsVisible, "mirrorsVisible") } }
    /// Espejos desbloqueados: se ven siempre y se pueden arrastrar. No se guarda.
    @Published var mirrorsUnlocked = false
    @Published var screenCaptureAllowed = false
    /// Espejos con el selector de ventanas del sistema (true) o captura automática con permiso (false).
    @Published var mirrorPickerMode: Bool { didSet { save(mirrorPickerMode, "mirrorPickerMode") } }
    /// Si ya se eligió la ventana de Tibia en el selector (vale mientras la ventana siga abierta).
    @Published var mirrorWindowPicked = false
    /// Si el cliente de Tibia está abierto (se muestra en la barra superior).
    @Published var tibiaRunning = false
    /// Solo mientras está activo el overlay recibe clics (para arrastrarlo). No se guarda.
    @Published var overlayUnlocked = false
    /// Mientras se graba un atajo, los globales se desactivan para poder capturar cualquier combinación.
    @Published var recordingShortcut = false

    private let defaults = UserDefaults.standard
    private func save(_ value: Any, _ key: String) { defaults.set(value, forKey: key) }

    init() {
        volume = defaults.object(forKey: "volume") as? Double ?? 0.5
        muted = defaults.object(forKey: "muted") as? Bool ?? false
        overlayVisible = defaults.object(forKey: "overlayVisible") as? Bool ?? true
        if let xy = defaults.array(forKey: "overlayAnchor") as? [Double], xy.count == 2 { overlayAnchor = CGPoint(x: xy[0], y: xy[1]) }
        audioEnabled = defaults.object(forKey: "audioEnabled") as? Bool ?? false
        mirrors = defaults.data(forKey: "mirrors").flatMap { try? JSONDecoder().decode([MirrorConfig].self, from: $0) } ?? []
        mirrorsVisible = defaults.object(forKey: "mirrorsVisible") as? Bool ?? true
        mirrorPickerMode = defaults.object(forKey: "mirrorPickerMode") as? Bool ?? true

        // Atajos de la versión anterior (un solo timer): "toggle" pasa al timer Pez.
        var legacy: [String: Shortcut] = [:]
        if let data = defaults.data(forKey: "shortcuts"), let raw = try? JSONDecoder().decode([String: Shortcut].self, from: data) {
            legacy = raw
        }
        if let data = defaults.data(forKey: "globalShortcuts"), let raw = try? JSONDecoder().decode([String: Shortcut].self, from: data) {
            globalShortcuts = Dictionary(uniqueKeysWithValues: raw.compactMap { k, v in GlobalAction(rawValue: k).map { ($0, v) } })
        } else if !legacy.isEmpty {
            globalShortcuts = Dictionary(uniqueKeysWithValues: GlobalAction.allCases.compactMap { a in legacy[a.rawValue].map { (a, $0) } })
        } else {
            globalShortcuts = Dictionary(uniqueKeysWithValues: GlobalAction.allCases.map { ($0, $0.defaultShortcut) })
        }

        var configs: [TimerConfig]
        if let data = defaults.data(forKey: "timers"), let saved = try? JSONDecoder().decode([TimerConfig].self, from: data), !saved.isEmpty {
            configs = saved
        } else {
            // Primera vez (o migración): Pez con los ajustes de antes, Bakragore y Pilar.
            var pez = TimerPreset.pez.config
            pez.cycle = defaults.object(forKey: "cycle") as? Int ?? pez.cycle
            pez.warning = defaults.object(forKey: "warning") as? Int ?? pez.warning
            pez.beepSeconds = defaults.object(forKey: "beepSeconds") as? Int ?? pez.beepSeconds
            pez.cycleBeep = defaults.object(forKey: "cycleBeep") as? Bool ?? pez.cycleBeep
            pez.shortcut = legacy.isEmpty
                ? Shortcut(keyCode: kVK_ANSI_P, modifiers: controlKey | optionKey, key: "P")
                : legacy["toggle"]
            configs = [pez, TimerPreset.bakragore.config, TimerPreset.pilar.config]
        }
        // Versión 1.3: un solo aspecto de overlay para todos. Pasa a cada timer, y su sitio a la
        // esquina desde la que se apilan los contadores.
        if defaults.object(forKey: "overlayPerTimer") == nil {
            var style = OverlayStyle()
            style.scale = defaults.object(forKey: "overlayScale") as? Double ?? style.scale
            style.opacity = defaults.object(forKey: "overlayOpacity") as? Double ?? style.opacity
            style.counterColor = defaults.string(forKey: "counterColor") ?? style.counterColor
            style.alertColor = defaults.string(forKey: "alertColor") ?? style.alertColor
            style.backgroundColor = defaults.string(forKey: "backgroundColor") ?? style.backgroundColor
            for i in configs.indices { configs[i].overlay = style }
            if let frame = defaults.string(forKey: "NSWindow Frame AlfaHubOverlay") {
                let n = frame.split(separator: " ").compactMap { Double($0) }
                if n.count >= 4 { overlayAnchor = CGPoint(x: n[0] + n[2], y: n[1] + n[3]) }
            }
            save(true, "overlayPerTimer")
        }
        runs = configs.map(makeRun)
        let savedAudios = defaults.data(forKey: "audios").flatMap { try? JSONDecoder().decode([AudioConfig].self, from: $0) }
        audios = (savedAudios ?? []).map(makeAudio)
        let saved = defaults.string(forKey: "selectedTimer").flatMap(UUID.init(uuidString:))
        selectedID = runs.first { $0.id == saved }?.id ?? runs.first?.id
        saveTimers()
        Beeper.shared.volume = volume
        Beeper.shared.muted = muted
    }

    private func makeRun(_ config: TimerConfig) -> TimerRun {
        let run = TimerRun(config)
        run.onConfigChange = { [weak self] in self?.saveTimers() }
        return run
    }

    private var savedShortcuts: [Shortcut?] = []

    private func saveTimers() {
        save((try? JSONEncoder().encode(runs.map(\.config))) ?? Data(), "timers")
        // Mover un contador también guarda: solo se vuelven a registrar los atajos si cambiaron.
        let shortcuts = runs.map(\.config.shortcut)
        if shortcuts != savedShortcuts {
            savedShortcuts = shortcuts
            shortcutsVersion += 1
        }
    }

    private func makeAudio(_ config: AudioConfig) -> AudioRun {
        let audio = AudioRun(config)
        audio.onConfigChange = { [weak self] in self?.saveAudios() }
        return audio
    }

    private func saveAudios() {
        save((try? JSONEncoder().encode(audios.map(\.config))) ?? Data(), "audios")
    }

    func addAudio() -> AudioRun {
        var config = AudioConfig()
        config.name = "Habilidad \(audios.count + 1)"
        let audio = makeAudio(config)
        audios.append(audio)
        saveAudios()
        return audio
    }

    func duplicate(_ audio: AudioRun) {
        var config = audio.config
        config.id = UUID()
        config.name += " (copia)"
        audios.insert(makeAudio(config), at: (audios.firstIndex { $0.id == audio.id } ?? audios.count - 1) + 1)
        saveAudios()
    }

    func remove(_ audio: AudioRun) {
        audio.cancel()
        audios.removeAll { $0.id == audio.id }
        saveAudios()
    }

    /// Una tecla pulsada en Tibia (solo llega si es de alguna habilidad y Tibia está al frente).
    func keyPressed(keyCode: Int, modifiers: Int) {
        guard audioEnabled else { return }
        for audio in audios where audio.config.enabled && audio.config.key?.keyCode == keyCode && audio.config.key?.modifiers == modifiers {
            audio.trigger()
        }
    }

    /// Teclas de Tibia Audio que también son atajos de Alfa Hub (esas no llegarían a Tibia).
    func audioKeyConflicts(_ key: Shortcut?) -> Bool {
        guard let key else { return false }
        return globalShortcuts.values.contains(key) || runs.contains { $0.config.shortcut == key }
    }

    var selected: TimerRun { runs.first { $0.id == selectedID } ?? runs[0] }

    /// Timers que se ven en el overlay: los que están en marcha o a medias; si no hay, el elegido.
    /// Desbloqueado se ven todos, para poder colocar cada uno. Nunca los que tienen el overlay apagado.
    var overlayRuns: [TimerRun] {
        if overlayUnlocked { return runs.filter(\.config.overlay.show) }
        let started = runs.filter(\.started)
        return (started.isEmpty ? [selected] : started).filter(\.config.overlay.show)
    }

    func add(_ preset: TimerPreset) {
        var config = preset.config
        let names = Set(runs.map(\.config.name))
        if names.contains(config.name) {
            var n = 2
            while names.contains("\(config.name) \(n)") { n += 1 }
            config.name = "\(config.name) \(n)"
        }
        let run = makeRun(config)
        runs.append(run)
        selectedID = run.id
        saveTimers()
    }

    func duplicate(_ run: TimerRun) {
        var config = run.config
        config.id = UUID()
        config.shortcut = nil  // una combinación solo puede estar en un timer
        config.name += " (copia)"
        let copy = makeRun(config)
        runs.insert(copy, at: (runs.firstIndex { $0.id == run.id } ?? runs.count - 1) + 1)
        saveTimers()
    }

    func remove(_ run: TimerRun) {
        guard runs.count > 1, let index = runs.firstIndex(where: { $0.id == run.id }) else { return }
        runs.remove(at: index)
        if selectedID == run.id { selectedID = runs[min(index, runs.count - 1)].id }
        saveTimers()
    }

    func toggle(_ run: TimerRun) { run.toggle(); lastUsedID = run.id }
    func reset(_ run: TimerRun) { run.reset(); lastUsedID = run.id }

    func perform(_ action: GlobalAction) {
        switch action {
        case .reset: (runs.first { $0.id == lastUsedID } ?? selected).reset()
        case .overlay: overlayVisible.toggle()
        }
    }

    func restoreGlobalShortcuts() {
        globalShortcuts = Dictionary(uniqueKeysWithValues: GlobalAction.allCases.map { ($0, $0.defaultShortcut) })
        shortcutsVersion += 1
    }

    /// Quita una combinación de todos los sitios (antes de asignarla a otro).
    private func release(_ shortcut: Shortcut) {
        for (action, existing) in globalShortcuts where existing == shortcut { globalShortcuts[action] = nil }
        for run in runs where run.config.shortcut == shortcut { run.config.shortcut = nil }
    }

    func setShortcut(_ shortcut: Shortcut?, for action: GlobalAction) {
        if let shortcut { release(shortcut) }
        globalShortcuts[action] = shortcut
        shortcutsVersion += 1
    }

    func setShortcut(_ shortcut: Shortcut?, for run: TimerRun) {
        if let shortcut { release(shortcut) }
        run.config.shortcut = shortcut
    }

    func tick() {
        runs.forEach { $0.tick() }
        audios.forEach { $0.tick() }
        let count = runs.filter(\.running).count
        if count != runningCount { runningCount = count }
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

// MARK: - Overlay

/// Un contador tal como se ve sobre Tibia. Se reutiliza en la vista previa del editor.
struct CounterBadge: View {
    let style: OverlayStyle
    let text: String
    let critical: Bool
    let running: Bool
    var label: String? = nil
    var unlocked = false
    var preview = false

    var body: some View {
        let s = style.scale
        let alert = critical && running
        let background = style.background
        // Sin fondo (o casi), una sombra mantiene el número legible sobre el juego.
        let bare = background.alpha < 0.3 && !unlocked
        HStack(alignment: .firstTextBaseline, spacing: 8 * s) {
            if let label {
                Text(label)
                    .font(.system(size: 12 * s, weight: .semibold, design: .rounded))
                    .foregroundStyle(.white.opacity(0.85))
                    .shadow(color: .black.opacity(bare ? 0.9 : 0), radius: 1.5)
            }
            Text(text)
                .font(.system(size: 28 * s, weight: .bold, design: .monospaced))
                .foregroundStyle(critical ? style.alert : style.counter)
                .shadow(color: .black.opacity(bare ? 0.9 : 0), radius: 2)
        }
        .opacity(running ? 1 : 0.6)
        .modifier(Pulsing(active: alert))
        .padding(.horizontal, 12 * s)
        .padding(.vertical, 4 * s)
        .background(
            RoundedRectangle(cornerRadius: 9 * s)
                .fill(unlocked && bare ? Color.black.opacity(0.6) : background)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 9 * s)
                .stroke(unlocked ? Color.amber : alert && !bare ? style.alert.opacity(0.6) : .clear,
                        style: StrokeStyle(lineWidth: 1.5, dash: unlocked ? [5, 3] : []))
        )
        .opacity(preview ? style.opacity : 1)
        .fixedSize()
    }
}

/// Lo que el controlador le dice a la ventana de un contador.
@MainActor
final class CounterPanelState: ObservableObject {
    @Published var showName = false
    @Published var unlocked = false
}

/// Contador de un timer en su ventana sobre Tibia.
struct RunOverlayView: View {
    @ObservedObject var run: TimerRun
    @ObservedObject var state: CounterPanelState

    var body: some View {
        CounterBadge(style: run.config.overlay, text: run.display.text, critical: run.display.critical,
                     running: run.running, label: state.showName ? run.config.name : nil, unlocked: state.unlocked)
            .gesture(WindowDragGesture())  // solo actúa desbloqueado (si no, la ventana no recibe clics)
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

/// Una ventana de overlay por timer. Las muestra solo con Tibia al frente (o desbloqueadas), las
/// coloca en su sitio (o apiladas arriba a la derecha si no tienen uno) y, al arrastrarlas
/// desbloqueadas, guarda su sitio en el timer.
@MainActor
final class OverlayController {
    private struct Entry {
        let panel: OverlayPanel
        let state: CounterPanelState
        let host: NSView
    }

    private let model: HubModel
    private var entries: [UUID: Entry] = [:]
    /// Mientras el controlador mueve una ventana (no cuenta como arrastre del usuario).
    private var placing = false

    init(model: HubModel) { self.model = model }

    func sync(tibiaActive: Bool) {
        for id in entries.keys where !model.runs.contains(where: { $0.id == id }) {
            entries[id]?.panel.orderOut(nil)
            entries[id] = nil
        }
        let visible = model.overlayVisible && (tibiaActive || model.overlayUnlocked)
        let shown = visible ? model.overlayRuns : []
        let ids = Set(shown.map(\.id))
        let screen = NSScreen.main?.visibleFrame ?? .zero
        let anchor = model.overlayAnchor ?? CGPoint(x: screen.maxX - 20, y: screen.maxY - 20)
        var autoTop = anchor.y
        // Mientras se arrastra uno, no se recolocan (el sitio se guarda al moverse).
        let dragging = model.overlayUnlocked && NSEvent.pressedMouseButtons & 1 != 0
        for run in model.runs {
            guard ids.contains(run.id) else {
                if let entry = entries[run.id], entry.panel.isVisible { entry.panel.orderOut(nil) }
                continue
            }
            let entry = entries[run.id] ?? make(run)
            let style = run.config.overlay
            if entry.state.showName != (shown.count > 1) { entry.state.showName = shown.count > 1 }
            if entry.state.unlocked != model.overlayUnlocked { entry.state.unlocked = model.overlayUnlocked }
            entry.panel.ignoresMouseEvents = !model.overlayUnlocked
            if entry.panel.alphaValue != style.opacity { entry.panel.alphaValue = style.opacity }
            let size = entry.host.fittingSize
            let own = style.right != nil && style.top != nil
            let top = own ? style.top! : autoTop
            if !own { autoTop -= size.height + 4 * style.scale }
            let frame = NSRect(x: (own ? style.right! : anchor.x) - size.width, y: top - size.height,
                               width: size.width, height: size.height)
            if !dragging && entry.panel.frame != frame {
                placing = true
                entry.panel.setFrame(frame, display: true)
                placing = false
            }
            if !entry.panel.isVisible { entry.panel.orderFrontRegardless() }
        }
    }

    private func make(_ run: TimerRun) -> Entry {
        let panel = OverlayPanel()
        let state = CounterPanelState()
        // La vista va dentro de un contenedor: así su tamaño no mueve la ventana (la coloca sync).
        let host = FirstMouseHostingView(rootView: RunOverlayView(run: run, state: state))
        let container = NSView()
        panel.contentView = container
        host.frame = container.bounds
        host.autoresizingMask = [.width, .height]
        container.addSubview(host)
        NotificationCenter.default.addObserver(forName: NSWindow.didMoveNotification, object: panel, queue: .main) { [weak self, weak run, weak panel] _ in
            MainActor.assumeIsolated {
                guard let self, let run, let panel, !self.placing, self.model.overlayUnlocked else { return }
                let right = Double(panel.frame.maxX), top = Double(panel.frame.maxY)
                if run.config.overlay.right != right || run.config.overlay.top != top {
                    run.config.overlay.right = right
                    run.config.overlay.top = top
                }
            }
        }
        let entry = Entry(panel: panel, state: state, host: host)
        entries[run.id] = entry
        return entry
    }
}

// MARK: - Hub: estructura (barra superior, barra lateral y páginas)

/// Sprite del personaje (Outfiter de TibiaWiki), empaquetado en Resources/character.png.
let characterImage: NSImage? = Bundle.main.image(forResource: "character")

extension Color {
    static let windowBackground = Color(red: 0.11, green: 0.11, blue: 0.12)
    static let sidebarBackground = Color(red: 0.085, green: 0.085, blue: 0.095)
    static let rowBackground = Color.white.opacity(0.05)
}

/// El personaje guardado (caminando si se eligió), al pie de la barra lateral.
struct Mascot: View {
    @ObservedObject var outfit: OutfitManager

    var body: some View {
        // Grande como el original (×2); los sprites con montura se reducen para caber.
        CharacterView(sprite: outfit.isOriginal ? nil : outfit.current, maxScale: 2, maxSide: 170)
            .frame(maxWidth: .infinity)
    }
}

/// Barra superior: estado de Tibia y del overlay. Ocupa la barra de título (los botones de la
/// ventana quedan a la izquierda).
struct TopBar: View {
    @ObservedObject var model: HubModel

    var body: some View {
        HStack(spacing: 10) {
            Spacer()
            HStack(spacing: 6) {
                Circle().fill(model.tibiaRunning ? Color.green : Color.muted).frame(width: 7, height: 7)
                Text(model.tibiaRunning ? "Tibia abierto" : "Tibia cerrado")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(Color.muted)
            }
            .help("El overlay solo se muestra con Tibia al frente")
            Button { model.overlayVisible.toggle() } label: {
                Label(model.overlayVisible ? "Overlay visible" : "Overlay oculto",
                      systemImage: model.overlayVisible ? "eye" : "eye.slash")
                    .font(.system(size: 12, weight: .semibold))
                    .padding(.horizontal, 10).padding(.vertical, 5)
                    .background(Capsule().fill(Color.white.opacity(0.08)))
                    .foregroundStyle(model.overlayVisible ? .white : Color.muted)
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 16)
        .frame(height: 52)
    }
}

enum HubPage: String, CaseIterable, Identifiable {
    case timers, audio, mirrors, outfit, shortcuts, sound
    var id: String { rawValue }
    var title: String {
        switch self {
        case .timers: "Timers"
        case .audio: "Tibia Audio"
        case .mirrors: "Espejos"
        case .outfit: "Outfit"
        case .shortcuts: "Atajos"
        case .sound: "Sonido"
        }
    }
    var subtitle: String {
        switch self {
        case .timers: "Cuentas atrás y rotaciones con aviso; cada uno con su contador sobre Tibia"
        case .audio: "Un aviso sonoro cuando una habilidad vuelve a estar lista"
        case .mirrors: "Copia las partes de Tibia que necesitas donde quieras"
        case .outfit: "Tu personaje: ícono de la app, barra lateral y barra de menús"
        case .shortcuts: "Controla los timers sin salir del juego"
        case .sound: "Volumen de los beeps y sonidos de aviso"
        }
    }
    var icon: String {
        switch self {
        case .timers: "timer"
        case .audio: "waveform"
        case .mirrors: "square.on.square.dashed"
        case .outfit: "person.crop.square"
        case .shortcuts: "keyboard"
        case .sound: "speaker.wave.2"
        }
    }
}

struct Sidebar: View {
    @ObservedObject var model: HubModel
    let outfit: OutfitManager
    @Binding var page: HubPage
    @Binding var editing: UUID?

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            SectionLabel("Principal")
            item(.timers, badge: model.runningCount)
            item(.audio)
            item(.mirrors)
            SectionLabel("Ajustes").padding(.top, 14)
            item(.outfit)
            item(.shortcuts)
            item(.sound)
            Spacer()
            Mascot(outfit: outfit).padding(.bottom, 14)
        }
        .padding(.horizontal, 10)
        .padding(.top, 8)
        .frame(width: 200)
        .background(Color.sidebarBackground)
    }

    private func item(_ target: HubPage, badge: Int = 0) -> some View {
        let selected = page == target
        return Button {
            page = target
            editing = nil
        } label: {
            HStack(spacing: 10) {
                Image(systemName: target.icon).frame(width: 18)
                Text(target.title)
                Spacer()
                if badge > 0 {
                    Text("\(badge)")
                        .font(.system(size: 10, weight: .bold))
                        .padding(.horizontal, 6).padding(.vertical, 1)
                        .background(Capsule().fill(Color.green.opacity(0.25)))
                        .foregroundStyle(Color.green)
                }
            }
            .font(.system(size: 13, weight: selected ? .semibold : .regular))
            .foregroundStyle(selected ? .white : Color.white.opacity(0.75))
            .padding(.horizontal, 10).padding(.vertical, 8)
            .background(
                RoundedRectangle(cornerRadius: 8).fill(selected ? Color.white.opacity(0.08) : .clear)
                    .overlay(alignment: .leading) {
                        if selected { Capsule().fill(Color.accentBlue).frame(width: 3, height: 18).offset(x: -4) }
                    }
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

struct SectionLabel: View {
    let text: String
    init(_ text: String) { self.text = text }
    var body: some View {
        Text(text.uppercased())
            .font(.system(size: 10, weight: .semibold))
            .tracking(1)
            .foregroundStyle(Color.muted)
            .padding(.horizontal, 10)
            .padding(.vertical, 4)
    }
}

/// Título y subtítulo de una página, con acciones a la derecha.
struct PageHeader<Actions: View>: View {
    let title: String
    let subtitle: String
    @ViewBuilder var actions: Actions

    var body: some View {
        HStack(alignment: .center) {
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(.system(size: 24, weight: .bold))
                Text(subtitle).font(.system(size: 13)).foregroundStyle(Color.muted)
            }
            Spacer()
            actions
        }
    }
}

extension PageHeader where Actions == EmptyView {
    init(title: String, subtitle: String) {
        self.init(title: title, subtitle: subtitle) { EmptyView() }
    }
}

/// Acciones que necesitan ventanas fuera del hub (elegir la región de un espejo sobre Tibia).
struct MirrorActions {
    var add: () -> Void = {}
    var pickWindow: () -> Void = {}
    var reselect: (UUID) -> Void = { _ in }
}

struct HubView: View {
    @ObservedObject var model: HubModel
    let outfit: OutfitManager
    var mirrorActions = MirrorActions()
    @AppStorage("hubPage") private var page: HubPage = .timers
    @State private var editing: UUID?
    @State private var editingMirror: UUID?
    @State private var editingAudio: UUID?

    var body: some View {
        VStack(spacing: 0) {
            TopBar(model: model)
            Divider().overlay(Color.white.opacity(0.06))
            HStack(spacing: 0) {
                Sidebar(model: model, outfit: outfit, page: $page, editing: $editing)
                    .onChange(of: page) { editingMirror = nil; editingAudio = nil }
                Divider().overlay(Color.white.opacity(0.06))
                content
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
        }
        .background(Color.windowBackground)
        .frame(minWidth: 820, minHeight: 600)
        .textFieldStyle(.roundedBorder)
        .ignoresSafeArea(.container, edges: .top)  // la barra superior ocupa la barra de título
    }

    @ViewBuilder private var content: some View {
        switch page {
        case .timers:
            if let id = editing, let run = model.runs.first(where: { $0.id == id }) {
                TimerEditor(model: model, run: run) { editing = nil }
            } else {
                TimersPage(model: model) { editing = $0 }
            }
        case .mirrors:
            if let id = editingMirror, let index = model.mirrors.firstIndex(where: { $0.id == id }) {
                MirrorEditor(model: model, mirror: $model.mirrors[index], back: { editingMirror = nil },
                             reselect: { mirrorActions.reselect(id) })
            } else {
                MirrorsPage(model: model, pickWindow: mirrorActions.pickWindow, add: mirrorActions.add,
                            reselect: mirrorActions.reselect) { editingMirror = $0 }
            }
        case .audio:
            if let id = editingAudio, let audio = model.audios.first(where: { $0.id == id }) {
                AudioEditor(model: model, audio: audio) { editingAudio = nil }
            } else {
                AudioPage(model: model) { editingAudio = $0 }
            }
        case .outfit:
            OutfitPage(outfit: outfit)
        case .shortcuts:
            PageContainer(page: .shortcuts) { ShortcutSettings(model: model) }
        case .sound:
            PageContainer(page: .sound) { SoundSettings(model: model) }
        }
    }
}

/// Página con cabecera y un formulario agrupado debajo.
struct PageContainer<Content: View>: View {
    let page: HubPage
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            PageHeader(title: page.title, subtitle: page.subtitle)
                .padding(.horizontal, 28).padding(.top, 22)
            content
                .formStyle(.grouped)
                .scrollContentBackground(.hidden)
                .padding(.horizontal, 8)
        }
    }
}

// MARK: - Hub: página de timers

struct TimersPage: View {
    @ObservedObject var model: HubModel
    let edit: (UUID) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            PageHeader(title: "Timers", subtitle: HubPage.timers.subtitle) {
                Menu {
                    ForEach(TimerPreset.allCases) { preset in
                        Button("\(preset.title) — \(preset.detail)") { model.add(preset) }
                    }
                } label: {
                    Label("Añadir timer", systemImage: "plus")
                        .font(.system(size: 13, weight: .bold))
                        .padding(.horizontal, 14).padding(.vertical, 8)
                        .background(Capsule().fill(Color.accentBlue))
                        .foregroundStyle(.white)
                }
                .menuStyle(.button)
                .menuIndicator(.hidden)
                .buttonStyle(.plain)
                .fixedSize()
            }
            HStack(spacing: 10) {
                Text("\(model.runs.count) \(model.runs.count == 1 ? "timer" : "timers") · \(model.runningCount) en marcha")
                    .font(.system(size: 12)).foregroundStyle(Color.muted)
                Spacer()
                SmallButton(title: model.overlayUnlocked ? "Bloquear contadores" : "Desbloquear para moverlos",
                            icon: model.overlayUnlocked ? "lock" : "lock.open") { model.overlayUnlocked.toggle() }
                    .help("Muestra todos los contadores sobre la pantalla para arrastrarlos a su sitio")
                SmallButton(title: "Pausar todos", icon: "pause.fill") { model.runs.filter(\.running).forEach { model.toggle($0) } }
                SmallButton(title: "Reiniciar todos", icon: "arrow.counterclockwise") { model.runs.forEach { model.reset($0) } }
            }
            ScrollView {
                VStack(spacing: 8) {
                    ForEach(model.runs) { run in
                        TimerRow(model: model, run: run) { edit(run.id) }
                    }
                }
                .padding(.bottom, 16)
            }
        }
        .padding(.horizontal, 28)
        .padding(.top, 22)
    }
}

struct SmallButton: View {
    let title: String
    let icon: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Label(title, systemImage: icon)
                .font(.system(size: 12, weight: .medium))
                .padding(.horizontal, 10).padding(.vertical, 5)
                .background(RoundedRectangle(cornerRadius: 7).fill(Color.white.opacity(0.07)))
                .overlay(RoundedRectangle(cornerRadius: 7).stroke(Color.white.opacity(0.08)))
                .foregroundStyle(.white)
        }
        .buttonStyle(.plain)
    }
}

/// Una fila de la lista de timers: estado, nombre, contador y acciones.
struct TimerRow: View {
    @ObservedObject var model: HubModel
    @ObservedObject var run: TimerRun
    let edit: () -> Void

    var body: some View {
        let d = run.display
        let stateColor: Color = d.finished ? run.config.overlay.alert : !run.started ? Color.white.opacity(0.15)
            : !run.running ? Color.amber : d.critical ? run.config.overlay.alert : Color.green
        HStack(spacing: 14) {
            RoundedRectangle(cornerRadius: 2).fill(stateColor).frame(width: 4)
            VStack(alignment: .leading, spacing: 3) {
                Text(run.config.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                Text(summary).font(.system(size: 11)).foregroundStyle(Color.muted).lineLimit(1)
            }
            .frame(minWidth: 150, alignment: .leading)
            .layoutPriority(1)
            Spacer(minLength: 8)
            VStack(alignment: .trailing, spacing: 4) {
                Text(d.text)
                    .font(.system(size: 22, weight: .bold, design: .monospaced))
                    .foregroundStyle(d.critical || d.finished ? run.config.overlay.alert : Color.accent)
                    .modifier(Pulsing(active: d.critical && run.running))
                ProgressBar(value: run.started ? d.progress : 0, color: d.critical ? run.config.overlay.alert : Color.accent)
                    .frame(width: 90)
            }
            HStack(spacing: 6) {
                CircleButton(icon: run.running ? "pause.fill" : "play.fill",
                             fill: run.running ? Color.amber : Color.accentBlue,
                             help: run.running ? "Pausar" : "Iniciar") { model.toggle(run) }
                CircleButton(icon: "arrow.counterclockwise", fill: Color.white.opacity(0.1), help: "Reiniciar") { model.reset(run) }
            }
            Button(action: edit) {
                Label("Editar", systemImage: "pencil")
                    .font(.system(size: 12, weight: .semibold))
                    .padding(.horizontal, 11).padding(.vertical, 6)
                    .background(RoundedRectangle(cornerRadius: 7).fill(Color.white.opacity(0.08)))
                    .foregroundStyle(.white)
            }
            .buttonStyle(.plain)
            MoreButton {
                var items = [MoreButton.Item(title: "Duplicar") { model.duplicate(run) }]
                if model.runs.count > 1 { items.append(.init(title: "Eliminar", destructive: true) { model.remove(run) }) }
                return items
            }
        }
        .padding(.vertical, 12)
        .padding(.leading, 10)
        .padding(.trailing, 12)
        .background(
            RoundedRectangle(cornerRadius: 10).fill(Color.rowBackground)
                .overlay(RoundedRectangle(cornerRadius: 10).stroke(model.selectedID == run.id ? Color.accentBlue.opacity(0.5) : Color.white.opacity(0.06)))
        )
        .contentShape(Rectangle())
        .onTapGesture { model.selectedID = run.id }
        .onTapGesture(count: 2, perform: edit)
    }

    private var summary: String {
        let c = run.config
        var parts: [String] = []
        if c.kind == .rotation {
            parts.append("Rotación cada \(c.step) s")
        } else if !c.repeats {
            parts.append("Cuenta atrás \(mmss(c.cycle))")
        } else if c.total > 0 {
            parts.append("Cada \(mmss(c.cycle)) durante \(c.total / 60) min")
        } else {
            parts.append("Cada \(mmss(c.cycle))")
        }
        parts.append(c.shortcut?.display ?? "Sin atajo")
        parts.append(soundName(c.sound, c.soundPath))
        if !c.overlay.show { parts.append("Sin overlay") }
        return parts.joined(separator: " · ")
    }
}

/// Botón ⋮: responde en toda su superficie (30 × 30) y abre un menú nativo donde se hace clic.
/// (Un Menu de SwiftUI con estilo plano solo respondía sobre el dibujo de los puntos.)
struct MoreButton: View {
    struct Item {
        let title: String
        var destructive = false
        let action: () -> Void
    }

    let items: () -> [Item]
    @State private var hover = false

    var body: some View {
        Button { MenuPresenter.show(items()) } label: {
            Image(systemName: "ellipsis")
                .rotationEffect(.degrees(90))
                .font(.system(size: 13, weight: .semibold))
                .frame(width: 30, height: 30)
                .background(Circle().fill(Color.white.opacity(hover ? 0.12 : 0)))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
        .help("Más opciones")
    }
}

@MainActor
enum MenuPresenter {
    private final class Target: NSObject {
        let action: () -> Void
        init(_ action: @escaping () -> Void) { self.action = action }
        @objc func run() { DispatchQueue.main.async(execute: action) }
    }

    static func show(_ items: [MoreButton.Item]) {
        let menu = NSMenu()
        let targets = items.map { Target($0.action) }
        for (item, target) in zip(items, targets) {
            let menuItem = NSMenuItem(title: item.title, action: #selector(Target.run), keyEquivalent: "")
            menuItem.target = target
            if item.destructive {
                menuItem.attributedTitle = NSAttributedString(string: item.title, attributes: [.foregroundColor: NSColor.systemRed])
            }
            menu.addItem(menuItem)
        }
        // popUp espera a que se cierre el menú, así que los destinos siguen vivos mientras tanto.
        menu.popUp(positioning: nil, at: NSEvent.mouseLocation, in: nil)
        withExtendedLifetime(targets) {}
    }
}

struct CircleButton: View {
    let icon: String
    let fill: Color
    var help = ""
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 11, weight: .bold))
                .frame(width: 30, height: 30)
                .background(Circle().fill(fill))
                .foregroundStyle(.white)
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .help(help)
    }
}

/// Detalle de un timer: tarjeta con el contador y sus ajustes.
struct TimerEditor: View {
    @ObservedObject var model: HubModel
    @ObservedObject var run: TimerRun
    let back: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Button(action: back) {
                Label("Volver a Timers", systemImage: "chevron.left")
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(Color.accentBlue)
            }
            .buttonStyle(.plain)
            .padding(.horizontal, 28)
            .padding(.top, 18)
            HeroView(model: model, run: run)
                .padding(.horizontal, 28)
            TimerSettings(model: model, run: run, onDelete: back)
                .formStyle(.grouped)
                .scrollContentBackground(.hidden)
                .padding(.horizontal, 8)
                .padding(.top, -14)
        }
        .onAppear { model.selectedID = run.id }
    }
}

/// Tarjeta con el contador grande de un timer y sus controles.
struct HeroView: View {
    @ObservedObject var model: HubModel
    @ObservedObject var run: TimerRun

    var body: some View {
        let d = run.display
        // El hub usa blanco hueso; el color del contador solo aplica al overlay.
        let accent = d.critical ? run.config.overlay.alert : Color.accent
        HStack(spacing: 20) {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 10) {
                    Text(run.config.name).font(.system(size: 20, weight: .bold)).lineLimit(1)
                    StatusChip(running: run.running, critical: d.critical, finished: d.finished, alertColor: run.config.overlay.alert)
                }
                Text(d.detail).font(.system(size: 12, design: .monospaced)).foregroundStyle(Color.muted)
                HStack(spacing: 10) {
                    Button { model.toggle(run) } label: {
                        Label(run.running ? "Pausar" : "Iniciar", systemImage: run.running ? "pause.fill" : "play.fill")
                            .font(.system(size: 13, weight: .semibold))
                            .frame(width: 110)
                            .padding(.vertical, 8)
                            .background(RoundedRectangle(cornerRadius: 9).fill(run.running ? Color.amber : Color.accentBlue))
                            .foregroundStyle(.white)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help(run.config.shortcut.map { "Atajo: \($0.display)" } ?? "")
                    Button { model.reset(run) } label: {
                        Label("Reiniciar", systemImage: "arrow.counterclockwise")
                            .font(.system(size: 13, weight: .semibold))
                            .padding(.vertical, 8)
                            .padding(.horizontal, 14)
                            .background(RoundedRectangle(cornerRadius: 9).fill(Color.white.opacity(0.06)))
                            .overlay(RoundedRectangle(cornerRadius: 9).stroke(Color.white.opacity(0.1)))
                            .foregroundStyle(.white)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
                .padding(.top, 4)
            }
            Spacer()
            VStack(alignment: .trailing, spacing: 8) {
                Text(d.text)
                    .font(.system(size: 54, weight: .bold, design: .monospaced))
                    .foregroundStyle(accent)
                    .modifier(Pulsing(active: d.critical && run.running))
                    .contentTransition(.numericText(countsDown: true))
                    .animation(.snappy, value: d.text)
                ProgressBar(value: d.progress, color: accent).frame(width: 200)
            }
        }
        .padding(18)
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
    let finished: Bool
    let alertColor: Color

    var body: some View {
        let (label, color): (String, Color) = finished ? ("Terminado", alertColor)
            : !running ? ("En pausa", Color.muted)
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

// MARK: - Hub: ajustes

/// Botón que graba una combinación de teclas al pulsarlo. Esc cancela.
struct ShortcutRecorder: View {
    @ObservedObject var model: HubModel
    let current: Shortcut?
    var allowPlain = false
    var emptyTitle = "Sin atajo"
    let assign: (Shortcut?) -> Void
    @State private var recording = false
    @State private var monitor: Any?

    var body: some View {
        HStack(spacing: 6) {
            Button {
                recording ? stop() : start()
            } label: {
                Text(recording ? (allowPlain ? "Pulsa la tecla…" : "Pulsa la combinación…") : current?.display ?? emptyTitle)
                    .font(.system(size: 12, weight: .medium, design: recording ? .default : .rounded))
                    .foregroundStyle(recording ? Color.amber : current == nil ? Color.muted : .white)
                    .frame(minWidth: 130)
            }
            if current != nil && !recording {
                Button { assign(nil) } label: {
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
            } else if let shortcut = Shortcut(event: event, allowPlain: allowPlain) {
                assign(shortcut)
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

/// Ajustes del timer elegido.
struct TimerSettings: View {
    @ObservedObject var model: HubModel
    @ObservedObject var run: TimerRun
    var onDelete: () -> Void = {}
    private static let presets = [60, 90, 120, 150, 180]

    private var minutes: Binding<Int> {
        Binding(get: { run.config.cycle / 60 },
                set: { run.config.cycle = max(5, max(0, $0) * 60 + run.config.cycle % 60) })
    }
    private var secs: Binding<Int> {
        Binding(get: { run.config.cycle % 60 },
                set: { run.config.cycle = max(5, run.config.cycle / 60 * 60 + max(0, min(59, $0))) })
    }
    private var totalMinutes: Binding<Int> {
        Binding(get: { run.config.total / 60 }, set: { run.config.total = max(0, $0) * 60 })
    }

    var body: some View {
        Form {
            Section {
                LabeledContent("Nombre") {
                    TextField("", text: $run.config.name).frame(width: 160).multilineTextAlignment(.trailing)
                }
                Picker("Tipo", selection: $run.config.kind) {
                    ForEach(TimerKind.allCases) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented)
                Menu("Cargar preset") {
                    ForEach(TimerPreset.allCases) { preset in
                        Button("\(preset.title) — \(preset.detail)") {
                            var config = preset.config
                            config.id = run.config.id
                            config.shortcut = run.config.shortcut
                            config.soundPath = run.config.soundPath
                            config.sound = run.config.sound
                            config.overlay = run.config.overlay
                            run.config = config
                        }
                    }
                }
            } header: {
                Text("Timer")
            }

            if run.config.kind == .cycle {
                Section("Duración") {
                    HStack(spacing: 6) {
                        ForEach(Self.presets, id: \.self) { preset in
                            Button(mmss(preset)) { run.config.cycle = preset }
                                .buttonStyle(.plain)
                                .font(.system(size: 12, weight: .semibold, design: .monospaced))
                                .padding(.horizontal, 9).padding(.vertical, 5)
                                .background(Capsule().fill(run.config.cycle == preset ? Color.accentBlue : Color.white.opacity(0.08)))
                                .foregroundStyle(.white)
                        }
                    }
                    .frame(maxWidth: .infinity)
                    LabeledContent("Personalizado") {
                        HStack(spacing: 4) {
                            TextField("", value: minutes, format: .number).frame(width: 38).multilineTextAlignment(.trailing)
                            Text("min")
                            TextField("", value: secs, format: .number).frame(width: 38).multilineTextAlignment(.trailing)
                            Text("s")
                            Stepper("", value: $run.config.cycle, in: 5...3600, step: 5).labelsHidden()
                        }
                    }
                    Toggle("Repetir al llegar a cero", isOn: $run.config.repeats)
                    if run.config.repeats {
                        LabeledContent("Duración total") {
                            HStack(spacing: 4) {
                                TextField("", value: totalMinutes, format: .number).frame(width: 44).multilineTextAlignment(.trailing)
                                Text("min")
                                Stepper("", value: totalMinutes, in: 0...600).labelsHidden()
                            }
                        }
                    }
                }
                Section {
                    Stepper("Alerta en los últimos \(run.config.warning) s", value: $run.config.warning, in: 0...max(0, run.config.cycle))
                    Stepper("Beeps en los últimos \(run.config.beepSeconds) s", value: $run.config.beepSeconds, in: 0...30)
                    Toggle("Sonido de aviso al empezar cada ciclo", isOn: $run.config.cycleBeep)
                } header: {
                    Text("Avisos")
                } footer: {
                    Text(run.config.repeats && run.config.total == 0
                         ? "Duración total en 0: se repite hasta que lo pares."
                         : "Al terminar suena el sonido de aviso.")
                        .foregroundStyle(.secondary)
                }
            } else {
                Section("Rotación") {
                    Stepper("Segundos por dirección: \(run.config.step)", value: $run.config.step, in: 1...60)
                    Picker("Dirección inicial", selection: $run.config.startDirection) {
                        ForEach(0..<directions.count, id: \.self) { i in Text("\(directions[i].0) \(directions[i].1)").tag(i) }
                    }
                    Stepper("Beeps en los últimos \(run.config.beepSeconds) s", value: $run.config.beepSeconds, in: 0...run.config.step)
                }
            }

            Section {
                SoundChooser(title: "Sonido de aviso", sound: $run.config.sound, path: $run.config.soundPath,
                             fileMessage: "Elige el sonido de aviso para \(run.config.name)") { run.playAlert() }
                LabeledContent("Atajo iniciar / pausar") {
                    ShortcutRecorder(model: model, current: run.config.shortcut) { model.setShortcut($0, for: run) }
                }
            } header: {
                Text("Sonido y atajo")
            } footer: {
                Text("El sonido de aviso suena al empezar cada ciclo (o cada dirección) y al terminar. Los beeps de los últimos segundos son aparte.")
                    .foregroundStyle(.secondary)
            }

            OverlayStyleSection(run: run)

            if model.runs.count > 1 {
                Section {
                    Button("Eliminar este timer", role: .destructive) {
                        model.remove(run)
                        onDelete()
                    }
                }
            }
        }
        // Sin foco inicial en "Nombre": así una tecla pulsada por error no lo sobrescribe.
        .onAppear { DispatchQueue.main.async { NSApp.keyWindow?.makeFirstResponder(nil) } }
    }
}

/// Sección "Overlay" del editor de un timer: su contador sobre Tibia.
struct OverlayStyleSection: View {
    @ObservedObject var run: TimerRun
    @State private var previewAlert = false

    private func color(_ key: WritableKeyPath<OverlayStyle, String>) -> Binding<Color> {
        Binding(get: { Color(hex: run.config.overlay[keyPath: key]) }, set: { run.config.overlay[keyPath: key] = $0.hex })
    }

    var body: some View {
        let style = run.config.overlay
        Section {
            Toggle("Mostrar en el overlay", isOn: $run.config.overlay.show)
            ZStack {
                // Fondo que imita el mapa de Tibia, para juzgar colores y tamaño.
                LinearGradient(colors: [Color(red: 0.29, green: 0.36, blue: 0.20), Color(red: 0.20, green: 0.16, blue: 0.12)],
                               startPoint: .topLeading, endPoint: .bottomTrailing)
                CounterBadge(style: style, text: previewAlert ? "00:05" : run.display.text,
                             critical: previewAlert, running: true, preview: true)
            }
            .frame(height: max(90, 60 * style.scale))
            .clipShape(RoundedRectangle(cornerRadius: 10))
            .listRowInsets(EdgeInsets(top: 8, leading: 8, bottom: 8, trailing: 8))
            Toggle("Ver en alerta", isOn: $previewAlert)
            LabeledContent("Tamaño") {
                HStack {
                    Slider(value: $run.config.overlay.scale, in: 0.6...3)
                    Text("\(Int(style.scale * 100)) %").monospacedDigit().foregroundStyle(Color.muted).frame(width: 48)
                }
            }
            LabeledContent("Opacidad") {
                HStack {
                    Slider(value: $run.config.overlay.opacity, in: 0.2...1)
                    Text("\(Int(style.opacity * 100)) %").monospacedDigit().foregroundStyle(Color.muted).frame(width: 48)
                }
            }
            ColorPicker("Contador", selection: color(\.counterColor), supportsOpacity: false)
            ColorPicker("Contador en alerta", selection: color(\.alertColor), supportsOpacity: false)
            ColorPicker("Fondo", selection: color(\.backgroundColor), supportsOpacity: true)
            HStack {
                Button("Restaurar colores") {
                    run.config.overlay.counterColor = OverlayStyle.defaultCounter
                    run.config.overlay.alertColor = OverlayStyle.defaultAlert
                    run.config.overlay.backgroundColor = OverlayStyle.defaultBackground
                }
                Button("Volver a su sitio automático") {
                    run.config.overlay.right = nil
                    run.config.overlay.top = nil
                }
                .disabled(style.right == nil)
            }
        } header: {
            Text("Overlay")
        } footer: {
            Text("El contador de este timer sobre Tibia: solo aparece con Tibia al frente y deja pasar los clics. Para moverlo, pulsa “Desbloquear para moverlos” en la lista de timers y arrástralo. Baja la opacidad del fondo a cero para ver solo el número.")
                .foregroundStyle(.secondary)
        }
    }
}

/// Nombre corto de un sonido: el archivo propio o el incluido.
func soundName(_ sound: String?, _ path: String?) -> String {
    path.map { URL(fileURLWithPath: $0).deletingPathExtension().lastPathComponent } ?? BuiltInSound.named(sound).title
}

/// Elige un sonido incluido (suena al elegirlo) o un archivo propio.
struct SoundChooser: View {
    let title: String
    @Binding var sound: String?
    @Binding var path: String?
    var fileMessage = "Elige un sonido"
    let play: () -> Void

    var body: some View {
        Picker(title, selection: Binding(
            get: { path != nil ? "file" : BuiltInSound.named(sound).rawValue },
            set: { value in
                guard value != "file" else { return }
                path = nil
                sound = value
                play()
            })) {
            ForEach(BuiltInSound.allCases) { Text($0.title).tag($0.rawValue) }
            if let path {
                Divider()
                Text(URL(fileURLWithPath: path).lastPathComponent).tag("file")
            }
        }
        HStack {
            Button("Archivo propio…", action: chooseFile)
            Button("Probar sonido", action: play)
        }
    }

    private func chooseFile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.audio]
        panel.allowsMultipleSelection = false
        panel.message = fileMessage
        if panel.runModal() == .OK, let url = panel.url { path = url.path }
    }
}

struct ShortcutSettings: View {
    @ObservedObject var model: HubModel

    var body: some View {
        Form {
            Section {
                ForEach(model.runs) { run in ShortcutRow(model: model, run: run) }
            } header: {
                Text("Iniciar / pausar cada timer")
            }

            Section {
                ForEach(GlobalAction.allCases) { action in
                    LabeledContent(action.title) {
                        ShortcutRecorder(model: model, current: model.globalShortcuts[action]) { model.setShortcut($0, for: action) }
                    }
                }
                Button("Restaurar atajos globales") { model.restoreGlobalShortcuts() }
            } header: {
                Text("Globales")
            } footer: {
                Text("Funcionan con Tibia al frente. Haz clic en un atajo y pulsa la nueva combinación (Esc cancela); usa al menos ⌃, ⌥, ⇧ o ⌘, o una tecla F sola. Una combinación solo puede estar en un sitio.")
                    .foregroundStyle(.secondary)
            }
        }
    }
}

struct ShortcutRow: View {
    @ObservedObject var model: HubModel
    @ObservedObject var run: TimerRun

    var body: some View {
        LabeledContent(run.config.name) {
            ShortcutRecorder(model: model, current: run.config.shortcut) { model.setShortcut($0, for: run) }
        }
    }
}

struct SoundSettings: View {
    @ObservedObject var model: HubModel

    var body: some View {
        Form {
            Section {
                Toggle("Silenciar", isOn: $model.muted)
                LabeledContent("Volumen") {
                    HStack {
                        Slider(value: $model.volume, in: 0...1).disabled(model.muted)
                        Text("\(Int(model.volume * 100)) %").monospacedDigit().foregroundStyle(Color.muted).frame(width: 44)
                    }
                }
                Button("Probar beep") { Beeper.shared.beep(1200, 0.15) }
            } footer: {
                Text("El sonido de cada timer y de cada habilidad de Tibia Audio se elige al editarlos: hay 14 sonidos incluidos o puedes usar un archivo propio.")
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
    private let outfit = OutfitManager()
    private lazy var overlays = OverlayController(model: model)
    private lazy var keyListener = KeyListener(model: model)
    private var hub: NSWindow!
    private var statusItem: NSStatusItem!
    private var ticker: Timer?
    private var activity: NSObjectProtocol?
    private var bag = Set<AnyCancellable>()
    /// El launcher (/Applications/Tibia.app) es com.tibia.launcher; el juego en sí es este.
    private static let tibiaClientID = "com.tibia.client"
    private let tibiaActive = CurrentValueSubject<Bool, Never>(false)
    private var tickCount = -1
    private lazy var mirrorController = MirrorController(model: model)
    private var selectionPanel: SelectionPanel?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Evita que App Nap ralentice el timer mientras Tibia está en primer plano.
        activity = ProcessInfo.processInfo.beginActivity(options: .userInitiated, reason: "Timer de Tibia en marcha")

        buildMainMenu()
        buildHub()
        watchOverlayUnlock()
        buildStatusItem()

        Publishers.CombineLatest(model.$shortcutsVersion, model.$recordingShortcut)
            .sink { [weak self] _, recording in
                // El modelo publica antes de aplicar el cambio: se registra en la siguiente vuelta.
                DispatchQueue.main.async { MainActor.assumeIsolated { self?.registerHotKeys(recording: recording) } }
            }
            .store(in: &bag)

        let ticker = Timer(timeInterval: 0.1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.model.tick()
                self.refreshTibiaActive()
                self.overlays.sync(tibiaActive: self.tibiaActive.value)
                self.keyListener.tibiaActive = self.tibiaActive.value
                if self.tickCount % 2 == 0 { self.mirrorController.sync(tibiaActive: self.tibiaActive.value) }
                if self.tickCount % 10 == 0 { self.keyListener.sync() }
            }
        }
        RunLoop.main.add(ticker, forMode: .common)
        self.ticker = ticker

        showHub()
    }

    private func registerHotKeys(recording: Bool) {
        guard !recording else { HotKeys.set([]); return }
        var bindings: [(Shortcut, () -> Void)] = []
        for action in GlobalAction.allCases {
            if let shortcut = model.globalShortcuts[action] {
                bindings.append((shortcut, { [weak self] in self?.model.perform(action) }))
            }
        }
        for run in model.runs {
            if let shortcut = run.config.shortcut {
                bindings.append((shortcut, { [weak self, weak run] in if let run { self?.model.toggle(run) } }))
            }
        }
        HotKeys.set(bindings)
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showHub()
        return true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    private func watchOverlayUnlock() {
        // Desbloquear muestra los contadores aunque el overlay estuviera oculto.
        model.$overlayUnlocked.sink { [weak self] unlocked in if unlocked { self?.model.overlayVisible = true } }.store(in: &bag)
    }

    /// Tibia cuenta como activo si es la app al frente y además tiene una ventana visible en el
    /// Space actual (si está en pantalla completa en otro Space, macOS puede darlo por "al frente"
    /// mientras se ve otra cosa).
    private func refreshTibiaActive() {
        tickCount += 1
        if tickCount % 10 == 0 {
            let running = NSWorkspace.shared.runningApplications.contains { $0.bundleIdentifier == Self.tibiaClientID }
            if running != model.tibiaRunning { model.tibiaRunning = running }
        }
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
        hub = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 640),
                       styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                       backing: .buffered, defer: false)
        hub.title = "Alfa Hub"
        hub.titleVisibility = .hidden
        hub.titlebarAppearsTransparent = true
        hub.isReleasedWhenClosed = false
        hub.isRestorable = false
        hub.appearance = NSAppearance(named: .darkAqua)
        let actions = MirrorActions(add: { [weak self] in self?.addMirror() },
                                    pickWindow: { [weak self] in self?.mirrorController.presentPicker() },
                                    reselect: { [weak self] id in self?.reselectMirror(id) })
        hub.contentViewController = NSHostingController(
            rootView: HubView(model: model, outfit: outfit, mirrorActions: actions))
        hub.setContentSize(NSSize(width: 900, height: min(640, (NSScreen.main?.visibleFrame.height ?? 800) - 60)))
        hub.contentMinSize = NSSize(width: 820, height: 600)
        if !hub.setFrameUsingName("AlfaHubWindow2") { hub.center() }
        hub.setFrameAutosaveName("AlfaHubWindow2")
    }

    private func buildStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        // El personaje elegido (al cambiarlo en la página Outfit también cambia el ícono de la app).
        OutfitIcons.apply(nil, statusItem: statusItem)
        outfit.onChange = { [weak self] sprite in OutfitIcons.apply(sprite, statusItem: self?.statusItem) }
        if !outfit.isOriginal { outfit.renderSaved() }  // por si terminó de dibujarse antes de conectar onChange
        let menu = NSMenu()
        menu.addItem(withTitle: "Abrir Alfa Hub", action: #selector(showHub), keyEquivalent: "").target = self
        menu.addItem(withTitle: "Iniciar / pausar el timer elegido", action: #selector(toggleTimer), keyEquivalent: "").target = self
        menu.addItem(withTitle: "Reiniciar el timer elegido", action: #selector(resetTimer), keyEquivalent: "").target = self
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

    // MARK: Espejos

    /// Oculta el hub y deja elegir una región arrastrando sobre la ventana de Tibia.
    private func selectRegion(_ completion: @escaping (CGRect, CGRect) -> Void) {
        guard let tibia = NSWorkspace.shared.runningApplications.first(where: { $0.bundleIdentifier == TibiaWindowInfo.clientID }),
              TibiaWindowInfo.frame() != nil else {
            let alert = NSAlert()
            alert.messageText = "Abre Tibia para elegir la región"
            alert.informativeText = "La región se elige arrastrando sobre la ventana de Tibia."
            alert.runModal()
            return
        }
        if !model.mirrorPickerMode && !MirrorCapture.allowed { MirrorCapture.requestAccess() }
        hub.orderOut(nil)
        tibia.activate()
        // Esperar a que termine el cambio de escritorio (Tibia en pantalla completa) y medir su ventana visible.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { [weak self] in
            guard let self, let frame = TibiaWindowInfo.frame() else { self?.showHub(); return }
            let panel = SelectionPanel(over: frame) { [weak self] region in
                guard let self else { return }
                self.selectionPanel = nil
                self.showHub()
                if let region { completion(region, frame) }
            }
            self.selectionPanel = panel
            panel.makeKeyAndOrderFront(nil)
            panel.orderFrontRegardless()
        }
    }

    private func addMirror() {
        selectRegion { [weak self] region, frame in
            guard let self else { return }
            var mirror = MirrorConfig()
            mirror.name = "Espejo \(self.model.mirrors.count + 1)"
            mirror.x = region.minX; mirror.y = region.minY
            mirror.width = region.width; mirror.height = region.height
            // Aparece junto a la región elegida (sin tapar el hub); queda desbloqueado para moverlo a su sitio.
            let regionOnScreen = CGRect(x: frame.minX + region.minX, y: frame.maxY - region.maxY, width: region.width, height: region.height)
            let origin = self.placeNear(regionOnScreen, size: mirror.displaySize)
            mirror.left = origin.x
            mirror.top = origin.y + mirror.displaySize.height
            self.model.mirrors.append(mirror)
            self.model.mirrorsUnlocked = true
            // Con el selector del sistema, si aún no se eligió la ventana de Tibia, se pide ahora.
            if self.model.mirrorPickerMode && !self.model.mirrorWindowPicked { self.mirrorController.presentPicker() }
        }
    }

    /// Primer sitio libre junto a la región (derecha, izquierda, debajo, encima) que no tape el hub.
    private func placeNear(_ region: CGRect, size: CGSize) -> CGPoint {
        let gap = 16.0
        let screen = NSScreen.screens.first { $0.frame.intersects(region) }?.visibleFrame ?? NSScreen.main?.visibleFrame ?? .zero
        let candidates = [
            CGPoint(x: region.maxX + gap, y: region.maxY - size.height),
            CGPoint(x: region.minX - gap - size.width, y: region.maxY - size.height),
            CGPoint(x: region.minX, y: region.minY - gap - size.height),
            CGPoint(x: region.minX, y: region.maxY + gap),
        ]
        for origin in candidates {
            let rect = CGRect(origin: origin, size: size)
            if screen.contains(rect) && !rect.intersects(hub.frame) { return origin }
        }
        return CGPoint(x: screen.maxX - size.width - 20, y: screen.maxY - size.height - 80)
    }

    private func reselectMirror(_ id: UUID) {
        selectRegion { [weak self] region, _ in
            guard let self, let index = self.model.mirrors.firstIndex(where: { $0.id == id }) else { return }
            self.model.mirrors[index].x = region.minX
            self.model.mirrors[index].y = region.minY
            self.model.mirrors[index].width = region.width
            self.model.mirrors[index].height = region.height
        }
    }

    @objc private func showHub() {
        hub.makeKeyAndOrderFront(nil)
        NSApp.activate()
    }

    @objc private func toggleTimer() { model.toggle(model.selected) }
    @objc private func resetTimer() { model.reset(model.selected) }
}

let app = NSApplication.shared
let delegate = MainActor.assumeIsolated { AppDelegate() }
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()

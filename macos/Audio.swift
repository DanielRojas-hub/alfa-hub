// Tibia Audio: al pulsar en Tibia la tecla de una habilidad, empieza su cooldown y suena un aviso
// cuando vuelve a estar lista.
//
// Las teclas se escuchan sin bloquearlas (un "event tap" de solo lectura): Tibia las recibe igual y
// Alfa Hub no envía ni cambia nada. Solo se miran las teclas configuradas y solo con Tibia al frente;
// no se guarda nada de lo que se escribe. macOS lo permite con el permiso de "Monitorización de entrada".

import AppKit
import Carbon.HIToolbox
import Combine
import SwiftUI

struct AudioConfig: Codable, Identifiable, Equatable {
    var id = UUID()
    var name = "Habilidad"
    /// Tecla (o combinación) con la que se usa la habilidad en Tibia.
    var key: Shortcut?
    var cooldown = 2.0          // segundos
    var sound: String? = BuiltInSound.ding.rawValue
    var soundPath: String?
    var volume = 1.0            // 0–1, sobre el volumen de la app
    /// Pulsar la tecla durante el cooldown lo vuelve a empezar (si no, se ignora).
    var retrigger = true
    var enabled = true
}

/// Lectura tolerante: los campos que falten toman su valor por defecto.
extension AudioConfig {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func get<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T { (try? c.decodeIfPresent(T.self, forKey: key)) ?? fallback }
        self.init()
        id = get(.id, id)
        name = get(.name, name)
        key = get(.key, key)
        cooldown = get(.cooldown, cooldown)
        sound = get(.sound, sound)
        soundPath = get(.soundPath, soundPath)
        volume = get(.volume, volume)
        retrigger = get(.retrigger, retrigger)
        enabled = get(.enabled, enabled)
    }
}

/// Una habilidad de Tibia Audio y su cooldown en curso.
@MainActor
final class AudioRun: ObservableObject, Identifiable {
    @Published var config: AudioConfig { didSet { onConfigChange() } }
    /// Segundos que faltan (0 = lista), redondeado a décimas para no redibujar de más.
    @Published private(set) var remaining = 0.0
    var onConfigChange: () -> Void = {}
    let id: UUID
    private var endsAt: Date?
    private var pending: DispatchWorkItem?

    init(_ config: AudioConfig) {
        id = config.id
        self.config = config
    }

    var active: Bool { endsAt != nil }
    var progress: Double { active ? 1 - remaining / max(0.1, config.cooldown) : 0 }

    /// La tecla se pulsó en Tibia: empieza el cooldown y el aviso suena al terminar (a la hora exacta).
    func trigger() {
        if active && !config.retrigger { return }
        let duration = max(0.1, config.cooldown)
        endsAt = Date().addingTimeInterval(duration)
        pending?.cancel()
        let work = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.endsAt = nil
                self.remaining = 0
                self.playSound()
            }
        }
        pending = work
        DispatchQueue.main.asyncAfter(deadline: .now() + duration, execute: work)
        tick()
    }

    func cancel() {
        pending?.cancel()
        pending = nil
        endsAt = nil
        remaining = 0
    }

    func tick() {
        guard let endsAt else { return }
        let left = (max(0, endsAt.timeIntervalSinceNow) * 10).rounded(.up) / 10
        if left != remaining { remaining = left }
    }

    func playSound() {
        let builtIn = BuiltInSound.named(config.sound)
        let gain = config.volume
        let play = { Beeper.shared.play(builtIn, gain: gain) }
        if let path = config.soundPath { Beeper.shared.play(file: path, gain: gain, fallback: play) } else { play() }
    }

    var summary: String {
        [config.key?.display ?? "Sin tecla", "\(seconds(config.cooldown)) s", soundName(config.sound, config.soundPath)]
            .joined(separator: " · ")
    }
}

/// "2", "1,5", "40".
func seconds(_ value: Double) -> String {
    value == value.rounded() ? String(Int(value)) : String(format: "%.1f", value).replacingOccurrences(of: ".", with: ",")
}

// MARK: - Escucha de teclas

/// Escucha de solo lectura del teclado. Está apagada salvo que Tibia Audio esté activado, haya alguna
/// habilidad con tecla y exista el permiso. Solo actúa con Tibia al frente.
@MainActor
final class KeyListener {
    private let model: HubModel
    private var tap: CFMachPort?
    private var source: CFRunLoopSource?
    /// Lo actualiza la app en cada tick.
    var tibiaActive = false

    init(model: HubModel) { self.model = model }

    /// Se llama una vez por segundo. CGPreflightListenEventAccess no muestra ningún aviso.
    func sync() {
        let allowed = CGPreflightListenEventAccess()
        if model.listenAllowed != allowed { model.listenAllowed = allowed }
        let wanted = model.audioEnabled && allowed
            && model.audios.contains { $0.config.enabled && $0.config.key != nil }
        if wanted && tap == nil { start() } else if !wanted && tap != nil { stop() }
        // macOS apaga el tap si alguna vez tarda en responder: se vuelve a encender.
        if let tap, !CGEvent.tapIsEnabled(tap: tap) { CGEvent.tapEnable(tap: tap, enable: true) }
        if model.listening != (tap != nil) { model.listening = tap != nil }
    }

    /// Pide el permiso (macOS añade Alfa Hub a la lista) y abre Ajustes del Sistema en esa lista.
    static func openSettings() {
        _ = CGRequestListenEventAccess()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent")!)
        }
    }

    private func start() {
        let mask = CGEventMask(1 << CGEventType.keyDown.rawValue)
        let refcon = Unmanaged.passUnretained(self).toOpaque()
        guard let tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .tailAppendEventTap, options: .listenOnly,
                                          eventsOfInterest: mask, callback: { _, type, event, refcon in
            if type == .keyDown, let refcon {
                let listener = Unmanaged<KeyListener>.fromOpaque(refcon).takeUnretainedValue()
                let code = Int(event.getIntegerValueField(.keyboardEventKeycode))
                let autorepeat = event.getIntegerValueField(.keyboardEventAutorepeat) != 0
                let flags = event.flags
                // El tap está en el run loop principal: la llamada llega en el hilo principal.
                MainActor.assumeIsolated { listener.handle(code: code, flags: flags, autorepeat: autorepeat) }
            }
            return Unmanaged.passUnretained(event)  // solo lectura: el evento sigue igual hacia Tibia
        }, userInfo: refcon) else { return }
        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        self.tap = tap
        self.source = source
    }

    private func stop() {
        if let tap { CGEvent.tapEnable(tap: tap, enable: false) }
        if let source { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        if let tap { CFMachPortInvalidate(tap) }
        tap = nil
        source = nil
    }

    private func handle(code: Int, flags: CGEventFlags, autorepeat: Bool) {
        // Mantener la tecla pulsada no cuenta como pulsaciones nuevas.
        guard tibiaActive, !autorepeat else { return }
        var mods = 0
        if flags.contains(.maskControl) { mods |= controlKey }
        if flags.contains(.maskAlternate) { mods |= optionKey }
        if flags.contains(.maskShift) { mods |= shiftKey }
        if flags.contains(.maskCommand) { mods |= cmdKey }
        model.keyPressed(keyCode: code, modifiers: mods)
    }
}

// MARK: - Hub: página de Tibia Audio

struct AudioPage: View {
    @ObservedObject var model: HubModel
    let edit: (UUID) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            PageHeader(title: HubPage.audio.title, subtitle: HubPage.audio.subtitle) {
                Button {
                    edit(model.addAudio().id)
                } label: {
                    Label("Añadir habilidad", systemImage: "plus")
                        .font(.system(size: 13, weight: .bold))
                        .padding(.horizontal, 14).padding(.vertical, 8)
                        .background(Capsule().fill(Color.accentBlue))
                        .foregroundStyle(.white)
                }
                .buttonStyle(.plain)
            }
            if model.audioEnabled && !model.listenAllowed {
                HStack(spacing: 12) {
                    Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(Color.amber)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Falta el permiso de Monitorización de entrada").font(.system(size: 13, weight: .semibold))
                        Text("macOS lo pide para que Alfa Hub oiga las teclas de tus habilidades (sin bloquearlas). Pulsa “Abrir Ajustes” y activa el interruptor de Alfa Hub; si sigue sin funcionar, cierra y vuelve a abrir Alfa Hub.")
                            .font(.system(size: 11)).foregroundStyle(Color.muted)
                    }
                    Spacer()
                    SmallButton(title: "Abrir Ajustes", icon: "gear") { KeyListener.openSettings() }
                }
                .padding(12)
                .background(RoundedRectangle(cornerRadius: 10).fill(Color.amber.opacity(0.12)))
            }
            HStack(spacing: 10) {
                Toggle("", isOn: $model.audioEnabled).labelsHidden().toggleStyle(.switch)
                    .help("Activa o desactiva Tibia Audio")
                Text(status).font(.system(size: 12)).foregroundStyle(Color.muted)
                Spacer()
                SmallButton(title: "Probar todas", icon: "play.fill") { model.audios.filter(\.config.enabled).forEach { $0.trigger() } }
                    .help("Simula que pulsas la tecla de cada habilidad")
            }
            ScrollView {
                VStack(spacing: 8) {
                    if model.audios.isEmpty {
                        Text("Todavía no hay habilidades. Pulsa “Añadir habilidad”, pon la tecla con la que la usas en Tibia y su cooldown: cuando la pulses en el juego, sonará el aviso al terminar el cooldown.")
                            .font(.system(size: 12)).foregroundStyle(Color.muted)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    ForEach(model.audios) { audio in
                        AudioRow(model: model, audio: audio) { edit(audio.id) }
                    }
                    Text("Alfa Hub solo mira si pulsas las teclas de esta lista, y solo con Tibia al frente; no las bloquea (Tibia las recibe igual) ni envía nada al juego.")
                        .font(.system(size: 11)).foregroundStyle(Color.muted)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.top, 4)
                }
                .padding(.bottom, 16)
            }
        }
        .padding(.horizontal, 28)
        .padding(.top, 22)
    }

    private var status: String {
        let count = model.audios.count
        let base = "\(count) \(count == 1 ? "habilidad" : "habilidades")"
        if !model.audioEnabled { return base + " · Desactivado" }
        if !model.listenAllowed { return base + " · Falta el permiso" }
        return base + (model.listening ? " · Escuchando con Tibia al frente" : " · Ninguna tiene tecla")
    }
}

struct AudioRow: View {
    @ObservedObject var model: HubModel
    @ObservedObject var audio: AudioRun
    let edit: () -> Void

    var body: some View {
        let c = audio.config
        HStack(spacing: 14) {
            RoundedRectangle(cornerRadius: 2)
                .fill(!c.enabled ? Color.white.opacity(0.15) : audio.active ? Color.amber : Color.green)
                .frame(width: 4)
            VStack(alignment: .leading, spacing: 3) {
                Text(c.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                Text(audio.summary).font(.system(size: 11)).foregroundStyle(Color.muted).lineLimit(1)
            }
            .frame(minWidth: 150, alignment: .leading)
            .layoutPriority(1)
            Spacer(minLength: 8)
            VStack(alignment: .trailing, spacing: 4) {
                Text(audio.active ? "\(seconds(audio.remaining)) s" : "Lista")
                    .font(.system(size: 15, weight: .bold, design: .monospaced))
                    .foregroundStyle(audio.active ? Color.amber : Color.accent)
                ProgressBar(value: audio.progress, color: Color.amber).frame(width: 70)
            }
            .frame(width: 80, alignment: .trailing)
            Toggle("", isOn: $audio.config.enabled).labelsHidden().toggleStyle(.switch).help("Activada")
            Slider(value: $audio.config.volume, in: 0...1).frame(width: 70).help("Volumen")
            Text("\(Int(c.volume * 100)) %").font(.system(size: 11).monospacedDigit()).foregroundStyle(Color.muted).frame(width: 38)
            Button(action: edit) {
                Label("Editar", systemImage: "pencil")
                    .font(.system(size: 12, weight: .semibold))
                    .padding(.horizontal, 11).padding(.vertical, 6)
                    .background(RoundedRectangle(cornerRadius: 7).fill(Color.white.opacity(0.08)))
                    .foregroundStyle(.white)
                    .fixedSize()
            }
            .buttonStyle(.plain)
            MoreButton {
                [
                    .init(title: "Probar") { audio.trigger() },
                    .init(title: "Duplicar") { model.duplicate(audio) },
                    .init(title: "Eliminar", destructive: true) { model.remove(audio) },
                ]
            }
        }
        .padding(.vertical, 12).padding(.leading, 10).padding(.trailing, 12)
        .background(
            RoundedRectangle(cornerRadius: 10).fill(Color.rowBackground)
                .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.white.opacity(0.06)))
        )
        .contentShape(Rectangle())
        .onTapGesture(count: 2, perform: edit)
    }
}

struct AudioEditor: View {
    @ObservedObject var model: HubModel
    @ObservedObject var audio: AudioRun
    let back: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Button(action: back) {
                Label("Volver a Tibia Audio", systemImage: "chevron.left")
                    .font(.system(size: 13, weight: .medium)).foregroundStyle(Color.accentBlue)
            }
            .buttonStyle(.plain)
            .padding(.horizontal, 28).padding(.top, 18)
            PageHeader(title: audio.config.name, subtitle: audio.summary)
                .padding(.horizontal, 28).padding(.top, 6)
            Form {
                Section {
                    LabeledContent("Nombre") {
                        TextField("", text: $audio.config.name).frame(width: 180).multilineTextAlignment(.trailing)
                    }
                    Toggle("Activada", isOn: $audio.config.enabled)
                    LabeledContent("Tecla en Tibia") {
                        ShortcutRecorder(model: model, current: audio.config.key, allowPlain: true, emptyTitle: "Sin tecla") {
                            audio.config.key = $0
                        }
                    }
                    if model.audioKeyConflicts(audio.config.key) {
                        Label("Esta tecla también es un atajo de Alfa Hub: Alfa Hub se la queda y no llega a Tibia. Cambia uno de los dos.",
                              systemImage: "exclamationmark.triangle.fill")
                            .font(.system(size: 11)).foregroundStyle(Color.amber)
                    }
                } header: {
                    Text("Habilidad")
                } footer: {
                    Text("Haz clic en el botón de la tecla y pulsa la misma tecla (o combinación) con la que usas la habilidad en Tibia. Esc cancela.")
                        .foregroundStyle(.secondary)
                }
                Section("Cooldown") {
                    LabeledContent("Duración") {
                        HStack(spacing: 4) {
                            TextField("", value: Binding(get: { audio.config.cooldown },
                                                         set: { audio.config.cooldown = min(3600, max(0.5, $0)) }),
                                      format: .number.precision(.fractionLength(0...1)))
                                .frame(width: 56).multilineTextAlignment(.trailing)
                            Text("s")
                            Stepper("", value: $audio.config.cooldown, in: 0.5...3600, step: 0.5).labelsHidden()
                        }
                    }
                    Picker("Si la pulsas durante el cooldown", selection: $audio.config.retrigger) {
                        Text("Vuelve a empezar").tag(true)
                        Text("Se ignora").tag(false)
                    }
                }
                Section("Sonido") {
                    SoundChooser(title: "Aviso al estar lista", sound: $audio.config.sound, path: $audio.config.soundPath,
                                 fileMessage: "Elige el sonido para \(audio.config.name)") { audio.playSound() }
                    LabeledContent("Volumen") {
                        HStack {
                            Slider(value: $audio.config.volume, in: 0...1)
                            Text("\(Int(audio.config.volume * 100)) %").monospacedDigit().foregroundStyle(Color.muted).frame(width: 48)
                        }
                    }
                    Button("Simular pulsación (cooldown completo)") { audio.trigger() }
                }
                Section {
                    Button("Eliminar esta habilidad", role: .destructive) {
                        let target = audio
                        back()
                        model.remove(target)
                    }
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
            .padding(.horizontal, 8)
            .onAppear { DispatchQueue.main.async { NSApp.keyWindow?.makeFirstResponder(nil) } }
        }
    }
}

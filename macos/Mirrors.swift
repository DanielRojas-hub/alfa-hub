// Espejos: regiones de la ventana de Tibia mostradas en ventanitas flotantes.
//
// En macOS no hay miniaturas de ventana como las de Windows (DWM), así que se captura la ventana de
// Tibia con ScreenCaptureKit (una sola captura a 30 fps) y cada espejo muestra su recorte. Necesita el
// permiso de "Grabación de pantalla". No se toca el proceso de Tibia.

import AppKit
import Combine
import CoreMedia
import ScreenCaptureKit
import SwiftUI

struct MirrorConfig: Codable, Identifiable, Equatable {
    var id = UUID()
    var name = "Espejo"
    /// Región, en puntos, desde la esquina superior izquierda de la ventana de Tibia.
    var x = 0.0, y = 0.0, width = 120.0, height = 60.0
    /// Esquina superior izquierda del espejo en pantalla (coordenadas de Cocoa: y hacia arriba).
    var left = 200.0, top = 600.0
    var zoom = 100       // %
    var opacity = 100    // %
    var visible = true
    var border = true

    var displaySize: CGSize {
        CGSize(width: max(8, width * Double(zoom) / 100), height: max(8, height * Double(zoom) / 100))
    }
}

// MARK: - Ventana de Tibia

enum TibiaWindowInfo {
    static let clientID = "com.tibia.client"

    /// Marco de la ventana principal de Tibia en coordenadas de Cocoa (o nil si no está abierta).
    /// Solo usa la lista de ventanas del sistema (no hace falta permiso para los marcos). Busca en
    /// todos los escritorios: con Tibia en pantalla completa, su ventana está en otro Space.
    static func frame() -> CGRect? {
        guard let app = NSWorkspace.shared.runningApplications.first(where: { $0.bundleIdentifier == clientID }) else { return nil }
        let windows = CGWindowListCopyWindowInfo([.optionAll, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
        let own = windows.filter {
            ($0[kCGWindowOwnerPID as String] as? pid_t) == app.processIdentifier && ($0[kCGWindowLayer as String] as? Int) == 0
        }
        // Preferir la ventana visible (Tibia crea varias ventanas ocultas del mismo tamaño).
        let onScreen = own.filter { ($0[kCGWindowIsOnscreen as String] as? Bool) == true }
        let bounds = (onScreen.isEmpty ? own : onScreen)
            .compactMap { ($0[kCGWindowBounds as String] as? NSDictionary).flatMap { CGRect(dictionaryRepresentation: $0) } }
            .max { $0.width * $0.height < $1.width * $1.height }
        guard let bounds, let screen = NSScreen.screens.first else { return nil }
        // CGWindowList usa y hacia abajo desde la pantalla principal; Cocoa, y hacia arriba.
        return CGRect(x: bounds.minX, y: screen.frame.maxY - bounds.maxY, width: bounds.width, height: bounds.height)
    }
}

// MARK: - Captura

/// Una captura de la ventana de Tibia; reparte cada fotograma a los espejos.
@MainActor
final class MirrorCapture: NSObject, SCStreamOutput, SCStreamDelegate {
    private var stream: SCStream?
    private var windowID: CGWindowID?
    private var windowSize: CGSize = .zero
    private let queue = DispatchQueue(label: "alfahub.espejos")
    var onFrame: (IOSurface, CGSize) -> Void = { _, _ in }
    private(set) var running = false

    /// Si hay permiso de grabación. CGPreflightScreenCaptureAccess no siempre lo refleja en macOS
    /// reciente, así que también vale si ScreenCaptureKit responde (ver refreshPermission).
    static private(set) var allowed = CGPreflightScreenCaptureAccess()
    private static var probing = false

    /// Comprueba el permiso pidiendo la lista de ventanas a ScreenCaptureKit (sin mostrar nada).
    static func refreshPermission() {
        guard !probing else { return }
        if CGPreflightScreenCaptureAccess() { allowed = true; return }
        probing = true
        Task { @MainActor in
            let ok = (try? await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)) != nil
            allowed = ok
            probing = false
        }
    }
    /// Pide el permiso. macOS solo muestra su aviso la primera vez; en macOS 15+ también lo muestra al
    /// pedir la lista de ventanas a ScreenCaptureKit.
    static func requestAccess() {
        _ = CGRequestScreenCaptureAccess()
        Task { _ = try? await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true) }
    }

    /// Abre Ajustes del Sistema en Grabación de pantalla y muestra la app en Finder, para poder
    /// arrastrarla a la lista si no aparece.
    static func openSettings() {
        requestAccess()
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!)
        NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL])
    }

    func start() {
        guard !running, Self.allowed else { return }
        running = true
        Task { @MainActor in
            do {
                let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
                guard running,
                      let window = content.windows
                        .filter({ $0.owningApplication?.bundleIdentifier == TibiaWindowInfo.clientID && $0.windowLayer == 0 })
                        .max(by: { $0.frame.width * $0.frame.height < $1.frame.width * $1.frame.height })
                else { running = false; return }
                let scale = NSScreen.main?.backingScaleFactor ?? 2
                let config = SCStreamConfiguration()
                config.width = Int(window.frame.width * scale)
                config.height = Int(window.frame.height * scale)
                config.minimumFrameInterval = CMTime(value: 1, timescale: 30)
                config.pixelFormat = kCVPixelFormatType_32BGRA
                config.showsCursor = false
                config.queueDepth = 3
                let stream = SCStream(filter: SCContentFilter(desktopIndependentWindow: window), configuration: config, delegate: self)
                try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
                try await stream.startCapture()
                self.stream = stream
                windowID = window.windowID
                windowSize = window.frame.size
            } catch {
                running = false
            }
        }
    }

    func stop() {
        guard running else { return }
        running = false
        let stream = self.stream
        self.stream = nil
        Task { try? await stream?.stopCapture() }
    }

    /// Si cambia el tamaño de la ventana de Tibia, la captura se rehace.
    func restartIfNeeded(windowSize size: CGSize) {
        guard running, stream != nil, abs(size.width - windowSize.width) > 1 || abs(size.height - windowSize.height) > 1 else { return }
        stop()
        start()
    }

    nonisolated func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen,
              let attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
              let statusRaw = attachments.first?[.status] as? Int,
              SCFrameStatus(rawValue: statusRaw) == .complete,
              let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer),
              let surface = CVPixelBufferGetIOSurface(pixelBuffer)?.takeUnretainedValue()
        else { return }
        let size = CGSize(width: CVPixelBufferGetWidth(pixelBuffer), height: CVPixelBufferGetHeight(pixelBuffer))
        let surfaceRef = surface as IOSurface
        DispatchQueue.main.async { MainActor.assumeIsolated { self.onFrame(surfaceRef, size) } }
    }

    nonisolated func stream(_ stream: SCStream, didStopWithError error: Error) {
        DispatchQueue.main.async { MainActor.assumeIsolated { self.running = false; self.stream = nil } }
    }
}

// MARK: - Ventana de un espejo

final class MirrorPanel: NSPanel {
    let imageLayer = CALayer()
    private let placeholder = NSTextField(labelWithString: "")
    var onMove: (NSPoint) -> Void = { _ in }

    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 120, height: 60),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .statusBar
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        hidesOnDeactivate = false
        ignoresMouseEvents = true
        isMovableByWindowBackground = true
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        let view = NSView()
        view.wantsLayer = true
        view.layer?.backgroundColor = NSColor(white: 0.08, alpha: 1).cgColor
        imageLayer.contentsGravity = .resize
        imageLayer.magnificationFilter = .nearest  // pixel-art de Tibia nítido al ampliar
        view.layer?.addSublayer(imageLayer)
        placeholder.textColor = NSColor(white: 1, alpha: 0.55)
        placeholder.font = .systemFont(ofSize: 11, weight: .medium)
        placeholder.alignment = .center
        placeholder.maximumNumberOfLines = 3
        view.addSubview(placeholder)
        contentView = view
        NotificationCenter.default.addObserver(forName: NSWindow.didMoveNotification, object: self, queue: .main) { [weak self] _ in
            guard let self else { return }
            self.onMove(NSPoint(x: self.frame.minX, y: self.frame.maxY))
        }
    }

    override var canBecomeKey: Bool { false }

    func apply(_ config: MirrorConfig, unlocked: Bool, message: String?) {
        let size = config.displaySize
        let frame = NSRect(x: config.left, y: config.top - size.height, width: size.width, height: size.height)
        if self.frame != frame { setFrame(frame, display: true) }
        ignoresMouseEvents = !unlocked
        alphaValue = CGFloat(max(10, config.opacity)) / 100
        guard let layer = contentView?.layer else { return }
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        imageLayer.frame = layer.bounds
        layer.borderWidth = unlocked ? 2 : config.border ? 1 : 0
        layer.borderColor = unlocked ? NSColor.systemOrange.cgColor : NSColor(white: 0.4, alpha: 1).cgColor
        CATransaction.commit()
        // En espejos pequeños el texto no cabe: solo un aviso (el detalle está en el hub).
        let small = size.width < 140 || size.height < 44
        placeholder.stringValue = message.map { small ? "⚠︎" : "\(config.name)\n\($0)" } ?? ""
        placeholder.font = .systemFont(ofSize: small ? 14 : 11, weight: .medium)
        placeholder.isHidden = message == nil
        placeholder.frame = layer.bounds.insetBy(dx: 4, dy: max(0, layer.bounds.height / 2 - 24))
    }

    /// Muestra el recorte de la región sobre el fotograma de la ventana de Tibia.
    func show(surface: IOSurface, frameSize: CGSize, windowSize: CGSize, config: MirrorConfig) {
        guard windowSize.width > 0, windowSize.height > 0 else { return }
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        imageLayer.contents = surface
        // contentsRect usa coordenadas unitarias con el origen abajo a la izquierda.
        let w = config.width / windowSize.width, h = config.height / windowSize.height
        imageLayer.contentsRect = CGRect(x: config.x / windowSize.width, y: 1 - config.y / windowSize.height - h, width: w, height: h)
        CATransaction.commit()
    }
}

// MARK: - Controlador

/// Crea, quita, muestra y oculta los espejos según el modelo; arranca la captura solo si hace falta.
@MainActor
final class MirrorController {
    private let model: HubModel
    private let capture = MirrorCapture()
    private var panels: [UUID: MirrorPanel] = [:]
    private var bag = Set<AnyCancellable>()
    private var tibiaFrame: CGRect?

    init(model: HubModel) {
        self.model = model
        capture.onFrame = { [weak self] surface, size in
            guard let self, let frame = self.tibiaFrame else { return }
            for config in self.model.mirrors {
                self.panels[config.id]?.show(surface: surface, frameSize: size, windowSize: frame.size, config: config)
            }
        }
    }

    /// Se llama en cada tick (10 veces por segundo).
    func sync(tibiaActive: Bool) {
        tibiaFrame = TibiaWindowInfo.frame()
        model.screenCaptureAllowed = MirrorCapture.allowed
        for id in panels.keys where !model.mirrors.contains(where: { $0.id == id }) {
            panels[id]?.orderOut(nil)
            panels[id] = nil
        }
        var anyVisible = false
        for config in model.mirrors {
            let panel = panels[config.id] ?? makePanel(config.id)
            let show = model.mirrorsVisible && config.visible && (model.mirrorsUnlocked || (tibiaActive && tibiaFrame != nil))
            let message: String? = !model.screenCaptureAllowed ? "Falta el permiso de Grabación de pantalla"
                : tibiaFrame == nil ? "Tibia cerrado" : nil
            panel.apply(config, unlocked: model.mirrorsUnlocked, message: message)
            if show {
                anyVisible = true
                if !panel.isVisible { panel.orderFrontRegardless() }
            } else if panel.isVisible {
                panel.orderOut(nil)
            }
        }
        if anyVisible && tibiaFrame != nil && model.screenCaptureAllowed {
            capture.start()
            if let size = tibiaFrame?.size { capture.restartIfNeeded(windowSize: size) }
        } else {
            capture.stop()
        }
    }

    private func makePanel(_ id: UUID) -> MirrorPanel {
        let panel = MirrorPanel()
        panel.onMove = { [weak self] topLeft in
            guard let self, let index = self.model.mirrors.firstIndex(where: { $0.id == id }) else { return }
            if self.model.mirrors[index].left != topLeft.x || self.model.mirrors[index].top != topLeft.y {
                self.model.mirrors[index].left = topLeft.x
                self.model.mirrors[index].top = topLeft.y
            }
        }
        panels[id] = panel
        return panel
    }
}

// MARK: - Elegir la región

/// Capa oscura sobre la ventana de Tibia para elegir la región de un espejo arrastrando.
final class SelectionPanel: NSPanel {
    private var completion: ((CGRect?) -> Void)?

    init(over frame: CGRect, completion: @escaping (CGRect?) -> Void) {
        self.completion = completion
        // Sin activar la app: así macOS no cambia de escritorio si Tibia está en pantalla completa.
        super.init(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .screenSaver
        isOpaque = false
        backgroundColor = .clear
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        let view = SelectionView(frame: NSRect(origin: .zero, size: frame.size))
        view.onDone = { [weak self] rect in self?.finish(rect) }
        contentView = view
    }

    override var canBecomeKey: Bool { true }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { finish(nil) } else { super.keyDown(with: event) }  // Esc
    }

    private func finish(_ rect: CGRect?) {
        let completion = self.completion
        self.completion = nil
        orderOut(nil)
        completion?(rect)
    }
}

private final class SelectionView: NSView {
    var onDone: (CGRect?) -> Void = { _ in }
    private var start: NSPoint?
    private var selection: NSRect = .zero

    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .crosshair) }

    override func mouseDown(with event: NSEvent) {
        start = convert(event.locationInWindow, from: nil)
        selection = .zero
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        guard let start else { return }
        let p = convert(event.locationInWindow, from: nil)
        selection = NSRect(x: min(start.x, p.x), y: min(start.y, p.y), width: abs(p.x - start.x), height: abs(p.y - start.y))
            .intersection(bounds)
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        guard start != nil else { return }
        start = nil
        guard selection.width >= 8, selection.height >= 8 else { needsDisplay = true; return }
        // Región desde la esquina superior izquierda de la ventana de Tibia.
        onDone(CGRect(x: selection.minX, y: bounds.height - selection.maxY, width: selection.width, height: selection.height))
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(white: 0, alpha: 0.55).setFill()
        bounds.fill()
        if selection.width > 0 {
            NSColor.clear.setFill()
            selection.fill(using: .copy)
            NSColor.systemBlue.setStroke()
            let path = NSBezierPath(rect: selection)
            path.lineWidth = 2
            path.stroke()
            let size = "\(Int(selection.width)) × \(Int(selection.height))" as NSString
            size.draw(at: NSPoint(x: selection.minX, y: selection.maxY + 4),
                      withAttributes: [.foregroundColor: NSColor.white, .font: NSFont.systemFont(ofSize: 11, weight: .semibold)])
        }
        let hint = "Arrastra para elegir la región del espejo  ·  Esc para cancelar" as NSString
        let attributes: [NSAttributedString.Key: Any] = [.foregroundColor: NSColor.white, .font: NSFont.systemFont(ofSize: 15, weight: .semibold)]
        let size = hint.size(withAttributes: attributes)
        hint.draw(at: NSPoint(x: (bounds.width - size.width) / 2, y: bounds.height - size.height - 24), withAttributes: attributes)
    }
}

// MARK: - Hub: página de espejos

struct MirrorsPage: View {
    @ObservedObject var model: HubModel
    let add: () -> Void
    let reselect: (UUID) -> Void
    let edit: (UUID) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            PageHeader(title: "Espejos", subtitle: "Copia las partes de Tibia que necesitas donde quieras") {
                Button(action: add) {
                    Label("Añadir espejo", systemImage: "plus")
                        .font(.system(size: 13, weight: .bold))
                        .padding(.horizontal, 14).padding(.vertical, 8)
                        .background(Capsule().fill(Color.accentBlue))
                        .foregroundStyle(.white)
                }
                .buttonStyle(.plain)
                .disabled(model.mirrors.count >= HubModel.maxMirrors)
            }
            if !model.screenCaptureAllowed {
                HStack(spacing: 12) {
                    Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(Color.amber)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Falta el permiso de Grabación de pantalla").font(.system(size: 13, weight: .semibold))
                        Text("macOS lo pide para que los espejos puedan copiar la ventana de Tibia. En Ajustes, activa Alfa Hub (si no aparece, pulsa + o arrastra la app desde Finder) y vuelve a abrir la app.")
                            .font(.system(size: 11)).foregroundStyle(Color.muted)
                    }
                    Spacer()
                    SmallButton(title: "Abrir Ajustes", icon: "gear") { MirrorCapture.openSettings() }
                }
                .padding(12)
                .background(RoundedRectangle(cornerRadius: 10).fill(Color.amber.opacity(0.12)))
            }
            HStack(spacing: 10) {
                Text("\(model.mirrors.count) de \(HubModel.maxMirrors) · \(model.mirrors.filter(\.visible).count) visibles")
                    .font(.system(size: 12)).foregroundStyle(Color.muted)
                Spacer()
                SmallButton(title: model.mirrorsVisible ? "Ocultar todos" : "Mostrar todos",
                            icon: model.mirrorsVisible ? "eye.slash" : "eye") { model.mirrorsVisible.toggle() }
                SmallButton(title: model.mirrorsUnlocked ? "Bloquear" : "Desbloquear para moverlos",
                            icon: model.mirrorsUnlocked ? "lock" : "lock.open") { model.mirrorsUnlocked.toggle() }
            }
            ScrollView {
                VStack(spacing: 8) {
                    if model.mirrors.isEmpty {
                        Text("Todavía no hay espejos. Con Tibia abierto, pulsa “Añadir espejo” y arrastra sobre la parte que quieras copiar (cooldowns, hechizos, barra de vida…).")
                            .font(.system(size: 12)).foregroundStyle(Color.muted)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    ForEach($model.mirrors) { $mirror in
                        MirrorRow(model: model, mirror: $mirror, edit: { edit(mirror.id) }, reselect: { reselect(mirror.id) })
                    }
                    Text("Los espejos solo se ven con Tibia al frente y dejan pasar los clics al juego. En macOS copian la ventana de Tibia con ScreenCaptureKit; no tocan el proceso del juego.")
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
}

struct MirrorRow: View {
    @ObservedObject var model: HubModel
    @Binding var mirror: MirrorConfig
    let edit: () -> Void
    let reselect: () -> Void

    var body: some View {
        HStack(spacing: 14) {
            RoundedRectangle(cornerRadius: 2).fill(mirror.visible ? Color.green : Color.white.opacity(0.15)).frame(width: 4)
            VStack(alignment: .leading, spacing: 3) {
                Text(mirror.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                Text("\(Int(mirror.width)) × \(Int(mirror.height)) pt · \(mirror.zoom) % · \(mirror.visible ? "Visible" : "Oculto")")
                    .font(.system(size: 11)).foregroundStyle(Color.muted)
            }
            .frame(minWidth: 150, alignment: .leading)
            Spacer(minLength: 8)
            Toggle("", isOn: $mirror.visible).labelsHidden().toggleStyle(.switch).help("Visible")
            Slider(value: Binding(get: { Double(mirror.opacity) }, set: { mirror.opacity = Int($0) }), in: 10...100)
                .frame(width: 100).help("Opacidad")
            Text("\(mirror.opacity) %").font(.system(size: 11).monospacedDigit()).foregroundStyle(Color.muted).frame(width: 38)
            Button(action: edit) {
                Label("Editar", systemImage: "pencil")
                    .font(.system(size: 12, weight: .semibold))
                    .padding(.horizontal, 11).padding(.vertical, 6)
                    .background(RoundedRectangle(cornerRadius: 7).fill(Color.white.opacity(0.08)))
                    .foregroundStyle(.white)
            }
            .buttonStyle(.plain)
            Menu {
                Button("Volver a elegir la región", action: reselect)
                Button("Eliminar", role: .destructive) { model.mirrors.removeAll { $0.id == mirror.id } }
            } label: {
                Image(systemName: "ellipsis").rotationEffect(.degrees(90)).frame(width: 18, height: 26)
            }
            .menuStyle(.button).menuIndicator(.hidden).buttonStyle(.plain).fixedSize()
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

struct MirrorEditor: View {
    @ObservedObject var model: HubModel
    @Binding var mirror: MirrorConfig
    let back: () -> Void
    let reselect: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Button(action: back) {
                Label("Volver a Espejos", systemImage: "chevron.left")
                    .font(.system(size: 13, weight: .medium)).foregroundStyle(Color.accentBlue)
            }
            .buttonStyle(.plain)
            .padding(.horizontal, 28).padding(.top, 18)
            PageHeader(title: mirror.name, subtitle: "Región de \(Int(mirror.width)) × \(Int(mirror.height)) pt de la ventana de Tibia")
                .padding(.horizontal, 28).padding(.top, 6)
            Form {
                Section("Espejo") {
                    LabeledContent("Nombre") {
                        TextField("", text: $mirror.name).frame(width: 180).multilineTextAlignment(.trailing)
                    }
                    Toggle("Visible", isOn: $mirror.visible)
                    LabeledContent("Tamaño") {
                        HStack {
                            Slider(value: Binding(get: { Double(mirror.zoom) }, set: { mirror.zoom = Int($0) }), in: 25...400)
                            Text("\(mirror.zoom) %").monospacedDigit().foregroundStyle(Color.muted).frame(width: 48)
                        }
                    }
                    LabeledContent("Opacidad") {
                        HStack {
                            Slider(value: Binding(get: { Double(mirror.opacity) }, set: { mirror.opacity = Int($0) }), in: 10...100)
                            Text("\(mirror.opacity) %").monospacedDigit().foregroundStyle(Color.muted).frame(width: 48)
                        }
                    }
                    Toggle("Borde", isOn: $mirror.border)
                    Button("Volver a elegir la región", action: reselect)
                }
                Section {
                    Button("Eliminar este espejo", role: .destructive) {
                        let id = mirror.id
                        back()
                        model.mirrors.removeAll { $0.id == id }
                    }
                } footer: {
                    Text("Para moverlo, usa “Desbloquear para moverlos” en la lista de espejos y arrástralo.")
                        .foregroundStyle(.secondary)
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
            .padding(.horizontal, 8)
            .onAppear { DispatchQueue.main.async { NSApp.keyWindow?.makeFirstResponder(nil) } }
        }
    }
}

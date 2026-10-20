// Sonidos incluidos: se sintetizan al vuelo (no hay archivos de audio). Windows usa la misma receta
// (windows/Sounds.cs), así que suenan igual en las dos versiones.

import Foundation

/// Una nota: frecuencia (o barrido de frecuencia), duración, caída y armónicos.
struct Tone {
    var start: Double
    var duration: Double
    var freq: Double
    var endFreq: Double? = nil
    /// Caída exponencial por segundo (0 = nota sostenida).
    var decay: Double = 0
    /// (múltiplo de la frecuencia, ganancia)
    var partials: [(Double, Double)] = [(1, 1)]
    var square = false
}

enum BuiltInSound: String, CaseIterable, Identifiable {
    case beep, doble, triple, ding, campana, carillon, jingle, moneda, alarma, sirena, burbuja, descenso, gong, tic

    var id: String { rawValue }

    var title: String {
        switch self {
        case .beep: "Beep"
        case .doble: "Doble beep"
        case .triple: "Triple beep"
        case .ding: "Ding"
        case .campana: "Campana"
        case .carillon: "Carillón"
        case .jingle: "Jingle"
        case .moneda: "Moneda"
        case .alarma: "Alarma"
        case .sirena: "Sirena"
        case .burbuja: "Burbuja"
        case .descenso: "Descenso"
        case .gong: "Gong"
        case .tic: "Tic"
        }
    }

    static func named(_ id: String?) -> BuiltInSound { id.flatMap(BuiltInSound.init(rawValue:)) ?? .beep }

    var tones: [Tone] {
        let bell: [(Double, Double)] = [(1, 1), (2, 0.3), (3, 0.1)]
        switch self {
        case .beep:
            return [Tone(start: 0, duration: 0.25, freq: 880, decay: 18.42)]
        case .doble:
            return [0, 0.17].map { Tone(start: $0, duration: 0.11, freq: 1000, decay: 6) }
        case .triple:
            return [0, 0.14, 0.28].map { Tone(start: $0, duration: 0.08, freq: 1250, decay: 8) }
        case .ding:
            return [Tone(start: 0, duration: 0.9, freq: 1318.5, decay: 5, partials: [(1, 1), (2, 0.3), (3, 0.08)])]
        case .campana:
            return [Tone(start: 0, duration: 1.6, freq: 784, decay: 2.6, partials: [(1, 1), (2, 0.55), (2.76, 0.35), (5.4, 0.15)])]
        case .carillon:
            return [1568, 1318.5, 1046.5].enumerated().map {
                Tone(start: Double($0.offset) * 0.2, duration: 1.0, freq: $0.element, decay: 4.5, partials: bell)
            }
        case .jingle:
            return [1046.5, 1318.5, 1568, 2093].enumerated().map {
                let last = $0.offset == 3
                return Tone(start: Double($0.offset) * 0.1, duration: last ? 0.7 : 0.3, freq: $0.element,
                            decay: last ? 4 : 8, partials: [(1, 1), (2, 0.2)])
            }
        case .moneda:
            return [Tone(start: 0, duration: 0.08, freq: 987.8, square: true),
                    Tone(start: 0.08, duration: 0.45, freq: 1318.5, decay: 6, square: true)]
        case .alarma:
            return (0..<4).flatMap { i in
                [Tone(start: Double(i) * 0.3, duration: 0.14, freq: 880, decay: 2, square: true),
                 Tone(start: Double(i) * 0.3 + 0.15, duration: 0.14, freq: 660, decay: 2, square: true)]
            }
        case .sirena:
            return (0..<4).map { i in
                Tone(start: Double(i) * 0.45, duration: 0.45, freq: i % 2 == 0 ? 600 : 1200, endFreq: i % 2 == 0 ? 1200 : 600)
            }
        case .burbuja:
            return [Tone(start: 0, duration: 0.16, freq: 400, endFreq: 1400, decay: 6)]
        case .descenso:
            return [Tone(start: 0, duration: 0.4, freq: 1400, endFreq: 350, decay: 3)]
        case .gong:
            return [Tone(start: 0, duration: 2.4, freq: 140, decay: 1.6,
                         partials: [(1, 1), (1.47, 0.7), (2.09, 0.5), (2.56, 0.35), (3.39, 0.25), (4.1, 0.15)])]
        case .tic:
            return [Tone(start: 0, duration: 0.05, freq: 2200, decay: 50, partials: [(1, 1), (2.7, 0.4)])]
        }
    }

    /// Las ondas cuadradas suenan más fuertes: se normalizan más bajo.
    private var peak: Double { tones.contains(where: \.square) ? 0.55 : 0.9 }

    /// WAV mono de 16 bits a 44,1 kHz.
    var wav: Data { Synth.wav(Synth.render(tones, peak: peak)) }
}

enum Synth {
    static let rate = 44_100.0

    static func render(_ tones: [Tone], peak: Double) -> [Double] {
        let length = (tones.map { $0.start + $0.duration }.max() ?? 0) + 0.02
        var buffer = [Double](repeating: 0, count: Int(length * rate))
        for tone in tones {
            let first = Int(tone.start * rate)
            let count = Int(tone.duration * rate)
            let f1 = tone.endFreq ?? tone.freq
            for i in 0..<count where first + i < buffer.count {
                let t = Double(i) / rate
                // Fase integrada: el barrido de frecuencia es continuo.
                let phase = 2 * .pi * (tone.freq * t + (f1 - tone.freq) * t * t / (2 * tone.duration))
                let envelope = min(1, t / 0.004) * exp(-tone.decay * t) * min(1, (tone.duration - t) / 0.01)
                var value = 0.0
                for (multiple, gain) in tone.partials {
                    let x = phase * multiple
                    value += gain * (tone.square ? (sin(x) + sin(3 * x) / 3 + sin(5 * x) / 5 + sin(7 * x) / 7) : sin(x))
                }
                buffer[first + i] += value * envelope
            }
        }
        let maxValue = buffer.map(abs).max() ?? 0
        if maxValue > 0 { buffer = buffer.map { $0 / maxValue * peak } }
        return buffer
    }

    static func wav(_ samples: [Double]) -> Data {
        let pcm = samples.map { Int16(max(-1, min(1, $0)) * 32_000) }
        var d = Data()
        func u32(_ v: UInt32) { withUnsafeBytes(of: v.littleEndian) { d.append(contentsOf: $0) } }
        func u16(_ v: UInt16) { withUnsafeBytes(of: v.littleEndian) { d.append(contentsOf: $0) } }
        d.append(contentsOf: Array("RIFF".utf8)); u32(UInt32(36 + pcm.count * 2))
        d.append(contentsOf: Array("WAVEfmt ".utf8)); u32(16); u16(1); u16(1); u32(44_100); u32(88_200); u16(2); u16(16)
        d.append(contentsOf: Array("data".utf8)); u32(UInt32(pcm.count * 2))
        pcm.withUnsafeBytes { d.append(contentsOf: $0) }
        return d
    }
}

// Genera los íconos de Alfa Hub a partir del sprite del personaje (outfit64.png, sacado del
// Outfiter de TibiaWiki: o=235, addon 1, colores 95/95/95/114).
//
//   swift assets/make_icons.swift
//
// Salidas:
//   assets/character.png          sprite recortado a su contorno (para mostrarlo en el hub)
//   macos/AppIcon.icns            ícono de la app de macOS
//   windows/AlfaHub.ico           ícono del .exe de Windows
//   windows/character.png         copia del sprite para incrustarlo en el .exe

import AppKit

let root = URL(fileURLWithPath: CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent()
let assets = root.appendingPathComponent("assets")

// MARK: - Sprite recortado

let source = NSBitmapImageRep(data: try! Data(contentsOf: assets.appendingPathComponent("outfit64.png")))!
var minX = source.pixelsWide, minY = source.pixelsHigh, maxX = -1, maxY = -1
for y in 0..<source.pixelsHigh {
    for x in 0..<source.pixelsWide where (source.colorAt(x: x, y: y)?.alphaComponent ?? 0) > 0.02 {
        minX = min(minX, x); maxX = max(maxX, x); minY = min(minY, y); maxY = max(maxY, y)
    }
}
let cropRect = CGRect(x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1)
let character = source.cgImage!.cropping(to: cropRect)!
let spriteW = character.width, spriteH = character.height
print("sprite recortado: \(spriteW)×\(spriteH)")
let cropped = NSBitmapImageRep(cgImage: character)
try! cropped.representation(using: .png, properties: [:])!.write(to: assets.appendingPathComponent("character.png"))
try! cropped.representation(using: .png, properties: [:])!.write(to: root.appendingPathComponent("windows/character.png"))

// MARK: - Ícono

/// Solo el personaje sobre fondo transparente, como el ícono de Tibia: macOS le pone su placa gris
/// automáticamente (la misma que a Tibia) y en Windows se ve sin fondo, igual que el de Tibia.
func icon(size: Int) -> NSBitmapImageRep {
    let s = CGFloat(size)
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    let ctx = NSGraphicsContext(bitmapImageRep: rep)!.cgContext
    // Lo más grande posible y centrado; escala entera cuando se puede (pixel-art nítido).
    let maxSide = s * 0.9
    var scale = min(maxSide / CGFloat(spriteW), maxSide / CGFloat(spriteH))
    if scale >= 1 { scale = floor(scale) }
    let w = CGFloat(spriteW) * scale, h = CGFloat(spriteH) * scale
    let rect = CGRect(x: ((s - w) / 2).rounded(), y: ((s - h) / 2).rounded(), width: w, height: h)
    ctx.interpolationQuality = scale >= 1 ? .none : .high
    ctx.draw(character, in: rect)
    ctx.flush()
    return rep
}

func png(_ rep: NSBitmapImageRep) -> Data { rep.representation(using: .png, properties: [:])! }

// macOS: .iconset → .icns
let iconset = FileManager.default.temporaryDirectory.appendingPathComponent("AlfaHub.iconset")
try? FileManager.default.removeItem(at: iconset)
try! FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
for base in [16, 32, 128, 256, 512] {
    try! png(icon(size: base)).write(to: iconset.appendingPathComponent("icon_\(base)x\(base).png"))
    try! png(icon(size: base * 2)).write(to: iconset.appendingPathComponent("icon_\(base)x\(base)@2x.png"))
}
let iconutil = Process()
iconutil.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
iconutil.arguments = ["-c", "icns", iconset.path, "-o", root.appendingPathComponent("macos/AppIcon.icns").path]
try! iconutil.run(); iconutil.waitUntilExit()
try! png(icon(size: 1024)).write(to: assets.appendingPathComponent("icon-macos-1024.png"))

// Windows: .ico con PNG dentro (Windows Vista en adelante).
let icoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
let images = icoSizes.map { png(icon(size: $0)) }
var ico = Data()
func le16(_ v: Int) { withUnsafeBytes(of: UInt16(v).littleEndian) { ico.append(contentsOf: $0) } }
func le32(_ v: Int) { withUnsafeBytes(of: UInt32(v).littleEndian) { ico.append(contentsOf: $0) } }
le16(0); le16(1); le16(icoSizes.count)
var offset = 6 + 16 * icoSizes.count
for (size, data) in zip(icoSizes, images) {
    ico.append(UInt8(size >= 256 ? 0 : size)); ico.append(UInt8(size >= 256 ? 0 : size))
    ico.append(0); ico.append(0); le16(1); le16(32); le32(data.count); le32(offset)
    offset += data.count
}
images.forEach { ico.append($0) }
try! ico.write(to: root.appendingPathComponent("windows/AlfaHub.ico"))
try! png(icon(size: 256)).write(to: assets.appendingPathComponent("icon-windows-256.png"))
print("íconos listos")

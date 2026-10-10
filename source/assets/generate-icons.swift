import AppKit
import Foundation

let output = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
func icon(_ size: Int) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
        samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let context = NSGraphicsContext.current!.cgContext
    context.scaleBy(x: CGFloat(size) / 1024, y: CGFloat(size) / 1024)
    let background = NSBezierPath(roundedRect: NSRect(x: 32, y: 32, width: 960, height: 960), xRadius: 215, yRadius: 215)
    let gradient = NSGradient(colors: [NSColor(calibratedRed: 0.16, green: 0.13, blue: 0.40, alpha: 1), NSColor(calibratedRed: 0.16, green: 0.28, blue: 0.66, alpha: 1), NSColor(calibratedRed: 0.07, green: 0.55, blue: 0.64, alpha: 1)])!
    gradient.draw(in: background, angle: 35)
    NSColor.white.withAlphaComponent(0.13).setStroke(); background.lineWidth = 3; background.stroke()
    func monitor(_ rect: NSRect, _ color: NSColor) {
        let path = NSBezierPath(roundedRect: rect, xRadius: 30, yRadius: 30)
        NSColor.white.withAlphaComponent(0.07).setFill(); path.fill()
        color.setStroke(); path.lineWidth = size <= 32 ? 60 : 24; path.stroke()
        let stand = NSBezierPath(); stand.move(to: NSPoint(x: rect.midX, y: rect.minY)); stand.line(to: NSPoint(x: rect.midX, y: rect.minY - 55))
        stand.move(to: NSPoint(x: rect.midX - 64, y: rect.minY - 55)); stand.line(to: NSPoint(x: rect.midX + 64, y: rect.minY - 55)); stand.lineWidth = size <= 32 ? 60 : 24; stand.lineCapStyle = .round; stand.stroke()
    }
    monitor(NSRect(x: 144, y: 370, width: 300, height: 285), NSColor.white.withAlphaComponent(0.96))
    monitor(NSRect(x: 580, y: 370, width: 300, height: 285), NSColor(calibratedRed: 0.36, green: 0.98, blue: 0.88, alpha: 1))
    let arrow = NSBezierPath(); arrow.lineWidth = size <= 32 ? 48 : 25; arrow.lineCapStyle = .round; arrow.lineJoinStyle = .round
    NSColor.white.setStroke(); arrow.move(to: NSPoint(x: 412, y: 744)); arrow.line(to: NSPoint(x: 612, y: 744))
    arrow.move(to: NSPoint(x: 570, y: 786)); arrow.line(to: NSPoint(x: 612, y: 744)); arrow.line(to: NSPoint(x: 570, y: 702)); arrow.stroke()
    let back = NSBezierPath(); back.lineWidth = size <= 32 ? 48 : 25; back.lineCapStyle = .round; back.lineJoinStyle = .round
    NSColor(calibratedRed: 0.36, green: 0.98, blue: 0.88, alpha: 1).setStroke()
    back.move(to: NSPoint(x: 612, y: 228)); back.line(to: NSPoint(x: 412, y: 228))
    back.move(to: NSPoint(x: 454, y: 270)); back.line(to: NSPoint(x: 412, y: 228)); back.line(to: NSPoint(x: 454, y: 186)); back.stroke()
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}
for size in [16, 24, 32, 48, 64, 128, 256, 512, 1024] {
    try icon(size).write(to: output.appendingPathComponent("icon-\(size).png"))
}

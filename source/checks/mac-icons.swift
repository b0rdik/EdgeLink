// Native AppKit resource audit. Does not launch EdgeLink, read pairing or use network/audio.
// swift source/checks/mac-icons.swift Mac/EdgeLink.app
import AppKit
import CryptoKit

var checks: [[String: Any]] = []
func check(_ ok: Bool, _ name: String) {
    checks.append(["name": name, "passed": ok])
    print("\(ok ? "PASS" : "FAIL"): \(name)")
}
guard CommandLine.arguments.count == 2,
      let bundle = Bundle(path: CommandLine.arguments[1]), let resources = bundle.resourceURL else {
    print("Usage: swift source/checks/mac-icons.swift Mac/EdgeLink.app"); exit(2)
}
let appIcon = resources.appendingPathComponent("EdgeLink.icns")
check(bundle.object(forInfoDictionaryKey: "CFBundleIconFile") as? String == "EdgeLink.icns", "Info.plist points to the bundled ICNS")
if let icon = NSImage(contentsOf: appIcon) {
    let sizes = Set(icon.representations.map { $0.pixelsWide })
    check(sizes.isSuperset(of: [16, 32, 64, 128, 256, 512, 1024]), "AppKit decodes the complete ICNS size set")
} else { check(false, "AppKit decodes ICNS") }
for (file, width, height) in [("AppIcon.png", 128, 128), ("EdgeLinkStatus.png", 23, 18), ("EdgeLinkStatus@2x.png", 46, 36)] {
    guard let data = try? Data(contentsOf: resources.appendingPathComponent(file)),
          let bitmap = NSBitmapImageRep(data: data) else { check(false, "Decode \(file)"); continue }
    check(bitmap.pixelsWide == width && bitmap.pixelsHigh == height && bitmap.hasAlpha, "\(file): native dimensions and alpha")
    check(bitmap.colorAt(x: 0, y: 0)?.alphaComponent == 0, "\(file): transparent outer corner")
    if file.hasPrefix("EdgeLinkStatus") {
        var hasInk = false, monochrome = true
        for y in 0..<height { for x in 0..<width {
            if let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB), color.alphaComponent > 0 {
                hasInk = true
                monochrome = monochrome && color.redComponent < 0.01 && color.greenComponent < 0.01 && color.blueComponent < 0.01
            }
        } }
        check(hasInk && monochrome, "\(file): nonempty black template mask")
    }
}
let finderIcon = NSWorkspace.shared.icon(forFile: bundle.bundlePath)
check(!finderIcon.representations.isEmpty, "NSWorkspace resolves the application's Finder/Dock icon")
let report: [String: Any] = ["passed": checks.allSatisfy { $0["passed"] as? Bool == true }, "checks": checks,
    "version": bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") ?? "",
    "icnsSHA256": (try? Data(contentsOf: appIcon)).map { SHA256.hash(data: $0).map { String(format: "%02x", $0) }.joined() } ?? ""]
if let data = try? JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys]) {
    print(String(decoding: data, as: UTF8.self))
}
exit(report["passed"] as? Bool == true ? 0 : 1)

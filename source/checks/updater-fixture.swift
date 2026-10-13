// Headless fixture for check-installers.py; compile this as main.swift together
// with mac/Wire.swift and mac/Updates.swift. Never launch a production app.
import Foundation
if CommandLine.arguments.count == 4 && CommandLine.arguments[1] == "--replace-bundle" {
    try Updates.replaceBundle(URL(fileURLWithPath: CommandLine.arguments[2]), target: URL(fileURLWithPath: CommandLine.arguments[3]))
    exit(0)
}
let executable = URL(fileURLWithPath: CommandLine.arguments[0])
let isWindowsFixture = executable.pathExtension == "exe"
let mode: String
if isWindowsFixture {
    let bytes = (try? Data(contentsOf: executable)) ?? Data()
    mode = String(data: bytes.suffix(4), encoding: .utf8) ?? "OLD!"
} else {
    mode = (try? String(contentsOf: executable.deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Resources/mode.txt"), encoding: .utf8)) ?? "OLD!"
}
if mode == "NEW!", let index = CommandLine.arguments.firstIndex(of: "--update-ready"), CommandLine.arguments.count > index + 2 {
    if isWindowsFixture { try Data(CommandLine.arguments[index + 2].utf8).write(to: URL(fileURLWithPath: CommandLine.arguments[index + 1])) }
    else { Updates.reportReady() }
}
if mode == "HANG" { Thread.sleep(forTimeInterval: 60) }

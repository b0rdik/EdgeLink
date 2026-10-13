import Foundation
var failed = false
func check(_ condition: Bool, _ name: String) {
    print("\(condition ? "PASS" : "FAIL"): \(name)"); failed = failed || !condition
}
let attempt = ConnectionAttempt()
let first = attempt.begin(); check(attempt.isCurrent(first), "fresh async start may publish listeners")
attempt.cancel(); check(!attempt.isCurrent(first), "Stop or Ethernet change cancels a pending capture start")
let second = attempt.begin(); attempt.setSleeping(true)
check(!attempt.isCurrent(second) && attempt.suspended, "sleep cancels a pending start")
attempt.setInactive(true); attempt.setSleeping(false)
check(attempt.suspended, "wake cannot restart while the user session is still inactive")
attempt.setInactive(false)
check(!attempt.suspended && !attempt.isCurrent(second), "unlock permits a new attempt without reviving the stale one")
let third = attempt.begin(); check(attempt.isCurrent(third), "a fresh attempt works after wake and unlock")
let replacement = attempt.begin(); check(!attempt.isCurrent(third) && attempt.isCurrent(replacement), "replacement invalidates the previous attempt")
let folder = FileManager.default.temporaryDirectory.appendingPathComponent("EdgeLink-update-" + UUID().uuidString)
try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: folder) }
let nonce = UUID().uuidString, ready = folder.appendingPathComponent("ready-" + nonce + ".txt")
Updates.reportReady(["fixture", "--update-ready", ready.path, nonce])
check((try? String(contentsOf: ready, encoding: .utf8)) == nonce, "Mac update receipt confirms initialized application")
let unrelated = folder.appendingPathComponent("unrelated.txt")
Updates.reportReady(["fixture", "--update-ready", unrelated.path, nonce])
check(!FileManager.default.fileExists(atPath: unrelated.path), "Mac update receipt refuses an unrelated path")
let old = folder.appendingPathComponent("EdgeLink.previous-" + UUID().uuidString + ".app")
let foreign = folder.appendingPathComponent("EdgeLink.previous-" + UUID().uuidString + ".app")
let ordinary = folder.appendingPathComponent("EdgeLink.previous-personal.app")
for (url, identifier) in [(old, "local.edgelink.mac"), (foreign, "test.unrelated"), (ordinary, "local.edgelink.mac")] {
    try FileManager.default.createDirectory(at: url.appendingPathComponent("Contents"), withIntermediateDirectories: true)
    let data = try PropertyListSerialization.data(fromPropertyList: ["CFBundleIdentifier": identifier, "CFBundlePackageType": "APPL"], format: .xml, options: 0)
    try data.write(to: url.appendingPathComponent("Contents/Info.plist"))
}
check(Updates.removeLegacyBackups(target: folder.appendingPathComponent("EdgeLink.app")) == 1 && !FileManager.default.fileExists(atPath: old.path), "legacy updater's UUID backup is removed after startup")
check(FileManager.default.fileExists(atPath: foreign.path) && FileManager.default.fileExists(atPath: ordinary.path), "legacy cleanup preserves other apps and user-named backups")
if let export = ProcessInfo.processInfo.environment["INSTALLER_SCRIPT_EXPORT"] {
    try Updates.installerScript.write(toFile: export, atomically: true, encoding: .utf8)
}
exit(failed ? 1 : 0)

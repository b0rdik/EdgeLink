// Live OTA check: compile with Wire.swift + Updates.swift as main.swift. Never installs.
import Foundation
import AppKit
let expected = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "0.3.1"
Task {
    do {
        let client = Updates.session(); defer { client.finishTasksAndInvalidate() }
        let (data, response) = try await client.data(from: URL(string: "https://api.github.com/repos/b0rdik/EdgeLink/releases/latest")!)
        guard (response as? HTTPURLResponse)?.statusCode == 200, let release = try Updates.parse(data, current: "0.0.0"), release.version.description == expected else { throw LinkError.message("Unexpected published release") }
        print("PASS: GitHub live Mac release metadata / version / digest")
        let app = try await Updates.stage(release)
        try FileManager.default.removeItem(at: app.deletingLastPathComponent().deletingLastPathComponent())
        print("PASS: Mac OTA downloads / verifies digest, arm64, bundle version and code signature")
        exit(0)
    } catch { print("FAIL: \(error)"); exit(1) }
}
RunLoop.main.run()

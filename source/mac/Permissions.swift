import Foundation
import ApplicationServices
import ScreenCaptureKit

enum Permissions {
    static var inputAllowed: Bool { AXIsProcessTrusted() || CGPreflightPostEventAccess() }
    static var audioAllowed: Bool { CGPreflightScreenCaptureAccess() }
    static func verifyAudio() async throws {
        // This is an explicit user action: ScreenCaptureKit may display its permission prompt.
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard !content.displays.isEmpty else { throw LinkError.message("macOS не предоставила список экранов") }
    }
    static func resetOwnGrants() throws {
        // Invoked only after the user confirms the in-app repair dialog; never on launch/update.
        for service in ["Accessibility", "ScreenCapture"] {
            _ = try Updates.run("/usr/bin/tccutil", ["reset", service, "local.edgelink.mac"])
        }
    }
}

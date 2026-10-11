import Foundation
import IOKit.pwr_mgt

// One process-owned assertion prevents idle display sleep and, consequently,
// idle system sleep. It does not override explicit sleep or lid-close policy.
final class KeepAwake {
    private(set) var assertionID: IOPMAssertionID?
    private let name: String
    var isActive: Bool { assertionID != nil }

    init(name: String = "EdgeLink Keep Awake") { self.name = name }

    @discardableResult
    func setEnabled(_ enabled: Bool) -> IOReturn {
        if enabled {
            guard assertionID == nil else { return kIOReturnSuccess }
            var created: IOPMAssertionID = 0
            let result = IOPMAssertionCreateWithName(kIOPMAssertionTypePreventUserIdleDisplaySleep as CFString,
                IOPMAssertionLevel(kIOPMAssertionLevelOn), name as CFString, &created)
            if result == kIOReturnSuccess { assertionID = created }
            return result
        }
        guard let current = assertionID else { return kIOReturnSuccess }
        let result = IOPMAssertionRelease(current)
        if result == kIOReturnSuccess { assertionID = nil }
        return result
    }

    deinit { if let assertionID { IOPMAssertionRelease(assertionID) } }
}

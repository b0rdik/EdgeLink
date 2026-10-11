// Native IOKit lifecycle check; no UI, network, credentials or synthetic input.
// Compile with source/mac/KeepAwake.swift, naming this entry file main.swift.
import Foundation
import IOKit.pwr_mgt

var failed = false
func check(_ value: Bool, _ name: String) {
    print("\(value ? "PASS" : "FAIL"): \(name)")
    failed = failed || !value
}
func properties(_ id: IOPMAssertionID) -> [String: Any]? {
    IOPMAssertionCopyProperties(id)?.takeRetainedValue() as? [String: Any]
}
let awake = KeepAwake(name: "EdgeLink Keep Awake lifecycle checks")
check(!awake.isActive && awake.setEnabled(false) == kIOReturnSuccess, "Disabled start has no assertion")
check(awake.setEnabled(true) == kIOReturnSuccess && awake.isActive, "Enable creates native assertion")
if let first = awake.assertionID {
    let props = properties(first)
    check(props?[kIOPMAssertionTypeKey] as? String == kIOPMAssertionTypePreventUserIdleDisplaySleep,
          "Native assertion prevents idle display sleep")
    check(((props?[kIOPMAssertionLevelKey] as? NSNumber)?.intValue ?? -1) == Int(kIOPMAssertionLevelOn),
          "Native assertion is enabled")
    check(awake.setEnabled(true) == kIOReturnSuccess && awake.assertionID == first,
          "Repeated enable reuses one assertion")
    check(awake.setEnabled(false) == kIOReturnSuccess && !awake.isActive && properties(first) == nil,
          "Disable releases native assertion")
    check(awake.setEnabled(false) == kIOReturnSuccess && !awake.isActive, "Repeated disable is safe")
    check(awake.setEnabled(true) == kIOReturnSuccess && awake.isActive, "Re-enable creates an active assertion")
    awake.setEnabled(false)
} else { check(false, "Native assertion ID is available") }
var scoped: KeepAwake? = KeepAwake(name: "EdgeLink Keep Awake deinit check")
check(scoped!.setEnabled(true) == kIOReturnSuccess, "Scoped assertion created")
let scopedID = scoped?.assertionID
scoped = nil
check(scopedID.map { properties($0) == nil } ?? false, "Deinit releases native assertion")
exit(failed ? 1 : 0)

// Main-thread lifecycle token. A pending async capture start must not publish
// listeners after Stop, sleep/lock, or an Ethernet-address change.
final class ConnectionAttempt {
    private var generation = 0
    private var sleeping = false, inactive = false
    var suspended: Bool { sleeping || inactive }
    func begin() -> Int { cancel(); return generation }
    func cancel() { generation &+= 1 }
    func isCurrent(_ token: Int) -> Bool { token == generation && !suspended }
    func setSleeping(_ value: Bool) { sleeping = value; if value { cancel() } }
    func setInactive(_ value: Bool) { inactive = value; if value { cancel() } }
}

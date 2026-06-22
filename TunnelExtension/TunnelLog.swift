import Foundation

enum TunnelLog {
    static func debug(_ message: @autoclosure () -> String) {
        #if DEBUG
        NSLog("%@", message())
        #endif
    }
}

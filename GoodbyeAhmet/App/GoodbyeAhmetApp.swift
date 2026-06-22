import SwiftUI

@main
struct GoodbyeAhmetApp: App {
    @StateObject private var settings = AppSettings()
    @StateObject private var vpnManager = VPNManager()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(settings)
                .environmentObject(vpnManager)
                .task {
                    await vpnManager.prepare()
                    if settings.activateOnStart,
                       settings.hasAcceptedVPNDisclosure,
                       vpnManager.status == .disconnected {
                        await vpnManager.connect(using: settings.configuration)
                    }
                }
        }
    }
}

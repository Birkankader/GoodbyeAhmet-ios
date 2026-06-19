import Combine
import Foundation
import NetworkExtension

@MainActor
final class VPNManager: ObservableObject {
    enum DisplayStatus: Equatable {
        case unavailable
        case disconnected
        case connecting
        case connected
        case disconnecting

        var title: String {
            switch self {
            case .unavailable: return "Hazir degil"
            case .disconnected: return "Baglanti kesik"
            case .connecting: return "Baglaniyor"
            case .connected: return "Bagli"
            case .disconnecting: return "Kapatiliyor"
            }
        }

        var isConnected: Bool { self == .connected }
        var isBusy: Bool { self == .connecting || self == .disconnecting }
    }

    @Published private(set) var status: DisplayStatus = .unavailable
    @Published private(set) var lastError: String?

    private let extensionBundleIdentifier = "com.birkankader.dpi.extension"
    private var manager: NETunnelProviderManager?
    private var statusObserver: NSObjectProtocol?

    init() {
        statusObserver = NotificationCenter.default.addObserver(
            forName: .NEVPNStatusDidChange,
            object: nil,
            queue: .main
        ) { [weak self] notification in
            guard let connection = notification.object as? NEVPNConnection else { return }
            Task { @MainActor [weak self] in
                self?.updateStatus(connection.status)
            }
        }
    }

    deinit {
        if let statusObserver {
            NotificationCenter.default.removeObserver(statusObserver)
        }
    }

    func prepare() async {
        do {
            manager = try await findOrCreateManager()
            if let manager {
                try await load(manager)
                updateStatus(manager.connection.status)
            }
        } catch {
            lastError = error.localizedDescription
            status = .unavailable
        }
    }

    func toggle(using configuration: VPNConfiguration) async {
        if status == .connected || status == .connecting {
            disconnect()
        } else {
            await connect(using: configuration)
        }
    }

    func connect(using configuration: VPNConfiguration) async {
        lastError = nil
        status = .connecting

        do {
            let manager = try await findOrCreateManager()
            try await load(manager)

            let tunnelProtocol = (manager.protocolConfiguration as? NETunnelProviderProtocol)
                ?? NETunnelProviderProtocol()
            tunnelProtocol.providerBundleIdentifier = extensionBundleIdentifier
            tunnelProtocol.serverAddress = "GoodbyeAhmet Local Provider"
            tunnelProtocol.disconnectOnSleep = false
            tunnelProtocol.providerConfiguration = configuration.providerDictionary

            manager.protocolConfiguration = tunnelProtocol
            manager.localizedDescription = "GoodbyeAhmet VPN"
            manager.isEnabled = true

            try await save(manager)
            try await load(manager)
            self.manager = manager

            guard let session = manager.connection as? NETunnelProviderSession else {
                throw VPNError.invalidSession
            }

            try session.startTunnel(options: ["PresetKey": configuration.presetKey as NSString])
            updateStatus(session.status)
        } catch {
            lastError = permissionMessage(for: error)
            status = .disconnected
        }
    }

    func disconnect() {
        lastError = nil
        status = .disconnecting
        manager?.connection.stopVPNTunnel()
    }

    private func findOrCreateManager() async throws -> NETunnelProviderManager {
        let managers = try await loadAllManagers()
        if let existing = managers.first(where: {
            ($0.protocolConfiguration as? NETunnelProviderProtocol)?.providerBundleIdentifier == extensionBundleIdentifier
        }) {
            return existing
        }
        return NETunnelProviderManager()
    }

    private func updateStatus(_ nativeStatus: NEVPNStatus) {
        switch nativeStatus {
        case .connected: status = .connected
        case .connecting, .reasserting: status = .connecting
        case .disconnecting: status = .disconnecting
        case .disconnected: status = .disconnected
        case .invalid: status = .unavailable
        @unknown default: status = .unavailable
        }
    }

    private func loadAllManagers() async throws -> [NETunnelProviderManager] {
        try await withCheckedThrowingContinuation {
            (continuation: CheckedContinuation<[NETunnelProviderManager], Error>) in
            NETunnelProviderManager.loadAllFromPreferences { managers, error in
                if let error {
                    continuation.resume(throwing: error)
                } else {
                    continuation.resume(returning: managers ?? [])
                }
            }
        }
    }

    private func load(_ manager: NETunnelProviderManager) async throws {
        try await withCheckedThrowingContinuation {
            (continuation: CheckedContinuation<Void, Error>) in
            manager.loadFromPreferences { error in
                if let error {
                    continuation.resume(throwing: error)
                } else {
                    continuation.resume()
                }
            }
        }
    }

    private func save(_ manager: NETunnelProviderManager) async throws {
        try await withCheckedThrowingContinuation {
            (continuation: CheckedContinuation<Void, Error>) in
            manager.saveToPreferences { error in
                if let error {
                    continuation.resume(throwing: error)
                } else {
                    continuation.resume()
                }
            }
        }
    }

    private func permissionMessage(for error: Error) -> String {
        let nsError = error as NSError
        if nsError.domain == NEVPNErrorDomain {
            return "VPN izni verilmedi. Ayarlar > Genel > VPN ve Aygit Yonetimi bolumunu kontrol et."
        }
        return error.localizedDescription
    }
}

private enum VPNError: LocalizedError {
    case invalidSession

    var errorDescription: String? {
        "Packet Tunnel oturumu olusturulamadi."
    }
}

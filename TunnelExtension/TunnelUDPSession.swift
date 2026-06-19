import Foundation
import Network

final class TunnelUDPSession {
    private weak var provider: PacketTunnelProvider?
    private let clientAddress: IPv4Address
    private let clientPort: UInt16
    private let destinationAddress: IPv4Address
    private let destinationPort: UInt16
    let key: String

    private let queue: DispatchQueue
    private let connection: NWConnection
    private var isReady = false
    private var isClosed = false
    private var hasStarted = false
    private var pending: [Data] = []
    private(set) var lastActivity = Date()

    init(
        provider: PacketTunnelProvider,
        key: String,
        clientAddress: IPv4Address,
        clientPort: UInt16,
        destinationAddress: IPv4Address,
        destinationPort: UInt16,
        remoteAddress: IPv4Address,
        remotePort: UInt16
    ) {
        self.provider = provider
        self.key = key
        self.clientAddress = clientAddress
        self.clientPort = clientPort
        self.destinationAddress = destinationAddress
        self.destinationPort = destinationPort
        queue = DispatchQueue(label: "com.birkankader.dpi.udp.\(clientPort)")
        connection = NWConnection(
            host: NWEndpoint.Host(remoteAddress.description),
            port: NWEndpoint.Port(rawValue: remotePort)!,
            using: .udp
        )
    }

    func startIfNeeded() {
        guard !hasStarted else { return }
        hasStarted = true
        connection.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready:
                self.isReady = true
                let queued = self.pending
                self.pending.removeAll(keepingCapacity: false)
                queued.forEach { self.sendOnQueue($0) }
                self.receiveNext()
            case .failed(let error):
                NSLog("UDP session failed: %@", String(describing: error))
                self.closeOnQueue()
            case .cancelled:
                self.provider?.removeUDPSession(key: self.key, session: self)
            default:
                break
            }
        }
        connection.start(queue: queue)
    }

    func send(_ payload: Data) {
        queue.async { [weak self] in
            guard let self, !self.isClosed else { return }
            self.lastActivity = Date()
            if self.isReady {
                self.sendOnQueue(payload)
            } else if self.pending.count < 32 {
                self.pending.append(payload)
            }
        }
    }

    func close() {
        queue.async { [weak self] in self?.closeOnQueue() }
    }

    func closeIfIdle(before cutoff: Date) {
        queue.async { [weak self] in
            guard let self, self.lastActivity < cutoff else { return }
            self.closeOnQueue()
        }
    }

    private func sendOnQueue(_ payload: Data) {
        connection.send(content: payload, completion: .contentProcessed { [weak self] error in
            guard let self, let error else { return }
            NSLog("UDP write failed: %@", String(describing: error))
            self.closeOnQueue()
        })
    }

    private func receiveNext() {
        guard !isClosed else { return }
        connection.receiveMessage { [weak self] data, _, _, error in
            guard let self, !self.isClosed else { return }
            if let error {
                NSLog("UDP read failed: %@", String(describing: error))
                self.closeOnQueue()
                return
            }
            if let data, !data.isEmpty {
                self.lastActivity = Date()
                self.provider?.writeUDPPacket(
                    source: self.destinationAddress,
                    sourcePort: self.destinationPort,
                    destination: self.clientAddress,
                    destinationPort: self.clientPort,
                    payload: data
                )
            }
            self.receiveNext()
        }
    }

    private func closeOnQueue() {
        guard !isClosed else { return }
        isClosed = true
        connection.stateUpdateHandler = nil
        connection.cancel()
        provider?.removeUDPSession(key: key, session: self)
    }
}

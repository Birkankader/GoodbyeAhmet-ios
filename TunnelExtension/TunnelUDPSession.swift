import Foundation
import Network

final class TunnelUDPSession {
    private static let maximumQueuedDatagrams = 32
    private static let maximumQueuedBytes = 64 * 1_024

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
    private var outgoing: [Data] = []
    private var queuedBytes = 0
    private var writeInProgress = false
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
                self.processNextSend()
                self.receiveNext()
            case .failed(let error):
                TunnelLog.debug("UDP session failed: \(error)")
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
            let queuedDatagramCount = self.outgoing.count + (self.writeInProgress ? 1 : 0)
            guard !payload.isEmpty,
                  payload.count <= PacketTunnelProvider.tunnelMTU - 28,
                  queuedDatagramCount < Self.maximumQueuedDatagrams,
                  self.queuedBytes + payload.count <= Self.maximumQueuedBytes else { return }
            self.outgoing.append(payload)
            self.queuedBytes += payload.count
            self.processNextSend()
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

    private func processNextSend() {
        guard isReady, !writeInProgress, !isClosed, !outgoing.isEmpty else { return }
        writeInProgress = true
        let payload = outgoing.removeFirst()
        connection.send(content: payload, completion: .contentProcessed { [weak self] error in
            guard let self else { return }
            self.queuedBytes = max(0, self.queuedBytes - payload.count)
            self.writeInProgress = false
            if let error {
                TunnelLog.debug("UDP write failed: \(error)")
                self.closeOnQueue()
            } else {
                self.processNextSend()
            }
        })
    }

    private func receiveNext() {
        guard !isClosed else { return }
        connection.receiveMessage { [weak self] data, _, _, error in
            guard let self, !self.isClosed else { return }
            if let error {
                TunnelLog.debug("UDP read failed: \(error)")
                self.closeOnQueue()
                return
            }
            if let data,
               !data.isEmpty,
               data.count <= PacketTunnelProvider.tunnelMTU - 28 {
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
        outgoing.removeAll(keepingCapacity: false)
        queuedBytes = 0
        writeInProgress = false
        connection.stateUpdateHandler = nil
        connection.cancel()
        provider?.removeUDPSession(key: key, session: self)
    }
}

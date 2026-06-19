import Foundation
import Network

final class TunnelTCPSession {
    private enum Flag {
        static let synAck: UInt8 = 0x12
        static let ack: UInt8 = 0x10
        static let finAck: UInt8 = 0x11
        static let reset: UInt8 = 0x04
        static let pushAck: UInt8 = 0x18
    }

    private struct OutgoingChunk {
        let data: Data
        let delay: TimeInterval
    }

    private weak var provider: PacketTunnelProvider?
    private let clientAddress: IPv4Address
    private let clientPort: UInt16
    private let serverAddress: IPv4Address
    private let serverPort: UInt16
    private let settings: TunnelSettings
    let key: String

    private let queue: DispatchQueue
    private let connection: NWConnection
    private var clientSequence: UInt32
    private var serverSequence: UInt32 = 1_000
    private var isReady = false
    private var isClosed = false
    private var transformedFirstPayload = false
    private var pendingBeforeConnect: [Data] = []
    private var outgoing: [OutgoingChunk] = []
    private var writeInProgress = false

    init(
        provider: PacketTunnelProvider,
        clientAddress: IPv4Address,
        clientPort: UInt16,
        serverAddress: IPv4Address,
        serverPort: UInt16,
        initialSequence: UInt32,
        settings: TunnelSettings
    ) {
        self.provider = provider
        self.clientAddress = clientAddress
        self.clientPort = clientPort
        self.serverAddress = serverAddress
        self.serverPort = serverPort
        self.settings = settings
        key = "\(clientAddress):\(clientPort)-\(serverAddress):\(serverPort)"
        queue = DispatchQueue(label: "com.birkankader.dpi.tcp.\(clientPort)")
        clientSequence = initialSequence &+ 1
        connection = NWConnection(
            host: NWEndpoint.Host(serverAddress.description),
            port: NWEndpoint.Port(rawValue: serverPort)!,
            using: .tcp
        )
    }

    func start() {
        sendPacket(sequence: serverSequence, acknowledgment: clientSequence, flags: Flag.synAck)
        serverSequence &+= 1

        connection.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready:
                self.isReady = true
                let pending = self.pendingBeforeConnect
                self.pendingBeforeConnect.removeAll(keepingCapacity: false)
                pending.forEach { self.enqueueClientPayload($0) }
                self.readFromServer()
            case .failed(let error):
                self.fail("TCP connection failed: \(error)")
            case .cancelled:
                self.finishRemoval()
            default:
                break
            }
        }
        connection.start(queue: queue)
    }

    func receiveClientData(_ data: Data, sequence: UInt32) {
        queue.async { [weak self] in
            guard let self, !self.isClosed else { return }
            self.clientSequence = sequence &+ UInt32(data.count)
            self.sendPacket(sequence: self.serverSequence, acknowledgment: self.clientSequence, flags: Flag.ack)
            if self.isReady {
                self.enqueueClientPayload(data)
            } else if self.pendingBeforeConnect.count < 32 {
                self.pendingBeforeConnect.append(data)
            }
        }
    }

    func receiveClientFIN(sequence: UInt32) {
        queue.async { [weak self] in
            guard let self, !self.isClosed else { return }
            self.clientSequence = sequence &+ 1
            self.sendPacket(sequence: self.serverSequence, acknowledgment: self.clientSequence, flags: Flag.finAck)
            self.serverSequence &+= 1
            self.close()
        }
    }

    func close() {
        queue.async { [weak self] in self?.closeOnQueue() }
    }

    private func enqueueClientPayload(_ data: Data) {
        if !transformedFirstPayload {
            transformedFirstPayload = true
            outgoing.append(contentsOf: transformedChunks(for: data))
        } else {
            outgoing.append(.init(data: data, delay: 0))
        }
        processNextWrite()
    }

    private func transformedChunks(for data: Data) -> [OutgoingChunk] {
        let bytes = [UInt8](data)
        if settings.splitClientHello,
           bytes.count > 5,
           bytes[0] == 0x16,
           bytes[1] == 0x03,
           let fragments = Self.fragmentTLSRecord(bytes, requestedPosition: settings.splitPosition) {
            return [
                .init(data: Data(fragments.0), delay: 0),
                .init(data: Data(fragments.1), delay: 0.01)
            ]
        }

        if settings.mixHostCase, Self.isHTTPMethod(bytes) {
            return [.init(data: Data(Self.mixHTTPHostCase(bytes)), delay: 0)]
        }
        return [.init(data: data, delay: 0)]
    }

    private func processNextWrite() {
        guard !writeInProgress, !isClosed, !outgoing.isEmpty else { return }
        writeInProgress = true
        let chunk = outgoing.removeFirst()
        let send = { [weak self] in
            guard let self, !self.isClosed else { return }
            self.connection.send(content: chunk.data, completion: .contentProcessed { [weak self] error in
                guard let self else { return }
                if let error {
                    self.fail("TCP write failed: \(error)")
                    return
                }
                self.writeInProgress = false
                self.processNextWrite()
            })
        }
        if chunk.delay > 0 {
            queue.asyncAfter(deadline: .now() + chunk.delay, execute: send)
        } else {
            send()
        }
    }

    private func readFromServer() {
        guard !isClosed else { return }
        connection.receive(minimumIncompleteLength: 1, maximumLength: PacketTunnelProvider.tunnelMTU - 40) {
            [weak self] data, _, isComplete, error in
            guard let self, !self.isClosed else { return }
            if let data, !data.isEmpty {
                self.sendPacket(
                    sequence: self.serverSequence,
                    acknowledgment: self.clientSequence,
                    flags: Flag.pushAck,
                    payload: data
                )
                self.serverSequence &+= UInt32(data.count)
            }
            if error != nil || isComplete {
                self.sendPacket(sequence: self.serverSequence, acknowledgment: self.clientSequence, flags: Flag.finAck)
                self.serverSequence &+= 1
                self.closeOnQueue()
            } else {
                self.readFromServer()
            }
        }
    }

    private func sendPacket(
        sequence: UInt32,
        acknowledgment: UInt32,
        flags: UInt8,
        payload: Data = Data()
    ) {
        provider?.writeTCPPacket(
            source: serverAddress,
            sourcePort: serverPort,
            destination: clientAddress,
            destinationPort: clientPort,
            sequence: sequence,
            acknowledgment: acknowledgment,
            flags: flags,
            payload: payload
        )
    }

    private func fail(_ message: String) {
        NSLog("%@", message)
        sendPacket(sequence: serverSequence, acknowledgment: clientSequence, flags: Flag.reset)
        closeOnQueue()
    }

    private func closeOnQueue() {
        guard !isClosed else { return }
        isClosed = true
        connection.stateUpdateHandler = nil
        connection.cancel()
        finishRemoval()
    }

    private func finishRemoval() {
        provider?.removeTCPSession(key: key, session: self)
    }

    private static func isHTTPMethod(_ bytes: [UInt8]) -> Bool {
        guard bytes.count >= 3 else { return false }
        let prefix = String(decoding: bytes.prefix(3), as: UTF8.self)
        return ["GET", "POS", "HEA", "PUT", "DEL", "CON", "OPT", "PAT"].contains(prefix)
    }

    private static func mixHTTPHostCase(_ bytes: [UInt8]) -> [UInt8] {
        var result = bytes
        guard result.count >= 5 else { return result }
        for index in 0...(result.count - 5) {
            let candidate = String(decoding: result[index..<(index + 5)], as: UTF8.self).lowercased()
            if candidate == "host:" {
                result.replaceSubrange(index..<(index + 4), with: Array("hOsT".utf8))
                break
            }
        }
        return result
    }

    private static func fragmentTLSRecord(
        _ bytes: [UInt8],
        requestedPosition: Int
    ) -> ([UInt8], [UInt8])? {
        let headerLength = 5
        guard bytes.count > headerLength, bytes[0] == 0x16, bytes[1] == 0x03 else { return nil }
        let payloadLength = Int(bytes[3]) << 8 | Int(bytes[4])
        guard payloadLength >= 2 else { return nil }
        let split = min(max(requestedPosition, 1), payloadLength - 1)
        guard bytes.count >= headerLength + split else { return nil }

        var first = Array(bytes[0..<(headerLength + split)])
        first[3] = UInt8(split >> 8)
        first[4] = UInt8(split & 0xff)

        let remainingRecordLength = payloadLength - split
        let remainingPayload = Array(bytes[(headerLength + split)...])
        var second = [bytes[0], bytes[1], bytes[2], UInt8(remainingRecordLength >> 8), UInt8(remainingRecordLength & 0xff)]
        second += remainingPayload
        return (first, second)
    }
}

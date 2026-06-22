import Foundation
import NetworkExtension

final class PacketTunnelProvider: NEPacketTunnelProvider {
    static let tunnelMTU = 1_500
    private static let maxTCPSessions = 96
    private static let maxUDPSessions = 64
    private static let maxPendingPacketWrites = 512

    private let sessionLock = NSLock()
    private let stateLock = NSLock()
    private let packetWriteQueue = DispatchQueue(label: "com.birkankader.dpi.packet-write")
    private let blocklist = DNSBlocklist()
    private var settings = TunnelSettings.load(options: nil, protocolConfiguration: nil)
    private var tcpSessions: [String: TunnelTCPSession] = [:]
    private var udpSessions: [String: TunnelUDPSession] = [:]
    private var cleanupTimer: DispatchSourceTimer?
    private var stopping = false
    private var pendingPacketWrites = 0
    private var tunnelGeneration: UInt64 = 0

    override func startTunnel(
        options: [String: NSObject]?,
        completionHandler: @escaping (Error?) -> Void
    ) {
        let generation = beginTunnelStart()
        settings = TunnelSettings.load(
            options: options,
            protocolConfiguration: protocolConfiguration as? NETunnelProviderProtocol
        )

        blocklist.load(
            enabled: settings.adBlockEnabled,
            listURL: settings.adBlockListURL
        ) { [weak self] in
            guard let self else {
                completionHandler(PacketTunnelError.providerReleased)
                return
            }
            guard self.isActive(generation) else {
                completionHandler(PacketTunnelError.cancelled)
                return
            }
            self.configureTunnel(generation: generation, completionHandler: completionHandler)
        }
    }

    override func stopTunnel(
        with reason: NEProviderStopReason,
        completionHandler: @escaping () -> Void
    ) {
        invalidateTunnel()

        sessionLock.lock()
        let tcp = Array(tcpSessions.values)
        let udp = Array(udpSessions.values)
        tcpSessions.removeAll()
        udpSessions.removeAll()
        sessionLock.unlock()

        tcp.forEach { $0.close() }
        udp.forEach { $0.close() }
        TunnelLog.debug("Tunnel stopped, reason: \(reason.rawValue)")
        completionHandler()
    }

    override func handleAppMessage(_ messageData: Data, completionHandler: ((Data?) -> Void)? = nil) {
        completionHandler?(Data())
    }

    func writeTCPPacket(
        source: IPv4Address,
        sourcePort: UInt16,
        destination: IPv4Address,
        destinationPort: UInt16,
        sequence: UInt32,
        acknowledgment: UInt32,
        flags: UInt8,
        payload: Data = Data()
    ) {
        guard payload.count <= Self.tunnelMTU - 40 else { return }
        let packet = PacketBuilder.tcp(
            source: source,
            sourcePort: sourcePort,
            destination: destination,
            destinationPort: destinationPort,
            sequence: sequence,
            acknowledgment: acknowledgment,
            flags: flags,
            payload: payload
        )
        writePacket(packet)
    }

    func writeUDPPacket(
        source: IPv4Address,
        sourcePort: UInt16,
        destination: IPv4Address,
        destinationPort: UInt16,
        payload: Data
    ) {
        guard payload.count <= Self.tunnelMTU - 28 else { return }
        writePacket(PacketBuilder.udp(
            source: source,
            sourcePort: sourcePort,
            destination: destination,
            destinationPort: destinationPort,
            payload: payload
        ))
    }

    func removeTCPSession(key: String, session: TunnelTCPSession) {
        sessionLock.lock()
        if tcpSessions[key] === session { tcpSessions.removeValue(forKey: key) }
        sessionLock.unlock()
    }

    func removeUDPSession(key: String, session: TunnelUDPSession) {
        sessionLock.lock()
        if udpSessions[key] === session { udpSessions.removeValue(forKey: key) }
        sessionLock.unlock()
    }

    private func configureTunnel(
        generation: UInt64,
        completionHandler: @escaping (Error?) -> Void
    ) {
        let dnsAddress = settings.dnsRedirectAddress?.description ?? "8.8.8.8"
        let ipv4 = NEIPv4Settings(addresses: ["10.120.0.2"], subnetMasks: ["255.255.255.0"])
        ipv4.includedRoutes = [NEIPv4Route.default()]

        let dns = NEDNSSettings(servers: [dnsAddress])
        dns.matchDomains = [""]

        let networkSettings = NEPacketTunnelNetworkSettings(tunnelRemoteAddress: "10.120.0.1")
        networkSettings.ipv4Settings = ipv4
        networkSettings.dnsSettings = dns
        networkSettings.mtu = NSNumber(value: Self.tunnelMTU)

        setTunnelNetworkSettings(networkSettings) { [weak self] error in
            guard let self else {
                completionHandler(PacketTunnelError.providerReleased)
                return
            }
            if let error {
                completionHandler(error)
                return
            }
            guard self.isActive(generation) else {
                completionHandler(PacketTunnelError.cancelled)
                return
            }

            self.startCleanupTimer(generation: generation)
            self.readPackets(generation: generation)
            TunnelLog.debug(
                "Tunnel active. Preset=\(self.settings.presetKey) " +
                    "DNS=\(dnsAddress):\(self.settings.dnsRedirectPort) Adblock=\(self.blocklist.count)"
            )
            completionHandler(nil)
        }
    }

    private func readPackets(generation: UInt64) {
        guard isActive(generation) else { return }
        packetFlow.readPackets { [weak self] packets, _ in
            guard let self, self.isActive(generation) else { return }
            for packet in packets {
                self.processIPv4Packet([UInt8](packet))
            }
            self.readPackets(generation: generation)
        }
    }

    private func processIPv4Packet(_ packet: [UInt8]) {
        guard packet.count >= 20, packet[0] >> 4 == 4 else { return }
        let ipHeaderLength = Int(packet[0] & 0x0f) * 4
        let totalLength = Int(read16(packet, at: 2))
        let fragmentField = read16(packet, at: 6)
        guard ipHeaderLength >= 20,
              totalLength >= ipHeaderLength,
              totalLength <= min(packet.count, Self.tunnelMTU),
              fragmentField & 0x3fff == 0 else { return }
        let validatedPacket = Array(packet.prefix(totalLength))
        let source = IPv4Address(validatedPacket[12..<16])
        let destination = IPv4Address(validatedPacket[16..<20])

        switch validatedPacket[9] {
        case 6:
            processTCP(validatedPacket, ipHeaderLength: ipHeaderLength, source: source, destination: destination)
        case 17:
            processUDP(validatedPacket, ipHeaderLength: ipHeaderLength, source: source, destination: destination)
        default:
            break
        }
    }

    private func processTCP(
        _ packet: [UInt8],
        ipHeaderLength: Int,
        source: IPv4Address,
        destination: IPv4Address
    ) {
        let offset = ipHeaderLength
        guard packet.count >= offset + 20 else { return }
        let sourcePort = read16(packet, at: offset)
        let destinationPort = read16(packet, at: offset + 2)
        guard sourcePort != 0, destinationPort != 0 else { return }
        let sequence = read32(packet, at: offset + 4)
        let dataOffset = Int(packet[offset + 12] >> 4) * 4
        guard dataOffset >= 20, offset + dataOffset <= packet.count else { return }
        let flags = packet[offset + 13]
        let isSYN = flags & 0x02 != 0
        let isACK = flags & 0x10 != 0
        let isFIN = flags & 0x01 != 0
        let isReset = flags & 0x04 != 0
        let key = "\(source):\(sourcePort)-\(destination):\(destinationPort)"

        if isSYN && !isACK {
            let session = TunnelTCPSession(
                provider: self,
                clientAddress: source,
                clientPort: sourcePort,
                serverAddress: destination,
                serverPort: destinationPort,
                initialSequence: sequence,
                settings: settings
            )

            sessionLock.lock()
            let previous = tcpSessions[key]
            let canAdd = previous != nil || tcpSessions.count < Self.maxTCPSessions
            if canAdd { tcpSessions[key] = session }
            sessionLock.unlock()

            guard canAdd else { return }
            previous?.close()
            session.start()
            return
        }

        sessionLock.lock()
        let session = tcpSessions[key]
        sessionLock.unlock()
        guard let session else { return }

        if isReset {
            session.close()
            return
        }
        if isFIN {
            session.receiveClientFIN(sequence: sequence)
            return
        }

        let payloadStart = offset + dataOffset
        guard isACK, payloadStart <= packet.count else { return }
        if payloadStart < packet.count {
            session.receiveClientData(Data(packet[payloadStart...]), sequence: sequence)
        }
    }

    private func processUDP(
        _ packet: [UInt8],
        ipHeaderLength: Int,
        source: IPv4Address,
        destination: IPv4Address
    ) {
        let offset = ipHeaderLength
        guard packet.count >= offset + 8 else { return }
        let sourcePort = read16(packet, at: offset)
        let destinationPort = read16(packet, at: offset + 2)
        let udpLength = Int(read16(packet, at: offset + 4))
        guard sourcePort != 0,
              destinationPort != 0,
              udpLength >= 8,
              offset + udpLength <= packet.count else { return }

        // QUIC cannot be fragmented like TLS-over-TCP, so force HTTPS fallback to TCP.
        guard destinationPort != 443 else { return }
        let payloadStart = offset + 8
        guard payloadStart < offset + udpLength else { return }
        let payload = Data(packet[payloadStart..<(offset + udpLength)])

        if destinationPort == 53 {
            let domain = DNSPacket.queryDomain([UInt8](payload))
            if blocklist.contains(domain), let response = DNSPacket.blockedResponse(for: [UInt8](payload)) {
                writeUDPPacket(
                    source: destination,
                    sourcePort: destinationPort,
                    destination: source,
                    destinationPort: sourcePort,
                    payload: response
                )
                return
            }
        }

        let remoteAddress = destinationPort == 53 ? (settings.dnsRedirectAddress ?? destination) : destination
        let remotePort = destinationPort == 53 && settings.dnsRedirectAddress != nil
            ? settings.dnsRedirectPort
            : destinationPort
        let key = "\(source):\(sourcePort)-\(destination):\(destinationPort)>\(remoteAddress):\(remotePort)"

        sessionLock.lock()
        var session = udpSessions[key]
        let canAdd = udpSessions.count < Self.maxUDPSessions
        if session == nil, canAdd {
            session = TunnelUDPSession(
                provider: self,
                key: key,
                clientAddress: source,
                clientPort: sourcePort,
                destinationAddress: destination,
                destinationPort: destinationPort,
                remoteAddress: remoteAddress,
                remotePort: remotePort
            )
            udpSessions[key] = session
        }
        sessionLock.unlock()

        guard let session else { return }
        session.startIfNeeded()
        session.send(payload)
    }

    private func writePacket(_ packet: Data) {
        guard reservePacketWrite() else { return }
        packetWriteQueue.async { [weak self] in
            guard let self else { return }
            defer { self.releasePacketWrite() }
            guard !self.isStopping else { return }
            self.packetFlow.writePackets([packet], withProtocols: [NSNumber(value: 2)])
        }
    }

    private func startCleanupTimer(generation: UInt64) {
        let timer = DispatchSource.makeTimerSource(queue: DispatchQueue(label: "com.birkankader.dpi.cleanup"))
        timer.schedule(deadline: .now() + 10, repeating: 10)
        timer.setEventHandler { [weak self] in
            guard let self, self.isActive(generation) else { return }
            self.sessionLock.lock()
            let sessions = Array(self.udpSessions.values)
            self.sessionLock.unlock()
            let cutoff = Date().addingTimeInterval(-30)
            sessions.forEach { $0.closeIfIdle(before: cutoff) }
        }

        stateLock.lock()
        let previousTimer = cleanupTimer
        let shouldRun = !stopping && tunnelGeneration == generation
        if shouldRun { cleanupTimer = timer }
        stateLock.unlock()

        timer.resume()
        if shouldRun {
            previousTimer?.cancel()
        } else {
            timer.cancel()
        }
    }

    private var isStopping: Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        return stopping
    }

    private func beginTunnelStart() -> UInt64 {
        stateLock.lock()
        tunnelGeneration &+= 1
        stopping = false
        let generation = tunnelGeneration
        let previousTimer = cleanupTimer
        cleanupTimer = nil
        stateLock.unlock()
        previousTimer?.cancel()
        return generation
    }

    private func invalidateTunnel() {
        stateLock.lock()
        tunnelGeneration &+= 1
        stopping = true
        let timer = cleanupTimer
        cleanupTimer = nil
        stateLock.unlock()
        timer?.cancel()
    }

    private func isActive(_ generation: UInt64) -> Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        return !stopping && tunnelGeneration == generation
    }

    private func reservePacketWrite() -> Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        guard !stopping, pendingPacketWrites < Self.maxPendingPacketWrites else { return false }
        pendingPacketWrites += 1
        return true
    }

    private func releasePacketWrite() {
        stateLock.lock()
        pendingPacketWrites = max(0, pendingPacketWrites - 1)
        stateLock.unlock()
    }

    private func read16(_ bytes: [UInt8], at offset: Int) -> UInt16 {
        UInt16(bytes[offset]) << 8 | UInt16(bytes[offset + 1])
    }

    private func read32(_ bytes: [UInt8], at offset: Int) -> UInt32 {
        UInt32(bytes[offset]) << 24 |
            UInt32(bytes[offset + 1]) << 16 |
            UInt32(bytes[offset + 2]) << 8 |
            UInt32(bytes[offset + 3])
    }
}

private enum PacketTunnelError: LocalizedError {
    case cancelled
    case providerReleased

    var errorDescription: String? {
        switch self {
        case .cancelled: return "Packet tunnel startup was cancelled."
        case .providerReleased: return "Packet tunnel provider released before startup."
        }
    }
}

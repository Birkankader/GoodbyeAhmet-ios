import Foundation

enum PacketBuilder {
    static func tcp(
        source: IPv4Address,
        sourcePort: UInt16,
        destination: IPv4Address,
        destinationPort: UInt16,
        sequence: UInt32,
        acknowledgment: UInt32,
        flags: UInt8,
        payload: Data = Data()
    ) -> Data {
        let payloadBytes = [UInt8](payload)
        let totalLength = 40 + payloadBytes.count
        var packet = [UInt8](repeating: 0, count: totalLength)
        packet[0] = 0x45
        set16(UInt16(totalLength), in: &packet, at: 2)
        packet[5] = 1
        packet[8] = 64
        packet[9] = 6
        packet.replaceSubrange(12..<16, with: source.bytes)
        packet.replaceSubrange(16..<20, with: destination.bytes)
        set16(checksum(Array(packet[0..<20])), in: &packet, at: 10)

        set16(sourcePort, in: &packet, at: 20)
        set16(destinationPort, in: &packet, at: 22)
        set32(sequence, in: &packet, at: 24)
        set32(acknowledgment, in: &packet, at: 28)
        packet[32] = 5 << 4
        packet[33] = flags
        packet[34] = 0xff
        packet[35] = 0xff
        if !payloadBytes.isEmpty {
            packet.replaceSubrange(40..<totalLength, with: payloadBytes)
        }

        var pseudoHeader = source.bytes + destination.bytes + [0, 6, UInt8((totalLength - 20) >> 8), UInt8((totalLength - 20) & 0xff)]
        var tcpSegment = Array(packet[20..<totalLength])
        tcpSegment[16] = 0
        tcpSegment[17] = 0
        pseudoHeader += tcpSegment
        set16(checksum(pseudoHeader), in: &packet, at: 36)
        return Data(packet)
    }

    static func udp(
        source: IPv4Address,
        sourcePort: UInt16,
        destination: IPv4Address,
        destinationPort: UInt16,
        payload: Data
    ) -> Data {
        let bytes = [UInt8](payload)
        let totalLength = 28 + bytes.count
        var packet = [UInt8](repeating: 0, count: totalLength)
        packet[0] = 0x45
        set16(UInt16(totalLength), in: &packet, at: 2)
        packet[8] = 64
        packet[9] = 17
        packet.replaceSubrange(12..<16, with: source.bytes)
        packet.replaceSubrange(16..<20, with: destination.bytes)
        set16(checksum(Array(packet[0..<20])), in: &packet, at: 10)
        set16(sourcePort, in: &packet, at: 20)
        set16(destinationPort, in: &packet, at: 22)
        set16(UInt16(8 + bytes.count), in: &packet, at: 24)
        packet.replaceSubrange(28..<totalLength, with: bytes)
        return Data(packet)
    }

    private static func checksum(_ bytes: [UInt8]) -> UInt16 {
        var sum: UInt32 = 0
        var index = 0
        while index < bytes.count {
            sum += UInt32(bytes[index]) << 8
            if index + 1 < bytes.count { sum += UInt32(bytes[index + 1]) }
            index += 2
        }
        while sum >> 16 != 0 { sum = (sum & 0xffff) + (sum >> 16) }
        return ~UInt16(sum & 0xffff)
    }

    private static func set16(_ value: UInt16, in bytes: inout [UInt8], at index: Int) {
        bytes[index] = UInt8((value >> 8) & 0xff)
        bytes[index + 1] = UInt8(value & 0xff)
    }

    private static func set32(_ value: UInt32, in bytes: inout [UInt8], at index: Int) {
        bytes[index] = UInt8((value >> 24) & 0xff)
        bytes[index + 1] = UInt8((value >> 16) & 0xff)
        bytes[index + 2] = UInt8((value >> 8) & 0xff)
        bytes[index + 3] = UInt8(value & 0xff)
    }
}

import Foundation

enum DNSPacket {
    static func queryDomain(_ payload: [UInt8]) -> String? {
        guard payload.count >= 13, payload[2] & 0x80 == 0, payload[2] & 0x78 == 0 else { return nil }
        let questionCount = Int(payload[4]) << 8 | Int(payload[5])
        guard questionCount > 0 else { return nil }

        var offset = 12
        var labels: [String] = []
        while offset < payload.count {
            let length = Int(payload[offset])
            if length == 0 { break }
            guard length <= 63, length & 0xc0 == 0 else { return nil }
            offset += 1
            guard offset + length <= payload.count else { return nil }
            labels.append(String(decoding: payload[offset..<(offset + length)], as: UTF8.self))
            offset += length
        }
        return labels.isEmpty ? nil : labels.joined(separator: ".")
    }

    static func blockedResponse(for query: [UInt8]) -> Data? {
        guard let questionEnd = questionSectionEnd(query) else { return nil }
        let typeOffset = questionEnd - 4
        let queryType = Int(query[typeOffset]) << 8 | Int(query[typeOffset + 1])
        guard queryType == 1 || queryType == 28 else {
            var response = Array(query.prefix(questionEnd))
            response[2] = 0x85
            response[3] = 0x83
            response[6] = 0
            response[7] = 0
            response[8] = 0
            response[9] = 0
            response[10] = 0
            response[11] = 0
            return Data(response)
        }

        let addressLength = queryType == 1 ? 4 : 16
        var response = Array(query.prefix(questionEnd))
        response[2] = 0x85
        response[3] = 0x80
        response[6] = 0
        response[7] = 1
        response[8] = 0
        response[9] = 0
        response[10] = 0
        response[11] = 0
        response += [0xc0, 0x0c, UInt8(queryType >> 8), UInt8(queryType & 0xff), 0x00, 0x01]
        response += [0x00, 0x00, 0x01, 0x2c, 0x00, UInt8(addressLength)]
        response += Array(repeating: 0, count: addressLength)
        return Data(response)
    }

    private static func questionSectionEnd(_ payload: [UInt8]) -> Int? {
        guard payload.count >= 13 else { return nil }
        var offset = 12
        while offset < payload.count {
            let length = Int(payload[offset])
            if length == 0 {
                offset += 1
                break
            }
            if length & 0xc0 == 0xc0 {
                offset += 2
                break
            }
            guard length <= 63 else { return nil }
            offset += 1 + length
        }
        offset += 4
        return offset <= payload.count ? offset : nil
    }
}

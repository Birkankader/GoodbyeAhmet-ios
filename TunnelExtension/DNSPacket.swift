import Foundation

enum DNSPacket {
    static func queryDomain(_ payload: [UInt8]) -> String? {
        guard payload.count >= 13, payload[2] & 0x80 == 0, payload[2] & 0x78 == 0 else { return nil }
        let questionCount = Int(payload[4]) << 8 | Int(payload[5])
        guard questionCount == 1 else { return nil }

        var offset = 12
        var labels: [String] = []
        var reachedTerminator = false
        while offset < payload.count {
            let length = Int(payload[offset])
            if length == 0 {
                offset += 1
                guard offset + 4 <= payload.count else { return nil }
                reachedTerminator = true
                break
            }
            guard length <= 63, length & 0xc0 == 0 else { return nil }
            offset += 1
            guard offset + length <= payload.count else { return nil }
            let label = payload[offset..<(offset + length)]
            guard label.allSatisfy({ byte in
                (byte >= 48 && byte <= 57) ||
                    (byte >= 65 && byte <= 90) ||
                    (byte >= 97 && byte <= 122) ||
                    byte == 45 || byte == 95
            }) else { return nil }
            labels.append(String(decoding: label, as: UTF8.self))
            offset += length
        }
        guard reachedTerminator else { return nil }
        let queryClass = Int(payload[offset + 2]) << 8 | Int(payload[offset + 3])
        guard queryClass == 1 else { return nil }
        let domain = labels.joined(separator: ".")
        return !domain.isEmpty && domain.utf8.count <= 253 ? domain : nil
    }

    static func blockedResponse(for query: [UInt8]) -> Data? {
        guard query.count >= 13,
              query[4] == 0,
              query[5] == 1,
              let questionEnd = questionSectionEnd(query) else { return nil }
        let typeOffset = questionEnd - 4
        let queryType = Int(query[typeOffset]) << 8 | Int(query[typeOffset + 1])
        let queryClass = Int(query[typeOffset + 2]) << 8 | Int(query[typeOffset + 3])
        guard queryClass == 1 else { return nil }
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
            guard length <= 63, length & 0xc0 == 0 else { return nil }
            guard offset + 1 + length <= payload.count else { return nil }
            offset += 1 + length
        }
        offset += 4
        return offset <= payload.count ? offset : nil
    }
}

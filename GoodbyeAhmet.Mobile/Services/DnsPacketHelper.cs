using System.Net;
using System.Text;

namespace GoodbyeAhmet.Mobile.Services;

/// <summary>
/// Utility class for parsing DNS query packets and crafting spoofed responses.
/// Works directly on raw DNS payload bytes (after stripping IP + UDP headers).
///
/// DNS packet layout (RFC 1035):
///   Header:  12 bytes (ID, Flags, QDCount, ANCount, NSCount, ARCount)
///   Question: variable (QNAME + QTYPE + QCLASS)
///   Answer:   variable (only in responses)
/// </summary>
public static class DnsPacketHelper
{
    /// <summary>
    /// Parses the queried domain name from a raw DNS payload.
    /// Returns null if the packet is malformed or not a standard query.
    /// </summary>
    /// <param name="dnsPayload">Raw DNS bytes (header + question section).</param>
    /// <param name="payloadLength">Number of valid bytes in the buffer.</param>
    /// <returns>
    /// The domain name as a string (e.g. "ads.example.com"), or null if parsing fails.
    /// </returns>
    public static string? ParseQueryDomain(byte[] dnsPayload, int payloadLength)
    {
        // Minimum DNS header = 12 bytes + at least 1 byte of QNAME
        if (payloadLength < 13)
            return null;

        // Check that QR=0 (query) and OPCODE=0 (standard query)
        byte flags1 = dnsPayload[2];
        if ((flags1 & 0x80) != 0)  // QR bit set → this is a response, not a query
            return null;
        if ((flags1 & 0x78) != 0)  // OPCODE != 0
            return null;

        // QDCount (question count) — we handle the first question
        int qdCount = (dnsPayload[4] << 8) | dnsPayload[5];
        if (qdCount < 1)
            return null;

        // Parse QNAME starting at offset 12
        int offset = 12;
        var sb = new StringBuilder(64);

        while (offset < payloadLength)
        {
            byte labelLen = dnsPayload[offset];

            if (labelLen == 0)
            {
                // End of QNAME
                break;
            }

            // Pointer compression (top 2 bits = 11) — shouldn't appear in queries
            // but handle gracefully
            if ((labelLen & 0xC0) == 0xC0)
                return null; // compressed labels in query section = malformed

            // Label must not exceed 63 bytes
            if (labelLen > 63)
                return null;

            offset++;
            if (offset + labelLen > payloadLength)
                return null; // truncated

            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(dnsPayload, offset, labelLen));
            offset += labelLen;
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// Returns the QNAME end offset (past the trailing 0x00 + QTYPE + QCLASS)
    /// so we know the full question section length.
    /// Returns -1 if parsing fails.
    /// </summary>
    public static int GetQuestionSectionEnd(byte[] dnsPayload, int payloadLength)
    {
        if (payloadLength < 13) return -1;

        int offset = 12;
        while (offset < payloadLength)
        {
            byte labelLen = dnsPayload[offset];
            if (labelLen == 0) { offset++; break; }
            if ((labelLen & 0xC0) == 0xC0) { offset += 2; break; }
            if (labelLen > 63) return -1;
            offset += 1 + labelLen;
        }

        // After QNAME: QTYPE (2 bytes) + QCLASS (2 bytes)
        offset += 4;
        return offset <= payloadLength ? offset : -1;
    }

    /// <summary>
    /// Crafts a spoofed DNS response that resolves the queried domain to 0.0.0.0.
    ///
    /// Response format:
    ///   - Same Transaction ID as query
    ///   - QR=1, OPCODE=0, AA=1, RD=1, RA=1, RCODE=0
    ///   - Original question section echoed back
    ///   - One A record answer: domain → 0.0.0.0, TTL=300
    /// </summary>
    /// <param name="queryPayload">Original DNS query bytes.</param>
    /// <param name="queryLength">Length of the query.</param>
    /// <returns>The spoofed DNS response bytes, or null if the query couldn't be parsed.</returns>
    public static byte[]? CraftBlockedResponse(byte[] queryPayload, int queryLength)
    {
        int questionEnd = GetQuestionSectionEnd(queryPayload, queryLength);
        if (questionEnd < 0)
            return null;

        // Check QTYPE — only spoof A (0x0001) and AAAA (0x001C) records
        int qtypeOffset = questionEnd - 4;
        int qtype = (queryPayload[qtypeOffset] << 8) | queryPayload[qtypeOffset + 1];

        bool isTypeA = qtype == 1;     // A record
        bool isTypeAAAA = qtype == 28;  // AAAA record

        if (!isTypeA && !isTypeAAAA)
        {
            // For non-A/AAAA queries, return NXDOMAIN
            return CraftNxDomainResponse(queryPayload, queryLength, questionEnd);
        }

        // Answer section: pointer to QNAME (2) + TYPE (2) + CLASS (2) + TTL (4) + RDLENGTH (2) + RDATA
        int rdataLen = isTypeA ? 4 : 16;  // 4 bytes for IPv4, 16 for IPv6
        int answerLen = 2 + 2 + 2 + 4 + 2 + rdataLen;
        int responseLen = questionEnd + answerLen;

        var response = new byte[responseLen];

        // Copy header + question section from query
        Buffer.BlockCopy(queryPayload, 0, response, 0, questionEnd);

        // ── Modify header flags ─────────────────────────────
        // Byte 2: QR=1 | OPCODE=0 | AA=1 | TC=0 | RD=1  → 0x85
        response[2] = 0x85;
        // Byte 3: RA=1 | Z=0 | RCODE=0                    → 0x80
        response[3] = 0x80;

        // QDCount = 1 (already set from copy)
        // ANCount = 1
        response[6] = 0;
        response[7] = 1;
        // NSCount = 0
        response[8] = 0;
        response[9] = 0;
        // ARCount = 0
        response[10] = 0;
        response[11] = 0;

        // ── Answer section ──────────────────────────────────
        int a = questionEnd;

        // Name pointer → offset 12 (start of QNAME in question)
        response[a] = 0xC0;
        response[a + 1] = 0x0C;
        a += 2;

        // TYPE
        response[a] = (byte)(qtype >> 8);
        response[a + 1] = (byte)(qtype & 0xFF);
        a += 2;

        // CLASS IN (0x0001)
        response[a] = 0x00;
        response[a + 1] = 0x01;
        a += 2;

        // TTL = 300 seconds (0x0000012C)
        response[a] = 0x00;
        response[a + 1] = 0x00;
        response[a + 2] = 0x01;
        response[a + 3] = 0x2C;
        a += 4;

        // RDLENGTH
        response[a] = (byte)(rdataLen >> 8);
        response[a + 1] = (byte)(rdataLen & 0xFF);
        a += 2;

        // RDATA: 0.0.0.0 for A, :: for AAAA (all zeros, already default)
        // (response array is zero-initialized)

        return response;
    }

    /// <summary>
    /// Crafts an NXDOMAIN response for non-A/AAAA query types.
    /// </summary>
    private static byte[] CraftNxDomainResponse(byte[] queryPayload, int queryLength, int questionEnd)
    {
        var response = new byte[questionEnd];
        Buffer.BlockCopy(queryPayload, 0, response, 0, questionEnd);

        // QR=1, AA=1, RD=1
        response[2] = 0x85;
        // RA=1, RCODE=3 (NXDOMAIN)
        response[3] = 0x83;

        // ANCount = 0
        response[6] = 0;
        response[7] = 0;
        // NSCount = 0
        response[8] = 0;
        response[9] = 0;
        // ARCount = 0
        response[10] = 0;
        response[11] = 0;

        return response;
    }
}

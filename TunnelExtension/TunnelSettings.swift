import Foundation
import NetworkExtension

struct TunnelSettings {
    var presetKey: String
    var splitClientHello: Bool
    var splitPosition: Int
    var mixHostCase: Bool
    var fakeTTL: Int
    var dnsRedirectAddress: IPv4Address?
    var dnsRedirectPort: UInt16
    var adBlockEnabled: Bool
    var adBlockListURL: String

    static func load(
        options: [String: NSObject]?,
        protocolConfiguration: NETunnelProviderProtocol?
    ) -> TunnelSettings {
        let provider = protocolConfiguration?.providerConfiguration ?? [:]
        let requestedPreset = string("PresetKey", options: options, provider: provider) ?? "default"
        let presetKey = knownPresetKeys.contains(requestedPreset) ? requestedPreset : "default"
        var settings = preset(presetKey)

        settings.splitClientHello = bool("SplitClientHello", options: options, provider: provider) ?? settings.splitClientHello
        settings.splitPosition = min(128, max(1, integer("SplitPosition", options: options, provider: provider) ?? settings.splitPosition))
        settings.mixHostCase = bool("MixHostCase", options: options, provider: provider) ?? settings.mixHostCase
        settings.fakeTTL = min(255, max(0, integer("FakeTtl", options: options, provider: provider) ?? settings.fakeTTL))
        let requestedPort = integer("DnsRedirectPort", options: options, provider: provider) ?? Int(settings.dnsRedirectPort)
        settings.dnsRedirectPort = UInt16((1...65_535).contains(requestedPort) ? requestedPort : 53)
        settings.adBlockEnabled = bool("AdBlockEnabled", options: options, provider: provider) ?? settings.adBlockEnabled
        if let listURL = string("AdBlockListUrl", options: options, provider: provider), listURL.utf8.count <= 2_048 {
            settings.adBlockListURL = listURL
        }

        if let address = string("DnsRedirectAddress", options: options, provider: provider) {
            settings.dnsRedirectAddress = IPv4Address(address)
        }
        return settings
    }

    private static let knownPresetKeys: Set<String> = [
        "default", "aggressive", "dns_redirect", "turkey", "turkey_so", "russia", "custom_host"
    ]

    private static func preset(_ key: String) -> TunnelSettings {
        let defaultList = DNSBlocklist.defaultListURL
        switch key {
        case "aggressive":
            return .init(presetKey: key, splitClientHello: true, splitPosition: 2, mixHostCase: true, fakeTTL: 3, dnsRedirectAddress: nil, dnsRedirectPort: 53, adBlockEnabled: false, adBlockListURL: defaultList)
        case "dns_redirect":
            return .init(presetKey: key, splitClientHello: true, splitPosition: 2, mixHostCase: false, fakeTTL: 0, dnsRedirectAddress: IPv4Address("77.88.8.8"), dnsRedirectPort: 1253, adBlockEnabled: false, adBlockListURL: defaultList)
        case "turkey":
            return .init(presetKey: key, splitClientHello: true, splitPosition: 2, mixHostCase: false, fakeTTL: 5, dnsRedirectAddress: IPv4Address("77.88.8.8"), dnsRedirectPort: 1253, adBlockEnabled: false, adBlockListURL: defaultList)
        case "turkey_so":
            return .init(presetKey: key, splitClientHello: false, splitPosition: 2, mixHostCase: false, fakeTTL: 3, dnsRedirectAddress: nil, dnsRedirectPort: 53, adBlockEnabled: false, adBlockListURL: defaultList)
        case "russia":
            return .init(presetKey: key, splitClientHello: true, splitPosition: 2, mixHostCase: true, fakeTTL: 0, dnsRedirectAddress: nil, dnsRedirectPort: 53, adBlockEnabled: false, adBlockListURL: defaultList)
        case "custom_host":
            return .init(presetKey: key, splitClientHello: false, splitPosition: 2, mixHostCase: true, fakeTTL: 0, dnsRedirectAddress: nil, dnsRedirectPort: 53, adBlockEnabled: false, adBlockListURL: defaultList)
        default:
            return .init(presetKey: key, splitClientHello: true, splitPosition: 2, mixHostCase: false, fakeTTL: 0, dnsRedirectAddress: nil, dnsRedirectPort: 53, adBlockEnabled: false, adBlockListURL: defaultList)
        }
    }

    private static func value(
        _ key: String,
        options: [String: NSObject]?,
        provider: [String: Any]
    ) -> Any? {
        options?[key] ?? provider[key]
    }

    private static func string(_ key: String, options: [String: NSObject]?, provider: [String: Any]) -> String? {
        if let string = value(key, options: options, provider: provider) as? String {
            let trimmed = string.trimmingCharacters(in: .whitespacesAndNewlines)
            return trimmed.isEmpty ? nil : trimmed
        }
        return nil
    }

    private static func integer(_ key: String, options: [String: NSObject]?, provider: [String: Any]) -> Int? {
        let candidate = value(key, options: options, provider: provider)
        if let number = candidate as? NSNumber { return number.intValue }
        if let string = candidate as? String { return Int(string) }
        return nil
    }

    private static func bool(_ key: String, options: [String: NSObject]?, provider: [String: Any]) -> Bool? {
        let candidate = value(key, options: options, provider: provider)
        if let number = candidate as? NSNumber { return number.boolValue }
        if let string = candidate as? String { return Bool(string) }
        return nil
    }
}

struct IPv4Address: Hashable, CustomStringConvertible {
    let rawValue: UInt32

    init(_ bytes: ArraySlice<UInt8>) {
        let values = Array(bytes.prefix(4))
        rawValue = values.count == 4
            ? UInt32(values[0]) << 24 | UInt32(values[1]) << 16 | UInt32(values[2]) << 8 | UInt32(values[3])
            : 0
    }

    init?(_ text: String) {
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 4,
              let a = UInt8(parts[0]), let b = UInt8(parts[1]),
              let c = UInt8(parts[2]), let d = UInt8(parts[3]) else { return nil }
        rawValue = UInt32(a) << 24 | UInt32(b) << 16 | UInt32(c) << 8 | UInt32(d)
    }

    var bytes: [UInt8] {
        [
            UInt8((rawValue >> 24) & 0xff),
            UInt8((rawValue >> 16) & 0xff),
            UInt8((rawValue >> 8) & 0xff),
            UInt8(rawValue & 0xff)
        ]
    }

    var description: String { bytes.map(String.init).joined(separator: ".") }
}

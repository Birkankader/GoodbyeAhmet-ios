import Foundation

@MainActor
final class AppSettings: ObservableObject {
    static let defaultBlocklistURL = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts"
    private static let disclosureVersion = 1

    private enum Key {
        static let preset = "selected_preset"
        static let split = "split_client_hello"
        static let splitPosition = "split_position"
        static let mixHost = "mix_host_case"
        static let ttl = "ttl"
        static let dnsAddress = "dns_v4_address"
        static let dnsPort = "dns_v4_port"
        static let adBlock = "adblock_enabled"
        static let adBlockURL = "adblock_list_url"
        static let activateOnStart = "activate_on_start"
        static let disclosureVersion = "vpn_disclosure_version"
    }

    @Published var selectedPresetKey: String { didSet { save(Key.preset, selectedPresetKey) } }
    @Published var splitClientHello: Bool { didSet { save(Key.split, splitClientHello) } }
    @Published var splitPosition: Int { didSet { save(Key.splitPosition, String(splitPosition)) } }
    @Published var mixHostCase: Bool { didSet { save(Key.mixHost, mixHostCase) } }
    @Published var fakeTTL: Int { didSet { save(Key.ttl, String(fakeTTL)) } }
    @Published var dnsAddress: String { didSet { save(Key.dnsAddress, dnsAddress) } }
    @Published var dnsPort: Int { didSet { save(Key.dnsPort, String(dnsPort)) } }
    @Published var adBlockEnabled: Bool { didSet { save(Key.adBlock, adBlockEnabled) } }
    @Published var adBlockListURL: String { didSet { save(Key.adBlockURL, adBlockListURL) } }
    @Published var activateOnStart: Bool { didSet { save(Key.activateOnStart, activateOnStart) } }
    @Published private(set) var hasAcceptedVPNDisclosure: Bool

    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        let key = defaults.string(forKey: Key.preset).flatMap { $0.isEmpty ? nil : $0 } ?? "default"
        let preset = VPNPreset.find(key)
        selectedPresetKey = key
        splitClientHello = defaults.object(forKey: Key.split) as? Bool ?? preset.splitClientHello
        splitPosition = Int(defaults.string(forKey: Key.splitPosition) ?? "") ?? preset.splitPosition
        mixHostCase = defaults.object(forKey: Key.mixHost) as? Bool ?? preset.mixHostCase
        fakeTTL = Int(defaults.string(forKey: Key.ttl) ?? "") ?? preset.fakeTTL
        dnsAddress = defaults.string(forKey: Key.dnsAddress) ?? preset.dnsAddress
        dnsPort = Int(defaults.string(forKey: Key.dnsPort) ?? "") ?? preset.dnsPort
        adBlockEnabled = defaults.object(forKey: Key.adBlock) as? Bool ?? false
        adBlockListURL = defaults.string(forKey: Key.adBlockURL) ?? Self.defaultBlocklistURL
        activateOnStart = defaults.object(forKey: Key.activateOnStart) as? Bool ?? false
        hasAcceptedVPNDisclosure = defaults.integer(forKey: Key.disclosureVersion) >= Self.disclosureVersion
    }

    var selectedPreset: VPNPreset { VPNPreset.find(selectedPresetKey) }

    var configuration: VPNConfiguration {
        .init(
            presetKey: selectedPresetKey,
            splitClientHello: splitClientHello,
            splitPosition: max(1, splitPosition),
            mixHostCase: mixHostCase,
            fakeTTL: max(0, fakeTTL),
            dnsAddress: dnsAddress,
            dnsPort: (1...65535).contains(dnsPort) ? dnsPort : 53,
            adBlockEnabled: adBlockEnabled,
            adBlockListURL: Self.sanitizedBlocklistURL(adBlockListURL)
        )
    }

    func applyPreset(_ key: String) {
        let preset = VPNPreset.find(key)
        selectedPresetKey = preset.id
        splitClientHello = preset.splitClientHello
        splitPosition = preset.splitPosition
        mixHostCase = preset.mixHostCase
        fakeTTL = preset.fakeTTL
        dnsAddress = preset.dnsAddress
        dnsPort = preset.dnsPort
    }

    func acceptVPNDisclosure() {
        defaults.set(Self.disclosureVersion, forKey: Key.disclosureVersion)
        hasAcceptedVPNDisclosure = true
    }

    var isBlocklistURLValid: Bool {
        Self.sanitizedBlocklistURL(adBlockListURL) == adBlockListURL.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func sanitizedBlocklistURL(_ rawValue: String) -> String {
        let trimmed = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.utf8.count <= 2_048,
              let url = URL(string: trimmed),
              url.scheme?.lowercased() == "https",
              url.host != nil,
              url.user == nil,
              url.password == nil else { return defaultBlocklistURL }
        return trimmed
    }

    private func save(_ key: String, _ value: Any) {
        defaults.set(value, forKey: key)
    }
}

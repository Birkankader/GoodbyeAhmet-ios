import Foundation

struct VPNPreset: Identifiable, Hashable {
    let id: String
    let name: String
    let detail: String
    let splitClientHello: Bool
    let splitPosition: Int
    let mixHostCase: Bool
    let fakeTTL: Int
    let dnsAddress: String
    let dnsPort: Int

    static let all: [VPNPreset] = [
        .init(id: "default", name: "Varsayilan", detail: "Cogu ag icin TLS ClientHello bolme", splitClientHello: true, splitPosition: 2, mixHostCase: false, fakeTTL: 0, dnsAddress: "", dnsPort: 53),
        .init(id: "aggressive", name: "Agresif", detail: "TLS bolme ve HTTP Host harf karistirma", splitClientHello: true, splitPosition: 2, mixHostCase: true, fakeTTL: 3, dnsAddress: "", dnsPort: 53),
        .init(id: "dns_redirect", name: "DNS Yonlendirme", detail: "Yandex DNS 77.88.8.8:1253", splitClientHello: true, splitPosition: 2, mixHostCase: false, fakeTTL: 0, dnsAddress: "77.88.8.8", dnsPort: 1253),
        .init(id: "turkey", name: "Turkiye", detail: "TLS bolme ve DNS yonlendirme", splitClientHello: true, splitPosition: 2, mixHostCase: false, fakeTTL: 5, dnsAddress: "77.88.8.8", dnsPort: 1253),
        .init(id: "turkey_so", name: "Turkcell Superonline", detail: "Parcalama olmadan uyumluluk", splitClientHello: false, splitPosition: 2, mixHostCase: false, fakeTTL: 3, dnsAddress: "", dnsPort: 53),
        .init(id: "russia", name: "Rusya", detail: "TLS bolme ve Host harf karistirma", splitClientHello: true, splitPosition: 2, mixHostCase: true, fakeTTL: 0, dnsAddress: "", dnsPort: 53),
        .init(id: "custom_host", name: "HTTP Host", detail: "Yalnizca HTTP Host basligini degistirir", splitClientHello: false, splitPosition: 2, mixHostCase: true, fakeTTL: 0, dnsAddress: "", dnsPort: 53)
    ]

    static func find(_ id: String) -> VPNPreset {
        all.first { $0.id == id } ?? all[0]
    }
}

struct VPNConfiguration {
    let presetKey: String
    let splitClientHello: Bool
    let splitPosition: Int
    let mixHostCase: Bool
    let fakeTTL: Int
    let dnsAddress: String
    let dnsPort: Int
    let adBlockEnabled: Bool
    let adBlockListURL: String

    var providerDictionary: [String: Any] {
        var values: [String: Any] = [
            "PresetKey": presetKey,
            "SplitClientHello": splitClientHello,
            "SplitPosition": splitPosition,
            "MixHostCase": mixHostCase,
            "FakeTtl": fakeTTL,
            "DnsRedirectPort": dnsPort,
            "AdBlockEnabled": adBlockEnabled,
            "AdBlockListUrl": adBlockListURL
        ]
        if !dnsAddress.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            values["DnsRedirectAddress"] = dnsAddress.trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return values
    }
}

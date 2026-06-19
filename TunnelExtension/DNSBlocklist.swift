import Foundation

final class DNSBlocklist {
    static let defaultListURL = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts"

    private let lock = NSLock()
    private var domainHashes: Set<UInt64> = []

    var count: Int {
        lock.lock()
        defer { lock.unlock() }
        return domainHashes.count
    }

    func load(enabled: Bool, listURL: String, completion: @escaping () -> Void) {
        guard enabled else {
            replace(with: [])
            completion()
            return
        }

        let cacheURL = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("blocklist-hosts.txt")
        let attributes = try? FileManager.default.attributesOfItem(atPath: cacheURL.path)
        let modified = attributes?[.modificationDate] as? Date
        let cacheIsFresh = modified.map { Date().timeIntervalSince($0) < 86_400 } ?? false

        if cacheIsFresh {
            parse(cacheURL, completion: completion)
            return
        }

        let source = URL(string: listURL) ?? URL(string: Self.defaultListURL)!
        var request = URLRequest(url: source)
        request.timeoutInterval = 12
        URLSession.shared.dataTask(with: request) { [weak self] data, _, error in
            if error == nil, let data {
                try? data.write(to: cacheURL, options: .atomic)
            }
            if FileManager.default.fileExists(atPath: cacheURL.path) {
                self?.parse(cacheURL, completion: completion)
            } else {
                self?.replace(with: [])
                completion()
            }
        }.resume()
    }

    func contains(_ domain: String?) -> Bool {
        guard var candidate = domain?.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: ".")),
              !candidate.isEmpty else { return false }
        lock.lock()
        defer { lock.unlock() }
        while true {
            if domainHashes.contains(Self.hash(candidate)) { return true }
            guard let dot = candidate.firstIndex(of: ".") else { return false }
            candidate = String(candidate[candidate.index(after: dot)...])
        }
    }

    private func parse(_ url: URL, completion: @escaping () -> Void) {
        DispatchQueue.global(qos: .utility).async { [weak self] in
            var hashes: Set<UInt64> = []
            if let content = try? String(contentsOf: url, encoding: .utf8) {
                content.enumerateLines { line, _ in
                    if let domain = Self.domain(from: line) {
                        hashes.insert(Self.hash(domain))
                    }
                }
            }
            self?.replace(with: hashes)
            completion()
        }
    }

    private func replace(with hashes: Set<UInt64>) {
        lock.lock()
        domainHashes = hashes
        lock.unlock()
    }

    private static func domain(from rawLine: String) -> String? {
        let uncommented = rawLine.split(separator: "#", maxSplits: 1, omittingEmptySubsequences: false)[0]
        let fields = uncommented.split(whereSeparator: { $0 == " " || $0 == "\t" })
        guard let field = fields.last else { return nil }
        let domain = field.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "."))
        guard !domain.isEmpty, domain != "localhost", IPv4Address(domain) == nil else { return nil }
        return domain
    }

    private static func hash(_ domain: String) -> UInt64 {
        var result: UInt64 = 14_695_981_039_346_656_037
        for byte in domain.utf8 {
            result ^= UInt64(byte)
            result = result &* 1_099_511_628_211
        }
        return result
    }
}

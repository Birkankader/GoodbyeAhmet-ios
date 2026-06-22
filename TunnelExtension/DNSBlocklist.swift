import Foundation

final class DNSBlocklist {
    static let defaultListURL = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts"

    private static let cacheLifetime: TimeInterval = 86_400
    private static let maximumDownloadBytes = 4 * 1_024 * 1_024
    private static let maximumDomainCount = 150_000

    private let lock = NSLock()
    private var domainHashes: Set<UInt64> = []
    private var loadGeneration: UInt64 = 0
    private lazy var session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 12
        configuration.timeoutIntervalForResource = 20
        configuration.httpMaximumConnectionsPerHost = 1
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        configuration.urlCache = nil
        return URLSession(configuration: configuration)
    }()

    deinit {
        session.invalidateAndCancel()
    }

    var count: Int {
        lock.lock()
        defer { lock.unlock() }
        return domainHashes.count
    }

    func load(enabled: Bool, listURL: String, completion: @escaping () -> Void) {
        let generation = beginLoad()
        guard enabled else {
            replace(with: [], generation: generation)
            completion()
            return
        }

        guard let source = Self.validatedHTTPSURL(listURL)
            ?? URL(string: Self.defaultListURL) else {
            replace(with: [], generation: generation)
            completion()
            return
        }
        let cacheKey = String(format: "%016llx", Self.hash(source.absoluteString))
        let cacheURL = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("blocklist-\(cacheKey).txt")
        let attributes = try? FileManager.default.attributesOfItem(atPath: cacheURL.path)
        let modified = attributes?[.modificationDate] as? Date
        let cacheIsFresh = modified.map { Date().timeIntervalSince($0) < Self.cacheLifetime } ?? false

        if cacheIsFresh {
            parseAndApply(cacheURL, generation: generation, completion: completion)
            return
        }

        var request = URLRequest(url: source)
        request.timeoutInterval = 12

        session.downloadTask(with: request) { [weak self] temporaryURL, response, error in
            guard let self else {
                completion()
                return
            }
            guard self.isCurrentLoad(generation) else {
                completion()
                return
            }

            guard error == nil,
                  let temporaryURL,
                  let response = response as? HTTPURLResponse,
                  response.statusCode == 200,
                  response.url?.scheme?.lowercased() == "https",
                  response.expectedContentLength <= Int64(Self.maximumDownloadBytes) || response.expectedContentLength < 0,
                  Self.fileSize(at: temporaryURL) <= Self.maximumDownloadBytes else {
                self.loadCachedFallback(cacheURL, generation: generation, completion: completion)
                return
            }

            self.parse(temporaryURL) { hashes in
                guard self.isCurrentLoad(generation) else {
                    completion()
                    return
                }
                guard !hashes.isEmpty else {
                    self.loadCachedFallback(cacheURL, generation: generation, completion: completion)
                    return
                }

                self.replace(with: hashes, generation: generation)
                self.replaceCache(at: cacheURL, with: temporaryURL)
                completion()
            }
        }.resume()
    }

    func contains(_ domain: String?) -> Bool {
        guard var candidate = domain?.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: ".")),
              Self.isValidDomain(candidate) else { return false }
        lock.lock()
        defer { lock.unlock() }
        while true {
            if domainHashes.contains(Self.hash(candidate)) { return true }
            guard let dot = candidate.firstIndex(of: ".") else { return false }
            candidate = String(candidate[candidate.index(after: dot)...])
        }
    }

    private func loadCachedFallback(
        _ cacheURL: URL,
        generation: UInt64,
        completion: @escaping () -> Void
    ) {
        guard isCurrentLoad(generation) else {
            completion()
            return
        }
        guard FileManager.default.fileExists(atPath: cacheURL.path),
              Self.fileSize(at: cacheURL) <= Self.maximumDownloadBytes else {
            replace(with: [], generation: generation)
            completion()
            return
        }
        parseAndApply(cacheURL, generation: generation, completion: completion)
    }

    private func parseAndApply(
        _ url: URL,
        generation: UInt64,
        completion: @escaping () -> Void
    ) {
        parse(url) { [weak self] hashes in
            self?.replace(with: hashes, generation: generation)
            completion()
        }
    }

    private func parse(_ url: URL, completion: @escaping (Set<UInt64>) -> Void) {
        DispatchQueue.global(qos: .utility).async {
            var hashes: Set<UInt64> = []
            if let content = try? String(contentsOf: url, encoding: .utf8) {
                content.enumerateLines { line, stop in
                    if let domain = Self.domain(from: line) {
                        hashes.insert(Self.hash(domain))
                    }
                    if hashes.count >= Self.maximumDomainCount {
                        stop = true
                    }
                }
            }
            completion(hashes)
        }
    }

    private func replaceCache(at cacheURL: URL, with downloadedURL: URL) {
        let fileManager = FileManager.default
        let stagingURL = cacheURL.appendingPathExtension("new")
        try? fileManager.removeItem(at: stagingURL)
        do {
            try fileManager.copyItem(at: downloadedURL, to: stagingURL)
            try? fileManager.removeItem(at: cacheURL)
            try fileManager.moveItem(at: stagingURL, to: cacheURL)
        } catch {
            try? fileManager.removeItem(at: stagingURL)
        }
    }

    private func beginLoad() -> UInt64 {
        lock.lock()
        loadGeneration &+= 1
        let generation = loadGeneration
        lock.unlock()
        return generation
    }

    private func isCurrentLoad(_ generation: UInt64) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        return loadGeneration == generation
    }

    private func replace(with hashes: Set<UInt64>, generation: UInt64) {
        lock.lock()
        if loadGeneration == generation {
            domainHashes = hashes
        }
        lock.unlock()
    }

    private static func validatedHTTPSURL(_ rawValue: String) -> URL? {
        guard rawValue.utf8.count <= 2_048,
              let url = URL(string: rawValue),
              url.scheme?.lowercased() == "https",
              url.host != nil,
              url.user == nil,
              url.password == nil else { return nil }
        return url
    }

    private static func fileSize(at url: URL) -> Int {
        let attributes = try? FileManager.default.attributesOfItem(atPath: url.path)
        return (attributes?[.size] as? NSNumber)?.intValue ?? Int.max
    }

    private static func domain(from rawLine: String) -> String? {
        let uncommented = rawLine.split(separator: "#", maxSplits: 1, omittingEmptySubsequences: false)[0]
        let fields = uncommented.split(whereSeparator: { $0 == " " || $0 == "\t" })
        guard let field = fields.last else { return nil }
        let domain = field.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "."))
        guard domain != "localhost", IPv4Address(domain) == nil, isValidDomain(domain) else { return nil }
        return domain
    }

    private static func isValidDomain(_ domain: String) -> Bool {
        guard !domain.isEmpty, domain.utf8.count <= 253 else { return false }
        let labels = domain.split(separator: ".", omittingEmptySubsequences: false)
        guard !labels.isEmpty else { return false }
        return labels.allSatisfy { label in
            guard !label.isEmpty, label.utf8.count <= 63 else { return false }
            return label.utf8.allSatisfy { byte in
                (byte >= 48 && byte <= 57) ||
                    (byte >= 97 && byte <= 122) ||
                    byte == 45 || byte == 95
            }
        }
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

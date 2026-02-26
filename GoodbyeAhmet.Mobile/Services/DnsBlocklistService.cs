using System.Collections.Concurrent;

namespace GoodbyeAhmet.Mobile.Services;

/// <summary>
/// Manages a set of blocked ad/tracker domains.
/// Domains are stored in a HashSet for O(1) lookup.
/// Supports loading from a raw-text URL (one domain per line, hosts-file format).
/// Persists the downloaded blocklist to local storage so it survives app restarts.
/// </summary>
public sealed class DnsBlocklistService
{
    private volatile HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _enabled;
    private int _blockedCount;

    private static string CacheFilePath =>
        Path.Combine(FileSystem.AppDataDirectory, "blocklist_cache.txt");

    /// <summary>Number of domains currently loaded.</summary>
    public int DomainCount => _blockedDomains.Count;

    /// <summary>Number of DNS queries blocked since VPN start.</summary>
    public int BlockedQueryCount => _blockedCount;

    /// <summary>Whether ad-blocking is active.</summary>
    public bool IsEnabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Returns true if the domain (or any parent domain) is in the blocklist.
    /// E.g. "ads.example.com" matches if "example.com" or "ads.example.com" is blocked.
    /// </summary>
    public bool IsBlocked(string domain)
    {
        if (!_enabled || string.IsNullOrEmpty(domain))
            return false;

        // Normalize
        domain = domain.TrimEnd('.').ToLowerInvariant();

        // Check exact match + parent domains
        var set = _blockedDomains;
        while (!string.IsNullOrEmpty(domain))
        {
            if (set.Contains(domain))
            {
                Interlocked.Increment(ref _blockedCount);
                return true;
            }

            // Move up one level: "a.b.c" → "b.c"
            int dot = domain.IndexOf('.');
            if (dot < 0) break;
            domain = domain[(dot + 1)..];
        }

        return false;
    }

    /// <summary>Reset the per-session blocked query counter.</summary>
    public void ResetCounter() => Interlocked.Exchange(ref _blockedCount, 0);

    /// <summary>
    /// Loads domains from a blocklist URL (e.g. StevenBlack hosts, AdGuard, etc.).
    /// Supports hosts-file format (127.0.0.1 domain) and plain domain-per-line.
    /// The downloaded text is cached to local storage for offline reuse.
    /// </summary>
    public async Task<int> LoadFromUrlAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var text = await http.GetStringAsync(url, ct);

            // Save to local cache so the blocklist survives app restarts
            await SaveCacheAsync(text);

            return ParseAndLoad(text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Blocklist] Failed to load '{url}': {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Loads the blocklist from local cache file (if it exists).
    /// Called on app startup to restore the previously downloaded list.
    /// </summary>
    public async Task<int> LoadFromCacheAsync()
    {
        try
        {
            if (!File.Exists(CacheFilePath))
                return 0;

            var text = await File.ReadAllTextAsync(CacheFilePath);
            var count = ParseAndLoad(text);
            System.Diagnostics.Debug.WriteLine($"[Blocklist] Restored {count} domains from cache");
            return count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Blocklist] Failed to load cache: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Whether a cached blocklist file exists on disk.</summary>
    public bool HasCache => File.Exists(CacheFilePath);

    private static async Task SaveCacheAsync(string text)
    {
        try
        {
            await File.WriteAllTextAsync(CacheFilePath, text);
            System.Diagnostics.Debug.WriteLine("[Blocklist] Saved cache to disk");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Blocklist] Failed to save cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads domains from raw text content (hosts-file or plain list).
    /// </summary>
    public int LoadFromText(string text) => ParseAndLoad(text);

    /// <summary>
    /// Adds a single domain to the blocklist.
    /// </summary>
    public void AddDomain(string domain)
    {
        domain = domain.Trim().TrimEnd('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(domain))
        {
            // Create a new set (copy-on-write for thread-safety)
            var newSet = new HashSet<string>(_blockedDomains, StringComparer.OrdinalIgnoreCase)
            {
                domain
            };
            _blockedDomains = newSet;
        }
    }

    /// <summary>Clears all loaded domains.</summary>
    public void Clear()
    {
        _blockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ResetCounter();
    }

    /// <summary>
    /// Loads the built-in default blocklist URLs concurrently.
    /// </summary>
    public async Task LoadDefaultListsAsync(CancellationToken ct = default)
    {
        // StevenBlack unified hosts — ~80k domains of ads+malware
        const string stevenBlack = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts";

        var count = await LoadFromUrlAsync(stevenBlack, ct);
        System.Diagnostics.Debug.WriteLine($"[Blocklist] Loaded {count} domains from StevenBlack hosts");
    }

    // ── Internals ───────────────────────────────────────────────

    private int ParseAndLoad(string text)
    {
        var newSet = new HashSet<string>(_blockedDomains, StringComparer.OrdinalIgnoreCase);
        int added = 0;

        foreach (var rawLine in text.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty || line[0] == '#') continue;

            // hosts-file format: "0.0.0.0 domain" or "127.0.0.1 domain"
            var lineStr = line.ToString();
            string domain;

            if (lineStr.StartsWith("0.0.0.0 ") || lineStr.StartsWith("127.0.0.1 "))
            {
                var parts = lineStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                domain = parts[1];
            }
            else if (lineStr.Contains(' ') || lineStr.Contains('\t'))
            {
                // Other hosts-file variants
                var parts = lineStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                domain = parts[1];
            }
            else
            {
                // Plain domain per line
                domain = lineStr;
            }

            // Strip inline comments
            int commentIdx = domain.IndexOf('#');
            if (commentIdx >= 0) domain = domain[..commentIdx];

            domain = domain.Trim().TrimEnd('.').ToLowerInvariant();

            // Skip localhost entries and IPs
            if (string.IsNullOrEmpty(domain)) continue;
            if (domain == "localhost") continue;
            if (domain.StartsWith("0.0.0.0") || domain.StartsWith("127.")) continue;
            if (domain.All(c => char.IsDigit(c) || c == '.')) continue; // Skip IPs

            if (newSet.Add(domain)) added++;
        }

        _blockedDomains = newSet;
        return added;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GoodbyeAhmetWPF.Services
{
    /// <summary>
    /// Manages a set of blocked ad/tracker domains via a local hosts-file blocklist.
    /// On Windows the blocking works by writing a hosts file that maps ad domains to 0.0.0.0.
    /// </summary>
    public sealed class DnsBlocklistService
    {
        public const string DefaultBlocklistUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts";

        // Defense-in-depth limits.
        private const int HttpTimeoutSeconds = 30;
        private const long MaxDownloadBytes = 50L * 1024 * 1024; // 50 MB

        private volatile HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);
        private volatile bool _enabled;

        private static string CacheFilePath => AppPaths.BlocklistCachePath;

        private static string CustomHostsPath => AppPaths.CustomHostsPath;

        /// <summary>Number of domains currently loaded.</summary>
        public int DomainCount => _blockedDomains.Count;

        /// <summary>Whether ad-blocking is active.</summary>
        public bool IsEnabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>
        /// Returns true if the domain (or any parent domain) is in the blocklist.
        /// </summary>
        public bool IsBlocked(string domain)
        {
            if (!_enabled || string.IsNullOrEmpty(domain))
                return false;

            domain = domain.TrimEnd('.').ToLowerInvariant();

            var set = _blockedDomains;
            while (!string.IsNullOrEmpty(domain))
            {
                if (set.Contains(domain))
                    return true;

                int dot = domain.IndexOf('.');
                if (dot < 0) break;
                domain = domain[(dot + 1)..];
            }

            return false;
        }

        /// <summary>
        /// Loads domains from a blocklist URL.
        /// </summary>
        public async Task<int> LoadFromUrlAsync(string url, CancellationToken ct = default)
        {
            // Enforce HTTPS to prevent MITM attackers from injecting domains
            // (which would let them block arbitrary sites for the user).
            if (!InputValidator.IsHttpsUrl(url, out var urlError))
            {
                Logger.Error($"[Blocklist] Rejected URL '{url}': {urlError}");
                return 0;
            }

            try
            {
                using var http = HttpClientFactory.Create(TimeSpan.FromSeconds(HttpTimeoutSeconds));
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength is long len && len > MaxDownloadBytes)
                {
                    Logger.Error($"[Blocklist] Refusing to download '{url}': size {len} exceeds limit {MaxDownloadBytes}.");
                    return 0;
                }

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var ms = new MemoryStream();
                var buf = new byte[81920];
                long total = 0;
                int read;
                while ((read = await stream.ReadAsync(buf.AsMemory(), ct)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                    {
                        Logger.Error($"[Blocklist] Aborted download from '{url}': exceeded size limit while streaming.");
                        return 0;
                    }
                    ms.Write(buf, 0, read);
                }

                var text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                await SaveCacheAsync(text);
                var count = ParseAndLoad(text);
                WriteCustomHostsFile();
                Logger.Info($"[Blocklist] Loaded {count} new domains from '{url}'. Total: {DomainCount}");
                return count;
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"[Blocklist] Download cancelled: {url}");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error($"[Blocklist] Failed to load '{url}'.", ex);
                return 0;
            }
        }

        /// <summary>
        /// Loads the blocklist from local cache file.
        /// </summary>
        public async Task<int> LoadFromCacheAsync()
        {
            try
            {
                if (!File.Exists(CacheFilePath))
                    return 0;

                var text = await File.ReadAllTextAsync(CacheFilePath);
                var count = ParseAndLoad(text);
                Logger.Info($"[Blocklist] Restored {count} domains from cache");
                return count;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Blocklist] Failed to load cache.", ex);
                return 0;
            }
        }

        /// <summary>Whether a cached blocklist file exists on disk.</summary>
        public bool HasCache => File.Exists(CacheFilePath);

        /// <summary>
        /// Ensures a blocklist is available.
        /// </summary>
        public async Task<int> EnsureLoadedAsync(string? url = null, CancellationToken ct = default)
        {
            if (DomainCount > 0)
                return DomainCount;

            if (HasCache)
            {
                await LoadFromCacheAsync();
                if (DomainCount > 0)
                    return DomainCount;
            }

            var effectiveUrl = string.IsNullOrWhiteSpace(url)
                ? DefaultBlocklistUrl
                : url.Trim();

            await LoadFromUrlAsync(effectiveUrl, ct);
            return DomainCount;
        }

        /// <summary>Clears all loaded domains and removes hosts file.</summary>
        public void Clear()
        {
            _blockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(CustomHostsPath)) File.Delete(CustomHostsPath); } catch { }
        }

        /// <summary>Deletes the on-disk cache file (used by the "Clear Cache" UI button).</summary>
        public void ClearCache()
        {
            try
            {
                if (File.Exists(CacheFilePath))
                {
                    File.Delete(CacheFilePath);
                    Logger.Info($"[Blocklist] Cache deleted: {CacheFilePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[Blocklist] Failed to delete cache.", ex);
            }
        }

        /// <summary>
        /// Writes the blocked domains to a custom hosts file that GoodbyeDPI
        /// can use with --blacklist parameter.
        /// </summary>
        private void WriteCustomHostsFile()
        {
            try
            {
                var dir = Path.GetDirectoryName(CustomHostsPath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllLines(CustomHostsPath, _blockedDomains.Order());
                Logger.Info($"[Blocklist] Wrote {_blockedDomains.Count} domains to {CustomHostsPath}");
            }
            catch (Exception ex)
            {
                Logger.Warn("[Blocklist] Failed to write hosts file.", ex);
            }
        }

        private static async Task SaveCacheAsync(string text)
        {
            try
            {
                var dir = Path.GetDirectoryName(CacheFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(CacheFilePath, text);
            }
            catch (Exception ex)
            {
                Logger.Warn("[Blocklist] Failed to save cache.", ex);
            }
        }

        private int ParseAndLoad(string text)
        {
            var newSet = new HashSet<string>(_blockedDomains, StringComparer.OrdinalIgnoreCase);
            int added = 0;

            foreach (var rawLine in text.AsSpan().EnumerateLines())
            {
                var line = rawLine.Trim();
                if (line.IsEmpty || line[0] == '#') continue;

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
                    var parts = lineStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    domain = parts[1];
                }
                else
                {
                    domain = lineStr;
                }

                int commentIdx = domain.IndexOf('#');
                if (commentIdx >= 0) domain = domain[..commentIdx];

                domain = domain.Trim().TrimEnd('.').ToLowerInvariant();

                if (string.IsNullOrEmpty(domain)) continue;
                if (domain == "localhost") continue;
                if (domain.StartsWith("0.0.0.0") || domain.StartsWith("127.")) continue;
                if (domain.All(c => char.IsDigit(c) || c == '.')) continue;

                if (newSet.Add(domain)) added++;
            }

            _blockedDomains = newSet;
            return added;
        }
    }
}

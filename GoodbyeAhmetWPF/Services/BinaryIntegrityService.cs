using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace GoodbyeAhmetWPF.Services
{
    /// <summary>
    /// Verifies the integrity of bundled GoodbyeDPI binaries against a
    /// known-good SHA-256 list. Prevents launching a tampered/replaced
    /// goodbyedpi.exe from the essentials folder.
    ///
    /// The expected hashes are read from "essentials/goodbyedpi/hashes.txt"
    /// (one "<hex-hash>  <relative-path>" per line, '#' comments allowed).
    /// If the hash file is missing, integrity check is skipped and a
    /// warning is logged (so OSS users can still build without the file).
    /// </summary>
    public static class BinaryIntegrityService
    {
        private static readonly object _gate = new();
        private static Dictionary<string, string>? _expected; // path-relative -> sha256 hex (lowercase)
        private static readonly HashSet<string> _verifiedFiles = new(StringComparer.OrdinalIgnoreCase);

        private static string HashFilePath =>
            Path.Combine(AppPaths.AppBaseDirectory, "essentials", "goodbyedpi", "hashes.txt");

        public static bool IsKnown(string filePath)
        {
            EnsureLoaded();
            if (_expected == null || _expected.Count == 0) return false;
            var rel = MakeRelative(filePath);
            return _expected.ContainsKey(rel);
        }

        /// <summary>
        /// Verifies the file matches the expected hash. Returns true when:
        ///   * The file matches its expected hash, OR
        ///   * No hash list is available (logs a warning — fail-open by design
        ///     to preserve usability for users building from source).
        /// Returns false when an expected hash exists but does not match.
        /// </summary>
        public static bool Verify(string filePath)
        {
            EnsureLoaded();

            if (_expected == null || _expected.Count == 0)
            {
                Logger.Warn($"Binary integrity check skipped (no hashes.txt) for: {filePath}");
                return true;
            }

            var rel = MakeRelative(filePath);
            if (!_expected.TryGetValue(rel, out var expected))
            {
                Logger.Warn($"No expected hash entry for '{rel}'. Skipping check.");
                return true;
            }

            // Cache: same file already verified this process.
            if (_verifiedFiles.Contains(rel)) return true;

            string actual;
            try { actual = ComputeSha256(filePath); }
            catch (Exception ex)
            {
                Logger.Error($"Failed to hash '{filePath}'.", ex);
                return false;
            }

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error(
                    $"INTEGRITY FAILURE: '{rel}' hash mismatch. Expected={expected}, Actual={actual}");
                return false;
            }

            _verifiedFiles.Add(rel);
            Logger.Info($"Integrity OK: {rel} ({actual[..12]}...)");
            return true;
        }

        private static void EnsureLoaded()
        {
            if (_expected != null) return;
            lock (_gate)
            {
                if (_expected != null) return;
                _expected = LoadHashes();
            }
        }

        private static Dictionary<string, string> LoadHashes()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(HashFilePath)) return dict;
                foreach (var raw in File.ReadAllLines(HashFilePath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2) continue;
                    var hash = parts[0].Trim().ToLowerInvariant();
                    var rel = NormalizeRelative(parts[1].Trim().TrimStart('*'));
                    if (hash.Length == 64) dict[rel] = hash;
                }
                Logger.Info($"Loaded {dict.Count} expected hashes from {HashFilePath}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load hash file '{HashFilePath}'.", ex);
            }
            return dict;
        }

        private static string MakeRelative(string fullPath)
        {
            try
            {
                var baseDir = Path.Combine(AppPaths.AppBaseDirectory, "essentials", "goodbyedpi");
                if (fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeRelative(fullPath[baseDir.Length..]);
                }
                var name = Path.GetFileName(fullPath);
                return NormalizeRelative(name);
            }
            catch { return fullPath; }
        }

        private static string NormalizeRelative(string p)
            => p.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            var hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

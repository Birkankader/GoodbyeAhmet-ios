using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GoodbyeAhmetWPF.Services
{
    /// <summary>
    /// Checks GitHub Releases for a newer version of the application.
    /// Network failures are non-fatal: we log and return null.
    /// </summary>
    public static class UpdateService
    {
        // Public, read-only Releases endpoint. Keep change in one place.
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/yourusername/GoodbyeAhmet/releases/latest";

        public sealed class UpdateInfo
        {
            public string Version { get; init; } = "";
            public string HtmlUrl { get; init; } = "";
            public string Notes { get; init; } = "";
        }

        public static Version CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

        /// <summary>
        /// Returns update info if a newer version is available, otherwise null.
        /// </summary>
        public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
        {
            try
            {
                using var http = HttpClientFactory.Create(TimeSpan.FromSeconds(10));
                using var resp = await http.GetAsync(LatestReleaseUrl, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    Logger.Info($"[Update] GitHub returned {(int)resp.StatusCode}, skipping.");
                    return null;
                }

                var json = await resp.Content.ReadAsStringAsync(ct);
                var release = JsonSerializer.Deserialize<GitHubRelease>(json);
                if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                    return null;

                var tag = release.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(tag, out var latest))
                {
                    Logger.Warn($"[Update] Could not parse tag '{release.TagName}' as Version.");
                    return null;
                }

                if (latest <= CurrentVersion)
                {
                    Logger.Info($"[Update] Up to date (current={CurrentVersion}, latest={latest}).");
                    return null;
                }

                Logger.Info($"[Update] New version available: {latest}");
                return new UpdateInfo
                {
                    Version = latest.ToString(),
                    HtmlUrl = release.HtmlUrl ?? "",
                    Notes = release.Body ?? "",
                };
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                Logger.Warn("[Update] Update check failed.", ex);
                return null;
            }
        }

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")] public string? TagName { get; set; }
            [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
            [JsonPropertyName("body")] public string? Body { get; set; }
        }
    }
}

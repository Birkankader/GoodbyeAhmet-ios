using GoodbyeAhmet.Mobile.Models;

namespace GoodbyeAhmet.Mobile.Services;

/// <summary>
/// Provides the built-in DPI bypass presets.
/// Mirrors the desktop presets translated into Android-compatible
/// packet manipulation parameters.
/// </summary>
public sealed class PresetService
{
    /// <summary>
    /// Returns all available bypass presets.
    /// The first entry is treated as the default.
    /// </summary>
    public IReadOnlyList<BypassPreset> GetPresets() => Presets;

    /// <summary>
    /// Looks up a preset by its <see cref="BypassPreset.Key"/>.
    /// Falls back to the first preset if the key is not found.
    /// </summary>
    public BypassPreset GetByKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return Presets[0];

        return Presets.FirstOrDefault(p => p.Key == key) ?? Presets[0];
    }

    // ── Built-in presets ────────────────────────────────────────

    private static readonly IReadOnlyList<BypassPreset> Presets = new List<BypassPreset>
    {
        new()
        {
            Key              = "default",
            DisplayName      = "Default (Split Hello)",
            Description      = "Splits TLS ClientHello at byte 2. Works against most basic DPI.",
            SplitClientHello = true,
            SplitPosition    = 2,
            MixHostCase      = false,
            FakeTtl          = 0,
        },
        new()
        {
            Key              = "aggressive",
            DisplayName      = "Aggressive Fragmentation",
            Description      = "Splits at byte 2 + mixes HTTP Host case + fake TTL.",
            SplitClientHello = true,
            SplitPosition    = 2,
            MixHostCase      = true,
            FakeTtl          = 3,
        },
        new()
        {
            Key              = "dns_redirect",
            DisplayName      = "DNS Redirect (Yandex)",
            Description      = "Redirects DNS to 77.88.8.8:1253 to bypass ISP DNS hijacking.",
            SplitClientHello = true,
            SplitPosition    = 2,
            MixHostCase      = false,
            FakeTtl          = 0,
            DnsRedirectAddress = "77.88.8.8",
            DnsRedirectPort    = 1253,
        },
        new()
        {
            Key              = "turkey",
            DisplayName      = "Turkey (DNS + TTL)",
            Description      = "Optimized for Turkish ISPs: split + TTL 5 + DNS redirect.",
            SplitClientHello = true,
            SplitPosition    = 2,
            MixHostCase      = false,
            FakeTtl          = 5,
            DnsRedirectAddress = "77.88.8.8",
            DnsRedirectPort    = 1253,
        },
        new()
        {
            Key              = "turkey_so",
            DisplayName      = "Turkey Superonline",
            Description      = "Superonline-specific: TTL 3, no fragmentation.",
            SplitClientHello = false,
            MixHostCase      = false,
            FakeTtl          = 3,
        },
        new()
        {
            Key              = "russia",
            DisplayName      = "Russia",
            Description      = "General Russian ISP bypass via fragmentation.",
            SplitClientHello = true,
            SplitPosition    = 2,
            MixHostCase      = true,
            FakeTtl          = 0,
        },
        new()
        {
            Key              = "custom_host",
            DisplayName      = "Custom HTTP Headers",
            Description      = "Mixes HTTP Host header case only. No TLS manipulation.",
            SplitClientHello = false,
            MixHostCase      = true,
            FakeTtl          = 0,
        },
    };
}

namespace GoodbyeAhmet.Mobile.Models;

/// <summary>
/// Represents a DPI bypass configuration preset.
/// Each preset maps to a different packet-manipulation strategy
/// that will be applied inside the VPN packet-processing loop.
/// </summary>
public sealed class BypassPreset
{
    /// <summary>Unique key used for persistence (Preferences).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-readable name shown in the UI picker.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Short description of what this preset does.</summary>
    public string Description { get; set; } = string.Empty;

    // ── Packet manipulation parameters ──────────────────────────

    /// <summary>
    /// Whether to split the TLS ClientHello across multiple TCP segments.
    /// This is the most common DPI bypass technique.
    /// </summary>
    public bool SplitClientHello { get; set; }

    /// <summary>
    /// Byte offset at which to split the first TLS record.
    /// Typical values: 2, 5, or the SNI field offset (~40-50).
    /// Only used when <see cref="SplitClientHello"/> is true.
    /// </summary>
    public int SplitPosition { get; set; } = 2;

    /// <summary>
    /// Whether to manipulate the HTTP Host header
    /// (e.g., mix case: "Host" → "hOsT").
    /// </summary>
    public bool MixHostCase { get; set; }

    /// <summary>
    /// TTL value to set on the first outgoing packet of each connection.
    /// A low fake-TTL can fool stateful DPI boxes while the packet expires
    /// before reaching the real server. 0 = do not modify.
    /// </summary>
    public int FakeTtl { get; set; }

    /// <summary>
    /// Custom DNS-over-UDP server to redirect DNS queries to,
    /// bypassing ISP DNS hijacking. Null = use system DNS.
    /// </summary>
    public string? DnsRedirectAddress { get; set; }

    /// <summary>Port for the custom DNS server. Default 53.</summary>
    public int DnsRedirectPort { get; set; } = 53;

    public override string ToString() => DisplayName;
}

namespace GoodbyeAhmet.Mobile.Services;

/// <summary>
/// Thin wrapper around MAUI <see cref="Preferences"/> for persisting
/// user settings (selected preset key, auto-start toggle, advanced params, etc.).
/// </summary>
public sealed class SettingsService
{
    private const string KeySelectedPreset = "selected_preset";
    private const string KeyStartOnBoot = "start_on_boot";
    private const string KeyActivateOnStart = "activate_on_start";
    private const string KeyLanguage = "language";

    // Advanced DPI parameters
    private const string KeyTtl = "ttl";
    private const string KeySplitPosition = "split_position";
    private const string KeySplitClientHello = "split_client_hello";
    private const string KeyMixHostCase = "mix_host_case";
    private const string KeyDnsV4Address = "dns_v4_address";
    private const string KeyDnsV4Port = "dns_v4_port";
    private const string KeyDnsV6Address = "dns_v6_address";
    private const string KeyDnsV6Port = "dns_v6_port";
    private const string KeyAdBlockEnabled = "adblock_enabled";
    private const string KeyAdBlockListUrl = "adblock_list_url";

    private const string DefaultBlocklistUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts";

    // ── Selected Preset ─────────────────────────────────────────

    public string? SelectedPresetKey
    {
        get => Preferences.Default.Get<string?>(KeySelectedPreset, null);
        set => Preferences.Default.Set(KeySelectedPreset, value ?? string.Empty);
    }

    // ── Start on Boot ───────────────────────────────────────────

    public bool StartOnBoot
    {
        get => Preferences.Default.Get(KeyStartOnBoot, false);
        set => Preferences.Default.Set(KeyStartOnBoot, value);
    }

    // ── Activate on Start ───────────────────────────────────────

    public bool ActivateOnStart
    {
        get => Preferences.Default.Get(KeyActivateOnStart, false);
        set => Preferences.Default.Set(KeyActivateOnStart, value);
    }

    // ── Language ────────────────────────────────────────────────

    public string Language
    {
        get => Preferences.Default.Get(KeyLanguage, "en-US");
        set => Preferences.Default.Set(KeyLanguage, value ?? "en-US");
    }

    // ── Advanced Parameters ─────────────────────────────────────

    public string Ttl
    {
        get => Preferences.Default.Get(KeyTtl, "0");
        set => Preferences.Default.Set(KeyTtl, value ?? "0");
    }

    public string SplitPosition
    {
        get => Preferences.Default.Get(KeySplitPosition, "2");
        set => Preferences.Default.Set(KeySplitPosition, value ?? "2");
    }

    public bool SplitClientHello
    {
        get => Preferences.Default.Get(KeySplitClientHello, true);
        set => Preferences.Default.Set(KeySplitClientHello, value);
    }

    public bool MixHostCase
    {
        get => Preferences.Default.Get(KeyMixHostCase, false);
        set => Preferences.Default.Set(KeyMixHostCase, value);
    }

    public string DnsV4Address
    {
        get => Preferences.Default.Get(KeyDnsV4Address, "");
        set => Preferences.Default.Set(KeyDnsV4Address, value ?? "");
    }

    public string DnsV4Port
    {
        get => Preferences.Default.Get(KeyDnsV4Port, "");
        set => Preferences.Default.Set(KeyDnsV4Port, value ?? "");
    }

    public string DnsV6Address
    {
        get => Preferences.Default.Get(KeyDnsV6Address, "");
        set => Preferences.Default.Set(KeyDnsV6Address, value ?? "");
    }

    public string DnsV6Port
    {
        get => Preferences.Default.Get(KeyDnsV6Port, "");
        set => Preferences.Default.Set(KeyDnsV6Port, value ?? "");
    }

    // ── Reset ───────────────────────────────────────────────────

    public bool AdBlockEnabled
    {
        get => Preferences.Default.Get(KeyAdBlockEnabled, false);
        set => Preferences.Default.Set(KeyAdBlockEnabled, value);
    }

    public string AdBlockListUrl
    {
        get => Preferences.Default.Get(KeyAdBlockListUrl, DefaultBlocklistUrl);
        set => Preferences.Default.Set(KeyAdBlockListUrl, value ?? DefaultBlocklistUrl);
    }

    public void ResetAll()
    {
        Preferences.Default.Clear();
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GoodbyeAhmet.Mobile.Models;
using GoodbyeAhmet.Mobile.Services;

namespace GoodbyeAhmet.Mobile.ViewModels;

/// <summary>
/// ViewModel for SettingsPage — mirrors the WPF SettingsWindow:
/// language, preset selection, advanced DPI parameters, and toggles.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly PresetService _presetService;
    private readonly SettingsService _settingsService;

    private readonly DnsBlocklistService _blocklistService;
    private readonly LocalizationService _loc;

    public SettingsViewModel(PresetService presetService, SettingsService settingsService, DnsBlocklistService blocklistService)
    {
        _presetService = presetService;
        _settingsService = settingsService;
        _blocklistService = blocklistService;
        _loc = LocalizationService.Instance;

        // ── Languages ───────────────────────────────
        foreach (var lang in AvailableLanguagesList)
            Languages.Add(lang);

        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == _settingsService.Language)
                            ?? Languages.First();

        // ── Presets ─────────────────────────────────
        foreach (var p in _presetService.GetPresets())
            Presets.Add(p);

        _selectedPreset = _presetService.GetByKey(_settingsService.SelectedPresetKey);

        // ── Advanced parameters ─────────────────────
        _ttl = _settingsService.Ttl;
        _splitPosition = _settingsService.SplitPosition;
        _splitClientHello = _settingsService.SplitClientHello;
        _mixHostCase = _settingsService.MixHostCase;
        _dnsV4Address = _settingsService.DnsV4Address;
        _dnsV4Port = _settingsService.DnsV4Port;
        _dnsV6Address = _settingsService.DnsV6Address;
        _dnsV6Port = _settingsService.DnsV6Port;

        // ── Ad-Block ────────────────────────────────
        _adBlockEnabled = _settingsService.AdBlockEnabled;
        _adBlockListUrl = _settingsService.AdBlockListUrl;
        _adBlockDomainCount = _blocklistService.DomainCount;
        _adBlockStatus = _blocklistService.DomainCount > 0
            ? _loc.Get("DomainsLoaded", _blocklistService.DomainCount.ToString("N0"))
            : _loc["NoBlocklistLoaded"];

        // ── Toggles ─────────────────────────────────
        _startOnBoot = _settingsService.StartOnBoot;
        _activateOnStart = _settingsService.ActivateOnStart;
    }

    // ── Language ────────────────────────────────────────────────

    public ObservableCollection<LanguageOption> Languages { get; } = new();

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    // ── Preset ──────────────────────────────────────────────────

    public ObservableCollection<BypassPreset> Presets { get; } = new();

    [ObservableProperty]
    private BypassPreset? _selectedPreset;

    partial void OnSelectedPresetChanged(BypassPreset? value)
    {
        if (value is null) return;

        // Apply preset values to the advanced fields
        Ttl = value.FakeTtl.ToString();
        SplitPosition = value.SplitPosition.ToString();
        SplitClientHello = value.SplitClientHello;
        MixHostCase = value.MixHostCase;
        DnsV4Address = value.DnsRedirectAddress ?? "";
        DnsV4Port = value.DnsRedirectPort > 0 ? value.DnsRedirectPort.ToString() : "";
    }

    // ── Advanced Parameters ─────────────────────────────────────

    [ObservableProperty]
    private string _ttl;

    [ObservableProperty]
    private string _splitPosition;

    [ObservableProperty]
    private bool _splitClientHello;

    [ObservableProperty]
    private bool _mixHostCase;

    [ObservableProperty]
    private string _dnsV4Address;

    [ObservableProperty]
    private string _dnsV4Port;

    [ObservableProperty]
    private string _dnsV6Address;

    [ObservableProperty]
    private string _dnsV6Port;

    // ── Ad-Block ────────────────────────────────────────────────

    [ObservableProperty]
    private bool _adBlockEnabled;

    [ObservableProperty]
    private string _adBlockListUrl;

    [ObservableProperty]
    private int _adBlockDomainCount;

    [ObservableProperty]
    private string _adBlockStatus;

    [ObservableProperty]
    private bool _isLoadingBlocklist;

    // ── Toggles ─────────────────────────────────────────────────

    [ObservableProperty]
    private bool _startOnBoot;

    [ObservableProperty]
    private bool _activateOnStart;

    // ── Commands ────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Persist everything
        _settingsService.Language = SelectedLanguage?.Code ?? "en-US";
        _settingsService.SelectedPresetKey = SelectedPreset?.Key;
        _settingsService.Ttl = Ttl;
        _settingsService.SplitPosition = SplitPosition;
        _settingsService.SplitClientHello = SplitClientHello;
        _settingsService.MixHostCase = MixHostCase;
        _settingsService.DnsV4Address = DnsV4Address;
        _settingsService.DnsV4Port = DnsV4Port;
        _settingsService.DnsV6Address = DnsV6Address;
        _settingsService.DnsV6Port = DnsV6Port;
        _settingsService.StartOnBoot = StartOnBoot;
        _settingsService.ActivateOnStart = ActivateOnStart;
        _settingsService.AdBlockEnabled = AdBlockEnabled;
        _settingsService.AdBlockListUrl = AdBlockListUrl;

        // Sync blocklist service state
        _blocklistService.IsEnabled = AdBlockEnabled;

        // Apply language change
        await _loc.LoadLanguageAsync(SelectedLanguage?.Code ?? "en-US");

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task LoadBlocklistAsync()
    {
        if (IsLoadingBlocklist) return;

        IsLoadingBlocklist = true;
        AdBlockStatus = _loc["DownloadingBlocklist"];

        try
        {
            var url = string.IsNullOrWhiteSpace(AdBlockListUrl)
                ? "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts"
                : AdBlockListUrl.Trim();

            var count = await _blocklistService.LoadFromUrlAsync(url);
            _blocklistService.IsEnabled = AdBlockEnabled;

            AdBlockDomainCount = _blocklistService.DomainCount;
            AdBlockStatus = _loc.Get("DomainsLoaded", _blocklistService.DomainCount.ToString("N0"));
        }
        catch (Exception ex)
        {
            AdBlockStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingBlocklist = false;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        // Reset to defaults
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == "en-US") ?? Languages.First();
        SelectedPreset = Presets.FirstOrDefault();
        Ttl = "0";
        SplitPosition = "2";
        SplitClientHello = true;
        MixHostCase = false;
        DnsV4Address = "";
        DnsV4Port = "";
        DnsV6Address = "";
        DnsV6Port = "";
        StartOnBoot = false;
        ActivateOnStart = false;
        AdBlockEnabled = false;
        AdBlockListUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts";
    }

    // ── Static Data ─────────────────────────────────────────────

    private static readonly List<LanguageOption> AvailableLanguagesList = new()
    {
        new("English",    "en-US"),
        new("Türkçe",     "tr-TR"),
        new("Deutsch",    "de-DE"),
        new("Ελληνικά",   "el-GR"),
        new("Español",    "es-ES"),
        new("Français",   "fr-FR"),
        new("Português",  "pt-PT"),
        new("Русский",    "ru-RU"),
        new("中文(简体)",  "zh-CN"),
        new("中文(繁體)",  "zh-TW"),
    };
}

/// <summary>Simple record for language picker items.</summary>
public sealed record LanguageOption(string Name, string Code)
{
    public override string ToString() => Name;
}

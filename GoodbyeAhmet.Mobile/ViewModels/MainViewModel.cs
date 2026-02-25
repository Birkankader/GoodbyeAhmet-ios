using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GoodbyeAhmet.Mobile.Models;
using GoodbyeAhmet.Mobile.Services;

#if ANDROID
using GoodbyeAhmet.Mobile.Platforms.Android;
#endif

namespace GoodbyeAhmet.Mobile.ViewModels;

/// <summary>
/// Main view-model: drives the Start/Stop button, preset picker,
/// auto-start toggle, and log console shown on <see cref="MainPage"/>.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly PresetService _presetService;
    private readonly SettingsService _settingsService;

    // ── Constructor ─────────────────────────────────────────────

    public MainViewModel(PresetService presetService, SettingsService settingsService)
    {
        _presetService = presetService;
        _settingsService = settingsService;

        // Populate presets list
        var presets = _presetService.GetPresets();
        foreach (var p in presets)
            Presets.Add(p);

        // Restore persisted settings
        SelectedPreset = _presetService.GetByKey(_settingsService.SelectedPresetKey);
        StartOnBoot = _settingsService.StartOnBoot;

#if ANDROID
        // Subscribe to VPN service events
        DpiBypassVpnService.ConnectionStateChanged += OnVpnStateChanged;
        DpiBypassVpnService.LogReceived += OnVpnLog;
        IsConnected = DpiBypassVpnService.IsRunning;
#endif

        AddLog("GoodbyeAhmet ready. Select a preset and tap Start.");
    }

    // ── Observable Properties ───────────────────────────────────

    /// <summary>Available bypass presets for the Picker.</summary>
    public ObservableCollection<BypassPreset> Presets { get; } = new();

    /// <summary>Log entries shown in the console CollectionView.</summary>
    public ObservableCollection<string> LogEntries { get; } = new();

    /// <summary>Whether the VPN tunnel is currently active.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyCanExecuteChangedFor(nameof(ToggleVpnCommand))]
    private bool _isConnected;

    /// <summary>True while a connect/disconnect operation is in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleVpnCommand))]
    private bool _isBusy;

    /// <summary>The currently selected bypass preset.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetSubtitle))]
    private BypassPreset? _selectedPreset;

    /// <summary>Whether to auto-start the VPN on device boot.</summary>
    [ObservableProperty]
    private bool _startOnBoot;

    /// <summary>Whether the log console panel is visible.</summary>
    [ObservableProperty]
    private bool _isLogVisible;

    // ── Derived Properties ──────────────────────────────────────

    public string StatusText => IsConnected ? "Connected" : "Disconnected";
    public Color StatusColor => IsConnected ? Color.FromArgb("#1E90FF") : Color.FromArgb("#B0B0B0");
    public string PresetSubtitle => SelectedPreset is not null ? $"Using: {SelectedPreset.DisplayName}" : "No preset selected";

    // ── Reload from persisted settings (called when returning from SettingsPage) ──

    public void ReloadFromSettings()
    {
        var savedKey = _settingsService.SelectedPresetKey;
        var preset = Presets.FirstOrDefault(p => p.Key == savedKey) ?? Presets.FirstOrDefault();
        if (preset != null && preset != SelectedPreset)
        {
#pragma warning disable MVVMTK0034
            _selectedPreset = preset; // set backing field to avoid re-saving
            _startOnBoot = _settingsService.StartOnBoot;
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(SelectedPreset));
            OnPropertyChanged(nameof(PresetSubtitle));
            OnPropertyChanged(nameof(StartOnBoot));
            AddLog($"Preset updated → {preset.DisplayName}");
        }
    }

    // ── Commands ────────────────────────────────────────────────

    /// <summary>
    /// Toggles the VPN connection on/off.
    /// The actual Android VpnService integration will be added in Step 4.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleVpn))]
    private async Task ToggleVpnAsync()
    {
        IsBusy = true;

        try
        {
            if (IsConnected)
            {
                // ── STOP ────────────────────────────────────
                AddLog("Stopping VPN…");

#if ANDROID
                VpnHelper.StopVpn();
                // State will be updated via ConnectionStateChanged event
                await Task.Delay(300); // brief wait for service teardown
#endif
            }
            else
            {
                // ── START ───────────────────────────────────
                if (SelectedPreset is null)
                {
                    AddLog("⚠ Please select a preset first.");
                    return;
                }

                AddLog($"Starting VPN with preset: {SelectedPreset.DisplayName}…");

#if ANDROID
                var started = await VpnHelper.StartVpnAsync(SelectedPreset.Key);
                if (!started)
                {
                    AddLog("⚠ VPN permission denied by user.");
                }
                // State will be updated via ConnectionStateChanged event
#endif
            }
        }
        catch (Exception ex)
        {
            AddLog($"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanToggleVpn() => !IsBusy;

    /// <summary>Toggles log console visibility.</summary>
    [RelayCommand]
    private void ToggleLog()
    {
        IsLogVisible = !IsLogVisible;
    }

    // ── VPN Service Event Handlers ─────────────────────────────

#if ANDROID
    private void OnVpnStateChanged(bool connected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnected = connected;
            AddLog(connected ? "VPN connected ✓" : "VPN stopped.");
        });
    }

    private void OnVpnLog(string message)
    {
        AddLog(message);
    }
#endif

    // ── Helpers ─────────────────────────────────────────────────

    /// <summary>Adds a timestamped entry to the log console.</summary>
    public void AddLog(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";

        // Ensure UI thread
        if (MainThread.IsMainThread)
            LogEntries.Add(entry);
        else
            MainThread.BeginInvokeOnMainThread(() => LogEntries.Add(entry));
    }
}

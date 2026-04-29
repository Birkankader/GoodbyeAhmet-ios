using GoodbyeAhmetWPF.Models;
using GoodbyeAhmetWPF.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace GoodbyeAhmetWPF.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly SettingsService _settingsService;
        private readonly GoodbyeDpiService _goodbyeDpiService;
        private readonly DnsBlocklistService _blocklistService = new();
        private SettingsFile _settings;
        private bool _isRunning;
        private Preset? _selectedPreset;

        // Watchdog state for auto-restart-on-crash with exponential backoff.
        private int _restartAttempts;
        private DateTime _restartWindowStart = DateTime.MinValue;
        private const int MaxRestartsPerWindow = 3;
        private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(2);

        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _settingsService.Load();
            _settings = _settingsService.Data;

            _goodbyeDpiService = new GoodbyeDpiService();
            _goodbyeDpiService.UnexpectedExit += OnGoodbyeDpiUnexpectedExit;

            // Built-in + user-defined presets
            var allPresets = new List<Preset>(PresetService.GetPresets());
            foreach (var custom in PresetImportExportService.LoadCustom())
            {
                custom.Name = $"★ {custom.Name}"; // mark user presets visually
                allPresets.Add(custom);
            }
            Presets = new ObservableCollection<Preset>(allPresets);

            // Try to match current settings to a preset
            var matchingPreset = Presets.FirstOrDefault(p =>
                p.Modeset == _settings.Modeset &&
                p.TTL == _settings.TTL &&
                p.DNSV4Address == _settings.V4Address &&
                p.DNSV4Port == _settings.V4Port &&
                p.DNSV6Address == _settings.V6Address &&
                p.DNSV6Port == _settings.V6Port);

            if (matchingPreset != null)
            {
                SelectedPreset = matchingPreset;
            }

            StartCommand = new RelayCommand(Start, CanStart);
            StopCommand = new RelayCommand(Stop, CanStop);
            ToggleCommand = new RelayCommand(Toggle);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            ResetSettingsCommand = new RelayCommand(ResetSettings);
            ApplyPresetCommand = new RelayCommand(ApplyPreset);
            AboutCommand = new RelayCommand(ShowAbout);
            LoadBlocklistCommand = new RelayCommand(async _ => await LoadBlocklistAsync(), _ => !IsLoadingBlocklist);
            ClearBlocklistCacheCommand = new RelayCommand(_ => ClearBlocklistCache());
            ImportPresetsCommand = new RelayCommand(_ => ImportPresets());
            ExportPresetsCommand = new RelayCommand(_ => ExportPresets());
            OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());

            // Initialize Language
            LocalizationService.Instance.CurrentLanguage = _settings.Language;

            // Initialize ad-block
            _blocklistService.IsEnabled = _settings.AdBlockEnabled;
            if (_settings.AdBlockEnabled)
            {
                _ = LoadBlocklistInBackgroundAsync();
            }

            // NOTE: ActivateOnStart is intentionally NOT invoked here.
            // MainWindow calls StartIfActivateOnStart() from its Loaded event so that any
            // exception is handled after the window exists (avoids constructor crashes).
        }

        /// <summary>
        /// Background update check (called by MainWindow Loaded).
        /// </summary>
        public async Task CheckForUpdatesIfEnabledAsync()
        {
            if (!_settings.CheckForUpdates) return;
            try
            {
                var info = await UpdateService.CheckAsync().ConfigureAwait(false);
                if (info == null) return;

                await DispatchAsync(() =>
                {
                    var msg = $"A new version is available: v{info.Version}\n\nOpen the release page?";
                    var result = MessageBox.Show(msg, L("Info"), MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(info.HtmlUrl))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = info.HtmlUrl,
                                UseShellExecute = true,
                            });
                        }
                        catch (Exception ex) { Logger.Warn("Could not open release URL.", ex); }
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn("Update check failed.", ex);
            }
        }

        public ICommand ClearBlocklistCacheCommand { get; private set; } = null!;
        public ICommand ImportPresetsCommand { get; private set; } = null!;
        public ICommand ExportPresetsCommand { get; private set; } = null!;
        public ICommand OpenLogFolderCommand { get; private set; } = null!;

        private void ClearBlocklistCache()
        {
            _blocklistService.ClearCache();
            _blocklistService.Clear();
            AdBlockDomainCount = 0;
            AdBlockStatus = L("NoBlocklistLoaded");
        }

        private void OpenLogFolder()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Logger.LogDirectory,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { Logger.Warn("Could not open log folder.", ex); }
        }

        private void ImportPresets()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                Title = "Import Presets",
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var imported = PresetImportExportService.ImportFromFile(dlg.FileName);
                if (imported.Count == 0)
                {
                    MessageBox.Show("No valid presets found in file.", L("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Persist + merge into current Presets list
                var existing = PresetImportExportService.LoadCustom();
                existing.AddRange(imported);
                PresetImportExportService.SaveCustom(existing);

                foreach (var p in imported)
                {
                    var copy = new Preset
                    {
                        Name = $"★ {p.Name}",
                        Modeset = p.Modeset,
                        TTL = p.TTL,
                        DNSV4Address = p.DNSV4Address,
                        DNSV4Port = p.DNSV4Port,
                        DNSV6Address = p.DNSV6Address,
                        DNSV6Port = p.DNSV6Port,
                        Blacklist = p.Blacklist,
                    };
                    Presets.Add(copy);
                }

                MessageBox.Show($"Imported {imported.Count} preset(s).", L("Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("Preset import failed.", ex);
                MessageBox.Show($"Import failed: {ex.Message}", L("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPresets()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                Title = "Export Presets",
                FileName = "goodbye-ahmet-presets.json",
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                // Snapshot current settings as a single "Current" preset for portability.
                var snapshot = new Preset
                {
                    Name = "Exported Configuration",
                    Modeset = _settings.Modeset,
                    TTL = _settings.TTL,
                    DNSV4Address = _settings.V4Address,
                    DNSV4Port = _settings.V4Port,
                    DNSV6Address = _settings.V6Address,
                    DNSV6Port = _settings.V6Port,
                    Blacklist = "",
                };

                var custom = PresetImportExportService.LoadCustom();
                custom.Add(snapshot);
                PresetImportExportService.ExportToFile(dlg.FileName, custom);
                MessageBox.Show($"Exported to:\n{dlg.FileName}", L("Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("Preset export failed.", ex);
                MessageBox.Show($"Export failed: {ex.Message}", L("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Called by the View after the main window is loaded. Honors the
        /// ActivateOnStart user preference and surfaces errors safely.
        /// </summary>
        public void StartIfActivateOnStart()
        {
            if (_settings.ActivateOnStart)
            {
                try
                {
                    Start(null);
                }
                catch (Exception ex)
                {
                    Logger.Error("Auto-start (ActivateOnStart) failed.", ex);
                }
            }
        }

        private async Task LoadBlocklistInBackgroundAsync()
        {
            try
            {
                await _blocklistService.EnsureLoadedAsync(_settings.AdBlockListUrl).ConfigureAwait(false);
                await DispatchAsync(() =>
                {
                    AdBlockDomainCount = _blocklistService.DomainCount;
                    AdBlockStatus = _blocklistService.DomainCount > 0
                        ? string.Format(LocalizationService.Instance["DomainsLoaded"], _blocklistService.DomainCount.ToString("N0"))
                        : LocalizationService.Instance["NoBlocklistLoaded"];
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load DNS blocklist in background.", ex);
                await DispatchAsync(() => AdBlockStatus = string.Format(L("ErrorInvalidUrl"), ex.Message)).ConfigureAwait(false);
            }
        }

        private static System.Threading.Tasks.Task DispatchAsync(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                try { action(); } catch (Exception ex) { Logger.Warn("Dispatch action threw.", ex); }
                return System.Threading.Tasks.Task.CompletedTask;
            }
            return dispatcher.InvokeAsync(action).Task;
        }

        private void OnGoodbyeDpiUnexpectedExit(object? sender, GoodbyeDpiExitedEventArgs e)
        {
            // Marshal to UI thread and update state.
            _ = DispatchAsync(() =>
            {
                IsRunning = false;
                NotificationService.Instance.ShowNotification(
                    L("AppTitle"),
                    string.Format(L("GoodbyeDpiCrashed"), e.ExitCode),
                    System.Windows.Forms.ToolTipIcon.Warning);

                // Auto-restart with rate-limited exponential backoff.
                if (_settings.AutoRestartOnCrash)
                {
                    var now = DateTime.UtcNow;
                    if (now - _restartWindowStart > RestartWindow)
                    {
                        _restartWindowStart = now;
                        _restartAttempts = 0;
                    }

                    if (_restartAttempts < MaxRestartsPerWindow)
                    {
                        _restartAttempts++;
                        var delaySec = Math.Pow(2, _restartAttempts); // 2, 4, 8s
                        Logger.Info($"[Watchdog] Auto-restart attempt {_restartAttempts}/{MaxRestartsPerWindow} in {delaySec}s.");
                        _ = Task.Delay(TimeSpan.FromSeconds(delaySec)).ContinueWith(_ =>
                            DispatchAsync(() => { try { Start(null); } catch (Exception ex) { Logger.Error("Auto-restart failed.", ex); } }));
                    }
                    else
                    {
                        Logger.Warn("[Watchdog] Max restart attempts reached; giving up until next manual start.");
                    }
                }
            });
        }

        private static string L(string key) => LocalizationService.Instance[key];

        public SettingsFile Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        public string SelectedLanguage
        {
            get => _settings.Language;
            set
            {
                if (_settings.Language != value)
                {
                    _settings.Language = value;
                    LocalizationService.Instance.CurrentLanguage = value;

                    OnPropertyChanged();

                    // Persist immediately so users don't lose their language
                    // selection if they close the window without clicking Save.
                    try { _settingsService.Save(); }
                    catch (Exception ex) { Logger.Warn("Auto-save of language preference failed.", ex); }
                }
            }
        }

        public List<LanguageInfo> AvailableLanguages => LocalizationService.Instance.AvailableLanguages;

        public ObservableCollection<Preset> Presets { get; }

        private bool _applyingPreset;
        public Preset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (SetProperty(ref _selectedPreset, value))
                {
                    // Re-entry guard: ApplyPreset can mutate Settings which may
                    // trigger another SelectedPreset change downstream during
                    // rapid user switches.
                    if (_applyingPreset || value == null) return;
                    try
                    {
                        _applyingPreset = true;
                        ApplyPreset(value);
                    }
                    finally
                    {
                        _applyingPreset = false;
                    }
                }
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    // Force re-evaluation of commands
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        // ── Ad-Block Properties ───────────────────────────────

        private int _adBlockDomainCount;
        public int AdBlockDomainCount
        {
            get => _adBlockDomainCount;
            set => SetProperty(ref _adBlockDomainCount, value);
        }

        private string _adBlockStatus = "";
        public string AdBlockStatus
        {
            get => _adBlockStatus;
            set => SetProperty(ref _adBlockStatus, value);
        }

        private bool _isLoadingBlocklist;
        public bool IsLoadingBlocklist
        {
            get => _isLoadingBlocklist;
            set
            {
                if (SetProperty(ref _isLoadingBlocklist, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ToggleCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetSettingsCommand { get; }
        public ICommand ApplyPresetCommand { get; }
        public ICommand AboutCommand { get; }
        public ICommand LoadBlocklistCommand { get; }

        private bool CanStart(object? parameter) => !IsRunning;

        private void Start(object? parameter)
        {
            try
            {
                if (!App.IsAdmin)
                {
                    Logger.Warn("Start() called without administrator privileges; GoodbyeDPI will likely fail.");
                }

                _goodbyeDpiService.Start(Settings, _blocklistService);
                IsRunning = true;

                // Reset watchdog counter on a successful manual start.
                _restartAttempts = 0;
                _restartWindowStart = DateTime.UtcNow;

                NotificationService.Instance.ShowNotification(L("AppTitle"), L("Connected"), System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Logger.Error("Error starting GoodbyeDPI.", ex);
                MessageBox.Show(
                    string.Format(L("ErrorStartingGoodbyeDpi"), ex.Message, Logger.LogDirectory),
                    L("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                IsRunning = false;
            }
        }

        private void Toggle(object? parameter)
        {
            if (IsRunning)
                Stop(null);
            else
                Start(null);
        }

        private bool CanStop(object? parameter) => IsRunning;

        private void Stop(object? parameter)
        {
            _goodbyeDpiService.Stop();
            IsRunning = false;
            NotificationService.Instance.ShowNotification(L("AppTitle"), L("Disconnected"), System.Windows.Forms.ToolTipIcon.Info);
        }

        private void SaveSettings(object? parameter)
        {
            // Validate user input before persisting / restarting the bypass.
            if (!ValidateSettings(out var validationError))
            {
                MessageBox.Show(validationError, L("InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Sync ad-block state
            _blocklistService.IsEnabled = _settings.AdBlockEnabled;
            if (_settings.AdBlockEnabled && _blocklistService.DomainCount == 0)
            {
                _ = LoadBlocklistInBackgroundAsync();
            }
            else if (!_settings.AdBlockEnabled)
            {
                _blocklistService.Clear();
                AdBlockDomainCount = 0;
                AdBlockStatus = LocalizationService.Instance["NoBlocklistLoaded"];
            }

            try
            {
                _settingsService.Save();
                MessageBox.Show(L("SettingsSaved"), L("Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save settings.", ex);
                MessageBox.Show(string.Format(L("ErrorSavingSettings"), ex.Message), L("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateSettings(out string error)
        {
            var errors = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrWhiteSpace(_settings.Modeset) && !InputValidator.IsModeset(_settings.Modeset))
                errors.Add(L("ValidationModeset"));

            if (!string.IsNullOrWhiteSpace(_settings.TTL) && !InputValidator.IsTtl(_settings.TTL))
                errors.Add(L("ValidationTtl"));

            if (!string.IsNullOrWhiteSpace(_settings.V4Address) && !InputValidator.IsIp(_settings.V4Address))
                errors.Add(L("ValidationIpV4"));

            if (!string.IsNullOrWhiteSpace(_settings.V6Address) && !InputValidator.IsIp(_settings.V6Address))
                errors.Add(L("ValidationIpV6"));

            if (!string.IsNullOrWhiteSpace(_settings.V4Port) && !InputValidator.IsPort(_settings.V4Port))
                errors.Add(L("ValidationPortV4"));

            if (!string.IsNullOrWhiteSpace(_settings.V6Port) && !InputValidator.IsPort(_settings.V6Port))
                errors.Add(L("ValidationPortV6"));

            if (_settings.AdBlockEnabled && !string.IsNullOrWhiteSpace(_settings.AdBlockListUrl))
            {
                if (!InputValidator.IsHttpsUrl(_settings.AdBlockListUrl, out var urlErr))
                    errors.Add(string.Format(L("ValidationAdBlockUrl"), urlErr));
            }

            error = string.Join(Environment.NewLine, errors);
            return errors.Count == 0;
        }

        private async Task LoadBlocklistAsync()
        {
            if (IsLoadingBlocklist) return;

            IsLoadingBlocklist = true;
            AdBlockStatus = LocalizationService.Instance["DownloadingBlocklist"];

            try
            {
                var url = string.IsNullOrWhiteSpace(_settings.AdBlockListUrl)
                    ? DnsBlocklistService.DefaultBlocklistUrl
                    : _settings.AdBlockListUrl.Trim();

                if (!InputValidator.IsHttpsUrl(url, out var urlError))
                {
                    AdBlockStatus = string.Format(L("ErrorInvalidUrl"), urlError);
                    Logger.Warn($"LoadBlocklist rejected URL '{url}': {urlError}");
                    return;
                }

                await _blocklistService.LoadFromUrlAsync(url);
                _blocklistService.IsEnabled = _settings.AdBlockEnabled;

                AdBlockDomainCount = _blocklistService.DomainCount;
                AdBlockStatus = string.Format(
                    LocalizationService.Instance["DomainsLoaded"],
                    _blocklistService.DomainCount.ToString("N0"));
            }
            catch (Exception ex)
            {
                AdBlockStatus = string.Format(L("ErrorInvalidUrl"), ex.Message);
            }
            finally
            {
                IsLoadingBlocklist = false;
            }
        }

        private void ResetSettings(object? parameter)
        {
            Stop(null);
            _settingsService.Load(); // Re-load or reset
            Settings = _settingsService.Data;
            // In a real app we might want to deep copy or create new instance logic in service
            MessageBox.Show(L("SettingsReset"), L("Info"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowAbout(object? parameter)
        {
            MessageBox.Show(L("AboutContent"), L("About"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ApplyPreset(object? parameter)
        {
            if (parameter is Preset preset)
            {
                Settings.UsePreset(preset);
                // Trigger property change for Settings fields if UI binds to them directly inside Settings object
                // Since Settings is an object, changes inside it might not notify unless SettingsFile implements INotifyPropertyChanged
                // For simplicity, we assume UI binds to MainViewModel.Settings which is the same object, 
                // but deep properties update might not reflect if not notified. 
                // Ideally SettingsFile should implement INotifyPropertyChanged.

                // Let's hack refresh by re-setting settings if needed or assume bindings are direct.
                // Better approach: Make SettingsFile observeable.
                OnPropertyChanged(nameof(Settings));
            }
        }

        public void Cleanup()
        {
            Dispose();
        }

        public void Dispose()
        {
            try
            {
                _goodbyeDpiService.UnexpectedExit -= OnGoodbyeDpiUnexpectedExit;
                _goodbyeDpiService.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn("Cleanup error.", ex);
            }
        }
    }
}

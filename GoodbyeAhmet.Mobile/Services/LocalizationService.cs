using System.ComponentModel;
using System.Text.Json;

namespace GoodbyeAhmet.Mobile.Services;

/// <summary>
/// Provides localized strings by loading JSON locale files from embedded resources.
/// Implements <see cref="INotifyPropertyChanged"/> so XAML bindings auto-refresh
/// when the language changes at runtime.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private Dictionary<string, string> _strings = new();
    private string _currentLanguage = "en-US";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Static singleton instance for XAML binding via {x:Static}.</summary>
    public static LocalizationService Instance { get; } = new();

    /// <summary>
    /// Indexer — returns translated string for the given key,
    /// or the key itself if no translation is found.
    /// Usage in XAML: Text="{Binding [SettingsTitle], Source={x:Static svc:LocalizationService.Instance}}"
    /// </summary>
    public string this[string key] =>
        _strings.TryGetValue(key, out var value) ? value : key;

    /// <summary>Current language code (e.g. "en-US", "tr-TR").</summary>
    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage == value) return;
            _currentLanguage = value;
            _ = LoadLanguageAsync(value);
        }
    }

    /// <summary>
    /// Loads the locale JSON for the specified language code.
    /// Falls back to en-US if the requested file doesn't exist.
    /// </summary>
    public async Task LoadLanguageAsync(string languageCode)
    {
        _currentLanguage = languageCode;
        var json = await LoadJsonAsync(languageCode);
        if (json == null && languageCode != "en-US")
        {
            // Fallback to English
            json = await LoadJsonAsync("en-US");
        }

        if (json != null)
        {
            _strings = json;
        }

        // Notify all bindings that translations have changed
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>
    /// Gets a translated string with format arguments.
    /// E.g. Get("Using", "Preset 1") → "Using: Preset 1"
    /// </summary>
    public string Get(string key, params object[] args)
    {
        var template = this[key];
        try
        {
            return args.Length > 0 ? string.Format(template, args) : template;
        }
        catch
        {
            return template;
        }
    }

    private static async Task<Dictionary<string, string>?> LoadJsonAsync(string code)
    {
        try
        {
            var filename = $"{code}.json";
            using var stream = await FileSystem.OpenAppPackageFileAsync(filename);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Localization] Failed to load '{code}': {ex.Message}");
            return null;
        }
    }
}

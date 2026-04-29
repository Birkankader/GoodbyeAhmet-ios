using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Globalization;
using GoodbyeAhmetWPF.Models;

namespace GoodbyeAhmetWPF.Services
{
    public class LocalizationService : INotifyPropertyChanged
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private Dictionary<string, string> _strings = new Dictionary<string, string>();
        private Dictionary<string, string> _fallback = new Dictionary<string, string>();
        private const string FallbackLanguage = "en-US";
        private readonly HashSet<string> _missingKeysReported = new();

        private string LocalFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local");

        public string this[string key]
        {
            get
            {
                if (_strings.TryGetValue(key, out var value)) return value;
                if (_fallback.TryGetValue(key, out var fallback))
                {
                    ReportMissing(key);
                    return fallback;
                }
                ReportMissing(key);
                return key;
            }
        }

        private void ReportMissing(string key)
        {
            if (_missingKeysReported.Add(key))
            {
                Logger.Warn($"Localization key missing in '{_currentLanguage}': {key}");
            }
        }

        private string _currentLanguage = "en-US";
        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    LoadLanguage(_currentLanguage);
                    OnPropertyChanged();
                    // Notify all properties changed so indexer bindings update
                    OnPropertyChanged(string.Empty);
                }
            }
        }

        public List<LanguageInfo> AvailableLanguages
        {
            get
            {
                if (Directory.Exists(LocalFolder))
                {
                    return Directory.GetFiles(LocalFolder, "*.json")
                        .Select(path =>
                        {
                            var code = Path.GetFileNameWithoutExtension(path);
                            string name = code;
                            try
                            {
                                var culture = CultureInfo.GetCultureInfo(code);
                                name = culture.NativeName;
                                // Capitalize first letter
                                if (name.Length > 0)
                                    name = char.ToUpper(name[0]) + name.Substring(1);
                            }
                            catch { }

                            return new LanguageInfo { Code = code, Name = name };
                        })
                        .OrderBy(x => x.Name)
                        .ToList();
                }
                return new List<LanguageInfo> { new LanguageInfo { Code = "en-US", Name = "English (United States)" } };
            }
        }

        private LocalizationService()
        {
            // Pre-load English fallback dictionary so untranslated keys
            // gracefully degrade to English instead of showing raw key names.
            _fallback = LoadDictionary(FallbackLanguage) ?? new Dictionary<string, string>();
            LoadLanguage(_currentLanguage);
        }

        private Dictionary<string, string>? LoadDictionary(string languageCode)
        {
            try
            {
                var path = Path.Combine(LocalFolder, $"{languageCode}.json");
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load language file '{languageCode}'.", ex);
                return null;
            }
        }

        public void LoadLanguage(string languageCode)
        {
            var dict = LoadDictionary(languageCode);
            if (dict != null)
            {
                _strings = dict;
            }
            else if (languageCode != FallbackLanguage)
            {
                Logger.Warn($"Language '{languageCode}' not found. Falling back to {FallbackLanguage}.");
                _strings = _fallback;
            }

            _missingKeysReported.Clear();

            // Notify that everything changed
            OnPropertyChanged(string.Empty);
        }

        // Helper to refresh when languages are added
        public void RefreshAvailableLanguages()
        {
            OnPropertyChanged(nameof(AvailableLanguages));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using GoodbyeAhmetWPF.Models;
using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Linq;

namespace GoodbyeAhmetWPF.Services
{
    public class SettingsService
    {
        // Per-user settings location. The previous version stored next to the exe;
        // we migrate it the first time we encounter it.
        private static string FilePath => AppPaths.SettingsFilePath;
        private static string LegacyFilePath =>
            Path.Combine(AppPaths.AppBaseDirectory, "settings.json");

        // Hard cap to defend against malicious/oversized JSON files.
        private const long MaxSettingsBytes = 1 * 1024 * 1024; // 1 MB

        public SettingsFile Data { get; private set; }

        public SettingsService()
        {
            Data = new SettingsFile();
        }

        public void Load()
        {
            // One-time migration: copy old settings.json next to exe to %LOCALAPPDATA%.
            try
            {
                if (!File.Exists(FilePath) && File.Exists(LegacyFilePath))
                {
                    File.Copy(LegacyFilePath, FilePath, overwrite: false);
                    Logger.Info($"Migrated legacy settings.json from '{LegacyFilePath}' to '{FilePath}'.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Legacy settings migration failed.", ex);
            }

            if (!File.Exists(FilePath))
            {
                Data = new SettingsFile();
                ApplyDefaults();
                return;
            }

            try
            {
                var info = new FileInfo(FilePath);
                if (info.Length > MaxSettingsBytes)
                {
                    Logger.Error($"Settings file too large ({info.Length} bytes). Using defaults.");
                    Data = new SettingsFile();
                    ApplyDefaults();
                    return;
                }

                string content = File.ReadAllText(FilePath);
                Data = JsonSerializer.Deserialize<SettingsFile>(content) ?? new SettingsFile();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading settings from '{FilePath}'. Using defaults.", ex);
                Data = new SettingsFile();
                ApplyDefaults();
            }
        }

        private void ApplyDefaults()
        {
            // Auto-detect system language
            try
            {
                var culture = CultureInfo.CurrentCulture;
                var available = LocalizationService.Instance.AvailableLanguages;

                var exact = available.FirstOrDefault(x => x.Code.Equals(culture.Name, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                {
                    Data.Language = exact.Code;
                }
                else
                {
                    var twoLetter = culture.TwoLetterISOLanguageName;
                    var match = available.FirstOrDefault(l => l.Code.StartsWith(twoLetter, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        Data.Language = match.Code;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Auto-detect language failed.", ex);
            }

            var presets = PresetService.GetPresets();
            if (presets.Count > 0)
            {
                Data.UsePreset(presets[0]);
            }
        }

        public void Save()
        {
            Data ??= new SettingsFile();

            try
            {
                Directory.CreateDirectory(AppPaths.UserDataDirectory);
                string content = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });

                // Atomic write: write to temp file then move into place.
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, content);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving settings to '{FilePath}'.", ex);
                throw;
            }
        }
    }
}

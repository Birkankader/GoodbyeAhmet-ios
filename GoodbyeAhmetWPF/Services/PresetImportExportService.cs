using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GoodbyeAhmetWPF.Models;

namespace GoodbyeAhmetWPF.Services
{
    /// <summary>
    /// Persists user-defined presets to disk and supports import/export
    /// to a user-chosen JSON file for sharing configurations.
    /// </summary>
    public static class PresetImportExportService
    {
        private const long MaxImportBytes = 256 * 1024; // 256 KB defensive cap

        public static string CustomPresetsPath =>
            Path.Combine(AppPaths.UserDataDirectory, "presets.json");

        /// <summary>
        /// Loads custom presets stored under %LOCALAPPDATA%. Returns an empty list on failure.
        /// </summary>
        public static List<Preset> LoadCustom()
        {
            try
            {
                if (!File.Exists(CustomPresetsPath)) return new List<Preset>();
                var info = new FileInfo(CustomPresetsPath);
                if (info.Length > MaxImportBytes)
                {
                    Logger.Warn($"[Presets] Custom preset file too large ({info.Length} bytes); ignoring.");
                    return new List<Preset>();
                }
                var json = File.ReadAllText(CustomPresetsPath);
                var list = JsonSerializer.Deserialize<List<Preset>>(json);
                return Sanitize(list ?? new List<Preset>());
            }
            catch (Exception ex)
            {
                Logger.Warn("[Presets] Failed to load custom presets.", ex);
                return new List<Preset>();
            }
        }

        public static void SaveCustom(IEnumerable<Preset> presets)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.UserDataDirectory);
                var json = JsonSerializer.Serialize(
                    Sanitize(presets.ToList()),
                    new JsonSerializerOptions { WriteIndented = true });
                var tmp = CustomPresetsPath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(CustomPresetsPath)) File.Replace(tmp, CustomPresetsPath, null);
                else File.Move(tmp, CustomPresetsPath);
            }
            catch (Exception ex)
            {
                Logger.Error("[Presets] Failed to save custom presets.", ex);
                throw;
            }
        }

        /// <summary>
        /// Exports a list of presets to an arbitrary JSON file.
        /// </summary>
        public static void ExportToFile(string filePath, IEnumerable<Preset> presets)
        {
            var json = JsonSerializer.Serialize(
                Sanitize(presets.ToList()),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Imports presets from a user-chosen file. Validates size and content.
        /// </summary>
        public static List<Preset> ImportFromFile(string filePath)
        {
            var info = new FileInfo(filePath);
            if (info.Length > MaxImportBytes)
                throw new InvalidOperationException($"Preset file is too large ({info.Length} bytes; limit {MaxImportBytes}).");

            var json = File.ReadAllText(filePath);
            var list = JsonSerializer.Deserialize<List<Preset>>(json) ?? new List<Preset>();
            return Sanitize(list);
        }

        /// <summary>
        /// Drops invalid or null entries and clamps fields to safe values.
        /// </summary>
        private static List<Preset> Sanitize(List<Preset> input)
        {
            var result = new List<Preset>(input.Count);
            foreach (var p in input)
            {
                if (p == null) continue;
                if (string.IsNullOrWhiteSpace(p.Name)) continue;
                if (!string.IsNullOrEmpty(p.Modeset) && !InputValidator.IsModeset(p.Modeset)) p.Modeset = "";
                if (!string.IsNullOrEmpty(p.TTL) && !InputValidator.IsTtl(p.TTL)) p.TTL = "";
                if (!string.IsNullOrEmpty(p.DNSV4Address) && !InputValidator.IsIp(p.DNSV4Address)) p.DNSV4Address = "";
                if (!string.IsNullOrEmpty(p.DNSV6Address) && !InputValidator.IsIp(p.DNSV6Address)) p.DNSV6Address = "";
                if (!string.IsNullOrEmpty(p.DNSV4Port) && !InputValidator.IsPort(p.DNSV4Port)) p.DNSV4Port = "";
                if (!string.IsNullOrEmpty(p.DNSV6Port) && !InputValidator.IsPort(p.DNSV6Port)) p.DNSV6Port = "";
                if (p.Name.Length > 80) p.Name = p.Name[..80];
                result.Add(p);
            }
            return result;
        }
    }
}

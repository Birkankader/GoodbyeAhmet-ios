using System;
using System.IO;

namespace GoodbyeAhmetWPF.Services
{
    /// <summary>
    /// Central provider for per-user data paths.
    /// Storing settings/cache under %LOCALAPPDATA% prevents one user from
    /// reading or sabotaging another user's configuration on shared installs,
    /// and avoids permission issues writing under Program Files.
    /// </summary>
    public static class AppPaths
    {
        private static readonly Lazy<string> _userDataDir = new(InitializeUserDataDir);

        /// <summary>Per-user data directory (created on first access).</summary>
        public static string UserDataDirectory => _userDataDir.Value;

        public static string SettingsFilePath => Path.Combine(UserDataDirectory, "settings.json");
        public static string BlocklistCachePath => Path.Combine(UserDataDirectory, "blocklist_cache.txt");
        public static string CustomHostsPath => Path.Combine(UserDataDirectory, "custom_hosts.txt");

        /// <summary>Read-only directory shipped with the app (essentials/goodbyedpi/etc.).</summary>
        public static string AppBaseDirectory => AppDomain.CurrentDomain.BaseDirectory;

        private static string InitializeUserDataDir()
        {
            string baseDir;
            try
            {
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = AppContext.BaseDirectory;
            }
            catch
            {
                baseDir = AppContext.BaseDirectory;
            }

            var dir = Path.Combine(baseDir, "GoodbyeAhmet");
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex)
            {
                Logger.Warn($"Could not create user data directory '{dir}'.", ex);
            }
            return dir;
        }
    }
}

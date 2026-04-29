using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GoodbyeAhmetWPF.Services
{
    /// <summary>
    /// Lightweight thread-safe rolling file logger.
    /// Writes to %LOCALAPPDATA%/GoodbyeAhmet/logs/app-yyyyMMdd.log.
    /// Falls back to AppContext.BaseDirectory if LocalAppData is not writable.
    /// </summary>
    public static class Logger
    {
        public enum Level { Debug, Info, Warn, Error }

        private static readonly object _gate = new();
        private static readonly Lazy<string> _logDirectory = new(InitializeLogDirectory);
        private const long MaxFileBytes = 5 * 1024 * 1024; // 5 MB
        private const int MaxFiles = 7;

        public static string LogDirectory => _logDirectory.Value;

        private static string InitializeLogDirectory()
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

            var dir = Path.Combine(baseDir, "GoodbyeAhmet", "logs");
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch
            {
                // Last-resort fallback to a temp folder.
                dir = Path.Combine(Path.GetTempPath(), "GoodbyeAhmet", "logs");
                try { Directory.CreateDirectory(dir); } catch { /* swallow */ }
            }
            return dir;
        }

        public static void Debug(string message) => Write(Level.Debug, message, null);
        public static void Info(string message) => Write(Level.Info, message, null);
        public static void Warn(string message, Exception? ex = null) => Write(Level.Warn, message, ex);
        public static void Error(string message, Exception? ex = null) => Write(Level.Error, message, ex);

        public static void Write(Level level, string message, Exception? ex)
        {
            try
            {
                var sb = new StringBuilder(256);
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
                  .Append(message);
                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append(ex.GetType().FullName).Append(": ").Append(ex.Message).AppendLine();
                    sb.Append(ex.StackTrace);
                }
                var line = sb.ToString();

                Trace.WriteLine(line);

                lock (_gate)
                {
                    var path = GetCurrentFilePath();
                    RotateIfNeeded(path);
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never throw.
            }
        }

        private static string GetCurrentFilePath()
            => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

        private static void RotateIfNeeded(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxFileBytes)
                {
                    var rotated = Path.Combine(LogDirectory,
                        $"app-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                    File.Move(path, rotated);
                }

                // Trim old logs.
                var files = new DirectoryInfo(LogDirectory)
                    .GetFiles("app-*.log");
                if (files.Length > MaxFiles)
                {
                    Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                    for (int i = 0; i < files.Length - MaxFiles; i++)
                    {
                        try { files[i].Delete(); } catch { /* ignore */ }
                    }
                }
            }
            catch
            {
                // Ignore rotation errors; logging continues to current file.
            }
        }
    }
}

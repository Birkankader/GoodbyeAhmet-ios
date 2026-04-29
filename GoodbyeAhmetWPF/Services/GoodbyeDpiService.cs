using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using GoodbyeAhmetWPF.Models;

namespace GoodbyeAhmetWPF.Services
{
    public class GoodbyeDpiService : IDisposable
    {
        private Process? _process;
        private readonly object _lock = new();
        private bool _stopRequested;

        // Path logic needs to be robust.
        // Assuming the essentials folder is copied to the output directory, same as the WinForms app.
        private static string APP_PATH_64 => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"essentials\goodbyedpi\x86_64\goodbyedpi.exe");
        private static string APP_PATH_32 => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"essentials\goodbyedpi\x86\goodbyedpi.exe");

        private static string APP_PATH => Environment.Is64BitProcess ? APP_PATH_64 : APP_PATH_32;

        /// <summary>
        /// Raised on the thread pool when the underlying GoodbyeDPI process exits unexpectedly
        /// (i.e. the user did not call Stop()). Subscribers should marshal to the UI thread.
        /// </summary>
        public event EventHandler<GoodbyeDpiExitedEventArgs>? UnexpectedExit;

        public bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    return _process != null && !_process.HasExited;
                }
            }
        }

        public void Start(SettingsFile settings, DnsBlocklistService? blocklist = null)
        {
            lock (_lock)
            {
                if (_process != null && !_process.HasExited)
                {
                    Logger.Debug("GoodbyeDpiService.Start ignored: already running.");
                    return;
                }

                // Dispose any previous (already-exited) handle before creating a new one.
                _process?.Dispose();
                _process = null;

                if (!File.Exists(APP_PATH))
                {
                    var msg = $"GoodbyeDPI not found at: {APP_PATH}";
                    Logger.Error(msg);
                    throw new FileNotFoundException(msg, APP_PATH);
                }

                if (!BinaryIntegrityService.Verify(APP_PATH))
                {
                    var msg = $"GoodbyeDPI binary failed integrity check: {APP_PATH}";
                    Logger.Error(msg);
                    throw new InvalidOperationException(msg);
                }

                var startInfo = new ProcessStartInfo(APP_PATH)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(APP_PATH) ?? AppDomain.CurrentDomain.BaseDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                BuildArguments(startInfo, settings, blocklist);

                Logger.Info($"Starting GoodbyeDPI: \"{APP_PATH}\" {startInfo.Arguments}");

                _stopRequested = false;
                try
                {
                    var proc = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                    proc.Exited += OnProcessExited;
                    proc.OutputDataReceived += (_, e) => { if (e.Data != null) Logger.Debug("[gdpi-out] " + e.Data); };
                    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Logger.Warn("[gdpi-err] " + e.Data); };

                    if (!proc.Start())
                    {
                        proc.Dispose();
                        throw new InvalidOperationException("Process.Start returned false.");
                    }

                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    _process = proc;
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to start GoodbyeDPI process.", ex);
                    throw;
                }
            }
        }

        private static void BuildArguments(ProcessStartInfo startInfo, SettingsFile settings, DnsBlocklistService? blocklist)
        {
            var arguments = new StringBuilder();

            // Whitelist every user-controlled value to prevent argument injection.
            if (InputValidator.IsModeset(settings.Modeset))
                arguments.Append(settings.Modeset).Append(' ');

            if (InputValidator.IsTtl(settings.TTL))
                arguments.Append("--set-ttl ").Append(settings.TTL).Append(' ');

            if (InputValidator.IsIp(settings.V4Address))
                arguments.Append("--dns-addr ").Append(settings.V4Address).Append(' ');

            if (InputValidator.IsPort(settings.V4Port))
                arguments.Append("--dns-port ").Append(settings.V4Port).Append(' ');

            if (InputValidator.IsIp(settings.V6Address))
                arguments.Append("--dnsv6-addr ").Append(settings.V6Address).Append(' ');

            if (InputValidator.IsPort(settings.V6Port))
                arguments.Append("--dnsv6-port ").Append(settings.V6Port);

            // Ad-block: supply custom blacklist file to GoodbyeDPI
            if (settings.AdBlockEnabled && blocklist != null && blocklist.IsEnabled && blocklist.DomainCount > 0)
            {
                var blacklistPath = AppPaths.CustomHostsPath;
                if (File.Exists(blacklistPath))
                {
                    arguments.Append(" --blacklist \"").Append(blacklistPath).Append('"');
                }
                else
                {
                    Logger.Warn($"Ad-block enabled but blacklist file missing: {blacklistPath}");
                }
            }

            startInfo.Arguments = arguments.ToString().Trim();
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            if (sender is not Process proc) return;

            int exitCode;
            try { exitCode = proc.ExitCode; }
            catch { exitCode = -1; }

            Logger.Info($"GoodbyeDPI process exited. Code={exitCode}, StopRequested={_stopRequested}");

            // Snapshot whether this was unexpected before we clear state.
            var unexpected = !_stopRequested;

            lock (_lock)
            {
                if (ReferenceEquals(_process, proc))
                {
                    try { _process.Dispose(); } catch { /* ignore */ }
                    _process = null;
                }
            }

            if (unexpected)
            {
                try
                {
                    UnexpectedExit?.Invoke(this, new GoodbyeDpiExitedEventArgs(exitCode));
                }
                catch (Exception ex)
                {
                    Logger.Warn("UnexpectedExit handler threw.", ex);
                }
            }
        }

        public void Stop()
        {
            _stopRequested = true;
            KillProcesses();
        }

        private void KillProcesses()
        {
            try
            {
                // Kill all instances of goodbyedpi to be safe.
                foreach (var p in Process.GetProcessesByName("goodbyedpi"))
                {
                    try
                    {
                        if (!p.HasExited)
                            p.Kill();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Process {p.Id} kill error.", ex);
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }

                lock (_lock)
                {
                    if (_process != null)
                    {
                        try
                        {
                            if (!_process.HasExited)
                                _process.Kill();
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn("Main process kill error.", ex);
                        }
                        finally
                        {
                            _process.Dispose();
                            _process = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("KillProcesses general error.", ex);
            }
        }

        public void Dispose()
        {
            try { Stop(); } catch { /* ignore */ }
        }
    }

    public sealed class GoodbyeDpiExitedEventArgs : EventArgs
    {
        public int ExitCode { get; }
        public GoodbyeDpiExitedEventArgs(int exitCode) => ExitCode = exitCode;
    }
}

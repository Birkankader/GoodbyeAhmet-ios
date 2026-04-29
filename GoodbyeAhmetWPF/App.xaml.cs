using System;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using GoodbyeAhmetWPF.Services;

namespace GoodbyeAhmetWPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;
    private static bool _mutexOwned;

    public static bool IsAdmin { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Build a per-version, per-user mutex name to avoid collisions between
        // different installations / portable copies on the same machine.
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? "anon";
        var mutexName = $"Local\\GoodbyeAhmetWPF_{version}_{userSid}";

        try
        {
            _mutex = new Mutex(initiallyOwned: true, mutexName, out _mutexOwned);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to create singleton mutex.", ex);
            _mutex = null;
            _mutexOwned = true; // Allow startup; better than blocking the user.
        }

        if (!_mutexOwned)
        {
            System.Windows.MessageBox.Show(
                "Goodbye Ahmet is already running!",
                "Warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        IsAdmin = IsRunningAsAdmin();
        Logger.Info($"Application starting. Version={version}, Admin={IsAdmin}");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Logger.Error("AppDomain unhandled exception.", ex);
            System.Windows.MessageBox.Show(
                $"A fatal error occurred and has been logged to:\n{Logger.LogDirectory}\n\n{ex?.Message}",
                "Crash",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Logger.Error("Dispatcher unhandled exception.", args.Exception);
            System.Windows.MessageBox.Show(
                $"An error occurred:\n{args.Exception.Message}\n\nDetails logged to:\n{Logger.LogDirectory}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Logger.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        if (!IsAdmin)
        {
            Logger.Warn("Application is NOT running as administrator. GoodbyeDPI will likely fail to start.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info($"Application exiting with code {e.ApplicationExitCode}.");
        try
        {
            if (_mutex != null)
            {
                if (_mutexOwned)
                {
                    try { _mutex.ReleaseMutex(); }
                    catch (ApplicationException) { /* not owned on this thread */ }
                }
                _mutex.Dispose();
                _mutex = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Error releasing mutex on exit.", ex);
        }
        base.OnExit(e);
    }

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}


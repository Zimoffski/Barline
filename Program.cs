using System.Diagnostics;
using System.Threading;
using Barline.Services;

namespace Barline;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Carry settings over from the former name and drop its autostart entries.
        LegacyMigration.Run();

        if (HasArgument(args, "--configure-sensor-access"))
        {
            StartupManager.EnableElevatedSensorAccess();
            return;
        }

        if (HasArgument(args, "--apply-startup"))
        {
            StartupManager.ApplyConfiguredStartup();
            return;
        }

        if (HasArgument(args, "--uninstall"))
        {
            StartupManager.RemoveConfiguration();
            StopRunningInstances();
            ForegroundFpsReader.StopTraceSession();
            return;
        }

        if (HasArgument(args, "--stop"))
        {
            StopRunningInstances();
            return;
        }

        if (HasArgument(args, "--diagnostics"))
        {
            WriteStandaloneDiagnostics();
            return;
        }

        var replaceExisting = HasArgument(args, "--replace");
        var isScheduledStartup = HasArgument(args, "--startup");
        var isEnablingSensors = HasArgument(args, "--enable-sensor-access");

        if (!replaceExisting &&
            !isScheduledStartup &&
            !isEnablingSensors &&
            StartupManager.TryStartConfiguredElevatedInstance())
        {
            return;
        }

        using var mutex = new Mutex(initiallyOwned: false, AppConstants.MutexName);
        var ownsMutex = false;

        try
        {
            try
            {
                ownsMutex = replaceExisting
                    ? mutex.WaitOne(TimeSpan.FromSeconds(15))
                    : mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                SignalRunningInstance();
                return;
            }

            RunApplication(enableSensorAccess: isEnablingSensors);
        }
        finally
        {
            if (ownsMutex)
                mutex.ReleaseMutex();
        }
    }

    private static void RunApplication(bool enableSensorAccess)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Write(e.ExceptionObject as Exception ?? new InvalidOperationException("Unknown unhandled exception"));

        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
        };

        app.DispatcherUnhandledException += (_, e) =>
        {
            Logger.Write(e.Exception);
            e.Handled = true;
        };

        if (enableSensorAccess)
            StartupManager.EnableElevatedSensorAccess();

        StartupManager.EnsureConfigured();

        var window = new MainWindow(UserSettings.AlwaysOnTop);
        using var tray = new TrayIconController(window, app);
        using var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            AppConstants.ActivationEventName);
        var activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                    app.Dispatcher.BeginInvoke(tray.NotifyAlreadyRunning);
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        try
        {
            app.Run(window);
        }
        finally
        {
            activationRegistration.Unregister(waitObject: null);
        }
    }

    private static bool HasArgument(IEnumerable<string> args, string expected) =>
        args.Any(argument => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));

    private static void SignalRunningInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(AppConstants.ActivationEventName);
            activationEvent.Set();
        }
        catch
        {
            // The first instance may still be starting; it will appear in the tray shortly.
        }
    }

    private static void WriteStandaloneDiagnostics()
    {
        try
        {
            using var sensors = new HardwareSensorReader();
            Thread.Sleep(500);
            Logger.WriteDiagnostics(sensors.CreateDiagnosticsReport());
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
        }
    }

    private static void StopRunningInstances()
    {
        var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(AppConstants.ProductName))
        {
            try
            {
                if (process.Id != current.Id)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort only. The tray menu provides graceful shutdown.
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}

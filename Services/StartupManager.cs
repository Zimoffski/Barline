using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Barline.Services;

internal static class StartupManager
{
    private const string RunValueName = AppConstants.ProductName;

    public static void EnsureConfigured()
    {
        try
        {
            UserSettings.EnsureDefaults();
            ApplyStartup(UserSettings.StartWithWindows, UserSettings.ElevatedSensorAccess);
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
        }
    }

    public static bool IsEnabled() => UserSettings.StartWithWindows;

    public static bool UsesElevatedStartup() => UserSettings.ElevatedSensorAccess;

    public static void EnableElevatedSensorAccess()
    {
        if (!ElevationService.IsAdministrator)
            return;

        try
        {
            UserSettings.ElevatedSensorAccess = true;
            ApplyStartup(UserSettings.StartWithWindows, elevatedStartup: true);
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
        }
    }

    /// <summary>
    /// Changes autostart and reports whether Windows will actually start the monitor at logon.
    /// </summary>
    public static bool SetEnabled(bool enabled)
    {
        UserSettings.StartWithWindows = enabled;

        var elevatedStartup = UserSettings.ElevatedSensorAccess;
        if (ElevationService.IsAdministrator || !elevatedStartup)
        {
            ApplyStartup(enabled, elevatedStartup);
            return true;
        }

        // The sensor-access task lives in Task Scheduler and needs administrator rights.
        // A non-elevated instance asks for them once instead of silently doing nothing.
        if (enabled ? !ScheduledTaskExists() : ScheduledTaskExists())
            ElevationService.TryRunElevatedAndWait("--apply-startup");

        if (!enabled)
        {
            ApplyRunEntry(enabled: false);
            return !ScheduledTaskExists();
        }

        if (ScheduledTaskExists())
        {
            ApplyRunEntry(enabled: false);
            return true;
        }

        // Without the task the monitor still starts, only without sensor access.
        ApplyRunEntry(enabled: true);
        return true;
    }

    public static void ApplyConfiguredStartup()
    {
        if (!ElevationService.IsAdministrator)
            return;

        try
        {
            ApplyStartup(UserSettings.StartWithWindows, UserSettings.ElevatedSensorAccess);
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
        }
    }

    public static bool TryStartConfiguredElevatedInstance()
    {
        if (ElevationService.IsAdministrator ||
            !UserSettings.StartWithWindows ||
            !UserSettings.ElevatedSensorAccess)
        {
            return false;
        }

        return RunTaskScheduler("/Run", "/TN", AppConstants.ScheduledTaskName);
    }

    public static void RemoveConfiguration()
    {
        try
        {
            ApplyRunEntry(enabled: false);
            if (ElevationService.IsAdministrator)
            {
                EndScheduledTask();
                DeleteScheduledTask();
            }
            UserSettings.Delete();
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
        }
    }

    private static void ApplyStartup(bool enabled, bool elevatedStartup)
    {
        if (!enabled)
        {
            ApplyRunEntry(enabled: false);
            if (ElevationService.IsAdministrator)
                DeleteScheduledTask();
            return;
        }

        if (elevatedStartup)
        {
            if (ElevationService.IsAdministrator && CreateOrUpdateScheduledTask())
                ApplyRunEntry(enabled: false);
            return;
        }

        ApplyRunEntry(enabled: true);
        if (ElevationService.IsAdministrator)
            DeleteScheduledTask();
    }

    private static void ApplyRunEntry(bool enabled)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(AppConstants.RegistryRunPath, writable: true) ??
                           Registry.CurrentUser.CreateSubKey(AppConstants.RegistryRunPath, writable: true);

        if (!enabled)
        {
            runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var command = $"\"{executable}\" --startup";
        if (!string.Equals(runKey.GetValue(RunValueName) as string, command, StringComparison.Ordinal))
            runKey.SetValue(RunValueName, command, RegistryValueKind.String);
    }

    private static bool CreateOrUpdateScheduledTask()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return false;

        var command = $"\"{executable}\" --startup";
        return RunTaskScheduler(
            "/Create",
            "/TN", AppConstants.ScheduledTaskName,
            "/TR", command,
            "/SC", "ONLOGON",
            "/IT",
            "/RL", "HIGHEST",
            "/F");
    }

    private static bool DeleteScheduledTask() => DeleteScheduledTask(AppConstants.ScheduledTaskName);

    public static bool DeleteScheduledTask(string taskName) =>
        !ScheduledTaskExists(taskName) ||
        RunTaskScheduler("/Delete", "/TN", taskName, "/F");

    private static bool ScheduledTaskExists() => ScheduledTaskExists(AppConstants.ScheduledTaskName);

    private static bool ScheduledTaskExists(string taskName) =>
        RunTaskScheduler(logFailure: false, "/Query", "/TN", taskName);

    private static bool EndScheduledTask() =>
        RunTaskScheduler("/End", "/TN", AppConstants.ScheduledTaskName);

    private static bool RunTaskScheduler(params string[] arguments) =>
        RunTaskScheduler(logFailure: true, arguments);

    private static bool RunTaskScheduler(bool logFailure, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            process.WaitForExit(10_000);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            if (process.ExitCode == 0)
                return true;

            var error = process.StandardError.ReadToEnd().Trim();
            if (logFailure && !string.IsNullOrEmpty(error))
                Logger.Write(new InvalidOperationException($"Task Scheduler failed: {error}"));
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
        }

        return false;
    }
}

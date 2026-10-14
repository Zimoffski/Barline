using System.IO;
using Microsoft.Win32;

namespace Barline.Services;

/// <summary>
/// One-way move from the program's former name, TaskbarMetrics (versions up to 3.2.1):
/// settings are carried over and the old autostart entries and data folder are removed,
/// so an upgrade leaves exactly one program behind. Every step is a no-op once done.
/// </summary>
internal static class LegacyMigration
{
    private const string LegacyName = "TaskbarMetrics";
    private const string LegacySettingsPath = @"Software\TaskbarMetrics";
    private const string LegacyScheduledTaskName = "TaskbarMetrics sensor access";

    public static void Run()
    {
        try
        {
            MoveSettings();
            RemoveRunEntry();

            // The old logon task was created elevated and can only be removed elevated;
            // the installer's elevated configuration step reaches this branch. The old data
            // folder is removed only unelevated: an elevated recursive delete inside the
            // user profile could be redirected by a link.
            if (ElevationService.IsAdministrator)
                StartupManager.DeleteScheduledTask(LegacyScheduledTaskName);
            else
                RemoveDataFolder();
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
        }
    }

    private static void MoveSettings()
    {
        using var legacy = Registry.CurrentUser.OpenSubKey(LegacySettingsPath, writable: false);
        if (legacy is null)
            return;

        using (var current = Registry.CurrentUser.CreateSubKey(AppConstants.RegistrySettingsPath, writable: true))
        {
            foreach (var name in legacy.GetValueNames())
            {
                // Values already written under the new name win.
                if (current.GetValue(name) is null && legacy.GetValue(name) is { } value)
                    current.SetValue(name, value, legacy.GetValueKind(name));
            }
        }

        legacy.Close();
        Registry.CurrentUser.DeleteSubKeyTree(LegacySettingsPath, throwOnMissingSubKey: false);
    }

    private static void RemoveRunEntry()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(AppConstants.RegistryRunPath, writable: true);
        runKey?.DeleteValue(LegacyName, throwOnMissingValue: false);
    }

    private static void RemoveDataFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyName);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }
}

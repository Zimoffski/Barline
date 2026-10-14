using Microsoft.Win32;

namespace Barline.Services;

internal static class UserSettings
{
    private const string StartWithWindowsName = "StartWithWindows";
    private const string ElevatedSensorAccessName = "ElevatedSensorAccess";
    private const string AlwaysOnTopName = "AlwaysOnTop";

    public static bool StartWithWindows
    {
        get => ReadBoolean(StartWithWindowsName, defaultValue: true);
        set => WriteBoolean(StartWithWindowsName, value);
    }

    public static bool ElevatedSensorAccess
    {
        get => ReadBoolean(ElevatedSensorAccessName, defaultValue: false);
        set => WriteBoolean(ElevatedSensorAccessName, value);
    }

    // On by default: staying readable over fullscreen games and video is the main feature.
    public static bool AlwaysOnTop
    {
        get => ReadBoolean(AlwaysOnTopName, defaultValue: true);
        set => WriteBoolean(AlwaysOnTopName, value);
    }

    public static void EnsureDefaults()
    {
        using var key = OpenKey(writable: true);
        if (key.GetValue(StartWithWindowsName) is null)
            key.SetValue(StartWithWindowsName, 1, RegistryValueKind.DWord);
    }

    public static void Delete()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(AppConstants.RegistrySettingsPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
        }
    }

    private static bool ReadBoolean(string name, bool defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppConstants.RegistrySettingsPath, writable: false);
            return key?.GetValue(name) is int value ? value != 0 : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private static void WriteBoolean(string name, bool value)
    {
        using var key = OpenKey(writable: true);
        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }

    private static RegistryKey OpenKey(bool writable) =>
        Registry.CurrentUser.CreateSubKey(AppConstants.RegistrySettingsPath, writable);
}

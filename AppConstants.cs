using System.Runtime.InteropServices;

// P/Invoke targets (user32, gdi32, nvml, ...) are loaded from System32 only, never from the
// current directory or PATH.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Barline;

internal static class AppConstants
{
    public const string ProductName = "Barline";
    public const string WindowTitle = "BarlineOverlay";
    public const string MutexName = @"Local\Barline.SingleInstance";
    public const string ActivationEventName = @"Local\Barline.Activate";
    public const string ScheduledTaskName = "Barline sensor access";
    public const string RegistrySettingsPath = @"Software\Barline";
    public const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
}

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Barline.Services;

internal static class ElevationService
{
    private static readonly Lazy<bool> Administrator = new(() =>
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    });

    // The process token cannot change while the process runs, so it is checked once.
    public static bool IsAdministrator => Administrator.Value;

    /// <summary>
    /// Runs a short elevated helper command of this executable and waits for it.
    /// Returns false when the user declines the UAC prompt.
    /// </summary>
    public static bool TryRunElevatedAndWait(string arguments)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            });

            return process is not null && process.WaitForExit(30_000) && process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user cancelled the UAC prompt.
            return false;
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
            return false;
        }
    }

    public static bool TryStartElevatedReplacement()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--replace --enable-sensor-access",
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user cancelled the UAC prompt.
            return false;
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
            return false;
        }
    }
}

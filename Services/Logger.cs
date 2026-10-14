using System.IO;

namespace Barline.Services;

internal static class Logger
{
    private const long MaxLogBytes = 1024 * 1024;
    private static readonly TimeSpan RepeatSuppression = TimeSpan.FromMinutes(10);
    private static readonly object Sync = new();
    private static readonly Dictionary<string, DateTime> LastWrittenUtc = new(StringComparer.Ordinal);

    public static void Write(Exception exception)
    {
        try
        {
            var directory = GetDataDirectory();
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "error.log");
            var nowUtc = DateTime.UtcNow;

            lock (Sync)
            {
                // A sensor that fails every second must not write a stack trace every second.
                var signature = $"{exception.GetType().FullName}|{exception.Message}|{exception.TargetSite}";
                if (LastWrittenUtc.TryGetValue(signature, out var lastUtc) && nowUtc - lastUtc < RepeatSuppression)
                    return;

                LastWrittenUtc[signature] = nowUtc;

                var file = new FileInfo(path);
                if (file.Exists && file.Length > MaxLogBytes)
                    File.Move(path, Path.Combine(directory, "error.old.log"), overwrite: true);

                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\r\n\r\n");
            }
        }
        catch
        {
            // Logging must never crash the monitor.
        }
    }

    public static string WriteDiagnostics(string report)
    {
        var directory = GetDataDirectory();
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "diagnostics.txt");
        lock (Sync)
            File.WriteAllText(path, report);

        return path;
    }

    /// <summary>
    /// An elevated monitor writes into the install folder (Program Files), which unelevated
    /// processes cannot modify; writing into the user profile would let them redirect the
    /// elevated write with a link. Unelevated runs use %LOCALAPPDATA%.
    /// </summary>
    public static string GetDataDirectory() => ElevationService.IsAdministrator
        ? Path.Combine(AppContext.BaseDirectory, "logs")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.ProductName);
}

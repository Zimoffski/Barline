using System.Diagnostics;
using System.Globalization;
using System.IO;
using Barline.Native;

namespace Barline.Services;

/// <summary>
/// Reads displayed-frame timing from Intel PresentMon without injecting code
/// into the foreground application.
/// </summary>
internal sealed class ForegroundFpsReader : IDisposable
{
    private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SampleExpiry = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(30);
    private const string TraceSessionName = "BarlineFPS";
    private const string FrameHostApplication = "ApplicationFrameHost";
    private static readonly HashSet<string> ExcludedApplications = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm",
        "explorer",
        "presentmon-2.5.1-x64",
        "barline"
    };

    private readonly object _sync = new();
    // Application -> swap chain -> recent frames. Swap chains are kept apart so that an
    // overlay presenting at 60 Hz inside a 144 Hz game does not blend into one number.
    private readonly Dictionary<string, Dictionary<string, Queue<FrameSample>>> _samples =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _canTrace = ElevationService.IsAdministrator;
    private uint _cachedProcessId;
    private string? _cachedApplication;
    private DateTime _cachedAtUtc;
    private readonly CancellationTokenSource _cancellation = new();
    private Process? _presentMon;
    private DateTime _lastStartAttemptUtc = DateTime.MinValue;
    private bool _disposed;

    public double? ReadFramesPerSecond()
    {
        EnsureStarted();

        var application = GetForegroundApplication();
        if (application is null || ExcludedApplications.Contains(application))
            return null;

        var now = DateTime.UtcNow;
        lock (_sync)
        {
            if (!_samples.TryGetValue(application, out var swapChains))
                return null;

            // The main swap chain of the application is the one that shows the most frames.
            Queue<FrameSample>? busiest = null;
            foreach (var samples in swapChains.Values)
            {
                Trim(samples, now - SampleWindow);
                if (samples.Count == 0 || now - samples.Last().CapturedAtUtc > SampleExpiry)
                    continue;

                if (busiest is null || samples.Count > busiest.Count)
                    busiest = samples;
            }

            if (busiest is null)
                return null;

            // Frames per second = displayed frames / time they were on screen.
            // Long frames are stutters and must lower the value, so nothing is trimmed.
            var frameCount = 0;
            var totalMilliseconds = 0.0;
            foreach (var sample in busiest)
            {
                if (sample.DisplayedMilliseconds is < 0.1 or > 5000)
                    continue;

                frameCount++;
                totalMilliseconds += sample.DisplayedMilliseconds;
            }

            return frameCount > 0 && totalMilliseconds > 0
                ? frameCount * 1000.0 / totalMilliseconds
                : null;
        }
    }

    /// <summary>
    /// Stops the ETW session left behind when the helper was terminated forcibly.
    /// </summary>
    public static void StopTraceSession()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "logman.exe"),
                Arguments = $"stop {TraceSessionName} -ets",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit(5000);
        }
        catch
        {
            // The session usually does not exist; nothing to clean up then.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellation.Cancel();

        lock (_sync)
        {
            if (_presentMon is { HasExited: false })
            {
                try
                {
                    _presentMon.Kill(entireProcessTree: true);
                    _presentMon.WaitForExit(1500);
                }
                catch
                {
                    // The helper may already be exiting.
                }
            }

            _presentMon?.Dispose();
            _presentMon = null;
        }

        _cancellation.Dispose();
    }

    private void EnsureStarted()
    {
        // PresentMon cannot open an ETW session without administrator rights.
        if (_disposed || !_canTrace)
            return;

        lock (_sync)
        {
            if (_presentMon is not null)
            {
                try
                {
                    if (!_presentMon.HasExited)
                        return;
                }
                catch
                {
                    // The process object is no longer usable; replace it below.
                }

                _presentMon.Dispose();
                _presentMon = null;
            }

            var now = DateTime.UtcNow;
            if (now - _lastStartAttemptUtc < RestartDelay)
                return;

            _lastStartAttemptUtc = now;
            var executable = Path.Combine(AppContext.BaseDirectory, "PresentMon-2.5.1-x64.exe");
            if (!File.Exists(executable))
                return;

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--output_stdout");
            startInfo.ArgumentList.Add("--no_console_stats");
            startInfo.ArgumentList.Add("--exclude_dropped");
            startInfo.ArgumentList.Add("--no_track_gpu");
            startInfo.ArgumentList.Add("--no_track_input");
            startInfo.ArgumentList.Add("--session_name");
            startInfo.ArgumentList.Add(TraceSessionName);
            startInfo.ArgumentList.Add("--stop_existing_session");
            foreach (var application in ExcludedApplications)
            {
                startInfo.ArgumentList.Add("--exclude");
                startInfo.ArgumentList.Add(application + ".exe");
            }

            try
            {
                _presentMon = Process.Start(startInfo);
                if (_presentMon is not null)
                {
                    _ = Task.Run(
                        () => ReadFramesAsync(_presentMon, _cancellation.Token),
                        _cancellation.Token);
                    _ = Task.Run(
                        () => DrainErrorsAsync(_presentMon, _cancellation.Token),
                        _cancellation.Token);
                }
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
                _presentMon?.Dispose();
                _presentMon = null;
            }
        }
    }

    private async Task ReadFramesAsync(Process process, CancellationToken cancellationToken)
    {
        var applicationIndex = -1;
        var swapChainIndex = -1;
        var frameDurationIndex = -1;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;

                var values = ParseCsvLine(line);
                if (values.Count == 0)
                    continue;

                if (applicationIndex < 0 || frameDurationIndex < 0)
                {
                    var headerApplicationIndex = values.FindIndex(value => value.TrimStart('\uFEFF') == "Application");
                    if (headerApplicationIndex >= 0)
                    {
                        applicationIndex = headerApplicationIndex;
                        swapChainIndex = values.FindIndex(value => value == "SwapChainAddress");
                        frameDurationIndex = values.FindIndex(
                            value => value is "MsBetweenDisplayChange" or "DisplayedTime");
                    }

                    continue;
                }

                if (values.Count <= Math.Max(applicationIndex, frameDurationIndex) ||
                    !double.TryParse(
                        values[frameDurationIndex],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var displayedMilliseconds))
                {
                    continue;
                }

                var application = NormalizeApplication(values[applicationIndex]);
                if (string.IsNullOrEmpty(application) || ExcludedApplications.Contains(application))
                    continue;

                var swapChain = swapChainIndex >= 0 && swapChainIndex < values.Count
                    ? values[swapChainIndex]
                    : string.Empty;
                Record(application, swapChain, displayedMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown.
        }
        catch (Exception ex) when (!_disposed)
        {
            Logger.Write(ex);
        }
    }

    private static async Task DrainErrorsAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await process.StandardError.ReadLineAsync(cancellationToken) is not null)
            {
                // PresentMon warnings are intentionally not surfaced every second.
            }
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown.
        }
    }

    private void Record(string application, string swapChain, double displayedMilliseconds)
    {
        var now = DateTime.UtcNow;
        lock (_sync)
        {
            if (!_samples.TryGetValue(application, out var swapChains))
            {
                swapChains = new Dictionary<string, Queue<FrameSample>>(StringComparer.OrdinalIgnoreCase);
                _samples[application] = swapChains;
            }

            if (!swapChains.TryGetValue(swapChain, out var samples))
            {
                // Swap chains of closed windows are dropped once they expire.
                var stale = swapChains
                    .Where(pair => pair.Value.Count == 0 || now - pair.Value.Last().CapturedAtUtc > SampleExpiry)
                    .Select(pair => pair.Key)
                    .ToList();
                foreach (var key in stale)
                    swapChains.Remove(key);

                samples = new Queue<FrameSample>();
                swapChains[swapChain] = samples;
            }

            samples.Enqueue(new FrameSample(now, displayedMilliseconds));
            Trim(samples, now - SampleExpiry);
            while (samples.Count > 512)
                samples.Dequeue();
        }
    }

    private string? GetForegroundApplication()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == nint.Zero ||
            NativeMethods.GetWindowThreadProcessId(foreground, out var processId) == 0 ||
            processId == 0)
        {
            return null;
        }

        var application = GetProcessName(processId);

        // Store apps are framed by ApplicationFrameHost; the app itself owns a child window.
        if (string.Equals(application, FrameHostApplication, StringComparison.OrdinalIgnoreCase))
        {
            var hostedProcessId = FindHostedProcessId(foreground, processId);
            if (hostedProcessId != 0)
                application = GetProcessName(hostedProcessId);
        }

        return application;
    }

    private string? GetProcessName(uint processId)
    {
        lock (_sync)
        {
            // A short lifetime protects against process id reuse.
            if (processId == _cachedProcessId && DateTime.UtcNow - _cachedAtUtc < TimeSpan.FromSeconds(5))
                return _cachedApplication;
        }

        string? name;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            name = NormalizeApplication(process.ProcessName);
        }
        catch
        {
            name = null;
        }

        lock (_sync)
        {
            _cachedProcessId = processId;
            _cachedApplication = name;
            _cachedAtUtc = DateTime.UtcNow;
        }

        return name;
    }

    private static uint FindHostedProcessId(nint frameWindow, uint frameProcessId)
    {
        uint hostedProcessId = 0;
        NativeMethods.EnumChildWindows(frameWindow, (child, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(child, out var childProcessId);
            if (childProcessId == 0 || childProcessId == frameProcessId)
                return true;

            hostedProcessId = childProcessId;
            return false;
        }, nint.Zero);

        return hostedProcessId;
    }

    private static void Trim(Queue<FrameSample> samples, DateTime cutoffUtc)
    {
        while (samples.Count > 0 && samples.Peek().CapturedAtUtc < cutoffUtc)
            samples.Dequeue();
    }

    private static string NormalizeApplication(string value)
    {
        var name = value.Trim().Trim('"');
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        values.Add(current.ToString());
        return values;
    }

    private sealed record FrameSample(DateTime CapturedAtUtc, double DisplayedMilliseconds);
}

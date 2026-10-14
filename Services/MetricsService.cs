using Barline.Models;

namespace Barline.Services;

internal sealed class MetricsService : IDisposable
{
    private readonly object _sync = new();
    private readonly CpuUsageReader _cpuUsage = new();
    private readonly NetworkUsageReader _networkUsage = new();
    private readonly HardwareSensorReader _hardware = new();
    private readonly ForegroundFpsReader _foregroundFps = new();

    public MetricsSnapshot ReadSnapshot()
    {
        lock (_sync)
        {
            var cpuLoad = _cpuUsage.ReadPercent();
            var memoryLoad = MemoryUsageReader.ReadPercent();
            var network = _networkUsage.ReadBytesPerSecond();
            var hardware = _hardware.Read();
            var framesPerSecond = _foregroundFps.ReadFramesPerSecond();

            return new MetricsSnapshot(
                cpuLoad,
                hardware.CpuTemperature,
                hardware.GpuLoad,
                hardware.GpuTemperature,
                memoryLoad,
                framesPerSecond,
                network.Download,
                network.Upload);
        }
    }

    public string CreateHardwareDiagnostics()
    {
        lock (_sync)
            return _hardware.CreateDiagnosticsReport();
    }

    public double? ReadForegroundFramesPerSecond() => _foregroundFps.ReadFramesPerSecond();

    public void Dispose()
    {
        lock (_sync)
        {
            _foregroundFps.Dispose();
            _hardware.Dispose();
            _networkUsage.Dispose();
        }
    }
}

namespace Barline.Models;

public sealed record MetricsSnapshot(
    double? CpuLoadPercent,
    double? CpuTemperatureC,
    double? GpuLoadPercent,
    double? GpuTemperatureC,
    double? MemoryLoadPercent,
    double? ForegroundFramesPerSecond,
    double? DownloadBytesPerSecond,
    double? UploadBytesPerSecond);

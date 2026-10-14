using System.Globalization;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace Barline.Services;

internal sealed class HardwareSensorReader : IDisposable
{
    private static readonly TimeSpan TemperatureGracePeriod = TimeSpan.FromSeconds(15);

    private readonly Computer _computer;
    private readonly NvmlGpuReader? _nvml = NvmlGpuReader.TryCreate();
    private bool _available = true;
    private double? _lastCpuTemperature;
    private DateTime _lastCpuTemperatureAtUtc;
    private double? _lastGpuTemperature;
    private DateTime _lastGpuTemperatureAtUtc;

    public HardwareSensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            // A number of boards expose the CPU package sensor through their
            // Super I/O controller rather than directly through the CPU node.
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };

        try
        {
            _computer.Open();
        }
        catch (Exception ex)
        {
            _available = false;
            Logger.Write(ex);
        }
    }

    public HardwareSample Read()
    {
        if (!_available)
            return HardwareSample.Empty;

        try
        {
            UpdateAvailableHardware();

            var cpuTemperature = KeepRecentTemperature(
                ReadCpuTemperature(),
                ref _lastCpuTemperature,
                ref _lastCpuTemperatureAtUtc);

            var (gpuLoad, currentGpuTemperature) = ReadGpu();
            var gpuTemperature = KeepRecentTemperature(
                currentGpuTemperature,
                ref _lastGpuTemperature,
                ref _lastGpuTemperatureAtUtc);

            return new HardwareSample(cpuTemperature, gpuLoad, gpuTemperature);
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
            return new HardwareSample(_lastCpuTemperature, null, _lastGpuTemperature);
        }
    }

    public string CreateDiagnosticsReport()
    {
        var report = new StringBuilder();
        report.AppendLine($"{AppConstants.ProductName} sensor diagnostics");
        report.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Windows: {Environment.OSVersion}");
        report.AppendLine($"64-bit process: {Environment.Is64BitProcess}");
        report.AppendLine($"Administrator: {YesNo(ElevationService.IsAdministrator)}");
        report.AppendLine($"LibreHardwareMonitor: {typeof(Computer).Assembly.GetName().Version}");
        report.AppendLine($"Sensor library available: {YesNo(_available)}");
        report.AppendLine($"NVIDIA via NVML: {YesNo(_nvml is not null)}");
        report.AppendLine();

        if (!_available)
        {
            report.AppendLine("The sensor library could not be opened. See error.log for details.");
            return report.ToString();
        }

        try
        {
            UpdateAvailableHardware(includeNvml: true);

            foreach (var hardware in EnumerateAllHardware())
            {
                report.AppendLine($"[{hardware.HardwareType}] {hardware.Name}");

                var relevantSensors = hardware.Sensors
                    .Where(sensor => sensor.SensorType is SensorType.Temperature or SensorType.Load)
                    .ToList();

                if (relevantSensors.Count == 0)
                {
                    report.AppendLine("  (no temperature/load sensors)");
                }
                else
                {
                    foreach (var sensor in relevantSensors)
                    {
                        var value = sensor.Value.HasValue ? sensor.Value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "no data";
                        report.AppendLine($"  {sensor.SensorType,-11} {sensor.Name}: {value}");
                    }
                }

                report.AppendLine();
            }

            var sample = Read();
            report.AppendLine($"Values shown by {AppConstants.ProductName}:");
            report.AppendLine($"  CPU temperature: {FormatDiagnosticTemperature(sample.CpuTemperature)}");
            report.AppendLine($"  GPU load: {FormatDiagnosticPercent(sample.GpuLoad)}");
            report.AppendLine($"  GPU temperature: {FormatDiagnosticTemperature(sample.GpuTemperature)}");

            if (!sample.CpuTemperature.HasValue && !sample.GpuTemperature.HasValue)
            {
                report.AppendLine();
                report.AppendLine(ElevationService.IsAdministrator
                    ? "Temperature sensors are not exposed to LibreHardwareMonitor by the firmware or driver."
                    : "Windows may block the temperature sensors. Restart the app with sensor access from the tray menu.");
            }
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
            report.AppendLine($"Error while reading sensors: {ex.Message}");
        }

        return report.ToString();
    }

    private void UpdateAvailableHardware(bool includeNvml = false)
    {
        foreach (var hardware in _computer.Hardware)
        {
            // NVIDIA cards are read through NVML; the full update is reserved for diagnostics.
            if (!includeNvml && UsesNvml(hardware))
                continue;

            try
            {
                UpdateRecursive(hardware);
            }
            catch (Exception ex)
            {
                // One broken controller must not suppress working CPU/GPU sensors.
                Logger.Write(ex);
            }
        }
    }

    private double? ReadCpuTemperature()
    {
        var candidates = new List<SensorCandidate>();

        foreach (var hardware in EnumerateAllHardware())
        {
            if (IsGpu(hardware.HardwareType))
                continue;

            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Temperature || !IsPlausibleTemperature(sensor.Value))
                    continue;

                var hardwareName = hardware.Name ?? string.Empty;
                var sensorName = sensor.Name ?? string.Empty;
                var combinedName = $"{hardwareName} {sensorName}";
                var score = hardware.HardwareType == HardwareType.Cpu ? 1000 : 0;

                if (sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase)) score += 300;
                if (sensorName.Contains("Tctl", StringComparison.OrdinalIgnoreCase)) score += 280;
                if (sensorName.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) score += 280;
                if (sensorName.Contains("Die", StringComparison.OrdinalIgnoreCase)) score += 220;
                if (sensorName.Contains("Average", StringComparison.OrdinalIgnoreCase)) score += 180;
                if (combinedName.Contains("CPU", StringComparison.OrdinalIgnoreCase)) score += 160;
                if (sensorName.Contains("CCD", StringComparison.OrdinalIgnoreCase)) score += 120;
                if (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase)) score += 80;

                if (combinedName.Contains("VRM", StringComparison.OrdinalIgnoreCase)) score -= 400;
                if (combinedName.Contains("PCH", StringComparison.OrdinalIgnoreCase)) score -= 400;
                if (combinedName.Contains("System", StringComparison.OrdinalIgnoreCase)) score -= 300;
                if (combinedName.Contains("Ambient", StringComparison.OrdinalIgnoreCase)) score -= 300;

                // Non-CPU hardware needs a clearly CPU-related sensor name.
                if (hardware.HardwareType != HardwareType.Cpu && score <= 0)
                    continue;

                candidates.Add(new SensorCandidate(score, sensor.Value!.Value));
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Value)
            .Select(candidate => (double?)candidate.Value)
            .FirstOrDefault();
    }

    private (double? Load, double? Temperature) ReadGpu()
    {
        var loadCandidates = new List<SensorCandidate>();
        var temperatureCandidates = new List<SensorCandidate>();

        foreach (var (nvmlLoad, nvmlTemperature) in _nvml?.Read() ?? [])
        {
            // Same weight as the "GPU Core" sensors of LibreHardwareMonitor.
            if (nvmlLoad is >= 0 and <= 100)
                loadCandidates.Add(new SensorCandidate(220, nvmlLoad.Value));
            if (IsPlausibleTemperature((float?)nvmlTemperature))
                temperatureCandidates.Add(new SensorCandidate(220, nvmlTemperature!.Value));
        }

        foreach (var hardware in EnumerateAllHardware())
        {
            if (!IsGpu(hardware.HardwareType) || UsesNvml(hardware))
                continue;

            foreach (var sensor in hardware.Sensors)
            {
                if (!sensor.Value.HasValue)
                    continue;

                var name = sensor.Name ?? string.Empty;
                if (sensor.SensorType == SensorType.Load && sensor.Value.Value is >= 0 and <= 100)
                {
                    var score = 0;
                    if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) score += 100;
                    if (name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase)) score += 95;
                    if (name.Contains("GPU", StringComparison.OrdinalIgnoreCase)) score += 70;
                    if (name.Contains("Core", StringComparison.OrdinalIgnoreCase)) score += 50;
                    if (name.Contains("Memory", StringComparison.OrdinalIgnoreCase)) score -= 30;

                    loadCandidates.Add(new SensorCandidate(score, sensor.Value.Value));
                }
                else if (sensor.SensorType == SensorType.Temperature && IsPlausibleTemperature(sensor.Value))
                {
                    var score = 0;
                    if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) score += 120;
                    if (name.Contains("Core", StringComparison.OrdinalIgnoreCase)) score += 100;
                    if (name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase)) score += 40;
                    if (name.Contains("Memory", StringComparison.OrdinalIgnoreCase)) score -= 20;

                    temperatureCandidates.Add(new SensorCandidate(score, sensor.Value.Value));
                }
            }
        }

        var load = loadCandidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Value)
            .Select(candidate => (double?)candidate.Value)
            .FirstOrDefault();

        var temperature = temperatureCandidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Value)
            .Select(candidate => (double?)candidate.Value)
            .FirstOrDefault();

        return (load, temperature);
    }

    private static double? KeepRecentTemperature(
        double? current,
        ref double? lastValue,
        ref DateTime lastValueAtUtc)
    {
        if (current.HasValue)
        {
            lastValue = current;
            lastValueAtUtc = DateTime.UtcNow;
            return current;
        }

        return lastValue.HasValue && DateTime.UtcNow - lastValueAtUtc <= TemperatureGracePeriod
            ? lastValue
            : null;
    }

    private IEnumerable<IHardware> EnumerateAllHardware()
    {
        foreach (var hardware in _computer.Hardware)
        {
            yield return hardware;

            foreach (var child in EnumerateChildren(hardware))
                yield return child;
        }
    }

    private static IEnumerable<IHardware> EnumerateChildren(IHardware hardware)
    {
        foreach (var child in hardware.SubHardware)
        {
            yield return child;

            foreach (var nested in EnumerateChildren(child))
                yield return nested;
        }
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var child in hardware.SubHardware)
            UpdateRecursive(child);
    }

    private bool UsesNvml(IHardware hardware) =>
        _nvml is not null && hardware.HardwareType == HardwareType.GpuNvidia;

    private static bool IsGpu(HardwareType type) =>
        type == HardwareType.GpuNvidia ||
        type == HardwareType.GpuAmd ||
        type == HardwareType.GpuIntel;

    private static bool IsPlausibleTemperature(float? value) => value is > 0 and < 130;

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string FormatDiagnosticTemperature(double? value) =>
        value.HasValue ? value.Value.ToString("0.#", CultureInfo.InvariantCulture) + " °C" : "no data";

    private static string FormatDiagnosticPercent(double? value) =>
        value.HasValue ? value.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "no data";

    public void Dispose()
    {
        _nvml?.Dispose();

        try
        {
            _computer.Close();
        }
        catch
        {
            // Ignore shutdown errors.
        }
    }

    private sealed record SensorCandidate(int Score, double Value);
}

internal sealed record HardwareSample(double? CpuTemperature, double? GpuLoad, double? GpuTemperature)
{
    public static HardwareSample Empty { get; } = new(null, null, null);
}

using System.Runtime.InteropServices;

namespace Barline.Services;

/// <summary>
/// Reads NVIDIA load and temperature straight from the driver's NVML library.
/// A full LibreHardwareMonitor update of an NVIDIA card also samples PCIe throughput and
/// other counters and costs about 80 ms every second; these two values cost well under 1 ms.
/// </summary>
internal sealed class NvmlGpuReader : IDisposable
{
    private const int Success = 0;
    private const int TemperatureGpu = 0;

    private readonly nint[] _devices;
    private bool _disposed;

    private NvmlGpuReader(nint[] devices)
    {
        _devices = devices;
    }

    public static NvmlGpuReader? TryCreate()
    {
        try
        {
            if (NativeNvml.nvmlInit_v2() != Success)
                return null;

            if (NativeNvml.nvmlDeviceGetCount_v2(out var count) != Success || count == 0)
            {
                _ = NativeNvml.nvmlShutdown();
                return null;
            }

            var devices = new List<nint>();
            for (uint index = 0; index < count; index++)
            {
                if (NativeNvml.nvmlDeviceGetHandleByIndex_v2(index, out var device) == Success)
                    devices.Add(device);
            }

            if (devices.Count == 0)
            {
                _ = NativeNvml.nvmlShutdown();
                return null;
            }

            return new NvmlGpuReader([.. devices]);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // No NVIDIA driver: LibreHardwareMonitor keeps handling every GPU.
            return null;
        }
    }

    public IEnumerable<(double? Load, double? Temperature)> Read()
    {
        if (_disposed)
            yield break;

        foreach (var device in _devices)
        {
            double? load = NativeNvml.nvmlDeviceGetUtilizationRates(device, out var utilization) == Success
                ? utilization.Gpu
                : null;
            double? temperature = NativeNvml.nvmlDeviceGetTemperature(device, TemperatureGpu, out var celsius) == Success
                ? celsius
                : null;

            yield return (load, temperature);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _ = NativeNvml.nvmlShutdown();
        }
        catch
        {
            // Ignore shutdown errors.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    private static class NativeNvml
    {
        [DllImport("nvml.dll")]
        public static extern int nvmlInit_v2();

        [DllImport("nvml.dll")]
        public static extern int nvmlShutdown();

        [DllImport("nvml.dll")]
        public static extern int nvmlDeviceGetCount_v2(out uint deviceCount);

        [DllImport("nvml.dll")]
        public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out nint device);

        [DllImport("nvml.dll")]
        public static extern int nvmlDeviceGetUtilizationRates(nint device, out Utilization utilization);

        [DllImport("nvml.dll")]
        public static extern int nvmlDeviceGetTemperature(nint device, int sensorType, out uint temperature);
    }
}

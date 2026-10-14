using Barline.Native;

namespace Barline.Services;

internal sealed class CpuUsageReader
{
    private bool _hasPrevious;
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;

    public double? ReadPercent()
    {
        if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return null;

        var idle = idleTime.ToUInt64();
        var kernel = kernelTime.ToUInt64();
        var user = userTime.ToUInt64();

        if (!_hasPrevious)
        {
            _hasPrevious = true;
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            return null;
        }

        var idleDelta = idle - _previousIdle;
        var kernelDelta = kernel - _previousKernel;
        var userDelta = user - _previousUser;

        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        var total = kernelDelta + userDelta;
        if (total == 0)
            return null;

        var busy = total > idleDelta ? total - idleDelta : 0;
        return Math.Clamp(busy * 100.0 / total, 0, 100);
    }
}

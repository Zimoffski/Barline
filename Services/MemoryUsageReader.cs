using Barline.Native;

namespace Barline.Services;

internal static class MemoryUsageReader
{
    public static double? ReadPercent()
    {
        var status = new NativeMethods.MemoryStatusEx();
        return NativeMethods.GlobalMemoryStatusEx(status) ? status.dwMemoryLoad : null;
    }
}

using System.Runtime.InteropServices;

namespace Barline.Native;

internal static class FullscreenDetector
{
    private const int EdgeTolerancePx = 2;

    public static bool IsForegroundFullscreenOnPrimaryMonitor(nint ownWindow)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == nint.Zero || foreground == ownWindow ||
            !NativeMethods.IsWindowVisible(foreground))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(
            foreground,
            NativeMethods.MonitorDefaultToNearest);
        if (monitor == nint.Zero)
            return false;

        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo) ||
            (monitorInfo.Flags & NativeMethods.MonitorInfoPrimary) == 0 ||
            !TryGetWindowBounds(foreground, out var windowBounds))
        {
            return false;
        }

        return CoversMonitor(windowBounds, monitorInfo.Monitor);
    }

    private static bool TryGetWindowBounds(nint window, out NativeMethods.Rect bounds)
    {
        if (NativeMethods.DwmGetWindowAttribute(
                window,
                NativeMethods.DwmwaExtendedFrameBounds,
                out bounds,
                Marshal.SizeOf<NativeMethods.Rect>()) == 0 &&
            bounds.Width > 0 && bounds.Height > 0)
        {
            return true;
        }

        return NativeMethods.GetWindowRect(window, out bounds) &&
               bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool CoversMonitor(NativeMethods.Rect window, NativeMethods.Rect monitor)
    {
        return window.Left <= monitor.Left + EdgeTolerancePx &&
               window.Top <= monitor.Top + EdgeTolerancePx &&
               window.Right >= monitor.Right - EdgeTolerancePx &&
               window.Bottom >= monitor.Bottom - EdgeTolerancePx;
    }
}

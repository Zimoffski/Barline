namespace Barline.Native;

internal static class TaskbarLocator
{
    public static bool TryGetPrimaryTaskbar(out nint taskbarHandle, out NativeMethods.Rect taskbarRect)
    {
        taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle != nint.Zero &&
            NativeMethods.GetWindowRect(taskbarHandle, out taskbarRect) &&
            taskbarRect.Width > 0 && taskbarRect.Height > 0)
        {
            return true;
        }

        taskbarRect = default;
        return false;
    }

    public static bool TryGetNotificationAreaRect(out NativeMethods.Rect trayRect)
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero)
        {
            trayRect = default;
            return false;
        }

        var tray = NativeMethods.FindWindowEx(taskbar, nint.Zero, "TrayNotifyWnd", null);
        if (tray == nint.Zero)
            tray = FindDescendantWindowByClass(taskbar, "TrayNotifyWnd");
        if (tray != nint.Zero && NativeMethods.GetWindowRect(tray, out trayRect) && trayRect.Width > 0)
            return true;

        trayRect = default;
        return false;
    }

    private static nint FindDescendantWindowByClass(nint parent, string expectedClass)
    {
        nint found = nint.Zero;

        NativeMethods.EnumChildWindows(parent, (hWnd, _) =>
        {
            if (string.Equals(GetWindowClassName(hWnd), expectedClass, StringComparison.Ordinal))
            {
                found = hWnd;
                return false;
            }

            return true;
        }, nint.Zero);

        return found;
    }

    private static string GetWindowClassName(nint hWnd)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(hWnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }
}

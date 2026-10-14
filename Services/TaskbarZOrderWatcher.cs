using Barline.Native;

namespace Barline.Services;

internal sealed class TaskbarZOrderWatcher : IDisposable
{
    private const string Windows11OverflowWindowClass = "TopLevelWindowForOverflowXamlIsland";
    private const string LegacyOverflowWindowClass = "NotifyIconOverflowWindow";

    private readonly Action _taskbarLayerChanged;
    private readonly NativeMethods.WinEventDelegate _winEventCallback;

    private nint _taskbarHandle;
    private nint _winEventHook;

    public TaskbarZOrderWatcher(Action taskbarLayerChanged)
    {
        _taskbarLayerChanged = taskbarLayerChanged;
        _winEventCallback = OnWinEvent;
    }

    public void Watch(nint taskbarHandle)
    {
        if (taskbarHandle == _taskbarHandle && _winEventHook != nint.Zero)
            return;

        StopWatching();

        _ = NativeMethods.GetWindowThreadProcessId(taskbarHandle, out var explorerProcessId);
        if (explorerProcessId == 0)
            return;

        _taskbarHandle = taskbarHandle;
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectShow,
            NativeMethods.EventObjectReorder,
            nint.Zero,
            _winEventCallback,
            explorerProcessId,
            0,
            NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess);

        if (_winEventHook == nint.Zero)
            _taskbarHandle = nint.Zero;
    }

    public void Dispose() => StopWatching();

    private void OnWinEvent(
        nint hWinEventHook,
        uint eventType,
        nint hWnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint eventTime)
    {
        if (hWnd == nint.Zero || _taskbarHandle == nint.Zero)
            return;

        var root = NativeMethods.GetAncestor(hWnd, NativeMethods.GaRoot);
        if (hWnd == _taskbarHandle ||
            root == _taskbarHandle ||
            NativeMethods.IsChild(_taskbarHandle, hWnd) ||
            IsTrayOverflowWindow(hWnd) ||
            IsTrayOverflowWindow(root))
        {
            _taskbarLayerChanged();
        }
    }

    private static bool IsTrayOverflowWindow(nint hWnd)
    {
        if (hWnd == nint.Zero)
            return false;

        var classNameBuffer = new char[128];
        var classNameLength = NativeMethods.GetClassName(
            hWnd,
            classNameBuffer,
            classNameBuffer.Length);
        if (classNameLength <= 0)
            return false;

        var className = new string(classNameBuffer, 0, classNameLength);
        return string.Equals(className, Windows11OverflowWindowClass, StringComparison.Ordinal) ||
               string.Equals(className, LegacyOverflowWindowClass, StringComparison.Ordinal);
    }

    private void StopWatching()
    {
        if (_winEventHook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = nint.Zero;
        }

        _taskbarHandle = nint.Zero;
    }
}

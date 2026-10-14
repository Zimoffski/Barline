using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Barline.Native;

namespace Barline.Services;

internal sealed class NetworkUsageReader : IDisposable
{
    // Enumerating adapters costs ~9 ms; reading counters of known adapters is nearly free.
    // The list is rebuilt when Windows reports a network change, and periodically as a fallback.
    private static readonly TimeSpan AdapterListLifetime = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, (long Received, long Sent)> _previous = new(StringComparer.Ordinal);
    private NetworkInterface[] _adapters = [];
    private DateTime _adaptersReadAtUtc = DateTime.MinValue;
    private volatile bool _adaptersChanged = true;
    private DateTime _previousAtUtc = DateTime.UtcNow;
    private bool _hasPrevious;

    public NetworkUsageReader()
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    public (double? Download, double? Upload) ReadBytesPerSecond()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _previousAtUtc).TotalSeconds;
        var current = new Dictionary<string, (long Received, long Sent)>(StringComparer.Ordinal);

        double bestTotal = -1;
        double? bestDownload = null;
        double? bestUpload = null;

        foreach (var nic in GetActiveAdapters(now))
        {
            try
            {
                var stats = nic.GetIPStatistics();
                var sample = (stats.BytesReceived, stats.BytesSent);
                current[nic.Id] = sample;

                if (!_hasPrevious || elapsed <= 0 || !_previous.TryGetValue(nic.Id, out var previous))
                    continue;

                var receivedDelta = sample.BytesReceived - previous.Received;
                var sentDelta = sample.BytesSent - previous.Sent;
                if (receivedDelta < 0 || sentDelta < 0)
                    continue;

                var download = receivedDelta / elapsed;
                var upload = sentDelta / elapsed;
                var total = download + upload;

                // Pick one busiest active adapter instead of summing adapters.
                // This avoids double-counting the same traffic when a VPN is active.
                if (total > bestTotal)
                {
                    bestTotal = total;
                    bestDownload = download;
                    bestUpload = upload;
                }
            }
            catch
            {
                // Some virtual adapters can disappear between enumeration and reading.
                _adaptersChanged = true;
            }
        }

        _previous.Clear();
        foreach (var pair in current)
            _previous[pair.Key] = pair.Value;

        _previousAtUtc = now;
        _hasPrevious = true;

        return (bestDownload, bestUpload);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
    }

    private NetworkInterface[] GetActiveAdapters(DateTime nowUtc)
    {
        if (!_adaptersChanged && nowUtc - _adaptersReadAtUtc < AdapterListLifetime)
            return _adapters;

        _adaptersChanged = false;
        _adaptersReadAtUtc = nowUtc;

        try
        {
            var active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToArray();

            // Physical adapters carry the real traffic. Virtual ones (VPN tunnels, WSL,
            // Hyper-V switches) can report mirrored counters, e.g. a TUN adapter showing
            // equal download and upload. They are used only when no physical adapter is up.
            var physical = active.Where(IsHardwareInterface).ToArray();
            _adapters = physical.Length > 0 ? physical : active;
        }
        catch
        {
            _adapters = [];
        }

        return _adapters;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _adaptersChanged = true;

    private static bool IsHardwareInterface(NetworkInterface nic)
    {
        int index;
        try
        {
            var properties = nic.GetIPProperties();
            index = properties.GetIPv4Properties()?.Index ?? properties.GetIPv6Properties()?.Index ?? 0;
        }
        catch
        {
            return false;
        }

        if (index == 0)
            return false;

        var row = Marshal.AllocHGlobal(NativeMethods.IfRow2Size);
        try
        {
            for (var offset = 0; offset < NativeMethods.IfRow2Size; offset++)
                Marshal.WriteByte(row, offset, 0);

            Marshal.WriteInt32(row, NativeMethods.IfRow2InterfaceIndexOffset, index);
            if (NativeMethods.GetIfEntry2(row) != 0)
                return false;

            var flags = Marshal.ReadByte(row, NativeMethods.IfRow2StatusFlagsOffset);
            return (flags & NativeMethods.IfFlagHardwareInterface) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(row);
        }
    }
}

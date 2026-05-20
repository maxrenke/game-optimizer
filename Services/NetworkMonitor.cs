using System.Net.NetworkInformation;

namespace GameOptimizer.Services;

public class NetworkMonitor
{
    public const int HistorySize = 300;

    private readonly int[] _rxHistory = new int[HistorySize];
    private readonly int[] _txHistory = new int[HistorySize];
    private int _idx;
    private long _prevRx, _prevTx;
    private DateTime _prevTime = DateTime.UtcNow;

    // Resolving a NIC enumerates every adapter - cache it and only
    // re-resolve when the cached adapter goes away (throws on query).
    private NetworkInterface? _nic;

    public string NicName { get; set; }

    public int[] RxHistory => _rxHistory;
    public int[] TxHistory => _txHistory;
    public int HistoryIndex => _idx;
    public int RxNow => _rxHistory[(_idx - 1 + HistorySize) % HistorySize];
    public int TxNow => _txHistory[(_idx - 1 + HistorySize) % HistorySize];
    public int RxPeak => _rxHistory.Max();
    public int TxPeak => _txHistory.Max();

    public NetworkMonitor(string nicName)
    {
        NicName = nicName;
        InitBaseline();
    }

    private void InitBaseline()
    {
        var stats = SafeGetStats();
        if (stats is null) return;
        _prevRx = stats.BytesReceived;
        _prevTx = stats.BytesSent;
        _prevTime = DateTime.UtcNow;
    }

    // Returns stats from the cached NIC; re-resolves once if the cached
    // adapter has gone away. Null when no matching adapter exists.
    private IPInterfaceStatistics? SafeGetStats()
    {
        _nic ??= FindNic(NicName);
        if (_nic is null) return null;
        try { return _nic.GetIPStatistics(); }
        catch { _nic = null; return null; }
    }

    public void Sample()
    {
        var stats = SafeGetStats();
        if (stats is null) return;
        var now = DateTime.UtcNow;
        var dt = (now - _prevTime).TotalSeconds;
        if (dt > 0 && _prevRx > 0)
        {
            var rxKbps = (int)Math.Max(0, (stats.BytesReceived - _prevRx) / dt / 1024);
            var txKbps = (int)Math.Max(0, (stats.BytesSent - _prevTx) / dt / 1024);
            _rxHistory[_idx] = rxKbps;
            _txHistory[_idx] = txKbps;
            _idx = (_idx + 1) % HistorySize;
        }
        _prevRx = stats.BytesReceived;
        _prevTx = stats.BytesSent;
        _prevTime = now;
    }

    // Name/description fragments that mark a virtual or VPN interface rather
    // than a real NIC carrying internet traffic.
    private static readonly string[] VirtualMarkers =
        ["virtual", "bluetooth", "tunnel", "vpn", "tailscale", "wireguard",
         "wintun", "tap-windows", "loopback", "vethernet"];

    private static bool IsRealAdapter(NetworkInterface n)
    {
        if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback
                                   or NetworkInterfaceType.Tunnel)
            return false;
        foreach (var m in VirtualMarkers)
            if (n.Description.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                n.Name.Contains(m, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static long RxBytes(NetworkInterface n)
    {
        try { return n.GetIPStatistics().BytesReceived; } catch { return 0; }
    }

    // Honor the configured NIC only if it is a real adapter; otherwise pick the
    // "Up" real adapter that has actually received the most traffic. Ordering
    // by bytes received - not link speed - avoids VPN tunnels that advertise a
    // huge fake speed (e.g. Tailscale at 100 Gbps) while carrying almost nothing.
    public static string AutoDetect(string preferred)
    {
        var match = FindNic(preferred);
        if (match is not null && IsRealAdapter(match)) return preferred;

        var best = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && IsRealAdapter(n))
            .OrderByDescending(RxBytes)
            .FirstOrDefault();
        return best?.Name ?? preferred;
    }

    private static NetworkInterface? FindNic(string name) =>
        NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

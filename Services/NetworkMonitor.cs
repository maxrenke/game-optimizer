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
        var nic = FindNic(NicName);
        if (nic is null) return;
        var stats = nic.GetIPStatistics();
        _prevRx = stats.BytesReceived;
        _prevTx = stats.BytesSent;
        _prevTime = DateTime.UtcNow;
    }

    public void Sample()
    {
        var nic = FindNic(NicName);
        if (nic is null) return;
        var stats = nic.GetIPStatistics();
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

    // Try named NIC first, then auto-detect the fastest "Up" physical adapter
    public static string AutoDetect(string preferred)
    {
        if (FindNic(preferred) is not null) return preferred;
        var fallback = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                     && !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                     && !n.Description.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.Speed)
            .FirstOrDefault();
        return fallback?.Name ?? preferred;
    }

    private static NetworkInterface? FindNic(string name) =>
        NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

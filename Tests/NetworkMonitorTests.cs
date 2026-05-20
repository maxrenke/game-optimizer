using GameOptimizer.Services;
using System.Net.NetworkInformation;
using Xunit;

namespace GameOptimizer.Tests;

public class NetworkMonitorTests
{
    [Fact]
    public void Constructor_DoesNotThrow_WithInvalidNic()
    {
        var ex = Record.Exception(() => new NetworkMonitor("NonExistentNic_XYZ_12345"));
        Assert.Null(ex);
    }

    [Fact]
    public void InitialState_IsAllZeros_WithInvalidNic()
    {
        var mon = new NetworkMonitor("NonExistentNic_XYZ_12345");
        Assert.Equal(0, mon.RxNow);
        Assert.Equal(0, mon.TxNow);
        Assert.Equal(0, mon.RxPeak);
        Assert.Equal(0, mon.TxPeak);
        Assert.Equal(0, mon.HistoryIndex);
    }

    [Fact]
    public void HistoryArrays_Are300Elements()
    {
        var mon = new NetworkMonitor("NonExistentNic_XYZ_12345");
        Assert.Equal(NetworkMonitor.HistorySize, mon.RxHistory.Length);
        Assert.Equal(NetworkMonitor.HistorySize, mon.TxHistory.Length);
        Assert.Equal(300, NetworkMonitor.HistorySize);
    }

    [Fact]
    public void AllHistorySlots_AreZero_InitiallyWithInvalidNic()
    {
        var mon = new NetworkMonitor("NonExistentNic_XYZ_12345");
        Assert.All(mon.RxHistory, v => Assert.Equal(0, v));
        Assert.All(mon.TxHistory, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Sample_DoesNotThrow_WithInvalidNic()
    {
        var mon = new NetworkMonitor("NonExistentNic_XYZ_12345");
        var ex = Record.Exception(() => mon.Sample());
        Assert.Null(ex);
    }

    [Fact]
    public void Sample_DoesNotAdvanceIndex_WhenNicMissing()
    {
        var mon = new NetworkMonitor("NonExistentNic_XYZ_12345");
        mon.Sample();
        mon.Sample();
        Assert.Equal(0, mon.HistoryIndex); // no NIC = no data written
    }

    private static readonly string[] VirtualMarkers =
        ["virtual", "bluetooth", "tunnel", "vpn", "tailscale", "wireguard",
         "wintun", "tap-windows", "vethernet"];

    private static bool LooksReal(NetworkInterface n) =>
        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
        !VirtualMarkers.Any(m =>
            n.Description.Contains(m, StringComparison.OrdinalIgnoreCase) ||
            n.Name.Contains(m, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AutoDetect_HonorsExistingRealAdapter()
    {
        var real = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && LooksReal(n));
        if (real is null) return; // skip if no real NIC (e.g. CI)
        Assert.Equal(real.Name, NetworkMonitor.AutoDetect(real.Name));
    }

    [Fact]
    public void AutoDetect_RejectsVirtualPreferred_WhenRealAdapterExists()
    {
        var all = NetworkInterface.GetAllNetworkInterfaces();
        var virtualNic = all.FirstOrDefault(n =>
            n.OperationalStatus == OperationalStatus.Up && !LooksReal(n));
        var realExists = all.Any(n =>
            n.OperationalStatus == OperationalStatus.Up && LooksReal(n));
        if (virtualNic is null || !realExists) return; // environment-dependent
        // A VPN tunnel / virtual adapter must not be honored over a real NIC
        Assert.NotEqual(virtualNic.Name, NetworkMonitor.AutoDetect(virtualNic.Name));
    }

    [Fact]
    public void AutoDetect_ReturnsSomething_WhenNicMissing()
    {
        var result = NetworkMonitor.AutoDetect("NonExistentNic_XYZ_12345");
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void AutoDetect_ResultIsNeverTunnelOrLoopback()
    {
        var result = NetworkMonitor.AutoDetect("NonExistentNic_XYZ_12345");
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Name.Equals(result, StringComparison.OrdinalIgnoreCase));
        if (nic is null) return; // returned the original name (no physical NIC) - acceptable
        Assert.NotEqual(NetworkInterfaceType.Loopback, nic.NetworkInterfaceType);
        Assert.NotEqual(NetworkInterfaceType.Tunnel, nic.NetworkInterfaceType);
    }
}

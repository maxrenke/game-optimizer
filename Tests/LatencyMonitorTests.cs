using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class LatencyMonitorTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var ex = Record.Exception(() => new LatencyMonitor("1.1.1.1"));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_BlankHost_DoesNotThrow()
    {
        // A blank host falls back to a default internally
        var ex = Record.Exception(() => new LatencyMonitor("   "));
        Assert.Null(ex);
    }

    [Fact]
    public void InitialState_IsZero()
    {
        var mon = new LatencyMonitor("1.1.1.1");
        Assert.Equal(0, mon.LastMs);
        Assert.Equal(0, mon.JitterMs);
        Assert.Equal(0, mon.HistoryIndex);
        Assert.False(mon.LastFailed);
    }

    [Fact]
    public void History_HasExpectedSize()
    {
        var mon = new LatencyMonitor("1.1.1.1");
        Assert.Equal(LatencyMonitor.HistorySize, mon.History.Length);
        Assert.Equal(120, LatencyMonitor.HistorySize);
    }

    [Fact]
    public async Task SampleAsync_UnresolvableHost_SetsFailed_NoThrow()
    {
        var mon = new LatencyMonitor("this-host-does-not-exist.invalid");
        var ex = await Record.ExceptionAsync(() => mon.SampleAsync());
        Assert.Null(ex);
        Assert.True(mon.LastFailed);
        Assert.Equal(0, mon.LastMs);       // no successful reading recorded
        Assert.Equal(0, mon.HistoryIndex); // history not advanced on failure
    }
}

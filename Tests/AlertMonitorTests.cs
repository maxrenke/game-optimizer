using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class AlertMonitorTests
{
    private static OptimizerConfig DefaultConfig() => new()
    {
        AlertGpuTempC = 80,
        AlertVramPct = 90,
        AlertGpuUtilPct = 95,
        AlertCpuZonePct = 90,
        AlertSustainedTicks = 4,
    };

    private static GpuData MakeGpu(int temp = 60, int util = 50, int memUsed = 4096, int memTot = 8192) =>
        new("Test", temp, util, 0, memUsed, memTot, 100, 250, 1800, 900);

    [Fact]
    public void GpuTempBelowThreshold_NoAlert()
    {
        var m = new AlertMonitor();
        m.Check(50, MakeGpu(temp: 79), DefaultConfig());
        Assert.Empty(m.Current);
    }

    [Fact]
    public void GpuTempAtThreshold_Alert()
    {
        var m = new AlertMonitor();
        m.Check(50, MakeGpu(temp: 80), DefaultConfig());
        Assert.Contains(m.Current, a => a.Contains("GPU temp"));
    }

    [Fact]
    public void VramAboveThreshold_Alert()
    {
        var m = new AlertMonitor();
        // 9830/10240 = 96% (above 90%)
        m.Check(50, MakeGpu(memUsed: 9830, memTot: 10240), DefaultConfig());
        Assert.Contains(m.Current, a => a.Contains("VRAM"));
    }

    [Fact]
    public void GpuUtilSustained_Alert()
    {
        var m = new AlertMonitor();
        var cfg = DefaultConfig();
        // Need AlertSustainedTicks=4 consecutive samples at or above threshold
        for (int i = 0; i < 4; i++)
            m.Check(50, MakeGpu(util: 95), cfg);
        Assert.Contains(m.Current, a => a.Contains("GPU util"));
    }

    [Fact]
    public void GpuUtilNotSustained_NoAlert()
    {
        var m = new AlertMonitor();
        var cfg = DefaultConfig();
        // 3 ticks high, then drops - resets counter
        for (int i = 0; i < 3; i++)
            m.Check(50, MakeGpu(util: 95), cfg);
        m.Check(50, MakeGpu(util: 50), cfg); // resets
        m.Check(50, MakeGpu(util: 95), cfg); // only 1 tick
        Assert.DoesNotContain(m.Current, a => a.Contains("GPU util"));
    }

    [Fact]
    public void CpuZoneSustained_Alert()
    {
        var m = new AlertMonitor();
        var cfg = DefaultConfig();
        for (int i = 0; i < 4; i++)
            m.Check(90, MakeGpu(util: 50), cfg);
        Assert.Contains(m.Current, a => a.Contains("CPU zone"));
    }
}

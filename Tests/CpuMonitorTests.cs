using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class CpuMonitorTests
{
    [Fact]
    public void ZonePct_EmptyData_ReturnsZero()
    {
        Assert.Equal(0, CpuMonitor.ZonePct([], 0xFFFF));
    }

    [Fact]
    public void ZonePct_MaskCoversAllCores_ReturnsAverage()
    {
        int[] data = [20, 40, 60, 80];
        long mask = 0b1111;
        Assert.Equal(50, CpuMonitor.ZonePct(data, mask));
    }

    [Fact]
    public void ZonePct_MaskCoversFirstCoreOnly()
    {
        int[] data = [75, 10, 10, 10];
        long mask = 0b0001;
        Assert.Equal(75, CpuMonitor.ZonePct(data, mask));
    }

    [Fact]
    public void ZonePct_MaskCoversLastCoreOnly()
    {
        int[] data = [10, 10, 10, 90];
        long mask = 0b1000;
        Assert.Equal(90, CpuMonitor.ZonePct(data, mask));
    }

    [Fact]
    public void ZonePct_MaskCoversMiddleTwoCores()
    {
        int[] data = [0, 60, 80, 0];
        long mask = 0b0110;
        Assert.Equal(70, CpuMonitor.ZonePct(data, mask));
    }

    [Fact]
    public void ZonePct_MaskMatchesNoCores_ReturnsZero()
    {
        int[] data = [50, 60, 70, 80];
        long mask = 0b10000; // bit 4 - no core at that index
        Assert.Equal(0, CpuMonitor.ZonePct(data, mask));
    }

    [Fact]
    public void ZonePct_ZeroMask_ReturnsZero()
    {
        int[] data = [50, 60, 70, 80];
        Assert.Equal(0, CpuMonitor.ZonePct(data, 0));
    }

    [Fact]
    public void ZonePct_AllZeroUtilization_ReturnsZero()
    {
        int[] data = [0, 0, 0, 0];
        long mask = 0b1111;
        Assert.Equal(0, CpuMonitor.ZonePct(data, mask));
    }

    [Fact]
    public void ZonePct_AllHundredPercent_Returns100()
    {
        int[] data = [100, 100, 100, 100];
        long mask = 0b1111;
        Assert.Equal(100, CpuMonitor.ZonePct(data, mask));
    }

    [Fact]
    public void ZonePct_HighCoreCount_16Cores()
    {
        var data = Enumerable.Repeat(50, 16).ToArray();
        long gameMask = 0x0FFF; // cores 0-11
        Assert.Equal(50, CpuMonitor.ZonePct(data, gameMask));
    }

    [Fact]
    public void ZonePct_NonContiguousMask_AveragesCorrectly()
    {
        // Mask 0b1010 = cores 1 and 3
        int[] data = [0, 40, 0, 100];
        long mask = 0b1010;
        Assert.Equal(70, CpuMonitor.ZonePct(data, mask));
    }
}

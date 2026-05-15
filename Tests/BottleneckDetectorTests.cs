using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class BottleneckDetectorTests
{
    private static GpuData MakeGpu(int util) =>
        new("Test", 60, util, 0, 4096, 8192, 100, 250, 1800, 900);

    [Fact]
    public void NoActiveGame_ReturnsNone()
    {
        var d = new BottleneckDetector();
        d.Update(80, MakeGpu(95), false);
        Assert.Equal(BottleneckState.None, d.Current);
    }

    [Fact]
    public void HighGpu_ReturnsGpu()
    {
        var d = new BottleneckDetector();
        // Fill the 5-sample window with high GPU
        for (int i = 0; i < 5; i++)
            d.Update(50, MakeGpu(91), true);
        Assert.Equal(BottleneckState.Gpu, d.Current);
    }

    [Fact]
    public void HighCpuLowGpu_ReturnsCpu()
    {
        var d = new BottleneckDetector();
        for (int i = 0; i < 5; i++)
            d.Update(76, MakeGpu(50), true);
        Assert.Equal(BottleneckState.Cpu, d.Current);
    }

    [Fact]
    public void BothMedium_ReturnsBalanced()
    {
        var d = new BottleneckDetector();
        for (int i = 0; i < 5; i++)
            d.Update(60, MakeGpu(60), true);
        Assert.Equal(BottleneckState.Balanced, d.Current);
    }

    [Fact]
    public void BothLow_ReturnsHeadroom()
    {
        var d = new BottleneckDetector();
        for (int i = 0; i < 5; i++)
            d.Update(30, MakeGpu(30), true);
        Assert.Equal(BottleneckState.Headroom, d.Current);
    }

    [Fact]
    public void RollingWindow_EarlySamplesCanBeOverridden()
    {
        // Start with high GPU readings but then shift to low - window rolls
        var d = new BottleneckDetector();

        // 3 high samples
        for (int i = 0; i < 3; i++)
            d.Update(50, MakeGpu(95), true);

        // After only 3 samples (window=5), avg is 57 which is Balanced not Gpu
        // The avg of [95,95,95,0,0] = 57
        Assert.NotEqual(BottleneckState.Gpu, d.Current);

        // Now fill window with 5 high GPU samples - should stabilize to Gpu
        for (int i = 0; i < 5; i++)
            d.Update(50, MakeGpu(95), true);
        Assert.Equal(BottleneckState.Gpu, d.Current);
    }
}

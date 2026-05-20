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
    public void FirstSample_ImmediatelyReflected_NotDraggedToZero()
    {
        // The window is seeded with the first sample, so a single high-GPU
        // update is recognized immediately rather than averaged against an
        // empty (zero-filled) history buffer.
        var d = new BottleneckDetector();
        d.Update(50, MakeGpu(95), true);
        Assert.Equal(BottleneckState.Gpu, d.Current);
    }

    [Fact]
    public void RollingWindow_OldSamplesAgeOut()
    {
        var d = new BottleneckDetector();

        // Saturate the window with high GPU -> GPU-bound
        for (int i = 0; i < 5; i++)
            d.Update(50, MakeGpu(95), true);
        Assert.Equal(BottleneckState.Gpu, d.Current);

        // Feed low samples - after 5 updates the window has fully rolled over
        for (int i = 0; i < 5; i++)
            d.Update(30, MakeGpu(30), true);
        Assert.Equal(BottleneckState.Headroom, d.Current);
    }
}

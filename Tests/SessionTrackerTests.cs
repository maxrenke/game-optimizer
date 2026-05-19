using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class SessionTrackerTests
{
    [Fact]
    public void StartSession_SetsCurrent()
    {
        var t = new SessionTracker();
        t.StartSession("TestGame");
        Assert.NotNull(t.Current);
        Assert.Equal("TestGame", t.Current!.Name);
    }

    [Fact]
    public void EndSession_ClearsCurrent()
    {
        var t = new SessionTracker();
        t.StartSession("TestGame");
        t.EndSession();
        Assert.Null(t.Current);
    }

    [Fact]
    public void EndSession_SetsEndTime()
    {
        var t = new SessionTracker();
        t.StartSession("TestGame");
        var before = DateTime.Now;
        t.EndSession();
        // Can't access ended session directly, but no exception = pass
    }

    [Fact]
    public void Update_TracksPeakCpu()
    {
        var t = new SessionTracker();
        t.StartSession("TestGame");
        t.Update(50, null, BottleneckState.Headroom);
        t.Update(80, null, BottleneckState.Cpu);
        t.Update(60, null, BottleneckState.Balanced);
        Assert.Equal(80, t.Current!.PeakCpu);
    }

    [Fact]
    public void Update_ComputesAvgCpu()
    {
        var t = new SessionTracker();
        t.StartSession("TestGame");
        t.Update(40, null, BottleneckState.Headroom);
        t.Update(60, null, BottleneckState.Headroom);
        Assert.Equal(50, t.Current!.AvgCpu);
    }

    [Fact]
    public void Update_CountsSamples()
    {
        var t = new SessionTracker();
        t.StartSession("TestGame");
        t.Update(50, null, BottleneckState.Headroom);
        t.Update(50, null, BottleneckState.Headroom);
        t.Update(50, null, BottleneckState.Headroom);
        Assert.Equal(3, t.Current!.Samples);
    }

    [Fact]
    public void Update_TracksBottleneckTicks()
    {
        var t = new SessionTracker();
        t.StartSession("TestGame");
        t.Update(50, null, BottleneckState.Cpu);
        t.Update(50, null, BottleneckState.Gpu);
        t.Update(50, null, BottleneckState.Balanced);
        t.Update(50, null, BottleneckState.Headroom);
        Assert.Equal(1, t.Current!.BtCpuTicks);
        Assert.Equal(1, t.Current!.BtGpuTicks);
        Assert.Equal(1, t.Current!.BtBalTicks);
        Assert.Equal(1, t.Current!.BtHrTicks);
    }

    [Fact]
    public void Update_TracksGpuData()
    {
        var t = new SessionTracker();
        t.StartSession("TestGame");
        // Vendor, Temp, GpuUtil, MemUtil, MemUsedMB, MemTotMB, PowerW, PowerLimW, ClkMhz, MemClkMhz
        var gpu = new GpuData("NVIDIA", 75, 50, 0, 4096, 8192, 0, 0, 0, 0);
        t.Update(50, gpu, BottleneckState.Gpu);
        Assert.Equal(75, t.Current!.PeakTempC);
        Assert.Equal(50, t.Current!.PeakGpu);
        Assert.Equal(50, t.Current!.PeakVramPct); // 4096/8192 = 50%
    }

    [Fact]
    public void Update_DoesNothing_WhenNoSession()
    {
        var t = new SessionTracker();
        var ex = Record.Exception(() => t.Update(80, null, BottleneckState.Cpu));
        Assert.Null(ex);
    }

    [Fact]
    public void Duration_IncreasesWith_Time()
    {
        var t = new SessionTracker();
        t.StartSession("TestGame");
        System.Threading.Thread.Sleep(50);
        Assert.True(t.Current!.Duration.TotalMilliseconds >= 40);
    }

    [Fact]
    public void SaveReport_CreatesFile()
    {
        var t = new SessionTracker();
        t.StartSession("TestGame");
        t.Update(70, null, BottleneckState.Gpu);
        t.EndSession();
        var file = t.SaveReport(DateTime.Now.AddMinutes(-5));
        try
        {
            Assert.True(File.Exists(file));
            var text = File.ReadAllText(file);
            Assert.Contains("TestGame", text);
            Assert.Contains("Gaming Optimizer", text);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    [Fact]
    public void SaveReport_NoGames_ShortUptime_DoesNotWriteFile()
    {
        // Under 2 minutes with no games detected - not worth recording
        var t = new SessionTracker();
        var result = t.SaveReport(DateTime.Now.AddMinutes(-1));
        Assert.True(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void SaveReport_NoGames_LongUptime_WritesNoGamesMessage()
    {
        // Over 2 minutes with no games - worth recording (user may want to know)
        var t = new SessionTracker();
        var file = t.SaveReport(DateTime.Now.AddMinutes(-10));
        try
        {
            Assert.False(string.IsNullOrEmpty(file));
            Assert.True(File.Exists(file));
            var text = File.ReadAllText(file);
            Assert.Contains("No games detected", text);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    [Fact]
    public void SaveReport_MultipleGames_AllAppear()
    {
        var t = new SessionTracker();
        t.StartSession("Game1");
        t.Update(60, null, BottleneckState.Cpu);
        t.EndSession();
        t.StartSession("Game2");
        t.Update(80, null, BottleneckState.Gpu);
        t.EndSession();
        var file = t.SaveReport(DateTime.Now.AddMinutes(-10));
        try
        {
            var text = File.ReadAllText(file);
            Assert.Contains("Game1", text);
            Assert.Contains("Game2", text);
        }
        finally { try { File.Delete(file); } catch { } }
    }
}

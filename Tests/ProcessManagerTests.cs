using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class ProcessManagerTests
{
    private static OptimizerConfig DefaultConfig() => new();

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var ex = Record.Exception(() => new ProcessManager(DefaultConfig()));
        Assert.Null(ex);
    }

    [Fact]
    public void PinningEnabled_DefaultIsFalse()
    {
        var pm = new ProcessManager(DefaultConfig());
        Assert.False(pm.PinningEnabled);
    }

    [Fact]
    public void ActiveGames_InitiallyEmpty()
    {
        var pm = new ProcessManager(DefaultConfig());
        Assert.Empty(pm.ActiveGames);
    }

    [Fact]
    public void RestoreAll_WithNoTrackedPids_DoesNotThrow()
    {
        var pm = new ProcessManager(DefaultConfig());
        var ex = Record.Exception(() => pm.RestoreAll());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_WithoutStartingWatchers_DoesNotThrow()
    {
        // Watchers are only created by StartWmiWatcher(); null-conditional in Dispose must handle this
        var pm = new ProcessManager(DefaultConfig());
        var ex = Record.Exception(() => pm.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Scan_DoesNotThrow()
    {
        var pm = new ProcessManager(DefaultConfig());
        var ex = Record.Exception(() => pm.Scan());
        Assert.Null(ex);
        pm.Dispose();
    }

    [Fact]
    public void Scan_ReturnsEmptyList_WhenNoGamePaths()
    {
        var pm = new ProcessManager(new OptimizerConfig { GamePaths = [] });
        var result = pm.Scan();
        Assert.NotNull(result);
        Assert.Empty(result);
        pm.Dispose();
    }

    [Fact]
    public void ThrottleBg_DoesNotThrow()
    {
        var pm = new ProcessManager(DefaultConfig());
        var ex = Record.Exception(() => pm.ThrottleBg());
        Assert.Null(ex);
        pm.Dispose();
    }

    [Fact]
    public void ReleasePinning_WithNoTrackedPids_DoesNotThrow()
    {
        var pm = new ProcessManager(DefaultConfig());
        var ex = Record.Exception(() => pm.ReleasePinning());
        Assert.Null(ex);
        pm.Dispose();
    }

    [Fact]
    public void GameProcessNames_InitiallyEmpty()
    {
        var pm = new ProcessManager(DefaultConfig());
        Assert.Empty(pm.GameProcessNames);
    }

    [Fact]
    public void BgProcessNames_InitiallyEmpty()
    {
        var pm = new ProcessManager(DefaultConfig());
        Assert.Empty(pm.BgProcessNames);
    }
}

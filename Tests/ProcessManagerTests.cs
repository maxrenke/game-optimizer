using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class ProcessManagerTests
{
    private static OptimizerConfig DefaultConfig() => new();

    // ── Construction ────────────────────────────────────────────────────────

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

    // ── Default behavior: PinningEnabled=false must not modify anything ──────

    [Fact]
    public void ThrottleBg_DoesNotThrow_WhenPinningDisabled()
    {
        var pm = new ProcessManager(DefaultConfig());
        Assert.False(pm.PinningEnabled);
        var ex = Record.Exception(() => pm.ThrottleBg());
        Assert.Null(ex);
        pm.Dispose();
    }

    [Fact]
    public void ThrottleBg_EmitsNoLogEntries_WhenPinningDisabled()
    {
        var pm = new ProcessManager(DefaultConfig());
        var logs = new List<string>();
        pm.LogEntry += logs.Add;

        pm.ThrottleBg();

        Assert.Empty(logs);
        pm.Dispose();
    }

    [Fact]
    public void Scan_EmitsNoFirefoxPinLog_WhenPinningDisabled()
    {
        var pm = new ProcessManager(new OptimizerConfig { GamePaths = [] });
        var logs = new List<string>();
        pm.LogEntry += logs.Add;
        Assert.False(pm.PinningEnabled);

        pm.Scan();

        Assert.DoesNotContain(logs, l => l.Contains("Firefox zone"));
        pm.Dispose();
    }

    [Fact]
    public void Scan_DoesNotThrow_WhenPinningDisabled()
    {
        var pm = new ProcessManager(DefaultConfig());
        Assert.False(pm.PinningEnabled);
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
    public void Scan_DoesNotPopulateActiveGames_WhenNoGamePaths()
    {
        var pm = new ProcessManager(new OptimizerConfig { GamePaths = [] });
        pm.Scan();
        Assert.Empty(pm.ActiveGames);
        pm.Dispose();
    }

    [Fact]
    public void ThrottleBg_DoesNotPopulateBgProcessNames_WhenPinningDisabled()
    {
        // ThrottleBg short-circuits when PinningEnabled=false, so nothing is tracked
        var pm = new ProcessManager(DefaultConfig());
        pm.ThrottleBg();
        Assert.Empty(pm.BgProcessNames);
        pm.Dispose();
    }

    [Fact]
    public void PinningEnabled_CanBeToggled()
    {
        var pm = new ProcessManager(DefaultConfig());
        Assert.False(pm.PinningEnabled);
        pm.PinningEnabled = true;
        Assert.True(pm.PinningEnabled);
        pm.PinningEnabled = false;
        Assert.False(pm.PinningEnabled);
        pm.Dispose();
    }

    // ── ClearState ───────────────────────────────────────────────────────────

    [Fact]
    public void ClearState_DoesNotThrow_WhenEmpty()
    {
        var pm = new ProcessManager(DefaultConfig());
        var ex = Record.Exception(() => pm.ClearState());
        Assert.Null(ex);
    }

    [Fact]
    public void ClearState_EmptiesActiveGames()
    {
        var pm = new ProcessManager(DefaultConfig());
        pm.ActiveGames[12345] = "fakegame";
        pm.ActiveGames[67890] = "anothergame";

        pm.ClearState();

        Assert.Empty(pm.ActiveGames);
    }

    [Fact]
    public void ClearState_EmptiesGameProcessNames()
    {
        var pm = new ProcessManager(DefaultConfig());
        pm.ActiveGames[1] = "fakegame";

        pm.ClearState();

        Assert.Empty(pm.GameProcessNames);
    }

    [Fact]
    public void ClearState_IsIdempotent()
    {
        var pm = new ProcessManager(DefaultConfig());
        pm.ActiveGames[1] = "fakegame";

        pm.ClearState();
        pm.ClearState(); // second call must not throw

        Assert.Empty(pm.ActiveGames);
    }

    [Fact]
    public void ClearState_DoesNotDispose_OrBreakSubsequentScans()
    {
        var pm = new ProcessManager(new OptimizerConfig { GamePaths = [] });
        pm.ActiveGames[1] = "fakegame";
        pm.ClearState();

        // Should be able to scan normally after a ClearState
        var ex = Record.Exception(() => pm.Scan());
        Assert.Null(ex);
        Assert.Empty(pm.ActiveGames);
        pm.Dispose();
    }

    // ── RestoreAll ───────────────────────────────────────────────────────────

    [Fact]
    public void RestoreAll_WithNoTrackedPids_DoesNotThrow()
    {
        var pm = new ProcessManager(DefaultConfig());
        var ex = Record.Exception(() => pm.RestoreAll());
        Assert.Null(ex);
    }

    [Fact]
    public void RestoreAll_AfterClearState_DoesNotThrow()
    {
        var pm = new ProcessManager(DefaultConfig());
        pm.ActiveGames[1] = "fakegame";
        pm.ClearState();
        var ex = Record.Exception(() => pm.RestoreAll());
        Assert.Null(ex);
    }

    // ── ReleasePinning / Dispose ─────────────────────────────────────────────

    [Fact]
    public void ReleasePinning_WithNoTrackedPids_DoesNotThrow()
    {
        var pm = new ProcessManager(DefaultConfig());
        var ex = Record.Exception(() => pm.ReleasePinning());
        Assert.Null(ex);
        pm.Dispose();
    }

    [Fact]
    public void Dispose_WithoutStartingWatchers_DoesNotThrow()
    {
        var pm = new ProcessManager(DefaultConfig());
        var ex = Record.Exception(() => pm.Dispose());
        Assert.Null(ex);
    }
}

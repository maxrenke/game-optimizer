using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

/// <summary>
/// Tests for OptimizerService that do NOT call Start() to avoid background threads
/// and WMI watchers. Covers construction, default state, ResetAll, and safe disposal.
/// </summary>
public class OptimizerServiceTests : IDisposable
{
    private static OptimizerConfig DefaultConfig() => new() { GamePaths = [] };

    private readonly List<OptimizerService> _services = [];

    private OptimizerService Make(OptimizerConfig? cfg = null)
    {
        var svc = new OptimizerService(cfg ?? DefaultConfig());
        _services.Add(svc);
        return svc;
    }

    public void Dispose()
    {
        foreach (var svc in _services)
            try { svc.Dispose(); } catch { }
    }

    // ── Construction ────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var ex = Record.Exception(() => Make());
        Assert.Null(ex);
    }

    [Fact]
    public void PinningEnabled_DefaultIsFalse()
    {
        var svc = Make();
        Assert.False(svc.PinningEnabled);
    }

    // ── Stop / Dispose ───────────────────────────────────────────────────────

    [Fact]
    public void Stop_WithoutStart_DoesNotThrow()
    {
        var svc = Make();
        var ex = Record.Exception(() => svc.Stop());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_WithoutStart_DoesNotThrow()
    {
        var svc = Make();
        var ex = Record.Exception(() => svc.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Stop_CalledTwice_DoesNotThrow()
    {
        var svc = Make();
        var ex = Record.Exception(() =>
        {
            svc.Stop();
            svc.Stop();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var svc = Make();
        var ex = Record.Exception(() =>
        {
            svc.Dispose();
            svc.Dispose();
        });
        Assert.Null(ex);
    }

    // ── ResetAll ─────────────────────────────────────────────────────────────

    [Fact]
    public void ResetAll_WithoutStart_DoesNotThrow()
    {
        var svc = Make();
        var ex = Record.Exception(() => svc.ResetAll());
        Assert.Null(ex);
    }

    [Fact]
    public void ResetAll_SetsPinningEnabledToFalse()
    {
        var svc = Make();
        // Force it true via the property setter so we can verify Reset turns it off
        // (Setting true calls RestorePinning, which may silently fail without admin - that's fine)
        try { svc.PinningEnabled = true; } catch { }

        svc.ResetAll();

        Assert.False(svc.PinningEnabled);
    }

    [Fact]
    public void ResetAll_IsIdempotent()
    {
        var svc = Make();
        var ex = Record.Exception(() =>
        {
            svc.ResetAll();
            svc.ResetAll();
        });
        Assert.Null(ex);
        Assert.False(svc.PinningEnabled);
    }

    [Fact]
    public void ResetAll_AfterStop_DoesNotThrow()
    {
        var svc = Make();
        svc.Stop();
        var ex = Record.Exception(() => svc.ResetAll());
        Assert.Null(ex);
    }

    // ── PinningEnabled ───────────────────────────────────────────────────────

    [Fact]
    public void PinningEnabled_CanBeSetFalseWhenAlreadyFalse()
    {
        var svc = Make();
        Assert.False(svc.PinningEnabled);
        svc.PinningEnabled = false; // should be a no-op, not throw
        Assert.False(svc.PinningEnabled);
    }

    // ── SnapshotReady event ──────────────────────────────────────────────────

    [Fact]
    public void SnapshotReady_CanSubscribeAndUnsubscribe_WithoutThrow()
    {
        var svc = Make();
        Action<OptimizerSnapshot> handler = _ => { };
        var ex = Record.Exception(() =>
        {
            svc.SnapshotReady += handler;
            svc.SnapshotReady -= handler;
        });
        Assert.Null(ex);
    }

    // ── ReportCount ──────────────────────────────────────────────────────────

    [Fact]
    public void ReportCount_WithoutStart_ReturnsNonNegative()
    {
        var svc = Make();
        var count = svc.ReportCount();
        Assert.True(count >= 0);
    }
}

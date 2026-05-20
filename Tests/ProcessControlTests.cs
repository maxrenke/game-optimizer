using System.Diagnostics;
using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class ProcessControlTests
{
    // A PID that is guaranteed not to exist: start a trivial process,
    // wait for it to exit, and reuse its (now-dead) PID.
    private static int DeadPid()
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        p.WaitForExit();
        return p.Id;
    }

    [Fact]
    public void Suspend_OnDeadPid_ReturnsFalse_NoThrow()
    {
        var dead = DeadPid();
        bool result = true;
        var ex = Record.Exception(() => result = ProcessControl.Suspend(dead));
        Assert.Null(ex);
        Assert.False(result);
    }

    [Fact]
    public void Resume_OnDeadPid_ReturnsFalse_NoThrow()
    {
        var dead = DeadPid();
        bool result = true;
        var ex = Record.Exception(() => result = ProcessControl.Resume(dead));
        Assert.Null(ex);
        Assert.False(result);
    }

    [Fact]
    public void SetIoPriorityVeryLow_OnDeadPid_ReturnsFalse_NoThrow()
    {
        var dead = DeadPid();
        bool result = true;
        var ex = Record.Exception(() => result = ProcessControl.SetIoPriorityVeryLow(dead));
        Assert.Null(ex);
        Assert.False(result);
    }

    [Fact]
    public void SetIoPriorityNormal_OnDeadPid_ReturnsFalse_NoThrow()
    {
        var dead = DeadPid();
        bool result = true;
        var ex = Record.Exception(() => result = ProcessControl.SetIoPriorityNormal(dead));
        Assert.Null(ex);
        Assert.False(result);
    }
}

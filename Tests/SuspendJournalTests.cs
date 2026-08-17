using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class SuspendJournalTests
{
    private static SuspendJournal.Entry Sample(long ticks = 1_000_000) => new()
    {
        Pid = 4242,
        ProcessName = "onedrive",
        StartTimeUtcTicks = ticks,
    };

    [Fact]
    public void Matches_SameNameAndStartTime_IsTrue()
    {
        Assert.True(SuspendJournal.Matches(Sample(), "onedrive", 1_000_000));
    }

    [Fact]
    public void Matches_IsCaseInsensitiveOnName()
    {
        Assert.True(SuspendJournal.Matches(Sample(), "OneDrive", 1_000_000));
    }

    [Fact]
    public void Matches_RecycledPidWithDifferentName_IsFalse()
    {
        // The whole point of the guard: PID 4242 is something else now
        Assert.False(SuspendJournal.Matches(Sample(), "notepad", 1_000_000));
    }

    [Fact]
    public void Matches_SameNameButLaterStartTime_IsFalse()
    {
        // A relaunched OneDrive reusing the PID must not be resumed
        var later = 1_000_000 + TimeSpan.TicksPerSecond * 30;
        Assert.False(SuspendJournal.Matches(Sample(), "onedrive", later));
    }

    [Fact]
    public void Matches_SubSecondClockSkew_IsTolerated()
    {
        var skewed = 1_000_000 + (TimeSpan.TicksPerSecond / 2);
        Assert.True(SuspendJournal.Matches(Sample(), "onedrive", skewed));
    }

    [Fact]
    public void Matches_UnreadableStartTime_FailsClosed()
    {
        // No start time means the entry cannot be verified, so never resume
        Assert.False(SuspendJournal.Matches(Sample(), "onedrive", null));
    }

    [Fact]
    public void AddThenRemove_LeavesNoJournalFile()
    {
        var journal = new SuspendJournal();
        try
        {
            journal.Add(4242, "onedrive", 1_000_000);
            Assert.True(File.Exists(SuspendJournal.JournalPath));

            journal.Remove(4242);
            Assert.False(File.Exists(SuspendJournal.JournalPath));
        }
        finally
        {
            journal.Clear();
        }
    }

    [Fact]
    public void Clear_RemovesJournalFile()
    {
        var journal = new SuspendJournal();
        journal.Add(4242, "onedrive", 1_000_000);
        journal.Clear();
        Assert.False(File.Exists(SuspendJournal.JournalPath));
    }

    [Fact]
    public void RecoverOrphans_WithNoJournal_ReturnsZero()
    {
        new SuspendJournal().Clear();
        Assert.Equal(0, SuspendJournal.RecoverOrphans());
    }

    [Fact]
    public void RecoverOrphans_EntryForDeadPid_ResumesNothingAndClearsJournal()
    {
        var journal = new SuspendJournal();
        // PID 0 is the System Idle Process - GetProcessById throws for it, so
        // this exercises the "already exited" branch without freezing anything
        journal.Add(0, "definitely-not-running", 1_000_000);

        var logged = new List<string>();
        Assert.Equal(0, SuspendJournal.RecoverOrphans(logged.Add));
        Assert.False(File.Exists(SuspendJournal.JournalPath));
    }

    [Fact]
    public void RecoverOrphans_NeverResumesARecycledPid()
    {
        var journal = new SuspendJournal();
        var self = System.Diagnostics.Process.GetCurrentProcess();

        // Journal this live PID under the wrong name: recovery must refuse it
        journal.Add(self.Id, "some-other-process", 1_000_000);

        var logged = new List<string>();
        var resumed = SuspendJournal.RecoverOrphans(logged.Add);

        Assert.Equal(0, resumed);
        Assert.Contains(logged, l => l.Contains("PID reuse"));
    }

    /// <summary>
    /// End-to-end proof of the bug this journal exists for: a real process is
    /// frozen and its suspender vanishes without resuming it, exactly as a
    /// stowed-exception crash does. Recovery must unfreeze it from disk alone.
    /// </summary>
    [Fact]
    public void RecoverOrphans_UnfreezesAProcessStrandedByASimulatedCrash()
    {
        var journal = new SuspendJournal();
        var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", "/c timeout /t 60 /nobreak")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        Assert.NotNull(child);

        try
        {
            var ticks = SuspendJournal.StartTicks(child);
            Assert.NotNull(ticks);

            journal.Add(child.Id, child.ProcessName, ticks!.Value);
            Assert.True(ProcessControl.Suspend(child.Id));
            Assert.True(AllThreadsSuspended(child.Id), "child should be frozen");

            // Simulate the crash: the owning instance disappears without ever
            // calling ResumeAllSuspended, leaving only the on-disk journal.
            // (No Clear() here - that is precisely what a crash skips.)

            var resumed = SuspendJournal.RecoverOrphans();

            Assert.Equal(1, resumed);
            Assert.False(AllThreadsSuspended(child.Id), "child should be running again");
            Assert.False(File.Exists(SuspendJournal.JournalPath));
        }
        finally
        {
            try { child!.Kill(entireProcessTree: true); } catch { }
            child!.Dispose();
            journal.Clear();
        }
    }

    private static bool AllThreadsSuspended(int pid)
    {
        using var p = System.Diagnostics.Process.GetProcessById(pid);
        p.Refresh();
        var threads = p.Threads.Cast<System.Diagnostics.ProcessThread>().ToList();
        return threads.Count > 0 && threads.All(t =>
            t.ThreadState == System.Diagnostics.ThreadState.Wait &&
            t.WaitReason == System.Diagnostics.ThreadWaitReason.Suspended);
    }

    [Fact]
    public void JournalPath_SitsBesideTheConfigFile()
    {
        Assert.Equal(
            Path.GetDirectoryName(OptimizerConfig.ConfigPath),
            Path.GetDirectoryName(SuspendJournal.JournalPath));
    }
}

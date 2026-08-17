using System.Diagnostics;
using System.Text.Json;

namespace GameOptimizer.Services;

/// <summary>
/// On-disk record of every process this app has suspended, so a crash cannot
/// strand one frozen forever.
/// <para>
/// <c>NtSuspendProcess</c> increments a per-thread kernel suspend count that is
/// not tied to the caller's lifetime - Windows never auto-resumes a process when
/// whoever suspended it dies. Every in-process cleanup path
/// (<see cref="ProcessManager.ResumeAllSuspended"/>, <c>ReleasePinning</c>,
/// <c>Dispose</c>) depends on managed code running at shutdown, and a WinUI
/// stowed exception (0xC000027B) terminates the process without running any of
/// it. This journal survives that: entries are written before the suspend and
/// removed after the resume, and <see cref="RecoverOrphans"/> unfreezes whatever
/// the last run left behind.
/// </para>
/// </summary>
public sealed class SuspendJournal
{
    public sealed class Entry
    {
        public int Pid { get; set; }
        public string ProcessName { get; set; } = "";

        /// <summary>
        /// Process creation time. PIDs are recycled, so this is what stops a
        /// stale journal from resuming an unrelated process that inherited the
        /// number - a resume of the wrong target is worse than a missed one.
        /// </summary>
        public long StartTimeUtcTicks { get; set; }
    }

    public static string JournalPath => Path.Combine(
        Path.GetDirectoryName(OptimizerConfig.ConfigPath)!, "suspended.json");

    private readonly object _gate = new();
    private readonly Dictionary<int, Entry> _entries = [];

    /// <summary>
    /// Records a suspended process. Call this BEFORE the suspend - a journal
    /// entry with no matching frozen process is harmless (recovery just skips
    /// it), whereas a frozen process with no entry is the bug being fixed.
    /// </summary>
    public void Add(int pid, string name, long startTicks)
    {
        lock (_gate)
        {
            _entries[pid] = new Entry
            {
                Pid = pid,
                ProcessName = name,
                StartTimeUtcTicks = startTicks,
            };
            Flush();
        }
    }

    public void Remove(int pid)
    {
        lock (_gate)
        {
            if (_entries.Remove(pid)) Flush();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_entries.Count == 0) return;
            _entries.Clear();
            Flush();
        }
    }

    // Caller holds _gate. Deletes the file rather than writing "[]" so a stale
    // journal never outlives the state it describes.
    private void Flush()
    {
        try
        {
            if (_entries.Count == 0)
            {
                if (File.Exists(JournalPath)) File.Delete(JournalPath);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
            File.WriteAllText(JournalPath,
                JsonSerializer.Serialize(_entries.Values.ToList(),
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { } // journal is a safety net; never let it break a real operation
    }

    /// <summary>
    /// Resumes every process left frozen by a previous run, then clears the
    /// journal. Safe to call at any time - an entry is only acted on when the
    /// live process still matches it exactly (see <see cref="Matches"/>).
    /// </summary>
    /// <returns>Number of processes actually resumed.</returns>
    public static int RecoverOrphans(Action<string>? log = null)
    {
        List<Entry>? entries;
        try
        {
            if (!File.Exists(JournalPath)) return 0;
            entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(JournalPath));
        }
        catch { entries = null; }

        if (entries is null || entries.Count == 0)
        {
            TryDeleteJournal();
            return 0;
        }

        var resumed = 0;
        foreach (var e in entries)
        {
            Process? p = null;
            try { p = Process.GetProcessById(e.Pid); } catch { }
            if (p is null)
            {
                log?.Invoke($"[RECOVER] {e.ProcessName} (PID {e.Pid}) already exited");
                continue;
            }

            using (p)
            {
                if (!Matches(e, p.ProcessName, StartTicks(p)))
                {
                    // PID was recycled - the process wearing this number now is
                    // not the one we froze. Leaving it alone is the safe error.
                    log?.Invoke($"[RECOVER] PID {e.Pid} is now '{p.ProcessName}', not " +
                                $"'{e.ProcessName}' - skipped (PID reuse)");
                    continue;
                }
                if (ProcessControl.Resume(e.Pid))
                {
                    resumed++;
                    log?.Invoke($"[RECOVER] Resumed {e.ProcessName} (PID {e.Pid}) " +
                                "left suspended by a previous run");
                }
                else
                {
                    log?.Invoke($"[RECOVER] Could not resume {e.ProcessName} (PID {e.Pid})");
                }
            }
        }

        TryDeleteJournal();
        return resumed;
    }

    /// <summary>
    /// True when a live process is the same one a journal entry describes.
    /// Name must match and creation time must agree to within a second - the
    /// tolerance absorbs FILETIME rounding without ever spanning two distinct
    /// processes, since a recycled PID is always created much later.
    /// A missing start time (access denied) fails closed.
    /// </summary>
    public static bool Matches(Entry e, string actualName, long? actualStartTicks)
    {
        if (!string.Equals(e.ProcessName, actualName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (actualStartTicks is null) return false;
        return Math.Abs(actualStartTicks.Value - e.StartTimeUtcTicks) <= TimeSpan.TicksPerSecond;
    }

    /// <summary>Creation time of a process in UTC ticks, or null if unreadable.</summary>
    public static long? StartTicks(Process p)
    {
        try { return p.StartTime.ToUniversalTime().Ticks; } catch { return null; }
    }

    private static void TryDeleteJournal()
    {
        try { if (File.Exists(JournalPath)) File.Delete(JournalPath); } catch { }
    }
}

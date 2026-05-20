using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;

namespace GameOptimizer.Services;

public class ProcessManager : IDisposable
{
    private static readonly HashSet<string> NotAGame = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam","steamwebhelper","steamservice","gameoverlayui",
        "gogalaxy","gogalaxy-notifications","gogcomm","gogservices",
        "battle.net","epicgameslauncher","epicwebhelper",
        "unitycrashandler64","unitycrashandler32","crashreportclient",
        "unrealcefsubprocess","easyanticheat","easyanticheat_setup",
        "bsoverlay","nvcapcli","nvidiaoverlaycontainer",
        "gamebarftstserver","gamebarpresencewriter",
        "stardocklauncher","unins000",
        "quicksfv","sfv","md5","hashcheck"
    };

    private static readonly string[] BuiltInBgProcs =
    [
        "onedrive","icloudckks","iclouddrive","icloudservices","icloudhome",
        "phoneexperiencehost","crossdeviceservice",
        "malwarebytes","mbamservice","hearthstonedecktracker",
        "backgroundtaskhost","windowspackagemanagerserver",
        "hwinfo64","nahimicsvc32","nahimicsvc64",
        "unigetui","appcontrol"
    ];

    private readonly OptimizerConfig _cfg;
    private readonly IntPtr _allCores;

    public ConcurrentDictionary<int, string> ActiveGames { get; } = new();
    private readonly ConcurrentDictionary<int, string> _appliedMediaZone = new();
    private readonly ConcurrentDictionary<int, string> _appliedBgProcs = new();
    private readonly ConcurrentDictionary<int, byte> _pathFailCache = new();

    // Track every PID we've modified so ReleasePinning/RestoreAll only touches those
    private readonly ConcurrentDictionary<int, byte> _modifiedPids = new();

    // WMI event watchers for instant process start/stop detection
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;

    public bool PinningEnabled { get; set; } = false;

    public event Action<string>? LogEntry;

    public ProcessManager(OptimizerConfig cfg)
    {
        _cfg = cfg;
        long allBits = (1L << Environment.ProcessorCount) - 1;
        _allCores = (IntPtr)allBits;
    }

    public IntPtr FirefoxAffinity => (IntPtr)_cfg.FirefoxAffinityMask;
    public IntPtr BgAffinity => (IntPtr)_cfg.BgAffinityMask;

    public IReadOnlyList<string> GameProcessNames => [.. ActiveGames.Values.Distinct()];
    public IReadOnlyList<string> MediaProcessNames => [.. _appliedMediaZone.Values.Distinct()];
    public IReadOnlyList<string> BgProcessNames => [.. _appliedBgProcs.Values.Distinct()];

    private string[] AllBgProcs => [.. BuiltInBgProcs, .. _cfg.ExtraThrottledProcs];

    // Subscribe to WMI process start/stop events for instant detection.
    // Fires on a WMI thread - all handlers are thread-safe.
    public void StartWmiWatcher()
    {
        try
        {
            _startWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _startWatcher.EventArrived += OnProcessStarted;
            _startWatcher.Start();

            _stopWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _stopWatcher.EventArrived += OnProcessStopped;
            _stopWatcher.Start();
        }
        catch { } // Win32_ProcessStartTrace requires admin; silently degrade
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var name = e.NewEvent["ProcessName"]?.ToString()?.Replace(".exe", "", StringComparison.OrdinalIgnoreCase) ?? "";
            var pid  = Convert.ToInt32(e.NewEvent["ProcessID"]);

            // Check firefox/vlc immediately
            if (name.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("vlc", StringComparison.OrdinalIgnoreCase))
            {
                if (PinningEnabled)
                {
                    using var proc = SafeGetProcess(pid);
                    if (proc is not null && !_appliedMediaZone.ContainsKey(pid))
                    {
                        ApplyMediaZone(proc);
                        _appliedMediaZone[pid] = name;
                        LogEntry?.Invoke($"[MEDIA] Pinned {name} (PID {pid}) to media zone");
                    }
                }
                return;
            }

            // Check bg procs immediately
            if (AllBgProcs.Any(b => b.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                if (PinningEnabled)
                {
                    using var proc = SafeGetProcess(pid);
                    if (proc is not null)
                    {
                        try
                        {
                            proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                            proc.ProcessorAffinity = BgAffinity;
                            _modifiedPids[pid] = 0;
                            _appliedBgProcs[pid] = name;
                        }
                        catch { }
                    }
                }
                return;
            }

            // Defer game path check to the next Scan() tick (needs path resolution)
        }
        catch { }
    }

    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pid = Convert.ToInt32(e.NewEvent["ProcessID"]);
            if (ActiveGames.TryRemove(pid, out var name))
            {
                LogEntry?.Invoke($"[ENDED] {name} closed (PID {pid})");
            }
            _appliedMediaZone.TryRemove(pid, out _);
            _appliedBgProcs.TryRemove(pid, out _);
            _modifiedPids.TryRemove(pid, out _);
        }
        catch { }
    }

    public List<string> Scan()
    {
        var newGames = new List<string>();
        foreach (var proc in SafeGetProcesses())
        {
            using (proc)
            {
                if (ActiveGames.ContainsKey(proc.Id)) continue;
                if (IsExcluded(proc.ProcessName)) continue;
                var path = GetProcPath(proc);
                if (IsGamePath(path))
                {
                    if (ApplyGame(proc))
                    {
                        ActiveGames[proc.Id] = proc.ProcessName;
                        _modifiedPids[proc.Id] = 0;
                        newGames.Add(proc.ProcessName);
                        LogEntry?.Invoke($"[GAME] Detected: {proc.ProcessName} (PID {proc.Id})");
                    }
                }
            }
        }

        // Re-apply affinity each scan to counter external tools
        if (PinningEnabled)
        {
            foreach (var pid in ActiveGames.Keys.ToList())
            {
                if (!ActiveGames.TryGetValue(pid, out var gameName)) continue;
                using var p = SafeGetProcess(pid);
                if (p is null) continue;
                var (mask, _) = ResolveGameSettings(_cfg, gameName);
                if (p.ProcessorAffinity.ToInt64() != mask)
                    try { p.ProcessorAffinity = (IntPtr)mask; } catch { }
            }
        }

        // Detect exited games (fallback for when WMI stop event is missed)
        foreach (var pid in ActiveGames.Keys.ToList())
        {
            using var p = SafeGetProcess(pid);
            if (p is null)
            {
                if (ActiveGames.TryRemove(pid, out var exitedName))
                    LogEntry?.Invoke($"[ENDED] {exitedName} closed (PID {pid})");
            }
        }

        // Pin new firefox + vlc (fallback for when WMI start event is missed)
        if (PinningEnabled)
        {
            foreach (var proc in SafeGetProcessesByName("firefox")
                .Concat(SafeGetProcessesByName("vlc")))
            {
                using (proc)
                {
                    if (!_appliedMediaZone.ContainsKey(proc.Id))
                    {
                        ApplyMediaZone(proc);
                        _appliedMediaZone[proc.Id] = proc.ProcessName;
                        LogEntry?.Invoke($"[MEDIA] Pinned {proc.ProcessName} (PID {proc.Id}) to media zone");
                    }
                }
            }
        }

        PruneDeadPids(_appliedMediaZone);
        PruneDeadPids(_appliedBgProcs);
        PruneDeadPids(_pathFailCache);

        return newGames;
    }

    /// <summary>
    /// Pins all known background process names to the BG affinity zone at BelowNormal priority.
    /// No-op when <see cref="PinningEnabled"/> is false - guaranteed to make zero process changes.
    /// Re-run periodically (~60s) to catch processes that started after the last scan.
    /// </summary>
    public void ThrottleBg()
    {
        if (!PinningEnabled) return;

        // Parallel across process name lookups to avoid serial GetProcessesByName calls
        Parallel.ForEach(AllBgProcs, name =>
        {
            foreach (var proc in SafeGetProcessesByName(name))
            {
                using (proc)
                {
                    try
                    {
                        proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                        proc.ProcessorAffinity = BgAffinity;
                        _modifiedPids[proc.Id] = 0;
                        _appliedBgProcs[proc.Id] = proc.ProcessName;
                    }
                    catch { }
                }
            }
        });
    }

    // Remove tracking entries for PIDs that no longer exist
    private static void PruneDeadPids<T>(ConcurrentDictionary<int, T> dict)
    {
        foreach (var pid in dict.Keys.ToList())
        {
            using var p = SafeGetProcess(pid);
            if (p is null) dict.TryRemove(pid, out _);
        }
    }

    public void ReleasePinning()
    {
        // Only reset PIDs we actually modified - no full process scan needed
        var pidsToReset = _modifiedPids.Keys.ToList();

        Parallel.ForEach(pidsToReset, pid =>
        {
            using var p = SafeGetProcess(pid);
            if (p is null) return;
            try
            {
                p.ProcessorAffinity = _allCores;
                if (p.PriorityClass == ProcessPriorityClass.BelowNormal ||
                    p.PriorityClass == ProcessPriorityClass.High)
                    p.PriorityClass = ProcessPriorityClass.Normal;
            }
            catch { }
        });

        LogEntry?.Invoke("[PIN] CPU pinning DISABLED - all processes running on all cores");
    }

    public void RestorePinning()
    {
        foreach (var pid in ActiveGames.Keys)
        {
            if (!ActiveGames.TryGetValue(pid, out var gameName)) continue;
            using var p = SafeGetProcess(pid);
            if (p is null) continue;
            var (mask, priority) = ResolveGameSettings(_cfg, gameName);
            try { p.ProcessorAffinity = (IntPtr)mask; p.PriorityClass = priority; } catch { }
        }
        foreach (var proc in SafeGetProcessesByName("firefox").Concat(SafeGetProcessesByName("vlc")))
        {
            using (proc)
            {
                try
                {
                    proc.ProcessorAffinity = FirefoxAffinity;
                    proc.PriorityClass = ProcessPriorityClass.Normal;
                    _appliedMediaZone[proc.Id] = proc.ProcessName;
                    _modifiedPids[proc.Id] = 0;
                }
                catch { }
            }
        }
        ThrottleBg();
        LogEntry?.Invoke("[PIN] CPU pinning ENABLED - affinities restored");
    }

    /// <summary>
    /// Wipes all internal tracking state without touching any live processes.
    /// Call this after <see cref="RestoreAll"/> to fully reset to a clean slate.
    /// Safe to call at any time; subsequent <see cref="Scan"/> calls work normally.
    /// </summary>
    public void ClearState()
    {
        ActiveGames.Clear();
        _appliedMediaZone.Clear();
        _appliedBgProcs.Clear();
        _modifiedPids.Clear();
        _pathFailCache.Clear();
    }

    /// <summary>
    /// Restores every process in <c>_modifiedPids</c> to all-cores affinity and Normal priority.
    /// Only touches PIDs that this instance actually modified - no full process scan.
    /// Does NOT clear internal tracking state; call <see cref="ClearState"/> afterward if needed.
    /// </summary>
    public void RestoreAll()
    {
        var pidsToReset = _modifiedPids.Keys.ToList();

        Parallel.ForEach(pidsToReset, pid =>
        {
            if (pid == Environment.ProcessId) return;
            using var p = SafeGetProcess(pid);
            if (p is null) return;
            try
            {
                if (p.ProcessorAffinity.ToInt64() != _allCores.ToInt64())
                    p.ProcessorAffinity = _allCores;
                if (p.PriorityClass == ProcessPriorityClass.BelowNormal ||
                    p.PriorityClass == ProcessPriorityClass.Idle ||
                    p.PriorityClass == ProcessPriorityClass.High)
                    p.PriorityClass = ProcessPriorityClass.Normal;
            }
            catch { }
        });
    }

    /// <summary>
    /// Resolves the affinity mask and priority for a game, honoring any matching
    /// per-game profile in <paramref name="cfg"/>. Falls back to the global game
    /// affinity mask and High priority when no profile matches.
    /// </summary>
    public static (long affinityMask, ProcessPriorityClass priority) ResolveGameSettings(
        OptimizerConfig cfg, string procName)
    {
        var name = procName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        var profile = cfg.GameProfiles.FirstOrDefault(p =>
            p.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase));

        long mask = profile is { AffinityMask: not 0 }
            ? profile.AffinityMask
            : cfg.GameAffinityMask;
        var priority = profile is not null
            && Enum.TryParse<ProcessPriorityClass>(profile.Priority, ignoreCase: true, out var p)
            ? p : ProcessPriorityClass.High;
        return (mask, priority);
    }

    private bool ApplyGame(Process proc)
    {
        try
        {
            if (PinningEnabled)
            {
                var (mask, priority) = ResolveGameSettings(_cfg, proc.ProcessName);
                proc.PriorityClass = priority;
                proc.ProcessorAffinity = (IntPtr)mask;
            }
            return true;
        }
        catch { return false; }
    }

    private void ApplyMediaZone(Process proc)
    {
        try
        {
            // Only called when PinningEnabled is true
            proc.PriorityClass = ProcessPriorityClass.Normal;
            proc.ProcessorAffinity = FirefoxAffinity;
            _modifiedPids[proc.Id] = 0;
        }
        catch { }
    }

    private string? GetProcPath(Process proc)
    {
        if (_pathFailCache.ContainsKey(proc.Id)) return null;
        string? path = null;
        try { path = proc.MainModule?.FileName; } catch { }
        if (path is null)
        {
            try
            {
                using var q = new ManagementObjectSearcher("root\\cimv2",
                    $"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId={proc.Id}");
                using var results = q.Get();
                foreach (ManagementObject mo in results)
                {
                    path = mo["ExecutablePath"]?.ToString();
                    mo.Dispose();
                    break;
                }
            }
            catch { }
        }
        if (path is null)
        {
            try
            {
                var ageMs = (DateTime.Now - proc.StartTime).TotalMilliseconds;
                if (ageMs > 5000) _pathFailCache[proc.Id] = 0;
            }
            catch { _pathFailCache[proc.Id] = 0; }
        }
        return path;
    }

    private bool IsGamePath(string? path)
    {
        if (path is null) return false;
        return _cfg.GamePaths.Any(root =>
            path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExcluded(string name) =>
        NotAGame.Contains(name.Replace(".exe", "", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Process> SafeGetProcesses()
    {
        try { return Process.GetProcesses(); } catch { return []; }
    }

    private static Process? SafeGetProcess(int pid)
    {
        try { return Process.GetProcessById(pid); } catch { return null; }
    }

    private static IEnumerable<Process> SafeGetProcessesByName(string name)
    {
        try { return Process.GetProcessesByName(name); } catch { return []; }
    }

    public void Dispose()
    {
        _startWatcher?.Stop();
        _startWatcher?.Dispose();
        _stopWatcher?.Stop();
        _stopWatcher?.Dispose();
        GC.SuppressFinalize(this);
    }
}

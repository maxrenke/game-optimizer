using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace GameOptimizer.Services;

public class ProcessManager : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

    private static readonly HashSet<string> NotAGame = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam","steamwebhelper","steamservice","gameoverlayui",
        "gogalaxy","gogalaxy-notifications","gogcomm","gogservices",
        "battle.net","epicgameslauncher","epicwebhelper",
        "unitycrashandler64","unitycrashandler32","crashreportclient",
        "unrealcefsubprocess","easyanticheat","easyanticheat_setup",
        "bsoverlay","nvcapcli","nvidiaoverlaycontainer",
        "gamebarftstserver","gamebarpresencewriter",
        "stardocklauncher","unins000"
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

    public IntPtr GameAffinity => (IntPtr)_cfg.GameAffinityMask;
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
                    var proc = SafeGetProcess(pid);
                    if (proc is not null && !_appliedMediaZone.ContainsKey(pid))
                    {
                        ApplyMediaZone(proc);
                        _appliedMediaZone[pid] = name;
                        LogEntry?.Invoke($"[FF] Pinned {name} PID {pid} to Firefox zone (instant)");
                    }
                }
                return;
            }

            // Check bg procs immediately
            if (AllBgProcs.Any(b => b.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                if (PinningEnabled)
                {
                    var proc = SafeGetProcess(pid);
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
                LogEntry?.Invoke($"[ENDED] {name} closed (PID {pid}) (instant)");
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
                    LogEntry?.Invoke($"[GAME] DETECTED: {proc.ProcessName} (PID {proc.Id})");
                }
            }
        }

        // Re-apply affinity each scan to counter external tools
        if (PinningEnabled)
        {
            foreach (var pid in ActiveGames.Keys.ToList())
            {
                var p = SafeGetProcess(pid);
                if (p is not null && p.ProcessorAffinity.ToInt64() != _cfg.GameAffinityMask)
                    try { p.ProcessorAffinity = GameAffinity; } catch { }
            }
        }

        // Detect exited games (fallback for when WMI stop event is missed)
        foreach (var pid in ActiveGames.Keys.ToList())
        {
            if (SafeGetProcess(pid) is null)
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
                if (!_appliedMediaZone.ContainsKey(proc.Id))
                {
                    ApplyMediaZone(proc);
                    _appliedMediaZone[proc.Id] = proc.ProcessName;
                    LogEntry?.Invoke($"[FF] Pinned {proc.ProcessName} PID {proc.Id} to Firefox zone");
                }
            }
        }

        foreach (var pid in _appliedMediaZone.Keys.ToList())
            if (SafeGetProcess(pid) is null) _appliedMediaZone.TryRemove(pid, out _);
        foreach (var pid in _appliedBgProcs.Keys.ToList())
            if (SafeGetProcess(pid) is null) _appliedBgProcs.TryRemove(pid, out _);
        foreach (var pid in _pathFailCache.Keys.ToList())
            if (SafeGetProcess(pid) is null) _pathFailCache.TryRemove(pid, out _);

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
                try
                {
                    proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                    proc.ProcessorAffinity = BgAffinity;
                    _modifiedPids[proc.Id] = 0;
                    _appliedBgProcs[proc.Id] = proc.ProcessName;
                }
                catch { }
            }
        });
    }

    public void ReleasePinning()
    {
        // Only reset PIDs we actually modified - no full process scan needed
        var pidsToReset = _modifiedPids.Keys.ToList();

        Parallel.ForEach(pidsToReset, pid =>
        {
            var p = SafeGetProcess(pid);
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
            var p = SafeGetProcess(pid);
            if (p is null) continue;
            try { p.ProcessorAffinity = GameAffinity; p.PriorityClass = ProcessPriorityClass.High; } catch { }
        }
        foreach (var proc in SafeGetProcessesByName("firefox").Concat(SafeGetProcessesByName("vlc")))
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
            var p = SafeGetProcess(pid);
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

    private bool ApplyGame(Process proc)
    {
        try
        {
            if (PinningEnabled)
            {
                proc.PriorityClass = ProcessPriorityClass.High;
                proc.ProcessorAffinity = GameAffinity;
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

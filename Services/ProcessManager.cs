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

    public Dictionary<int, string> ActiveGames { get; } = [];
    private readonly HashSet<int> _appliedMediaZone = [];
    private readonly HashSet<int> _pathFailCache = [];

    // Track every PID we've modified so ReleasePinning/RestoreAll only touches those
    private readonly HashSet<int> _modifiedPids = [];

    // WMI event watchers for instant process start/stop detection
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;

    public bool PinningEnabled { get; set; } = true;

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
                var proc = SafeGetProcess(pid);
                if (proc is not null && !_appliedMediaZone.Contains(pid))
                {
                    ApplyMediaZone(proc);
                    lock (_appliedMediaZone) _appliedMediaZone.Add(pid);
                    LogEntry?.Invoke($"[FF] Pinned {name} PID {pid} to Firefox zone (instant)");
                }
                return;
            }

            // Check bg procs immediately
            if (AllBgProcs.Any(b => b.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                var proc = SafeGetProcess(pid);
                if (proc is not null)
                {
                    try
                    {
                        proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                        if (PinningEnabled) proc.ProcessorAffinity = BgAffinity;
                        lock (_modifiedPids) _modifiedPids.Add(pid);
                    }
                    catch { }
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
            if (ActiveGames.TryGetValue(pid, out var name))
            {
                lock (ActiveGames) ActiveGames.Remove(pid);
                LogEntry?.Invoke($"[ENDED] {name} closed (PID {pid}) (instant)");
            }
            lock (_appliedMediaZone) _appliedMediaZone.Remove(pid);
            lock (_modifiedPids) _modifiedPids.Remove(pid);
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
                    lock (ActiveGames) ActiveGames[proc.Id] = proc.ProcessName;
                    lock (_modifiedPids) _modifiedPids.Add(proc.Id);
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
                LogEntry?.Invoke($"[ENDED] {ActiveGames[pid]} closed (PID {pid})");
                lock (ActiveGames) ActiveGames.Remove(pid);
            }
        }

        // Pin new firefox + vlc (fallback for when WMI start event is missed)
        foreach (var proc in SafeGetProcessesByName("firefox")
            .Concat(SafeGetProcessesByName("vlc")))
        {
            if (!_appliedMediaZone.Contains(proc.Id))
            {
                ApplyMediaZone(proc);
                lock (_appliedMediaZone) _appliedMediaZone.Add(proc.Id);
                lock (_modifiedPids) _modifiedPids.Add(proc.Id);
                LogEntry?.Invoke($"[FF] Pinned {proc.ProcessName} PID {proc.Id} to Firefox zone");
            }
        }

        _appliedMediaZone.RemoveWhere(pid => SafeGetProcess(pid) is null);
        _pathFailCache.RemoveWhere(pid => SafeGetProcess(pid) is null);

        return newGames;
    }

    public void ThrottleBg()
    {
        // Parallel across process name lookups to avoid serial GetProcessesByName calls
        Parallel.ForEach(AllBgProcs, name =>
        {
            foreach (var proc in SafeGetProcessesByName(name))
            {
                try
                {
                    proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                    if (PinningEnabled) proc.ProcessorAffinity = BgAffinity;
                    lock (_modifiedPids) _modifiedPids.Add(proc.Id);
                }
                catch { }
            }
        });
    }

    public void ReleasePinning()
    {
        // Only reset PIDs we actually modified - no full process scan needed
        var pidsToReset = new List<int>();
        lock (_modifiedPids) pidsToReset.AddRange(_modifiedPids);

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
                lock (_appliedMediaZone) _appliedMediaZone.Add(proc.Id);
                lock (_modifiedPids) _modifiedPids.Add(proc.Id);
            }
            catch { }
        }
        ThrottleBg();
        LogEntry?.Invoke("[PIN] CPU pinning ENABLED - affinities restored");
    }

    public void RestoreAll()
    {
        var pidsToReset = new List<int>();
        lock (_modifiedPids) pidsToReset.AddRange(_modifiedPids);

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
                    p.PriorityClass == ProcessPriorityClass.Idle)
                    p.PriorityClass = ProcessPriorityClass.Normal;
            }
            catch { }
        });
    }

    private bool ApplyGame(Process proc)
    {
        try
        {
            proc.PriorityClass = ProcessPriorityClass.High;
            if (PinningEnabled) proc.ProcessorAffinity = GameAffinity;
            return true;
        }
        catch { return false; }
    }

    private void ApplyMediaZone(Process proc)
    {
        try
        {
            proc.PriorityClass = ProcessPriorityClass.Normal;
            if (PinningEnabled) proc.ProcessorAffinity = FirefoxAffinity;
            lock (_modifiedPids) _modifiedPids.Add(proc.Id);
        }
        catch { }
    }

    private string? GetProcPath(Process proc)
    {
        if (_pathFailCache.Contains(proc.Id)) return null;
        string? path = null;
        try { path = proc.MainModule?.FileName; } catch { }
        if (path is null)
        {
            try
            {
                var q = new ManagementObjectSearcher("root\\cimv2",
                    $"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId={proc.Id}");
                path = q.Get().Cast<ManagementObject>().FirstOrDefault()?["ExecutablePath"]?.ToString();
            }
            catch { }
        }
        if (path is null)
        {
            try
            {
                var ageMs = (DateTime.Now - proc.StartTime).TotalMilliseconds;
                if (ageMs > 5000) _pathFailCache.Add(proc.Id);
            }
            catch { _pathFailCache.Add(proc.Id); }
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

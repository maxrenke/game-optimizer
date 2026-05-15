using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace GameOptimizer.Services;

public class ProcessManager
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

    // Processes whose names indicate they are NOT actual games (launchers, helpers, etc.)
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

    // Built-in background processes to throttle
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

    // PID -> process name for active games
    public Dictionary<int, string> ActiveGames { get; } = [];
    // PIDs we have applied firefox-zone affinity to (firefox + vlc)
    private readonly HashSet<int> _appliedMediaZone = [];
    // PIDs whose path we could not resolve yet
    private readonly HashSet<int> _pathFailCache = [];

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

    // Scan running processes for new games and new firefox/vlc instances.
    // Returns names of any newly detected games.
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
                    newGames.Add(proc.ProcessName);
                    LogEntry?.Invoke($"[GAME] DETECTED: {proc.ProcessName} (PID {proc.Id})");
                }
            }
        }

        // Re-apply affinity every loop to counter Process Lasso or other tools
        if (PinningEnabled)
        {
            foreach (var pid in ActiveGames.Keys.ToList())
            {
                var p = SafeGetProcess(pid);
                if (p is not null && p.ProcessorAffinity.ToInt64() != _cfg.GameAffinityMask)
                    try { p.ProcessorAffinity = GameAffinity; } catch { }
            }
        }

        // Detect exited games
        var exited = new List<int>();
        foreach (var (pid, name) in ActiveGames)
        {
            if (SafeGetProcess(pid) is null)
                exited.Add(pid);
        }
        foreach (var pid in exited)
        {
            LogEntry?.Invoke($"[ENDED] {ActiveGames[pid]} closed (PID {pid})");
            ActiveGames.Remove(pid);
        }

        // Pin new firefox + vlc
        foreach (var proc in SafeGetProcessesByName("firefox")
            .Concat(SafeGetProcessesByName("vlc")))
        {
            if (!_appliedMediaZone.Contains(proc.Id))
            {
                ApplyMediaZone(proc);
                _appliedMediaZone.Add(proc.Id);
                LogEntry?.Invoke($"[FF] Pinned {proc.ProcessName} PID {proc.Id} to Firefox zone");
            }
        }
        // Cleanup dead PIDs from tracking sets
        _appliedMediaZone.RemoveWhere(pid => SafeGetProcess(pid) is null);
        _pathFailCache.RemoveWhere(pid => SafeGetProcess(pid) is null);

        return newGames;
    }

    public void ThrottleBg()
    {
        foreach (var name in AllBgProcs)
        {
            foreach (var proc in SafeGetProcessesByName(name))
            {
                try
                {
                    proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                    if (PinningEnabled) proc.ProcessorAffinity = BgAffinity;
                }
                catch { }
            }
        }
    }

    public void ReleasePinning()
    {
        foreach (var pid in ActiveGames.Keys)
        {
            var p = SafeGetProcess(pid);
            if (p is null) continue;
            try { p.ProcessorAffinity = _allCores; p.PriorityClass = ProcessPriorityClass.Normal; } catch { }
        }
        foreach (var proc in SafeGetProcessesByName("firefox").Concat(SafeGetProcessesByName("vlc")))
        {
            try { proc.ProcessorAffinity = _allCores; proc.PriorityClass = ProcessPriorityClass.Normal; } catch { }
        }
        foreach (var name in AllBgProcs)
        {
            foreach (var p in SafeGetProcessesByName(name))
                try { p.ProcessorAffinity = _allCores; p.PriorityClass = ProcessPriorityClass.Normal; } catch { }
        }
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
                _appliedMediaZone.Add(proc.Id);
            }
            catch { }
        }
        ThrottleBg();
        LogEntry?.Invoke("[PIN] CPU pinning ENABLED - affinities restored");
    }

    // Restore everything to defaults (used on exit and in cleanup mode)
    public void RestoreAll()
    {
        foreach (var proc in SafeGetProcesses())
        {
            if (proc.Id == Environment.ProcessId) continue;
            try
            {
                var aff = proc.ProcessorAffinity.ToInt64();
                if (aff != _allCores.ToInt64()) proc.ProcessorAffinity = _allCores;
                if (proc.PriorityClass == ProcessPriorityClass.BelowNormal ||
                    proc.PriorityClass == ProcessPriorityClass.Idle)
                    proc.PriorityClass = ProcessPriorityClass.Normal;
            }
            catch { }
        }
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
}

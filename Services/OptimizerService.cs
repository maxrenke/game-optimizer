namespace GameOptimizer.Services;

public record OptimizerSnapshot(
    bool IsGaming,
    string GameName,
    int GameCpuPct,
    int FfCpuPct,
    int BgCpuPct,
    GpuData? Gpu,
    int RxNow, int TxNow,
    int RxPeak, int TxPeak,
    int[] RxHistory, int[] TxHistory, int HistoryIndex,
    BottleneckState Bottleneck,
    IReadOnlyList<string> Alerts,
    IReadOnlyList<string> Log,
    TimeSpan Uptime,
    bool PinningEnabled,
    int ReportCount,
    GameSession? CurrentSession,
    IReadOnlyList<string> GameProcesses,
    IReadOnlyList<string> MediaProcesses,
    IReadOnlyList<string> BgProcesses,
    int[] GameCpuHistory, int GameCpuHistoryIndex,
    int[] GpuUtilHistory, int GpuUtilHistoryIndex,
    int LatencyMs, int JitterMs,
    int[] LatencyHistory, int LatencyHistoryIndex,
    bool DataReady);

public class OptimizerService : IDisposable
{
    private readonly OptimizerConfig _cfg;
    private readonly ProcessManager _pm;
    private readonly NetworkMonitor _net;
    private readonly SystemService _sys;
    private readonly SessionTracker _sessions;
    private readonly BottleneckDetector _bottleneck;
    private readonly AlertMonitor _alerts;
    private readonly LatencyMonitor _latency;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _log = new();
    private const int LogMax = 25;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _stopped;       // Interlocked flag - prevents Cleanup running twice
    private int _scanCount;     // increments every 1s
    private DateTime _startTime;
    private int _prevAlertCount;

    // Last WMI/GPU sample - updated every 3s, read every 1s for snapshots
    private int[] _lastCoreData = [];
    private GpuData? _lastGpu;
    private bool _heavyReady;   // true once the first CPU/GPU sample has landed

    // 60-sample (1-minute) CPU and GPU history, written every tick
    private const int CpuGpuHistorySize = 60;
    private readonly int[] _cpuGameHistory = new int[CpuGpuHistorySize];
    private readonly int[] _gpuUtilHistory = new int[CpuGpuHistorySize];
    private int _cgHistIdx;

    public event Action<OptimizerSnapshot>? SnapshotReady;
    public event Action<string>? AlertFired;

    public bool PinningEnabled
    {
        get => _pm.PinningEnabled;
        set
        {
            _pm.PinningEnabled = value;
            if (value)
            {
                _pm.RestorePinning();
                if (_cfg.DisableGameDvrWhenPinning) _sys.DisableGameDvr();
            }
            else
            {
                _pm.ReleasePinning();
                _sys.RestoreGameDvr();   // idempotent - no-op if it was never disabled
            }
        }
    }

    public OptimizerService(OptimizerConfig cfg)
    {
        _cfg = cfg;
        var resolvedNic = NetworkMonitor.AutoDetect(cfg.NicName);
        if (resolvedNic != cfg.NicName)
            AddLog($"[INIT] NIC '{cfg.NicName}' unsuitable - monitoring '{resolvedNic}' instead");
        cfg.NicName = resolvedNic;

        _pm = new ProcessManager(cfg);
        _pm.LogEntry += AddLog;
        _net = new NetworkMonitor(cfg.NicName);
        _sys = new SystemService();
        _sys.LogEntry += AddLog;
        _sessions = new SessionTracker();
        _bottleneck = new BottleneckDetector();
        _alerts = new AlertMonitor();
        _latency = new LatencyMonitor(cfg.PingHost);
    }

    public void Start()
    {
        _startTime = DateTime.Now;
        _cts = new CancellationTokenSource();
        SystemService.CheckStalePriority(AddLog);
        _sys.EnableGamingOptimizations();
        _pm.Scan();
        _pm.ThrottleBg();
        _pm.StartWmiWatcher();
        AddLog("[INIT] v4 ready - monitoring active, pinning off");
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _cts?.Cancel();
        try { _loopTask?.Wait(TimeSpan.FromSeconds(10)); } catch { }
        Cleanup();
    }

    private void Cleanup()
    {
        if (_sessions.Current is not null) _sessions.EndSession();
        _sys.DisableGamingOptimizations();
        _pm.RestoreAll();
        _pm.Dispose();
        CpuMonitor.Cleanup();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Emergency recovery: immediately restores every modified process to Windows defaults and
    /// returns the optimizer to a clean monitoring-only state. The service keeps running.
    /// <list type="bullet">
    /// <item>Sets <see cref="PinningEnabled"/> to false (no further process changes)</item>
    /// <item>Walks every modified PID and resets affinity to all-cores, priority to Normal</item>
    /// <item>Clears all internal process-tracking state</item>
    /// <item>Restores Win32PrioritySeparation, re-enables SysMain, calls timeEndPeriod(1)</item>
    /// <item>Ends any active game session (stats for the partial session are discarded)</item>
    /// </list>
    /// </summary>
    public void ResetAll()
    {
        // Restore all process affinities and priorities to system defaults
        _pm.PinningEnabled = false;
        _pm.RestoreAll();
        _pm.ClearState();

        // Restore system settings: timer resolution, Win32PrioritySeparation, SysMain
        _sys.DisableGamingOptimizations();

        // Close any open session (no meaningful stats after a mid-session reset)
        if (_sessions.Current is not null) _sessions.EndSession();

        AddLog("[RESET] All process affinities, priorities, and system settings restored to defaults");
    }

    public string SaveReport() => _sessions.SaveReport(_startTime);

    /// <summary>
    /// Purges the Windows standby memory list and logs the result. Safe to
    /// call at any time; a no-op (logged as a failure) without admin rights.
    /// </summary>
    public void FlushStandbyRam()
    {
        var freedMb = MemoryService.FlushStandbyList();
        AddLog(freedMb >= 0
            ? $"[SYS] Standby RAM flushed - freed {freedMb} MB"
            : "[SYS] Standby RAM flush failed (needs admin)");
    }

    public int ReportCount()
    {
        try { return Directory.GetFiles(SessionTracker.ReportDir, "*.txt").Length; } catch { return 0; }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // Emit snapshot immediately so the UI renders its layout while the
        // first (slow) CPU/GPU sample loads - DataReady is still false here
        try { await ScanTickAsync(); } catch { }

        (_lastCoreData, _lastGpu) = await SampleHeavyAsync();
        _heavyReady = true;
        _ = _latency.SampleAsync();   // kick off an early latency reading

        while (!ct.IsCancellationRequested)
        {
            _scanCount++;

            try { await ScanTickAsync(); } catch { }

            // Ping every 2 ticks (~2s) - fire-and-forget so a slow/timed-out
            // ping never stalls the scan loop; the snapshot reads the latest.
            if (_scanCount % 2 == 0)
                _ = _latency.SampleAsync();

            // Heavy WMI sampling every 3 scan ticks (3s cadence)
            if (_scanCount % 3 == 0)
            {
                try
                {
                    (_lastCoreData, _lastGpu) = await SampleHeavyAsync();
                }
                catch { }
            }

            // Re-throttle bg every ~60s (60 ticks x 1s)
            if (_scanCount % 60 == 0)
            {
                try { await Task.Run(() => _pm.ThrottleBg(), ct); }
                catch (OperationCanceledException) { }
            }

            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    // Fast path: process scan + network sample + snapshot emit (runs every 1s)
    private Task ScanTickAsync()
    {
        var newGames = _pm.Scan();
        foreach (var g in newGames)
        {
            if (_sessions.Current is null) _sessions.StartSession(g);
        }
        if (newGames.Count > 0 && _cfg.AutoFlushStandbyOnGameStart)
            _ = Task.Run(FlushStandbyRam);
        if (_pm.ActiveGames.Count == 0 && _sessions.Current is not null)
            _sessions.EndSession();

        _net.Sample();

        var coreData = _lastCoreData;
        var gpu = _lastGpu;

        var gamePct = CpuMonitor.ZonePct(coreData, _cfg.GameAffinityMask);
        var ffPct   = CpuMonitor.ZonePct(coreData, _cfg.FirefoxAffinityMask);
        var bgPct   = CpuMonitor.ZonePct(coreData, _cfg.BgAffinityMask);

        _cpuGameHistory[_cgHistIdx] = gamePct;
        _gpuUtilHistory[_cgHistIdx] = gpu?.GpuUtil ?? 0;
        _cgHistIdx = (_cgHistIdx + 1) % CpuGpuHistorySize;

        _bottleneck.Update(gamePct, gpu, _pm.ActiveGames.Count > 0);
        if (_sessions.Current is not null)
            _sessions.Update(gamePct, gpu, _bottleneck.Current);

        _alerts.Check(gamePct, gpu, _cfg);
        if (_alerts.Current.Count > 0 && _alerts.Current.Count != _prevAlertCount)
        {
            foreach (var a in _alerts.Current) AddLog(a);
            AlertFired?.Invoke(string.Join("  |  ", _alerts.Current));
        }
        _prevAlertCount = _alerts.Current.Count;

        var snap = new OptimizerSnapshot(
            IsGaming: _pm.ActiveGames.Count > 0,
            GameName: _pm.ActiveGames.Count > 0
                ? string.Join(", ", _pm.ActiveGames.Values.Distinct()) : "-",
            GameCpuPct: gamePct,
            FfCpuPct: ffPct,
            BgCpuPct: bgPct,
            Gpu: gpu,
            RxNow: _net.RxNow,
            TxNow: _net.TxNow,
            RxPeak: _net.RxPeak,
            TxPeak: _net.TxPeak,
            RxHistory: (int[])_net.RxHistory.Clone(),
            TxHistory: (int[])_net.TxHistory.Clone(),
            HistoryIndex: _net.HistoryIndex,
            Bottleneck: _bottleneck.Current,
            Alerts: _alerts.Current.ToList(),
            Log: _log.ToList(),
            Uptime: DateTime.Now - _startTime,
            PinningEnabled: _pm.PinningEnabled,
            ReportCount: ReportCount(),
            CurrentSession: _sessions.Current,
            GameProcesses: _pm.GameProcessNames,
            MediaProcesses: _pm.MediaProcessNames,
            BgProcesses: _pm.BgProcessNames,
            GameCpuHistory: (int[])_cpuGameHistory.Clone(),
            GameCpuHistoryIndex: _cgHistIdx,
            GpuUtilHistory: (int[])_gpuUtilHistory.Clone(),
            GpuUtilHistoryIndex: _cgHistIdx,
            LatencyMs: _latency.LastMs,
            JitterMs: _latency.JitterMs,
            LatencyHistory: (int[])_latency.History.Clone(),
            LatencyHistoryIndex: _latency.HistoryIndex,
            DataReady: _heavyReady);

        SnapshotReady?.Invoke(snap);
        return Task.CompletedTask;
    }

    // Slow path: WMI CPU + GPU (runs every 3s)
    private static async Task<(int[] coreData, GpuData? gpu)> SampleHeavyAsync()
    {
        var cpuTask = CpuMonitor.SampleAsync();
        var gpuTask = GpuMonitor.GetDataAsync();
        await Task.WhenAll(cpuTask, gpuTask);
        return (await cpuTask, await gpuTask);
    }

    private void AddLog(string msg)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        _log.Enqueue($"[{ts}] {msg}");
        while (_log.Count > LogMax) _log.TryDequeue(out _);
    }

    public void Dispose() { Stop(); GC.SuppressFinalize(this); }
}

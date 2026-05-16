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
    int[] GpuUtilHistory, int GpuUtilHistoryIndex);

public class OptimizerService : IDisposable
{
    private readonly OptimizerConfig _cfg;
    private readonly ProcessManager _pm;
    private readonly NetworkMonitor _net;
    private readonly SystemService _sys;
    private readonly SessionTracker _sessions;
    private readonly BottleneckDetector _bottleneck;
    private readonly AlertMonitor _alerts;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _log = new();
    private const int LogMax = 25;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _scanCount;     // increments every 1s
    private DateTime _startTime;
    private int _prevAlertCount;

    // Last WMI/GPU sample - updated every 3s, read every 1s for snapshots
    private int[] _lastCoreData = [];
    private GpuData? _lastGpu;

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
            if (value) _pm.RestorePinning();
            else _pm.ReleasePinning();
        }
    }

    public OptimizerService(OptimizerConfig cfg)
    {
        _cfg = cfg;
        var resolvedNic = NetworkMonitor.AutoDetect(cfg.NicName);
        if (resolvedNic != cfg.NicName)
            AddLog($"[INIT] NIC '{cfg.NicName}' not found - auto-selected '{resolvedNic}'");
        cfg.NicName = resolvedNic;

        _pm = new ProcessManager(cfg);
        _pm.LogEntry += AddLog;
        _net = new NetworkMonitor(cfg.NicName);
        _sys = new SystemService();
        _sys.LogEntry += AddLog;
        _sessions = new SessionTracker();
        _bottleneck = new BottleneckDetector();
        _alerts = new AlertMonitor();
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
        AddLog("[INIT] v4 started - optimizations applied");
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
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
    }

    public string SaveReport() => _sessions.SaveReport(_startTime);

    public int ReportCount()
    {
        try { return Directory.GetFiles(SessionTracker.ReportDir, "*.txt").Length; } catch { return 0; }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // Kick off first WMI sample immediately so dashboard isn't blank
        (_lastCoreData, _lastGpu) = await SampleHeavyAsync();

        while (!ct.IsCancellationRequested)
        {
            _scanCount++;

            try { await ScanTickAsync(); } catch { }

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
                await Task.Run(() => _pm.ThrottleBg(), ct);

            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    // Fast path: process scan + network sample + snapshot emit (runs every 1s)
    private async Task ScanTickAsync()
    {
        var newGames = _pm.Scan();
        foreach (var g in newGames)
        {
            if (_sessions.Current is null) _sessions.StartSession(g);
        }
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
            GpuUtilHistoryIndex: _cgHistIdx);

        SnapshotReady?.Invoke(snap);
        await Task.CompletedTask;
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

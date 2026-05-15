namespace GameOptimizer.Services;

// Snapshot of all live data for the UI (immutable, thread-safe to pass to UI thread)
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
    int ReportCount);

public class OptimizerService : IDisposable
{
    private readonly OptimizerConfig _cfg;
    private readonly ProcessManager _pm;
    private readonly NetworkMonitor _net;
    private readonly SystemService _sys;
    private readonly SessionTracker _sessions;
    private readonly BottleneckDetector _bottleneck;
    private readonly AlertMonitor _alerts;
    private readonly System.Collections.Generic.Queue<string> _log = new();
    private const int LogMax = 10;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _loopCount;
    private DateTime _startTime;
    private int _prevAlertCount;

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
    }

    public string SaveReport() => _sessions.SaveReport(_startTime);

    public int ReportCount()
    {
        try { return Directory.GetFiles(SessionTracker.ReportDir, "*.txt").Length; } catch { return 0; }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _loopCount++;
            try { await TickAsync(ct); } catch { }

            // Re-throttle bg every ~60s (20 ticks x 3s)
            if (_loopCount % 20 == 0) _pm.ThrottleBg();

            try { await Task.Delay(3000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        // Parallel data collection
        var cpuTask = CpuMonitor.SampleAsync();
        var gpuTask = GpuMonitor.GetDataAsync();

        // Process management (sync, but fast)
        var newGames = _pm.Scan();
        foreach (var g in newGames)
        {
            if (_sessions.Current is null) _sessions.StartSession(g);
        }
        if (_pm.ActiveGames.Count == 0 && _sessions.Current is not null)
            _sessions.EndSession();

        _net.Sample();

        await Task.WhenAll(cpuTask, gpuTask);

        var coreData = await cpuTask;
        var gpu = await gpuTask;

        var gamePct = CpuMonitor.ZonePct(coreData, _cfg.GameAffinityMask);
        var ffPct   = CpuMonitor.ZonePct(coreData, _cfg.FirefoxAffinityMask);
        var bgPct   = CpuMonitor.ZonePct(coreData, _cfg.BgAffinityMask);

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
            ReportCount: ReportCount());

        SnapshotReady?.Invoke(snap);
    }

    private void AddLog(string msg)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        _log.Enqueue($"[{ts}] {msg}");
        while (_log.Count > LogMax) _log.Dequeue();
    }

    public void Dispose() { Stop(); GC.SuppressFinalize(this); }
}

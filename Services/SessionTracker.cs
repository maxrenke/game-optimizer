namespace GameOptimizer.Services;

public class GameSession
{
    public string Name { get; init; } = "";
    public DateTime StartTime { get; init; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
    public int PeakCpu { get; set; }
    public int PeakGpu { get; set; }
    public int PeakTempC { get; set; }
    public int PeakVramPct { get; set; }
    public int Samples { get; set; }
    public long SumCpu { get; set; }
    public long SumGpu { get; set; }
    public int BtCpuTicks { get; set; }
    public int BtGpuTicks { get; set; }
    public int BtBalTicks { get; set; }
    public int BtHrTicks { get; set; }

    public TimeSpan Duration =>
        ((EndTime ?? DateTime.Now) - StartTime);

    public int AvgCpu => Samples > 0 ? (int)(SumCpu / Samples) : 0;
    public int AvgGpu => Samples > 0 ? (int)(SumGpu / Samples) : 0;
}

public class SessionTracker
{
    public static string ReportDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GamingOptimizer");

    private readonly List<GameSession> _sessions = [];
    public GameSession? Current { get; private set; }

    public void StartSession(string gameName)
    {
        Current = new GameSession { Name = gameName, StartTime = DateTime.Now };
    }

    public void Update(int cpuPct, GpuData? gpu, BottleneckState bt)
    {
        if (Current is null) return;
        Current.Samples++;
        Current.SumCpu += cpuPct;
        if (cpuPct > Current.PeakCpu) Current.PeakCpu = cpuPct;

        if (gpu is not null)
        {
            Current.SumGpu += gpu.GpuUtil;
            if (gpu.GpuUtil > Current.PeakGpu) Current.PeakGpu = gpu.GpuUtil;
            if (gpu.Temp > Current.PeakTempC) Current.PeakTempC = gpu.Temp;
            if (gpu.MemTotMB > 0)
            {
                var vp = (int)Math.Round(gpu.MemUsedMB / (double)gpu.MemTotMB * 100);
                if (vp > Current.PeakVramPct) Current.PeakVramPct = vp;
            }
        }

        switch (bt)
        {
            case BottleneckState.Cpu: Current.BtCpuTicks++; break;
            case BottleneckState.Gpu: Current.BtGpuTicks++; break;
            case BottleneckState.Balanced: Current.BtBalTicks++; break;
            case BottleneckState.Headroom: Current.BtHrTicks++; break;
        }
    }

    public void EndSession()
    {
        if (Current is null) return;
        Current.EndTime = DateTime.Now;
        _sessions.Add(Current);
        Current = null;
    }

    public string SaveReport(DateTime sessionStart)
    {
        // Don't write a file if nothing happened worth recording
        if (_sessions.Count == 0 && (DateTime.Now - sessionStart).TotalMinutes < 2)
            return string.Empty;

        Directory.CreateDirectory(ReportDir);

        // Prune reports older than 30 days
        foreach (var f in Directory.GetFiles(ReportDir, "*.txt"))
            if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30))
                try { File.Delete(f); } catch { }

        var dt = DateTime.Now;
        var file = Path.Combine(ReportDir, $"session_{dt:yyyy-MM-dd_HHmm}.txt");
        var uptime = dt - sessionStart;
        var uptimeStr = $"{(int)uptime.TotalHours}h {uptime.Minutes}m";

        var lines = new List<string>();
        lines.Add("====================================================");
        lines.Add("  Gaming Optimizer  -  Session Report");
        lines.Add($"  {dt:yyyy-MM-dd HH:mm}    uptime {uptimeStr}");

        if (_sessions.Count == 0)
        {
            lines.Add("----------------------------------------------------");
            lines.Add("  No games detected this session.");
        }
        else
        {
            foreach (var g in _sessions)
            {
                var dur = g.Duration;
                var durStr = $"{(int)dur.TotalHours}h {dur.Minutes:D2}m {dur.Seconds:D2}s";
                lines.Add("====================================================");
                lines.Add($"  Game    : {g.Name}");
                lines.Add($"  Started : {g.StartTime:HH:mm:ss}    Duration: {durStr}");
                lines.Add($"  CPU zone  avg {g.AvgCpu,4}%   peak {g.PeakCpu,4}%");
                lines.Add($"  GPU util  avg {g.AvgGpu,4}%   peak {g.PeakGpu,4}%   temp peak {g.PeakTempC}C");
                lines.Add($"  VRAM peak {g.PeakVramPct,4}%");
                var btTotal = g.BtCpuTicks + g.BtGpuTicks + g.BtBalTicks + g.BtHrTicks;
                if (btTotal > 0)
                {
                    var pGpu = (int)Math.Round(g.BtGpuTicks * 100.0 / btTotal);
                    var pCpu = (int)Math.Round(g.BtCpuTicks * 100.0 / btTotal);
                    var pBal = (int)Math.Round(g.BtBalTicks * 100.0 / btTotal);
                    var pHr  = (int)Math.Round(g.BtHrTicks  * 100.0 / btTotal);
                    // Find the actual dominant state by raw tick count (not rounded %)
                    // to avoid ties when all round to 0 in short sessions.
                    var dom = new[] {
                        (g.BtGpuTicks, "GPU-bound"),
                        (g.BtCpuTicks, "CPU-bound"),
                        (g.BtBalTicks, "Balanced"),
                        (g.BtHrTicks,  "Headroom")
                    }.MaxBy(x => x.Item1).Item2;
                    lines.Add($"  Bottleneck: {dom} most of session");
                    lines.Add($"    GPU {pGpu,4}%  CPU {pCpu,4}%  Balanced {pBal,4}%  Headroom {pHr,4}%");
                }
            }
        }
        lines.Add("====================================================");
        File.WriteAllLines(file, lines);
        return file;
    }
}

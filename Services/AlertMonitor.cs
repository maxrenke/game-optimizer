namespace GameOptimizer.Services;

public class AlertMonitor
{
    private int _gpuHighTicks;
    private int _cpuHighTicks;

    public List<string> Current { get; } = [];

    public void Check(int gameCpuPct, GpuData? gpu, OptimizerConfig cfg)
    {
        Current.Clear();

        if (gpu is not null)
        {
            if (gpu.Temp >= cfg.AlertGpuTempC)
                Current.Add($"[!] GPU temp {gpu.Temp}C - exceeds {cfg.AlertGpuTempC}C");

            if (gpu.MemTotMB > 0)
            {
                var vp = (int)Math.Round(gpu.MemUsedMB / (double)gpu.MemTotMB * 100);
                if (vp >= cfg.AlertVramPct)
                    Current.Add($"[!] VRAM {vp}% ({gpu.MemUsedMB / 1024.0:F1}/{gpu.MemTotMB / 1024}GB)");
            }

            if (gpu.GpuUtil >= cfg.AlertGpuUtilPct) _gpuHighTicks++;
            else _gpuHighTicks = 0;
            if (_gpuHighTicks >= cfg.AlertSustainedTicks)
                Current.Add($"[!] GPU util {gpu.GpuUtil}% sustained - possible bottleneck");
        }

        if (gameCpuPct >= cfg.AlertCpuZonePct) _cpuHighTicks++;
        else _cpuHighTicks = 0;
        if (_cpuHighTicks >= cfg.AlertSustainedTicks)
            Current.Add($"[!] Game CPU zone {gameCpuPct}% - cores saturated");
    }
}

namespace GameOptimizer.Services;

public enum BottleneckState { None, Gpu, Cpu, Balanced, Headroom }

public class BottleneckDetector
{
    private const int WindowSize = 5;
    private readonly int[] _cpuHistory = new int[WindowSize];
    private readonly int[] _gpuHistory = new int[WindowSize];
    private int _idx;

    public BottleneckState Current { get; private set; } = BottleneckState.None;

    public void Update(int gameCpuPct, GpuData? gpu, bool hasActiveGame)
    {
        _cpuHistory[_idx] = gameCpuPct;
        _gpuHistory[_idx] = gpu?.GpuUtil ?? 0;
        _idx = (_idx + 1) % WindowSize;

        if (!hasActiveGame) { Current = BottleneckState.None; return; }

        var avgCpu = (int)_cpuHistory.Average();
        var avgGpu = (int)_gpuHistory.Average();

        Current = (avgCpu, avgGpu) switch
        {
            _ when avgGpu >= 90                          => BottleneckState.Gpu,
            _ when avgCpu >= 75 && avgGpu < 75           => BottleneckState.Cpu,
            _ when avgCpu >= 55 || avgGpu >= 55          => BottleneckState.Balanced,
            _                                            => BottleneckState.Headroom,
        };
    }
}

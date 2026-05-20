namespace GameOptimizer.Services;

public enum BottleneckState { None, Gpu, Cpu, Balanced, Headroom }

public class BottleneckDetector
{
    private const int WindowSize = 5;
    private readonly int[] _cpuHistory = new int[WindowSize];
    private readonly int[] _gpuHistory = new int[WindowSize];
    private int _idx;
    private bool _seeded;

    public BottleneckState Current { get; private set; } = BottleneckState.None;

    public void Update(int gameCpuPct, GpuData? gpu, bool hasActiveGame)
    {
        var gpuUtil = gpu?.GpuUtil ?? 0;

        // Seed the whole window with the first sample so early averages
        // are not dragged toward zero by an empty history buffer.
        if (!_seeded)
        {
            Array.Fill(_cpuHistory, gameCpuPct);
            Array.Fill(_gpuHistory, gpuUtil);
            _seeded = true;
        }

        _cpuHistory[_idx] = gameCpuPct;
        _gpuHistory[_idx] = gpuUtil;
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

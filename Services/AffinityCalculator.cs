using Microsoft.Win32;

namespace GameOptimizer.Services;

public static class AffinityCalculator
{
    public record CpuZones(long GameMask, long MediaMask, long BgMask, int TotalCores, string Source);

    public static CpuZones Calculate()
    {
        var total = Environment.ProcessorCount;
        return TryDetectHybrid(total) ?? FromCoreCount(total);
    }

    public static CpuZones FromCoreCount(int total)
    {
        if (total <= 2)
        {
            var all = (1L << total) - 1;
            return new CpuZones(all, all, all, total, "minimal");
        }
        var bgCount = Math.Max(1, total / 8);
        var mediaCount = Math.Max(1, total / 8);
        if (total - bgCount - mediaCount < 2) { bgCount = 1; mediaCount = 1; }

        var bgMask    = BuildMask(total - bgCount, bgCount);
        var mediaMask = BuildMask(total - bgCount - mediaCount, mediaCount);
        var gameMask  = ((1L << total) - 1) & ~bgMask & ~mediaMask;

        return new CpuZones(gameMask, mediaMask, bgMask, total, $"auto ({total} cores)");
    }

    private static CpuZones? TryDetectHybrid(int total)
    {
        try
        {
            var coreMhz = new List<int>();
            for (int i = 0; i < total; i++)
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"HARDWARE\DESCRIPTION\System\CentralProcessor\{i}");
                if (key?.GetValue("~MHz") is int mhz) coreMhz.Add(mhz);
            }
            if (coreMhz.Count != total) return null;

            var maxMhz = coreMhz.Max();
            var minMhz = coreMhz.Min();
            if ((double)(maxMhz - minMhz) / maxMhz < 0.15) return null;

            var pCores = coreMhz.Select((mhz, idx) => (mhz, idx))
                .Where(x => x.mhz > minMhz + (maxMhz - minMhz) * 0.15)
                .Select(x => x.idx).ToList();
            var eCores = Enumerable.Range(0, total).Except(pCores).ToList();
            if (pCores.Count < 2 || eCores.Count < 2) return null;

            var mediaCount = Math.Max(1, eCores.Count / 2);
            var bgCount    = Math.Max(1, eCores.Count - mediaCount);

            var gameMask  = pCores.Aggregate(0L, (m, i) => m | (1L << i));
            var mediaMask = eCores.Take(mediaCount).Aggregate(0L, (m, i) => m | (1L << i));
            var bgMask    = eCores.Skip(mediaCount).Aggregate(0L, (m, i) => m | (1L << i));
            if (bgMask == 0) bgMask = 1L << eCores.Last();

            return new CpuZones(gameMask, mediaMask, bgMask, total,
                $"hybrid P={pCores.Count} E={eCores.Count}");
        }
        catch { return null; }
    }

    private static long BuildMask(int startBit, int count)
    {
        long mask = 0;
        for (int i = 0; i < count; i++) mask |= 1L << (startBit + i);
        return mask;
    }
}

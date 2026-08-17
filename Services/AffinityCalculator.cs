using Microsoft.Win32;

namespace GameOptimizer.Services;

public static class AffinityCalculator
{
    public record CpuZones(long GameMask, long MediaMask, long BgMask, int TotalCores, string Source);

    /// <param name="mediaHeavy">
    /// Widens the media and background zones to a whole physical core each,
    /// for second-monitor video playback alongside a game. See
    /// <see cref="FromCoreCount"/>.
    /// </param>
    public static CpuZones Calculate(bool mediaHeavy = false)
    {
        var total = Environment.ProcessorCount;
        // Hybrid parts already give media half the E-cores, which is a whole
        // core's worth on every current layout - mediaHeavy adds nothing there.
        return TryDetectHybrid(total) ?? FromCoreCount(total, mediaHeavy);
    }

    public static CpuZones FromCoreCount(int total, bool mediaHeavy = false)
    {
        if (total <= 2)
        {
            var all = (1L << total) - 1;
            return new CpuZones(all, all, all, total, "minimal");
        }
        // Default: reserve ~one SMT pair (1 physical core) per 16 threads, split
        // between media and bg. On mainstream 8C/16T single-CCD parts this hands
        // the game 7 of 8 physical cores instead of 6 - reserving two whole cores
        // there costs more frametime than the background isolation saves.
        //
        // mediaHeavy: give media and bg two logical processors each. With SMT,
        // Windows enumerates siblings adjacently (CPU 2i / 2i+1 share physical
        // core i), so two aligned bits is a whole physical core rather than one
        // starved hyperthread. A 1440p/4K video decode plus browser compositing
        // does not fit in a single SMT sibling; it stutters, and its spillover
        // lands back on the game zone. Needs >= 12 threads to be worth 2 cores.
        var perZone = mediaHeavy && total >= 12 ? 2 : Math.Max(1, total / 16);
        var bgCount = perZone;
        var mediaCount = perZone;
        if (total - bgCount - mediaCount < 2) { bgCount = 1; mediaCount = 1; }

        var bgMask    = BuildMask(total - bgCount, bgCount);
        var mediaMask = BuildMask(total - bgCount - mediaCount, mediaCount);
        var gameMask  = ((1L << total) - 1) & ~bgMask & ~mediaMask;

        var label = mediaHeavy ? $"media-heavy ({total} cores)" : $"auto ({total} cores)";
        return new CpuZones(gameMask, mediaMask, bgMask, total, label);
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

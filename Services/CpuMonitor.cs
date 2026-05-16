using System.Runtime.InteropServices;

namespace GameOptimizer.Services;

public static class CpuMonitor
{
    [DllImport("pdh.dll")]
    private static extern int PdhOpenQuery(string? dataSource, nint userData, out nint queryHandle);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(nint queryHandle, string counterPath, nint userData, out nint counterHandle);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(nint queryHandle);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterValue(nint counterHandle, uint format, out int counterType, out PdhFmtCounterValue value);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(nint queryHandle);

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFmtCounterValue
    {
        [FieldOffset(0)] public int CStatus;
        [FieldOffset(8)] public double doubleValue;
    }

    private const uint PDH_FMT_DOUBLE = 0x00000200;

    private static readonly object _initLock = new();
    private static nint _query;
    private static nint[] _counters = [];
    private static bool _ready;

    private static void EnsureInit()
    {
        if (_ready) return;
        lock (_initLock)
        {
            if (_ready) return;
            int n = Environment.ProcessorCount;
            if (PdhOpenQuery(null, 0, out _query) != 0) return;
            _counters = new nint[n];
            for (int i = 0; i < n; i++)
                PdhAddEnglishCounterW(_query, $"\\Processor({i})\\% Processor Time", 0, out _counters[i]);
            PdhCollectQueryData(_query); // seed baseline - first call has no history
            _ready = true;
        }
    }

    public static void Cleanup()
    {
        lock (_initLock)
        {
            if (!_ready) return;
            PdhCloseQuery(_query);
            _query = 0;
            _ready = false;
        }
    }

    public static Task<int[]> SampleAsync() => Task.Run(() =>
    {
        try
        {
            EnsureInit();
            if (!_ready) return [];
            PdhCollectQueryData(_query);
            var results = new int[_counters.Length];
            for (int i = 0; i < _counters.Length; i++)
            {
                if (_counters[i] == 0) continue;
                if (PdhGetFormattedCounterValue(_counters[i], PDH_FMT_DOUBLE, out _, out var val) == 0)
                    results[i] = (int)Math.Round(Math.Clamp(val.doubleValue, 0, 100));
            }
            return results;
        }
        catch { return []; }
    });

    public static int ZonePct(int[] coreData, long mask)
    {
        if (coreData.Length == 0) return 0;
        var vals = new List<int>();
        for (int i = 0; i < coreData.Length; i++)
            if ((mask & (1L << i)) != 0) vals.Add(coreData[i]);
        return vals.Count > 0 ? (int)vals.Average() : 0;
    }
}

using System.Management;

namespace GameOptimizer.Services;

// Per-core CPU utilization via WMI Win32_PerfFormattedData_PerfOS_Processor
public static class CpuMonitor
{
    public static Task<int[]> SampleAsync() => Task.Run(() =>
    {
        try
        {
            var query = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor " +
                "WHERE Name != '_Total'");
            var rows = query.Get().Cast<ManagementObject>()
                .OrderBy(r => ParseCoreIndex(r["Name"]?.ToString() ?? "0"))
                .Select(r => (int)(ulong)(r["PercentProcessorTime"] ?? 0UL))
                .ToArray();
            return rows;
        }
        catch { return []; }
    });

    // Calculate average % for logical processors covered by an affinity mask
    public static int ZonePct(int[] coreData, long mask)
    {
        if (coreData.Length == 0) return 0;
        var vals = new List<int>();
        for (int i = 0; i < coreData.Length; i++)
        {
            if ((mask & (1L << i)) != 0)
                vals.Add(coreData[i]);
        }
        return vals.Count > 0 ? (int)vals.Average() : 0;
    }

    private static int ParseCoreIndex(string name) =>
        int.TryParse(name, out var n) ? n : 0;
}

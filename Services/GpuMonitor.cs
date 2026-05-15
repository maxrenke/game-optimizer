using System.Diagnostics;
using System.Management;

namespace GameOptimizer.Services;

public static class GpuMonitor
{
    public static async Task<GpuData?> GetDataAsync()
    {
        var nv = await TryNvidiaSmiAsync();
        if (nv is not null) return nv;

        var amd = await TryRocmSmiAsync();
        if (amd is not null) return amd;

        return await TryWddmAsync();
    }

    private static async Task<GpuData?> TryNvidiaSmiAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=temperature.gpu,utilization.gpu,utilization.memory,memory.used,memory.total,power.draw,power.limit,clocks.current.graphics,clocks.current.memory --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var p = output.Split(',').Select(s => s.Trim()).ToArray();
            if (p.Length >= 9 && int.TryParse(p[0], out var temp))
            {
                return new GpuData("NVIDIA", temp,
                    int.TryParse(p[1], out var gu) ? gu : 0,
                    int.TryParse(p[2], out var mu) ? mu : 0,
                    int.TryParse(p[3], out var memU) ? memU : 0,
                    int.TryParse(p[4], out var memT) ? memT : 0,
                    double.TryParse(p[5], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var pw) ? Math.Round(pw, 1) : 0,
                    double.TryParse(p[6], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var pl) ? Math.Round(pl, 0) : 0,
                    int.TryParse(p[7], out var clk) ? clk : 0,
                    int.TryParse(p[8], out var mclk) ? mclk : 0);
            }
        }
        catch { }
        return null;
    }

    private static async Task<GpuData?> TryRocmSmiAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("rocm-smi",
                "--showtemp --showuse --showmeminfo vram --json")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(output);
            var root = doc.RootElement;
            var card = root.EnumerateObject().FirstOrDefault().Value;
            if (card.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                long usedB = 0, totalB = 0;
                int gpuUtil = 0, temp = 0;
                if (card.TryGetProperty("VRAM Total Used Memory (B)", out var v)) usedB = v.GetInt64();
                if (card.TryGetProperty("VRAM Total Memory (B)", out var t)) totalB = t.GetInt64();
                if (card.TryGetProperty("GPU use (%)", out var u)) int.TryParse(u.GetString(), out gpuUtil);
                if (card.TryGetProperty("Temperature (Sensor junction) (C)", out var tc))
                    int.TryParse(tc.GetString(), out temp);
                return new GpuData("AMD", temp, gpuUtil, 0,
                    (int)(usedB / (1024 * 1024)), (int)(totalB / (1024 * 1024)),
                    0, 0, 0, 0);
            }
        }
        catch { }
        return null;
    }

    private static Task<GpuData?> TryWddmAsync() => Task.Run(() =>
    {
        try
        {
            var query = new ManagementObjectSearcher(
                @"root\cimv2",
                @"SELECT * FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE '%_3D'");
            double maxUtil = 0;
            foreach (ManagementObject obj in query.Get())
            {
                if (obj["UtilizationPercentage"] is ulong u && u > maxUtil) maxUtil = u;
            }
            long vramBytes = 0;
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
                if (k?.GetValue("HardwareInformation.MemorySize") is long mb) vramBytes = mb;
            }
            catch { }
            return (GpuData?)new GpuData("Generic", 0, (int)Math.Min(100, maxUtil), 0,
                0, (int)(vramBytes / (1024 * 1024)), 0, 0, 0, 0);
        }
        catch { }
        return null;
    });
}

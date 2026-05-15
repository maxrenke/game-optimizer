using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameOptimizer.Services;

public class OptimizerConfig
{
    public string NicName { get; set; } = "Ethernet 2";
    public long GameAffinityMask { get; set; } = 0x0FFF;
    public long FirefoxAffinityMask { get; set; } = 0x3000;
    public long BgAffinityMask { get; set; } = 0xC000;
    public int AlertGpuTempC { get; set; } = 80;
    public int AlertVramPct { get; set; } = 90;
    public int AlertGpuUtilPct { get; set; } = 95;
    public int AlertCpuZonePct { get; set; } = 90;
    public int AlertSustainedTicks { get; set; } = 4;

    public List<string> GamePaths { get; set; } =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common",
        @"C:\Program Files\Steam\steamapps\common",
        @"C:\Program Files (x86)\GOG Galaxy\Games",
        @"C:\Program Files\GOG Galaxy\Games",
        @"C:\Program Files (x86)\Hearthstone",
        @"C:\Program Files (x86)\Overwatch",
        @"C:\Program Files (x86)\Overwatch\_retail_",
        @"C:\Program Files (x86)\World of Warcraft",
        @"C:\Program Files (x86)\Diablo IV",
        @"C:\Program Files\Epic Games",
    ];

    public List<string> ExtraThrottledProcs { get; set; } = [];

    public bool StartMinimized { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;

    [JsonIgnore]
    public static string ConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     "GamingOptimizer", "config.json");

    public static OptimizerConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<OptimizerConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? CreateDefault();
            }
        }
        catch { }
        return CreateDefault();
    }

    private static OptimizerConfig CreateDefault()
    {
        var cfg = new OptimizerConfig();
        var zones = AffinityCalculator.Calculate();
        cfg.GameAffinityMask    = zones.GameMask;
        cfg.FirefoxAffinityMask = zones.MediaMask;
        cfg.BgAffinityMask      = zones.BgMask;

        // Merge auto-detected game library paths with the hardcoded defaults
        var detected = GameLibraryScanner.ScanAll();
        if (detected.Count > 0)
            cfg.GamePaths = [.. cfg.GamePaths.Union(detected, StringComparer.OrdinalIgnoreCase)];

        return cfg;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Services;
using System.Collections.ObjectModel;

namespace GameOptimizer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private OptimizerConfig _cfg;

    public SettingsViewModel(OptimizerConfig cfg)
    {
        _cfg = cfg;
        LoadFromConfig();
    }

    [ObservableProperty] public partial string NicName { get; set; } = "Ethernet 2";
    [ObservableProperty] public partial string GameAffinityHex { get; set; } = "0xFFF";
    [ObservableProperty] public partial string FirefoxAffinityHex { get; set; } = "0x3000";
    [ObservableProperty] public partial string BgAffinityHex { get; set; } = "0xC000";
    [ObservableProperty] public partial int AlertGpuTempC { get; set; } = 80;
    [ObservableProperty] public partial int AlertVramPct { get; set; } = 90;
    [ObservableProperty] public partial int AlertGpuUtilPct { get; set; } = 95;
    [ObservableProperty] public partial int AlertCpuZonePct { get; set; } = 90;
    [ObservableProperty] public partial int AlertSustainedTicks { get; set; } = 4;
    [ObservableProperty] public partial bool StartMinimized { get; set; } = false;
    [ObservableProperty] public partial bool StartWithWindows { get; set; } = false;

    public ObservableCollection<string> GamePaths { get; } = [];
    public ObservableCollection<string> ExtraThrottledProcs { get; } = [];

    [ObservableProperty] public partial string NewGamePath { get; set; } = "";
    [ObservableProperty] public partial string NewThrottledProc { get; set; } = "";

    [RelayCommand]
    private void AddGamePath()
    {
        if (!string.IsNullOrWhiteSpace(NewGamePath) && !GamePaths.Contains(NewGamePath))
        {
            GamePaths.Add(NewGamePath);
            NewGamePath = "";
        }
    }

    [RelayCommand]
    private void RemoveGamePath(string path) => GamePaths.Remove(path);

    [RelayCommand]
    private void AddThrottledProc()
    {
        if (!string.IsNullOrWhiteSpace(NewThrottledProc) && !ExtraThrottledProcs.Contains(NewThrottledProc))
        {
            ExtraThrottledProcs.Add(NewThrottledProc.ToLower());
            NewThrottledProc = "";
        }
    }

    [RelayCommand]
    private void RemoveThrottledProc(string proc) => ExtraThrottledProcs.Remove(proc);

    [RelayCommand]
    private void Save()
    {
        try
        {
            _cfg.NicName = NicName;
            _cfg.GameAffinityMask = Convert.ToInt64(GameAffinityHex.Replace("0x", "").Replace("0X", ""), 16);
            _cfg.FirefoxAffinityMask = Convert.ToInt64(FirefoxAffinityHex.Replace("0x", "").Replace("0X", ""), 16);
            _cfg.BgAffinityMask = Convert.ToInt64(BgAffinityHex.Replace("0x", "").Replace("0X", ""), 16);
            _cfg.AlertGpuTempC = AlertGpuTempC;
            _cfg.AlertVramPct = AlertVramPct;
            _cfg.AlertGpuUtilPct = AlertGpuUtilPct;
            _cfg.AlertCpuZonePct = AlertCpuZonePct;
            _cfg.AlertSustainedTicks = AlertSustainedTicks;
            _cfg.GamePaths = [.. GamePaths];
            _cfg.ExtraThrottledProcs = [.. ExtraThrottledProcs];
            _cfg.StartMinimized = StartMinimized;
            _cfg.StartWithWindows = StartWithWindows;
            _cfg.Save();
        }
        catch { }
    }

    private void LoadFromConfig()
    {
        NicName = _cfg.NicName;
        GameAffinityHex = $"0x{_cfg.GameAffinityMask:X}";
        FirefoxAffinityHex = $"0x{_cfg.FirefoxAffinityMask:X}";
        BgAffinityHex = $"0x{_cfg.BgAffinityMask:X}";
        AlertGpuTempC = _cfg.AlertGpuTempC;
        AlertVramPct = _cfg.AlertVramPct;
        AlertGpuUtilPct = _cfg.AlertGpuUtilPct;
        AlertCpuZonePct = _cfg.AlertCpuZonePct;
        AlertSustainedTicks = _cfg.AlertSustainedTicks;
        GamePaths.Clear();
        foreach (var p in _cfg.GamePaths) GamePaths.Add(p);
        ExtraThrottledProcs.Clear();
        foreach (var p in _cfg.ExtraThrottledProcs) ExtraThrottledProcs.Add(p);
        StartMinimized = _cfg.StartMinimized;
        StartWithWindows = _cfg.StartWithWindows;
    }
}

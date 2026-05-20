using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

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
    [ObservableProperty] public partial string PingHost { get; set; } = "1.1.1.1";
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
    [ObservableProperty] public partial bool AutoFlushStandby { get; set; } = false;

    public ObservableCollection<string> GamePaths { get; } = [];
    public ObservableCollection<string> ExtraThrottledProcs { get; } = [];
    public ObservableCollection<GameProfile> GameProfiles { get; } = [];
    public ObservableCollection<SuspendApp> SuspendApps { get; } = [];

    [ObservableProperty] public partial string NewGamePath { get; set; } = "";
    [ObservableProperty] public partial string NewThrottledProc { get; set; } = "";
    [ObservableProperty] public partial string NewSuspendApp { get; set; } = "";

    // ── Per-game profile add form ──────────────────────────────
    public string[] PriorityOptions { get; } = ["Normal", "AboveNormal", "High"];

    [ObservableProperty] public partial string NewProfileName { get; set; } = "";
    [ObservableProperty] public partial string NewProfileAffinityHex { get; set; } = "";
    [ObservableProperty] public partial string NewProfilePriority { get; set; } = "High";

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
    private void AddProfile()
    {
        var name = NewProfileName.Trim()
            .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        if (name.Length == 0) return;
        if (GameProfiles.Any(p => p.ProcessName == name)) return;

        long mask = 0;
        var hex = NewProfileAffinityHex.Trim()
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (hex.Length > 0)
            try { mask = Convert.ToInt64(hex, 16); } catch { mask = 0; }

        GameProfiles.Add(new GameProfile
        {
            ProcessName  = name,
            AffinityMask = mask,
            Priority     = string.IsNullOrWhiteSpace(NewProfilePriority) ? "High" : NewProfilePriority,
        });
        NewProfileName        = "";
        NewProfileAffinityHex = "";
        NewProfilePriority    = "High";
    }

    public void RemoveProfile(GameProfile profile) => GameProfiles.Remove(profile);

    [RelayCommand]
    private void AddSuspendApp()
    {
        var name = NewSuspendApp.Trim()
            .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        if (name.Length == 0) return;
        if (SuspendApps.Any(a => a.ProcessName == name)) return;
        SuspendApps.Add(new SuspendApp { ProcessName = name, Enabled = true });
        NewSuspendApp = "";
    }

    public void RemoveSuspendApp(SuspendApp app) => SuspendApps.Remove(app);

    [RelayCommand]
    private void Save()
    {
        try
        {
            _cfg.NicName = NicName;
            _cfg.PingHost = PingHost;
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
            _cfg.GameProfiles = [.. GameProfiles];
            _cfg.SuspendDuringGame = [.. SuspendApps];
            _cfg.StartMinimized = StartMinimized;
            _cfg.StartWithWindows = StartWithWindows;
            _cfg.AutoFlushStandbyOnGameStart = AutoFlushStandby;
            _cfg.Save();
            ApplyStartWithWindows(StartWithWindows);
        }
        catch { }
    }

    private static void ApplyStartWithWindows(bool enable)
    {
        try
        {
            string args;
            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath is null) return;
                args = $"/create /tn \"GameOptimizer\" /tr \"\\\"{exePath}\\\"\" /sc ONLOGON /rl HIGHEST /f";
            }
            else
            {
                args = "/delete /tn \"GameOptimizer\" /f";
            }
            Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(5000);
        }
        catch { }
    }

    private void LoadFromConfig()
    {
        NicName = _cfg.NicName;
        PingHost = _cfg.PingHost;
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
        GameProfiles.Clear();
        foreach (var p in _cfg.GameProfiles) GameProfiles.Add(p);
        SuspendApps.Clear();
        foreach (var a in _cfg.SuspendDuringGame) SuspendApps.Add(a);
        StartMinimized = _cfg.StartMinimized;
        StartWithWindows = _cfg.StartWithWindows;
        AutoFlushStandby = _cfg.AutoFlushStandbyOnGameStart;
    }
}

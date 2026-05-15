using CommunityToolkit.Mvvm.ComponentModel;
using GameOptimizer.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GameOptimizer.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcher;

    public MainPageViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    // ── Status ─────────────────────────────────────────────────
    [ObservableProperty] public partial bool IsGaming { get; set; }
    [ObservableProperty] public partial string GameName { get; set; } = "-";
    [ObservableProperty] public partial string UptimeText { get; set; } = "00h 00m 00s";
    [ObservableProperty] public partial bool PinningEnabled { get; set; } = true;

    // ── CPU Zones ──────────────────────────────────────────────
    [ObservableProperty] public partial int GameCpuPct { get; set; }
    [ObservableProperty] public partial int FfCpuPct { get; set; }
    [ObservableProperty] public partial int BgCpuPct { get; set; }

    // ── GPU ────────────────────────────────────────────────────
    [ObservableProperty] public partial int GpuUtil { get; set; }
    [ObservableProperty] public partial int VramPct { get; set; }
    [ObservableProperty] public partial string VramText { get; set; } = "-";
    [ObservableProperty] public partial int GpuTempC { get; set; }
    [ObservableProperty] public partial double GpuPowerW { get; set; }
    [ObservableProperty] public partial double GpuPowerLimW { get; set; }
    [ObservableProperty] public partial int GpuClkMhz { get; set; }
    [ObservableProperty] public partial int GpuMemClkMhz { get; set; }
    [ObservableProperty] public partial bool GpuAvailable { get; set; }
    [ObservableProperty] public partial string GpuVendor { get; set; } = "N/A";

    // ── Network ────────────────────────────────────────────────
    [ObservableProperty] public partial int RxNow { get; set; }
    [ObservableProperty] public partial int TxNow { get; set; }
    [ObservableProperty] public partial int RxPeak { get; set; }
    [ObservableProperty] public partial int TxPeak { get; set; }
    [ObservableProperty] public partial int[] RxHistory { get; set; } = new int[30];
    [ObservableProperty] public partial int[] TxHistory { get; set; } = new int[30];
    [ObservableProperty] public partial int NetHistoryIndex { get; set; }

    // ── Bottleneck ─────────────────────────────────────────────
    [ObservableProperty] public partial BottleneckState Bottleneck { get; set; } = BottleneckState.None;
    [ObservableProperty] public partial string BottleneckLabel { get; set; } = "[--------]  No game running";
    [ObservableProperty] public partial Brush BottleneckBrush { get; set; } = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80));

    // ── Alerts ─────────────────────────────────────────────────
    [ObservableProperty] public partial string AlertText { get; set; } = "All zones healthy";
    [ObservableProperty] public partial bool HasAlert { get; set; }

    // ── Log ────────────────────────────────────────────────────
    [ObservableProperty] public partial System.Collections.ObjectModel.ObservableCollection<LogLine> LogLines { get; set; } = [];

    // ── Session Summary ────────────────────────────────────────
    [ObservableProperty] public partial string SessionDuration { get; set; } = "0:00";
    [ObservableProperty] public partial string SessionPeakCpu { get; set; } = "-";
    [ObservableProperty] public partial string SessionPeakGpu { get; set; } = "-";
    [ObservableProperty] public partial string SessionPeakTemp { get; set; } = "-";

    // ── Footer ─────────────────────────────────────────────────
    [ObservableProperty] public partial int ReportCount { get; set; }
    [ObservableProperty] public partial string ReportDir { get; set; } = SessionTracker.ReportDir;

    public void ApplySnapshot(OptimizerSnapshot s)
    {
        _dispatcher.TryEnqueue(() =>
        {
            IsGaming = s.IsGaming;
            GameName = s.GameName;
            UptimeText = $"{(int)s.Uptime.TotalHours:D2}h {s.Uptime.Minutes:D2}m {s.Uptime.Seconds:D2}s";
            PinningEnabled = s.PinningEnabled;

            GameCpuPct = s.GameCpuPct;
            FfCpuPct = s.FfCpuPct;
            BgCpuPct = s.BgCpuPct;

            if (s.Gpu is not null)
            {
                GpuAvailable = true;
                GpuVendor = s.Gpu.Vendor;
                GpuUtil = s.Gpu.GpuUtil;
                if (s.Gpu.MemTotMB > 0)
                {
                    VramPct = (int)Math.Round(s.Gpu.MemUsedMB / (double)s.Gpu.MemTotMB * 100);
                    VramText = $"{s.Gpu.MemUsedMB / 1024.0:F1}/{s.Gpu.MemTotMB / 1024}GB";
                }
                GpuTempC = s.Gpu.Temp;
                GpuPowerW = s.Gpu.PowerW;
                GpuPowerLimW = s.Gpu.PowerLimW;
                GpuClkMhz = s.Gpu.ClkMhz;
                GpuMemClkMhz = s.Gpu.MemClkMhz;
            }
            else
            {
                GpuAvailable = false;
            }

            RxNow = s.RxNow; TxNow = s.TxNow;
            RxPeak = s.RxPeak; TxPeak = s.TxPeak;
            RxHistory = s.RxHistory; TxHistory = s.TxHistory;
            NetHistoryIndex = s.HistoryIndex;

            Bottleneck = s.Bottleneck;
            (BottleneckLabel, BottleneckBrush) = s.Bottleneck switch
            {
                BottleneckState.Gpu      => ("[GPU-BOUND]  Lower resolution/quality - GPU is the limit",
                                             new SolidColorBrush(Color.FromArgb(255, 230, 60, 60))),
                BottleneckState.Cpu      => ("[CPU-BOUND]  Lower draw dist/entities - CPU starving GPU",
                                             new SolidColorBrush(Color.FromArgb(255, 220, 170, 50))),
                BottleneckState.Balanced => ("[BALANCED ]  Healthy load - good settings balance",
                                             new SolidColorBrush(Color.FromArgb(255, 80, 200, 100))),
                BottleneckState.Headroom => ("[HEADROOM ]  Both low - you can raise quality settings",
                                             new SolidColorBrush(Color.FromArgb(255, 80, 180, 220))),
                _                        => ("[--------]   No game running",
                                             new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)))
            };

            HasAlert = s.Alerts.Count > 0;
            AlertText = s.Alerts.Count > 0 ? s.Alerts[0] : "All zones healthy";

            LogLines.Clear();
            foreach (var l in s.Log)
                LogLines.Add(new LogLine(l));

            ReportCount = s.ReportCount;

            if (s.CurrentSession is not null)
            {
                var dur = s.CurrentSession.Duration;
                SessionDuration = $"{(int)dur.TotalMinutes}:{dur.Seconds:D2}";
                SessionPeakCpu  = $"{s.CurrentSession.PeakCpu}%";
                SessionPeakGpu  = $"{s.CurrentSession.PeakGpu}%";
                SessionPeakTemp = s.CurrentSession.PeakTempC > 0 ? $"{s.CurrentSession.PeakTempC}C" : "-";
            }
        });
    }
}

public class LogLine
{
    public string Text { get; }
    public Brush Foreground { get; }

    public LogLine(string text)
    {
        Text = text;
        Foreground = text switch
        {
            _ when text.Contains("[GAME]")  => new SolidColorBrush(Color.FromArgb(255, 200, 100, 220)),
            _ when text.Contains("[ENDED]") => new SolidColorBrush(Color.FromArgb(255, 200, 180, 60)),
            _ when text.Contains("[FF]")    => new SolidColorBrush(Color.FromArgb(255, 80, 200, 220)),
            _ when text.Contains("[SYS]")   => new SolidColorBrush(Color.FromArgb(255, 200, 180, 60)),
            _ when text.Contains("[PIN]")   => new SolidColorBrush(Color.FromArgb(255, 80, 200, 100)),
            _ when text.Contains("[BG]")    => new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
            _ when text.Contains("[!]")     => new SolidColorBrush(Color.FromArgb(255, 220, 60, 60)),
            _ when text.Contains("[INIT]")  => new SolidColorBrush(Color.FromArgb(255, 80, 200, 100)),
            _                               => new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
        };
    }
}

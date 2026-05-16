using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GameOptimizer.Tray;

public sealed partial class TrayStatsControl : UserControl
{
    public TrayStatsControl()
    {
        InitializeComponent();
    }

    public void Update(bool isGaming, string gameName, int cpuPct, int gpuUtil, int gpuTempC, int rxKbps, bool pinEnabled)
    {
        if (isGaming)
        {
            StatusIcon.Glyph = "";
            StatusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 200, 100));
            StatusText.Text = gameName.Length > 22 ? gameName[..22] + "..." : gameName;
            StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 200, 100));
        }
        else
        {
            StatusIcon.Glyph = "";
            StatusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
            StatusText.Text = "IDLE";
            StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
        }

        CpuBar.Value = cpuPct;
        CpuText.Text = $"{cpuPct}%";

        if (gpuUtil >= 0)
        {
            GpuBar.Value = gpuUtil;
            GpuText.Text = $"{gpuUtil}%";
            TempText.Text = gpuTempC > 0 ? $"{gpuTempC}°C" : "-";
        }
        else
        {
            GpuBar.Value = 0;
            GpuText.Text = "N/A";
            TempText.Text = "-";
        }

        NetText.Text = rxKbps >= 1024
            ? $"{rxKbps / 1024.0:F1} MB/s"
            : $"{rxKbps} KB/s";

        PinIcon.Foreground = new SolidColorBrush(pinEnabled
            ? Color.FromArgb(255, 80, 200, 100)
            : Color.FromArgb(255, 128, 128, 128));
        PinText.Text = $"CPU Pinning {(pinEnabled ? "ON" : "OFF")}";
    }
}

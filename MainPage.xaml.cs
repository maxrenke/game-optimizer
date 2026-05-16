using GameOptimizer.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.System;

namespace GameOptimizer;

public sealed partial class MainPage : Page
{
    private bool _pendingScroll;
    public MainPageViewModel ViewModel => App.MainViewModel;

    public MainPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.RxHistory)
                               or nameof(ViewModel.TxHistory)
                               or nameof(ViewModel.NetHistoryIndex)
                               or nameof(ViewModel.GameCpuHistoryIndex)
                               or nameof(ViewModel.GpuUtilHistoryIndex))
                DrawSparklines();
        };

        ViewModel.LogLines.CollectionChanged += (_, _) =>
        {
            if (_pendingScroll) return;
            _pendingScroll = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _pendingScroll = false;
                if (LogListView.Items.Count > 0)
                    LogListView.ScrollIntoView(LogListView.Items[^1]);
            });
        };
    }

    private void DrawSparklines()
    {
        DrawNetworkSparklines();
        DrawCpuSparkline();
        DrawGpuSparkline();
    }

    private void DrawNetworkSparklines()
    {
        SparklineCanvas.Children.Clear();
        double w = SparklineCanvas.ActualWidth;
        double h = SparklineCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int totalSize = ViewModel.RxHistory.Length;
        int window = Math.Min(ViewModel.HistoryWindowSeconds, totalSize);

        DrawLine(SparklineCanvas, ViewModel.RxHistory, ViewModel.NetHistoryIndex, totalSize, window,
                 w, h * 0.5, 0, Windows.UI.Color.FromArgb(255, 64, 200, 200));
        DrawLine(SparklineCanvas, ViewModel.TxHistory, ViewModel.NetHistoryIndex, totalSize, window,
                 w, h * 0.5, h * 0.5, Windows.UI.Color.FromArgb(255, 100, 100, 100));
    }

    private void DrawCpuSparkline()
    {
        CpuSparklineCanvas.Children.Clear();
        double w = CpuSparklineCanvas.ActualWidth;
        double h = CpuSparklineCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int n = ViewModel.GameCpuHistory.Length;
        DrawLine(CpuSparklineCanvas, ViewModel.GameCpuHistory, ViewModel.GameCpuHistoryIndex, n, n,
                 w, h, 0, Windows.UI.Color.FromArgb(180, 100, 200, 120));
    }

    private void DrawGpuSparkline()
    {
        GpuSparklineCanvas.Children.Clear();
        double w = GpuSparklineCanvas.ActualWidth;
        double h = GpuSparklineCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int n = ViewModel.GpuUtilHistory.Length;
        DrawLine(GpuSparklineCanvas, ViewModel.GpuUtilHistory, ViewModel.GpuUtilHistoryIndex, n, n,
                 w, h, 0, Windows.UI.Color.FromArgb(180, 64, 160, 220));
    }

    private static void DrawLine(Canvas canvas, int[] history, int histIdx, int totalSize, int window,
                                  double canvasW, double lineH, double offsetY,
                                  Windows.UI.Color color)
    {
        if (window < 2) return;

        int peak = 0;
        for (int i = 0; i < window; i++)
            peak = Math.Max(peak, history[(histIdx - window + i + totalSize) % totalSize]);
        if (peak <= 0) peak = 1;

        var points = new PointCollection();
        for (int i = 0; i < window; i++)
        {
            int sampleIdx = (histIdx - window + i + totalSize) % totalSize;
            double x = i * (canvasW / (window - 1));
            double y = offsetY + lineH - (history[sampleIdx] / (double)peak * (lineH - 2)) - 1;
            points.Add(new Windows.Foundation.Point(x, y));
        }

        var poly = new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round
        };
        canvas.Children.Add(poly);
    }

    private void HistWindow_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var btn = (ToggleButton)sender;
        int seconds = int.Parse((string)btn.Tag);
        ViewModel.HistoryWindowSeconds = seconds;
        Hist30sBtn.IsChecked = btn == Hist30sBtn;
        Hist2mBtn.IsChecked  = btn == Hist2mBtn;
        Hist5mBtn.IsChecked  = btn == Hist5mBtn;
        NetHistoryLabel.Text = $"NETWORK  ({(seconds == 30 ? "30s" : seconds == 120 ? "2m" : "5m")} history)";
        DrawNetworkSparklines();
    }

    private void PinToggle_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => App.OptimizerService?.TogglePinning();

    private void TrayBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => App.Window?.HideToTray();

    private void SettingsBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => App.Window?.ShowSettings();

    private void ReportsBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var dir = GameOptimizer.Services.SessionTracker.ReportDir;
        if (Directory.Exists(dir))
            _ = Launcher.LaunchUriAsync(new Uri(dir));
        else
        {
            Directory.CreateDirectory(dir);
            _ = Launcher.LaunchUriAsync(new Uri(dir));
        }
    }

    private void ExitBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => App.RequestExit();
}

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
        // Each canvas redraws only when its own index advances (histories are
        // always set before their index, so the index change is the signal) -
        // redrawing everything on any history property meant up to 6 full
        // redraws of all four canvases per tick.
        ViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ViewModel.NetHistoryIndex):     DrawNetworkSparklines(); break;
                case nameof(ViewModel.GameCpuHistoryIndex): DrawCpuSparkline(); break;
                case nameof(ViewModel.GpuUtilHistoryIndex): DrawGpuSparkline(); break;
                case nameof(ViewModel.LatencyHistoryIndex): DrawLatencySparkline(); break;
            }
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

    private void DrawLatencySparkline()
    {
        LatencySparklineCanvas.Children.Clear();
        double w = LatencySparklineCanvas.ActualWidth;
        double h = LatencySparklineCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int n = ViewModel.LatencyHistory.Length;
        DrawLine(LatencySparklineCanvas, ViewModel.LatencyHistory, ViewModel.LatencyHistoryIndex, n, n,
                 w, h, 0, Windows.UI.Color.FromArgb(255, 120, 180, 240), fill: true);
    }

    private void DrawNetworkSparklines()
    {
        SparklineCanvas.Children.Clear();
        double w = SparklineCanvas.ActualWidth;
        double h = SparklineCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int totalSize = ViewModel.RxHistory.Length;
        int window = Math.Min(ViewModel.HistoryWindowSeconds, totalSize);

        // RX: top 55%, vivid cyan with gradient fill; TX: bottom 40%, muted
        DrawLine(SparklineCanvas, ViewModel.RxHistory, ViewModel.NetHistoryIndex, totalSize, window,
                 w, h * 0.54, 0, Windows.UI.Color.FromArgb(255, 0, 200, 232), fill: true);
        DrawLine(SparklineCanvas, ViewModel.TxHistory, ViewModel.NetHistoryIndex, totalSize, window,
                 w, h * 0.40, h * 0.58, Windows.UI.Color.FromArgb(255, 90, 90, 90));
    }

    private void DrawCpuSparkline()
    {
        CpuSparklineCanvas.Children.Clear();
        double w = CpuSparklineCanvas.ActualWidth;
        double h = CpuSparklineCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int n = ViewModel.GameCpuHistory.Length;
        DrawLine(CpuSparklineCanvas, ViewModel.GameCpuHistory, ViewModel.GameCpuHistoryIndex, n, n,
                 w, h, 0, Windows.UI.Color.FromArgb(255, 80, 210, 110), fill: true);
    }

    private void DrawGpuSparkline()
    {
        GpuSparklineCanvas.Children.Clear();
        double w = GpuSparklineCanvas.ActualWidth;
        double h = GpuSparklineCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int n = ViewModel.GpuUtilHistory.Length;
        DrawLine(GpuSparklineCanvas, ViewModel.GpuUtilHistory, ViewModel.GpuUtilHistoryIndex, n, n,
                 w, h, 0, Windows.UI.Color.FromArgb(255, 64, 160, 240), fill: true);
    }

    // Brushes are shareable across elements - cache per color instead of
    // allocating stroke/gradient/grid brushes on every 1s redraw
    private static readonly SolidColorBrush GridBrush = new(Windows.UI.Color.FromArgb(18, 255, 255, 255));
    private static readonly Dictionary<Windows.UI.Color, SolidColorBrush> StrokeCache = [];
    private static readonly Dictionary<Windows.UI.Color, LinearGradientBrush> FillCache = [];

    private static void DrawLine(Canvas canvas, int[] history, int histIdx, int totalSize, int window,
                                  double canvasW, double lineH, double offsetY,
                                  Windows.UI.Color color, bool fill = false)
    {
        if (window < 2) return;

        int peak = 0;
        for (int i = 0; i < window; i++)
            peak = Math.Max(peak, history[(histIdx - window + i + totalSize) % totalSize]);
        if (peak <= 0) peak = 1;

        // Faint horizontal grid lines at 25 / 50 / 75 %
        for (int g = 1; g < 4; g++)
        {
            double gy = offsetY + lineH * (1.0 - g / 4.0);
            canvas.Children.Add(new Line
            {
                X1 = 0, Y1 = gy, X2 = canvasW, Y2 = gy,
                Stroke = GridBrush,
                StrokeThickness = 1
            });
        }

        // Build point list (shared by both fill polygon and stroke line)
        var pts = new Windows.Foundation.Point[window];
        for (int i = 0; i < window; i++)
        {
            int sampleIdx = (histIdx - window + i + totalSize) % totalSize;
            double x = i * (canvasW / (window - 1));
            double y = offsetY + lineH - (history[sampleIdx] / (double)peak * (lineH - 2)) - 1;
            pts[i] = new Windows.Foundation.Point(x, y);
        }

        // Gradient fill polygon under the line
        if (fill)
        {
            var fillPts = new PointCollection();
            foreach (var p in pts) fillPts.Add(p);
            fillPts.Add(new Windows.Foundation.Point(canvasW, offsetY + lineH));
            fillPts.Add(new Windows.Foundation.Point(0, offsetY + lineH));

            if (!FillCache.TryGetValue(color, out var grad))
            {
                grad = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint   = new Windows.Foundation.Point(0, 1)
                };
                grad.GradientStops.Add(new GradientStop
                    { Color = Windows.UI.Color.FromArgb(55, color.R, color.G, color.B), Offset = 0 });
                grad.GradientStops.Add(new GradientStop
                    { Color = Windows.UI.Color.FromArgb(0,  color.R, color.G, color.B), Offset = 1 });
                FillCache[color] = grad;
            }

            canvas.Children.Add(new Polygon { Points = fillPts, Fill = grad });
        }

        // Stroke line on top
        var linePoints = new PointCollection();
        foreach (var p in pts) linePoints.Add(p);
        if (!StrokeCache.TryGetValue(color, out var stroke))
            StrokeCache[color] = stroke = new SolidColorBrush(color);
        canvas.Children.Add(new Polyline
        {
            Points = linePoints,
            Stroke = stroke,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round
        });
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

    private void FlushRamBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => App.OptimizerService?.FlushStandbyRam();
}

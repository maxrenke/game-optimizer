using GameOptimizer.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.System;

namespace GameOptimizer;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel => App.MainViewModel;

    public MainPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.RxHistory)
                               or nameof(ViewModel.TxHistory)
                               or nameof(ViewModel.NetHistoryIndex))
                DrawSparklines();
        };

        ViewModel.LogLines.CollectionChanged += (_, _) =>
        {
            if (LogListView.Items.Count > 0)
                LogListView.ScrollIntoView(LogListView.Items[^1]);
        };
    }

    private void DrawSparklines()
    {
        SparklineCanvas.Children.Clear();
        double w = SparklineCanvas.ActualWidth;
        double h = SparklineCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        DrawLine(ViewModel.RxHistory, ViewModel.NetHistoryIndex, w, h * 0.5, 0,
                 Windows.UI.Color.FromArgb(255, 64, 200, 200));
        DrawLine(ViewModel.TxHistory, ViewModel.NetHistoryIndex, w, h * 0.5, h * 0.5,
                 Windows.UI.Color.FromArgb(255, 100, 100, 100));
    }

    private void DrawLine(int[] history, int idx, double canvasW, double lineH, double offsetY,
                          Windows.UI.Color color)
    {
        int n = history.Length;
        int peak = history.Max();
        if (peak <= 0) peak = 1;

        var points = new PointCollection();
        for (int i = 0; i < n; i++)
        {
            int sampleIdx = (idx - n + i + n) % n;
            double x = i * (canvasW / (n - 1));
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
        SparklineCanvas.Children.Add(poly);
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

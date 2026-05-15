using GameOptimizer.Services;
using GameOptimizer.Tray;
using GameOptimizer.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.Security.Principal;

namespace GameOptimizer;

public partial class App : Application
{
    public static MainWindow Window { get; private set; } = null!;
    public static DispatcherQueue UIDispatcher { get; private set; } = null!;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public static OptimizerConfig Config { get; private set; } = OptimizerConfig.Load();
    public static OptimizerServiceWrapper? OptimizerService { get; private set; }
    public static TrayService? TrayService { get; private set; }
    public static MainPageViewModel MainViewModel { get; private set; } = null!;

    public App() { InitializeComponent(); }

    private static bool IsElevated() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!IsElevated())
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath is not null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(exePath)
                        { UseShellExecute = true, Verb = "runas" });
                    Application.Current.Exit();
                    return;
                }
                catch { } // user cancelled UAC or failed - continue without elevation
            }
            // Running without admin - affinity/priority changes will fail silently
        }
        UIDispatcher = DispatcherQueue.GetForCurrentThread();
        MainViewModel = new MainPageViewModel(UIDispatcher);

        Window = new MainWindow();
        Window.Activate();

        if (Config.StartMinimized)
            Window.HideToTray();

        // Tray icon (uses H.NotifyIcon.WinUI; must init on UI thread)
        TrayService = new TrayService(UIDispatcher) { WindowVisible = true };
        TrayService.TogglePinRequested    += () => OptimizerService?.TogglePinning();
        TrayService.ShowStatusRequested   += ShowTrayStatus;
        TrayService.ToggleWindowRequested += ToggleWindow;
        TrayService.ExitRequested         += RequestExit;
        TrayService.Initialize();

        // Start optimizer service on background thread so UI can render first
        Task.Run(() =>
        {
            var svc = new Services.OptimizerService(Config);
            svc.SnapshotReady += snap =>
            {
                MainViewModel.ApplySnapshot(snap);
                if (TrayService is not null)
                {
                    TrayService.PinEnabled  = snap.PinningEnabled;
                    TrayService.CurrentGame = snap.IsGaming ? snap.GameName : null;
                }
            };
            svc.AlertFired += msg => TrayService?.ShowBalloon(msg);
            svc.Start();
            UIDispatcher.TryEnqueue(() => OptimizerService = new OptimizerServiceWrapper(svc));
        });
    }

    private static void ShowTrayStatus()
    {
        var msg = $"{(MainViewModel.IsGaming ? $"Gaming: {MainViewModel.GameName}" : "Idle")}  |  " +
                  $"{(MainViewModel.PinningEnabled ? "Pinning ON" : "Pinning OFF")}  |  " +
                  $"{DateTime.Now:HH:mm}";
        TrayService?.ShowBalloon(msg);
    }

    private static void ToggleWindow()
    {
        if (TrayService?.WindowVisible == true)
            Window.HideToTray();
        else
            Window.ShowFromTray();
    }

    public static void RequestExit()
    {
        UIDispatcher.TryEnqueue(() =>
        {
            OptimizerService?.Stop();
            TrayService?.Dispose();
            Microsoft.UI.Xaml.Application.Current.Exit();
        });
    }
}

public class OptimizerServiceWrapper(Services.OptimizerService inner)
{
    public void TogglePinning()
    {
        Task.Run(() => inner.PinningEnabled = !inner.PinningEnabled);
    }

    public void Stop()
    {
        _ = inner.SaveReport();
        inner.Stop();
    }
}

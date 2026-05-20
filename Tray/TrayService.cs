using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace GameOptimizer.Tray;

public class TrayService : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint hInst, string lpszName, uint uType, int cx, int cy, uint fuLoad);
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x10;

    // The exe runs from the build root but assets may be staged under AppX\.
    private static string? FindIconPath()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
            Path.Combine(AppContext.BaseDirectory, "AppX", "Assets", "AppIcon.ico"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private TaskbarIcon? _icon;
    private readonly DispatcherQueue _dispatcher;

    public bool PinEnabled { get; set; } = true;
    public bool WindowVisible { get; set; } = true;
    public string? CurrentGame { get; set; }
    public int GameCpuPct { get; set; }
    public int GpuUtil { get; set; }
    public int GpuTempC { get; set; }
    public int RxNowKbps { get; set; }
    public bool GpuAvailable { get; set; }

    private string? _pendingBalloon;

    public event Action? TogglePinRequested;
    public event Action? FlushRamRequested;
    public event Action? ToggleWindowRequested;
    public event Action? ExitRequested;

    public TrayService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Initialize()
    {
        _dispatcher.TryEnqueue(() =>
        {
            // No IconSource: a BitmapImage from ms-appx loads asynchronously and
            // is still blank when H.NotifyIcon renders the tray icon. The real
            // icon is set synchronously below from a Win32 HICON.
            _icon = new TaskbarIcon
            {
                ToolTipText = "Gaming Optimizer - Idle",
            };

            // The tray menu renders as a native Win32 popup, which invokes each
            // item's Command - Click events are never raised because the items
            // are not in a live XAML visual tree. All items must use Command.
            var windowItem = new MenuFlyoutItem
            {
                Text = "Show Window",
                Command = new RelayCommand(() => ToggleWindowRequested?.Invoke()),
            };

            var pinItem = new MenuFlyoutItem
            {
                Text = "CPU Pinning: ON",
                Command = new RelayCommand(() => TogglePinRequested?.Invoke()),
            };

            var flushItem = new MenuFlyoutItem
            {
                Text = "Flush Standby RAM",
                Command = new RelayCommand(() => FlushRamRequested?.Invoke()),
            };

            var exitItem = new MenuFlyoutItem
            {
                Text = "Exit",
                Command = new RelayCommand(() => ExitRequested?.Invoke()),
            };

            var menu = new MenuFlyout();
            menu.Items.Add(windowItem);
            menu.Items.Add(pinItem);
            menu.Items.Add(flushItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(exitItem);

            _icon.ContextFlyout = menu;
            _icon.DoubleClickCommand = new RelayCommand(() => ToggleWindowRequested?.Invoke());

            var timer = _dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (_, _) =>
            {
                var pin = PinEnabled ? "PIN ON" : "PIN OFF";
                var gpuPart = GpuAvailable ? $" | GPU {GpuUtil}% {GpuTempC}C" : "";
                var tip = CurrentGame is not null
                    ? $"Gaming: {CurrentGame} | CPU {GameCpuPct}%{gpuPart} | {pin}"
                    : $"Idle | CPU {GameCpuPct}%{gpuPart} | {pin}";
                _icon.ToolTipText = tip[..Math.Min(127, tip.Length)];
                pinItem.Text = $"CPU Pinning: {(PinEnabled ? "ON" : "OFF")}";
                windowItem.Text = WindowVisible ? "Hide Window" : "Show Window";

                if (_pendingBalloon is not null)
                {
                    _icon.ShowNotification("Gaming Optimizer", _pendingBalloon,
                        NotificationIcon.Info, null, false, true, false, false, TimeSpan.FromSeconds(4));
                    _pendingBalloon = null;
                }
            };
            timer.Start();

            _icon.ForceCreate(false);

            // Set the tray image from a real Win32 HICON loaded synchronously
            var icoPath = FindIconPath();
            if (icoPath is not null)
            {
                var hIcon = LoadImage(0, icoPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                if (hIcon != 0) _icon.TrayIcon.UpdateIcon(hIcon);
            }
        });
    }

    public void ShowBalloon(string message) => _pendingBalloon = message;

    public void Dispose()
    {
        _dispatcher.TryEnqueue(() =>
        {
            _icon?.Dispose();
            _icon = null;
        });
        GC.SuppressFinalize(this);
    }
}

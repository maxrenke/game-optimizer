using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GameOptimizer.Tray;

public class TrayService : IDisposable
{
    private TaskbarIcon? _icon;
    private readonly DispatcherQueue _dispatcher;

    public bool PinEnabled { get; set; } = true;
    public bool WindowVisible { get; set; } = true;
    public string? CurrentGame { get; set; }

    private string? _pendingBalloon;

    public event Action? TogglePinRequested;
    public event Action? ShowStatusRequested;
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
            _icon = new TaskbarIcon
            {
                IconSource = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon.ico")),
                ToolTipText = "Gaming Optimizer - Idle",
            };

            var pinItem = new MenuFlyoutItem { Text = "CPU Pinning: ON" };
            pinItem.Click += (_, _) => TogglePinRequested?.Invoke();

            var statusItem = new MenuFlyoutItem { Text = "Show Status" };
            statusItem.Click += (_, _) => ShowStatusRequested?.Invoke();

            var windowItem = new MenuFlyoutItem { Text = "Hide to Tray" };
            windowItem.Click += (_, _) => ToggleWindowRequested?.Invoke();

            var exitItem = new MenuFlyoutItem { Text = "Exit" };
            exitItem.Click += (_, _) => ExitRequested?.Invoke();

            var menu = new MenuFlyout();
            menu.Items.Add(pinItem);
            menu.Items.Add(statusItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(windowItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(exitItem);

            _icon.ContextFlyout = menu;
            _icon.DoubleClickCommand = new RelayCommand(() => ToggleWindowRequested?.Invoke());

            var timer = _dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (_, _) =>
            {
                var pin = PinEnabled ? "PIN ON" : "PIN OFF";
                var tip = CurrentGame is not null ? $"Gaming: {CurrentGame} | {pin}" : $"Idle | {pin}";
                _icon.ToolTipText = tip[..Math.Min(63, tip.Length)];
                pinItem.Text = $"CPU Pinning: {(PinEnabled ? "ON" : "OFF")}";
                windowItem.Text = WindowVisible ? "Hide to Tray" : "Show Window";

                if (_pendingBalloon is not null)
                {
                    _icon.ShowNotification("Gaming Optimizer", _pendingBalloon,
                        NotificationIcon.Info, null, false, true, false, false, TimeSpan.FromSeconds(4));
                    _pendingBalloon = null;
                }
            };
            timer.Start();

            _icon.ForceCreate(false);
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

using Microsoft.UI.Xaml;

namespace GameOptimizer;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        Title = "Gaming Optimizer v4";

        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            HideToTray();
        };

        AppWindow.Changed += (_, _) =>
        {
            if (AppWindow.Size.Width < 800)
                AppWindow.Resize(new Windows.Graphics.SizeInt32(800, AppWindow.Size.Height));
            if (AppWindow.Size.Height < 600)
                AppWindow.Resize(new Windows.Graphics.SizeInt32(AppWindow.Size.Width, 600));
        };

        RootFrame.Navigate(typeof(MainPage));
    }

    public void HideToTray()
    {
        AppWindow.Hide();
        if (App.TrayService is not null) App.TrayService.WindowVisible = false;
    }

    public void ShowFromTray()
    {
        AppWindow.Show();
        if (App.TrayService is not null) App.TrayService.WindowVisible = true;
    }

    public void UpdatePinState(bool pinEnabled)
    {
        AppTitleBar.Subtitle = pinEnabled ? "CPU PINNING ON" : "CPU PINNING OFF";
    }

    public void ShowSettings() => RootFrame.Navigate(typeof(SettingsPage));
    public void ShowMain() => RootFrame.Navigate(typeof(MainPage));
}

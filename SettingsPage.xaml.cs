using GameOptimizer.Services;
using GameOptimizer.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace GameOptimizer;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new(App.Config);

    public SettingsPage()
    {
        InitializeComponent();
    }

    private void BackBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => App.Window.ShowMain();

    private void RemoveProfile_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameProfile profile })
            ViewModel.RemoveProfile(profile);
    }

    private void RemoveGamePath_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            ViewModel.RemoveGamePathCommand.Execute(path);
    }

    private void RemoveThrottledProc_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string proc })
            ViewModel.RemoveThrottledProcCommand.Execute(proc);
    }

    private void RemoveSuspendApp_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: SuspendApp app })
            ViewModel.RemoveSuspendApp(app);
    }

    private void SaveBackBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.SaveCommand.Execute(null);
        App.Window.ShowMain();
    }

    private async void ResetAllBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Reset all process state?",
            Content = "This will immediately restore all process affinities and priorities to " +
                      "Windows defaults and clear all pinning state.\n\n" +
                      "CPU pinning will be turned off. The optimizer will continue running.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (App.OptimizerService is null) return;
        App.OptimizerService.ResetAll();
    }
}

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

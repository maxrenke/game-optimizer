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
}

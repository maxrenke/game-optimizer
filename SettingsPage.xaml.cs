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
}

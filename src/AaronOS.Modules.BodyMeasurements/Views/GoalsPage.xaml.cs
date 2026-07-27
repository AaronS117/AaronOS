using AaronOS.Core;
using AaronOS.Modules.BodyMeasurements.Data;
using AaronOS.Modules.BodyMeasurements.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AaronOS.Modules.BodyMeasurements.Views;

public sealed partial class GoalsPage : Page
{
    public GoalsViewModel ViewModel { get; }

    public GoalsPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<GoalsViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void AchievedButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Goal goal })
        {
            _ = ViewModel.MarkAchievedCommand.ExecuteAsync(goal);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Goal goal })
        {
            _ = ViewModel.DeleteCommand.ExecuteAsync(goal);
        }
    }
}

using AaronOS.Core;
using AaronOS.Modules.BodyMeasurements.Data;
using AaronOS.Modules.BodyMeasurements.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.BodyMeasurements.Views;

public sealed partial class GoalsPage : Page
{
    public GoalsViewModel ViewModel { get; }

    public GoalsPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<GoalsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
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

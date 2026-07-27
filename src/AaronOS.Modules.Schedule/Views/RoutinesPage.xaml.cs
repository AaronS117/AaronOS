using System.Windows;
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class RoutinesPage : Page
{
    public RoutinesViewModel ViewModel { get; }

    public RoutinesPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<RoutinesViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RoutineRow row })
        {
            _ = ViewModel.CompleteCommand.ExecuteAsync(row);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RoutineRow row })
        {
            _ = ViewModel.DeleteRoutineCommand.ExecuteAsync(row);
        }
    }
}

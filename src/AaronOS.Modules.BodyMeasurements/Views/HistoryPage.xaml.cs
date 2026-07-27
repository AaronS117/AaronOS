using AaronOS.Core;
using AaronOS.Modules.BodyMeasurements.Data;
using AaronOS.Modules.BodyMeasurements.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.BodyMeasurements.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<HistoryViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BodyCheckIn checkIn })
        {
            _ = ViewModel.DeleteCommand.ExecuteAsync(checkIn);
        }
    }
}

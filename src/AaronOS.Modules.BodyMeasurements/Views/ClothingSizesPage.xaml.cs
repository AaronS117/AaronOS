using AaronOS.Core;
using AaronOS.Modules.BodyMeasurements.Data;
using AaronOS.Modules.BodyMeasurements.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.BodyMeasurements.Views;

public sealed partial class ClothingSizesPage : Page
{
    public ClothingSizesViewModel ViewModel { get; }

    public ClothingSizesPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<ClothingSizesViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClothingSizeEntry entry })
        {
            _ = ViewModel.DeleteCommand.ExecuteAsync(entry);
        }
    }
}

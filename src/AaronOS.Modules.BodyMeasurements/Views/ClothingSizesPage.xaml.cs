using AaronOS.Core;
using AaronOS.Modules.BodyMeasurements.Data;
using AaronOS.Modules.BodyMeasurements.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AaronOS.Modules.BodyMeasurements.Views;

public sealed partial class ClothingSizesPage : Page
{
    public ClothingSizesViewModel ViewModel { get; }

    public ClothingSizesPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<ClothingSizesViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClothingSizeEntry entry })
        {
            _ = ViewModel.DeleteCommand.ExecuteAsync(entry);
        }
    }
}

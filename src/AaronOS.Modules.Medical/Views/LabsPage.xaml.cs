using AaronOS.Core;
using AaronOS.Modules.Medical.Data;
using AaronOS.Modules.Medical.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Medical.Views;

public sealed partial class LabsPage : Page
{
    public LabsViewModel ViewModel { get; }

    public LabsPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<LabsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void DeleteResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LabResult item })
        {
            _ = ViewModel.DeleteResultCommand.ExecuteAsync(item);
        }
    }
}

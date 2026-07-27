using AaronOS.Core;
using AaronOS.Modules.Medical.Data;
using AaronOS.Modules.Medical.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Medical.Views;

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

    // Row actions read their item off DataContext, matching the pattern in ClothingSizesPage.xaml.cs.
    private void DeleteCondition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MedicalCondition item })
        {
            _ = ViewModel.DeleteConditionCommand.ExecuteAsync(item);
        }
    }

    private void DeleteProcedure_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MedicalProcedure item })
        {
            _ = ViewModel.DeleteProcedureCommand.ExecuteAsync(item);
        }
    }

    private void DeleteImmunization_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Immunization item })
        {
            _ = ViewModel.DeleteImmunizationCommand.ExecuteAsync(item);
        }
    }
}

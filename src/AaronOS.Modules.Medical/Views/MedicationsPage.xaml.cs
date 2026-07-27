using AaronOS.Core;
using AaronOS.Modules.Medical.Data;
using AaronOS.Modules.Medical.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Medical.Views;

public sealed partial class MedicationsPage : Page
{
    public MedicationsViewModel ViewModel { get; }

    public MedicationsPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<MedicationsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void StopMedication_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Medication item })
        {
            _ = ViewModel.StopMedicationCommand.ExecuteAsync(item);
        }
    }

    private void DeleteMedication_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Medication item })
        {
            _ = ViewModel.DeleteMedicationCommand.ExecuteAsync(item);
        }
    }

    private void DeleteAllergy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Allergy item })
        {
            _ = ViewModel.DeleteAllergyCommand.ExecuteAsync(item);
        }
    }
}

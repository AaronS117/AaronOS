using AaronOS.Core;
using AaronOS.Modules.Medical.Data;
using AaronOS.Modules.Medical.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Medical.Views;

public sealed partial class MoodPage : Page
{
    public MoodViewModel ViewModel { get; }

    public MoodPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<MoodViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MoodEntry entry })
        {
            _ = ViewModel.DeleteCommand.ExecuteAsync(entry);
        }
    }
}

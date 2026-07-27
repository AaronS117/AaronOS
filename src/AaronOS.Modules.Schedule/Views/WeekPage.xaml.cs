using System.Windows;
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class WeekPage : Page
{
    public WeekViewModel ViewModel { get; }

    public WeekPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<WeekViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScheduleBlock block })
        {
            _ = ViewModel.DeleteBlockCommand.ExecuteAsync(block);
        }
    }

    private void DayOff_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AgendaDay day })
        {
            _ = ViewModel.AddExceptionCommand.ExecuteAsync(day.Date);
        }
    }
}

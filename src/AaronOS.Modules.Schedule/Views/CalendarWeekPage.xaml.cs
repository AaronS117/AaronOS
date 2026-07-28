using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class CalendarWeekPage : Page
{
    public CalendarWeekViewModel ViewModel { get; }

    public CalendarWeekPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<CalendarWeekViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            // Open on the working day. Without this the grid starts at midnight and every real
            // commitment is below the fold.
            GridScroller.ScrollToVerticalOffset(Calendar.TimeGridLayout.HourHeight * 7);
        };
    }

    // Wired up in Task 6: click a column to create a block at that time.
    private void DayColumn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
    }
}

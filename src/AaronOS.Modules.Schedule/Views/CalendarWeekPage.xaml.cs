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

            // ScrollToVerticalOffset silently clamps to the current extent, and on first load the
            // grid has not been measured yet — so scrolling here directly leaves the view at
            // midnight, which is the one thing this scroll exists to prevent. Wait for one layout
            // pass, then scroll, once.
            void ScrollToMorning(object? _, EventArgs __)
            {
                GridScroller.LayoutUpdated -= ScrollToMorning;
                GridScroller.ScrollToVerticalOffset(Calendar.TimeGridLayout.HourHeight * 7);
            }

            GridScroller.LayoutUpdated += ScrollToMorning;
        };
    }

    // Wired up in Task 6: click a column to create a block at that time.
    private void DayColumn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
    }
}

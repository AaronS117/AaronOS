using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Schedule.Views;

/// <summary>
/// The module's single nav-pane entry point. Hosts an internal Frame so the shell needs only one
/// NavigationView item, per docs/MODULE_GUIDELINES.md.
/// </summary>
public sealed partial class ScheduleShellPage : Page
{
    public ScheduleShellPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ContentFrame.Navigate(new TodayPage());
    }

    private void Week_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new CalendarWeekPage());

    private void Routines_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new RoutinesPage());
}

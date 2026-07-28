using System.Windows;
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

/// <summary>
/// The Schedule module's contribution to the app's Settings page (see IAppModule.SettingsContentType):
/// calendar configuration is one-time setup, not a day-to-day workflow, so it belongs in Settings
/// rather than in the module's own sub-navigation — the same reasoning as Finance's bank linking.
/// </summary>
public sealed partial class ScheduleSettingsSection : UserControl
{
    public ScheduleSettingsViewModel ViewModel { get; }

    public ScheduleSettingsSection()
    {
        ViewModel = AppServices.Provider.GetRequiredService<ScheduleSettingsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExternalCalendar calendar })
        {
            _ = ViewModel.SyncNowCommand.ExecuteAsync(calendar);
        }
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExternalCalendar calendar })
        {
            _ = ViewModel.ToggleEnabledCommand.ExecuteAsync(calendar);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExternalCalendar calendar })
        {
            _ = ViewModel.RemoveCalendarCommand.ExecuteAsync(calendar);
        }
    }
}

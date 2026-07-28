using AaronOS.Core;
using AaronOS.Modules.Schedule.External;
using AaronOS.Modules.Schedule.ViewModels;
using AaronOS.Modules.Schedule.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule;

public class ScheduleModule : IAppModule
{
    public string Id => "schedule";
    public string DisplayName => "Schedule";
    public string IconGlyph => "CalendarLtr24";
    public Type HomePageType => typeof(ScheduleShellPage);

    /// <summary>Calendar configuration is one-time setup, so it belongs in Settings rather than in
    /// this module's own sub-navigation — the same reasoning as Finance's bank linking.</summary>
    public Type? SettingsContentType => typeof(ScheduleSettingsSection);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<TodayViewModel>();
        services.AddTransient<WeekViewModel>();
        services.AddTransient<CalendarWeekViewModel>();
        services.AddTransient<RoutinesViewModel>();
        services.AddTransient<ScheduleSettingsViewModel>();
        services.AddSingleton<ScheduleSyncService>();

        services.AddHttpClient(nameof(IcsFeedClient), client =>
        {
            // A published feed that hangs must not hold up a sync pass.
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IExternalCalendarSource, IcsFeedClient>();
    }
}

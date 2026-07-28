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

    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<TodayViewModel>();
        services.AddTransient<WeekViewModel>();
        services.AddTransient<RoutinesViewModel>();
        services.AddSingleton<ScheduleSyncService>();

        services.AddHttpClient(nameof(IcsFeedClient), client =>
        {
            // A published feed that hangs must not hold up a sync pass.
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IExternalCalendarSource, IcsFeedClient>();
    }
}

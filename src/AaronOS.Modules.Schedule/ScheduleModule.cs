using AaronOS.Core;
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
    }
}

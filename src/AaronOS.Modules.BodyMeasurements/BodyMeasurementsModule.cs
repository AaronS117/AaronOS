using AaronOS.Core;
using AaronOS.Modules.BodyMeasurements.ViewModels;
using AaronOS.Modules.BodyMeasurements.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace AaronOS.Modules.BodyMeasurements;

public class BodyMeasurementsModule : IAppModule
{
    public string Id => "body-measurements";
    public string DisplayName => "Body Measurements";
    public IconElement Icon => new FontIcon { Glyph = "" };
    public Type HomePageType => typeof(BodyMeasurementsShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CheckInViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<ClothingSizesViewModel>();
        services.AddTransient<GoalsViewModel>();
    }
}

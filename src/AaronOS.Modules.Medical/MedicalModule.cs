using AaronOS.Core;
using AaronOS.Modules.Medical.ViewModels;
using AaronOS.Modules.Medical.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Medical;

public class MedicalModule : IAppModule
{
    public string Id => "medical";
    public string DisplayName => "Medical";
    public string IconGlyph => "HeartPulse24";
    public Type HomePageType => typeof(MedicalShellPage);

    // SettingsContentType stays null: importing a record is a multi-step workflow with a review
    // table, which belongs on its own page rather than squeezed into a Settings card.

    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<MedicalOverviewViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<MedicationsViewModel>();
        services.AddTransient<VisitsViewModel>();
        services.AddTransient<LabsViewModel>();
        services.AddTransient<MoodViewModel>();
        services.AddTransient<ImportViewModel>();
    }
}

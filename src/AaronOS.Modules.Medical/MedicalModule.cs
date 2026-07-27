using AaronOS.Core;
using AaronOS.Modules.Medical.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Medical;

public class MedicalModule : IAppModule
{
    public string Id => "medical";
    public string DisplayName => "Medical";
    public string IconGlyph => "HeartPulse24"; // confirmed against Wpf.Ui.Controls.SymbolRegular at build time
    public Type HomePageType => typeof(MedicalShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        // Later tasks add ViewModel registrations here.
    }
}

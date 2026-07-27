using AaronOS.Core;
using AaronOS.Modules.Nutrition.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Nutrition;

public class NutritionModule : IAppModule
{
    public string Id => "nutrition";
    public string DisplayName => "Nutrition";
    public string IconGlyph => "Food24"; // confirm exact Wpf.Ui.Controls.SymbolRegular member when the app first builds
    public Type HomePageType => typeof(NutritionShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        // Later tasks add each ViewModel/service registration here as they're built.
    }
}

using AaronOS.Core;
using AaronOS.Modules.Nutrition.ShelfLife;
using AaronOS.Modules.Nutrition.Usda;
using AaronOS.Modules.Nutrition.ViewModels;
using AaronOS.Modules.Nutrition.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Nutrition;

public class NutritionModule : IAppModule
{
    public string Id => "nutrition";
    public string DisplayName => "Nutrition";
    public string IconGlyph => "Food24";
    public Type HomePageType => typeof(NutritionShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<UsdaCredentialStore>();
        services.AddSingleton<UsdaApiClient>();
        services.AddSingleton(_ => ShelfLifeEstimator.LoadFromEmbeddedResource());
        services.AddTransient<NutritionDashboardViewModel>();
        services.AddTransient<IngredientsViewModel>();
        services.AddTransient<RecipeEditViewModel>();
        services.AddTransient<InventoryViewModel>();
    }
}

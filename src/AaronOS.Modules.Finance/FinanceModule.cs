using AaronOS.Core;
using AaronOS.Modules.Finance.Plaid;
using AaronOS.Modules.Finance.ViewModels;
using AaronOS.Modules.Finance.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Finance;

public class FinanceModule : IAppModule
{
    public string Id => "finance";
    public string DisplayName => "Finance";
    public string IconGlyph => "Wallet24";
    public Type HomePageType => typeof(FinanceShellPage);

    /// <summary>Linking a bank is one-time configuration, so it lives in Settings rather than in
    /// this module's own sub-navigation.</summary>
    public Type? SettingsContentType => typeof(LinkAccountSection);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<PlaidCredentialStore>();
        services.AddSingleton<PlaidApiClient>();
        services.AddTransient<FinanceDashboardViewModel>();
        services.AddTransient<FinanceTransactionsViewModel>();
        services.AddTransient<LinkAccountViewModel>();
        services.AddTransient<RetirementViewModel>();
    }
}

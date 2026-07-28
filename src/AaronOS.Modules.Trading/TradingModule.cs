using AaronOS.Core;
using AaronOS.Modules.Trading.Agent;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Trading;
using AaronOS.Modules.Trading.ViewModels;
using AaronOS.Modules.Trading.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Trading;

public class TradingModule : IAppModule
{
    public string Id => "trading";
    public string DisplayName => "Trading";
    public string IconGlyph => "ChartMultiple24";
    public Type HomePageType => typeof(TradingShellPage);

    /// <summary>API keys are one-time configuration, so they live in Settings beside the bank link.</summary>
    public Type? SettingsContentType => typeof(TradingKeysSection);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<TradingCredentialStore>();
        services.AddSingleton<AlpacaClient>();
        services.AddSingleton<AnthropicClient>();

        // Registration order is the fallback order: an unrecognised provider name in the config
        // resolves to the first one listed.
        services.AddSingleton<IAgentProvider, AnthropicProvider>();
        services.AddSingleton<IAgentProvider, OpenAiCompatibleProvider>();
        services.AddSingleton<AgentProviderRegistry>();

        services.AddSingleton<SnapshotRecorder>();
        services.AddSingleton<TradingAgent>();

        // Singleton because it owns a timer: a transient scheduler would leave orphaned timers
        // running every time a page was navigated to.
        services.AddSingleton<TradingScheduler>();

        services.AddTransient<TradingViewModel>();
        services.AddTransient<TradingKeysViewModel>();
    }
}

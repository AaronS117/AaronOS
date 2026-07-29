using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Trading.Agent;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;
using AaronOS.Modules.Trading.ViewModels;
using AaronOS.Modules.Trading.Views;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// Starts the schedule if trading is switched on, so the run continues from launch to launch
    /// without anyone opening the Trading page. The switch in the database is the only thing that
    /// decides it, which means stopping the experiment is one toggle rather than a habit of not
    /// clicking Start.
    /// </summary>
    public async Task OnStartupAsync(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AaronOsDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var config = await db.Set<TradingConfig>().FirstOrDefaultAsync();
        if (config?.IsEnabled != true)
        {
            return;
        }

        // Arms a timer and returns; the first cycle runs in the background rather than holding up the
        // window behind a model call. The delay lets a local model server finish starting — at login
        // both are coming up at once, and a cycle that wins that race just logs a failure.
        await services.GetRequiredService<TradingScheduler>()
            .StartAsync(firstCycleDelay: TimeSpan.FromSeconds(90));
    }

    public void RegisterServices(IServiceCollection services)
    {
        // The real clock for the live app. A backtest substitutes one that it advances itself, which
        // is why the agent takes it rather than reading the wall clock: the daily order cap is
        // measured against "today", so a replay reading real time counts as a single day and refuses
        // every order after the first few.
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<TradingCredentialStore>();
        services.AddSingleton<AlpacaClient>();
        services.AddSingleton<AnthropicClient>();

        // Registration order is the fallback order: an unrecognised provider name in the config
        // resolves to the first one listed.
        services.AddSingleton<IAgentProvider, AnthropicProvider>();
        services.AddSingleton<IAgentProvider, OpenAiCompatibleProvider>();
        services.AddSingleton<AgentProviderRegistry>();

        services.AddSingleton<INewsSource, AlpacaNewsSource>();
        services.AddSingleton<StopLossGuard>();
        services.AddSingleton<SnapshotRecorder>();
        services.AddSingleton<TradingAgent>();

        // Singleton because it owns a timer: a transient scheduler would leave orphaned timers
        // running every time a page was navigated to.
        services.AddSingleton<TradingScheduler>();

        services.AddTransient<TradingViewModel>();
        services.AddTransient<TradingKeysViewModel>();
    }
}

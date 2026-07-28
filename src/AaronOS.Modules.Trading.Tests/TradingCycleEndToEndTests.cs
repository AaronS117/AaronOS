using System.Text.Json.Nodes;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Trading.Agent;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Trading.Tests;

/// <summary>A broker whose answers are scripted, and which records what was sent to it.</summary>
file sealed class ScriptedBroker(decimal equity, decimal cash, bool marketOpen, List<HeldPosition> positions)
    : AlpacaClient(new TradingCredentialStore())
{
    public List<(string Symbol, OrderSide Side, int Quantity)> Submitted { get; } = [];

    public override bool IsConfigured => true;

    public override Task<BrokerAccount> GetAccountAsync(CancellationToken token = default) =>
        Task.FromResult(new BrokerAccount(equity, cash, "ACTIVE"));

    public override Task<bool> IsMarketOpenAsync(CancellationToken token = default) =>
        Task.FromResult(marketOpen);

    public override Task<List<HeldPosition>> GetPositionsAsync(CancellationToken token = default) =>
        Task.FromResult(positions);

    public override Task<Dictionary<string, SymbolQuote>> GetQuotesAsync(
        IEnumerable<string> symbols, CancellationToken token = default) =>
        Task.FromResult(symbols.ToDictionary(
            s => s,
            s => new SymbolQuote(s, 99m, 101m),
            StringComparer.OrdinalIgnoreCase));

    public override Task<SubmittedOrder> PlaceMarketOrderAsync(
        string symbol, OrderSide side, int quantity, CancellationToken token = default)
    {
        Submitted.Add((symbol, side, quantity));
        return Task.FromResult(new SubmittedOrder($"order-{Submitted.Count}", "accepted"));
    }
}

/// <summary>A model whose turns are scripted, so the cycle is driven deterministically.</summary>
file sealed class ScriptedProvider(params AgentTurn[] turns) : IAgentProvider
{
    public string Name => "scripted";
    public bool IsConfigured => true;

    public IAgentConversation Start(
        string model, string systemPrompt, string firstUserMessage, IReadOnlyList<AgentTool> tools) =>
        new ScriptedConversation(turns);

    /// <summary>Everything the model was told, so the brief itself can be asserted on.</summary>
    public static string LastBrief { get; set; } = "";
}

file sealed class ScriptedConversation(AgentTurn[] turns) : IAgentConversation
{
    private int _index;

    public Task<AgentTurn> NextAsync(CancellationToken token = default) =>
        Task.FromResult(_index < turns.Length
            ? turns[_index++]
            : new AgentTurn("", [], 0, 0));

    public void AddToolResults(IEnumerable<(string ToolCallId, string Content)> results) =>
        Results.AddRange(results);

    public List<(string ToolCallId, string Content)> Results { get; } = [];
}

/// <summary>
/// Drives a whole cycle end to end against a scripted model and a scripted broker.
///
/// This is what can actually be proven before any money, real or simulated, is involved: that the
/// machinery does what it claims. It shows an allowed order reaching the broker and being recorded
/// with its reasoning, a refused order never reaching the broker while still appearing in the log,
/// and the refusal being handed back to the model within the same cycle. It proves nothing whatsoever
/// about whether the decisions are any good, which is a question only forward time can answer.
/// </summary>
public class TradingCycleEndToEndTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aaronos-cycle-{Guid.NewGuid():N}.db");

    private AaronOsDbContextFactory Factory => new(_dbPath);

    private sealed class AaronOsDbContextFactory(string path) : IDbContextFactory<AaronOsDbContext>
    {
        public AaronOsDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AaronOsDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            IAppModule[] modules = [new TradingModule()];
            return new AaronOsDbContext(options, modules);
        }
    }

    private async Task SeedConfigAsync(Action<TradingConfig>? adjust = null)
    {
        await using var db = Factory.CreateDbContext();
        await SchemaBootstrapper.EnsureSchemaAsync(db);

        var config = new TradingConfig
        {
            IsEnabled = true,
            Watchlist = "AAPL,MSFT",
            MaxPositionPercent = 10m,
            MaxInvestedPercent = 80m,
            MaxTradesPerDay = 6,
            Provider = "scripted",
        };
        adjust?.Invoke(config);
        db.Add(config);
        await db.SaveChangesAsync();
    }

    private static AgentTurn Turn(string text, params (string Name, JsonNode? Args)[] calls) =>
        new(text, calls.Select((c, i) => new AgentToolCall($"call-{i}", c.Name, c.Args)).ToList(), 100, 20);

    private static JsonNode Order(string symbol, string side, int quantity, string rationale = "because") =>
        new JsonObject
        {
            ["symbol"] = symbol,
            ["side"] = side,
            ["quantity"] = quantity,
            ["rationale"] = rationale,
        };

    private TradingAgent Agent(AlpacaClient broker, params AgentTurn[] turns) =>
        new(Factory, broker, new AgentProviderRegistry([new ScriptedProvider(turns)]));

    [Fact]
    public async Task AnAllowedOrderReachesTheBrokerAndIsRecordedWithItsReasoning()
    {
        await SeedConfigAsync();
        var broker = new ScriptedBroker(100_000m, 100_000m, true, []);

        var result = await Agent(
            broker,
            Turn("MSFT looks oversold.", ("place_order", Order("MSFT", "buy", 50, "Oversold on the weekly."))),
            Turn("Done for this cycle.")).RunCycleAsync();

        Assert.True(result.Ran);
        Assert.Equal("Bought 50 MSFT", result.Summary);
        Assert.Equal(("MSFT", OrderSide.Buy, 50), Assert.Single(broker.Submitted));

        await using var db = Factory.CreateDbContext();
        var order = await db.Set<TradeOrder>().SingleAsync();
        Assert.Equal("MSFT", order.Symbol);
        Assert.Equal(50, order.Quantity);
        Assert.Equal(100m, order.EstimatedPrice);
        Assert.Equal("Oversold on the weekly.", order.Rationale);

        var decision = await db.Set<AgentDecision>().SingleAsync();
        Assert.Contains("MSFT looks oversold.", decision.Reasoning);
        Assert.Null(decision.BlockedActions);
        Assert.Null(decision.Error);
        Assert.Equal(200, decision.InputTokens);
    }

    [Fact]
    public async Task AnOrderBreachingTheCapsNeverReachesTheBrokerAndIsLogged()
    {
        await SeedConfigAsync();
        var broker = new ScriptedBroker(100_000m, 100_000m, true, []);

        // 500 shares at 100 is 50,000 against a 10% cap of 10,000.
        var result = await Agent(
            broker,
            Turn("Going big on AAPL.", ("place_order", Order("AAPL", "buy", 500))),
            Turn("Understood.")).RunCycleAsync();

        Assert.Empty(broker.Submitted);
        Assert.Equal("No action — 1 order(s) refused", result.Summary);

        await using var db = Factory.CreateDbContext();
        Assert.Empty(await db.Set<TradeOrder>().ToListAsync());

        var decision = await db.Set<AgentDecision>().SingleAsync();
        Assert.NotNull(decision.BlockedActions);
        Assert.Contains("per-position cap", decision.BlockedActions);
    }

    [Fact]
    public async Task AnOffWatchlistOrderIsRefusedEvenWhenEverythingElseIsFine()
    {
        await SeedConfigAsync();
        var broker = new ScriptedBroker(100_000m, 100_000m, true, []);

        var result = await Agent(
            broker,
            Turn("TSLA is the trade.", ("place_order", Order("TSLA", "buy", 10))),
            Turn("Fine.")).RunCycleAsync();

        Assert.Empty(broker.Submitted);
        await using var db = Factory.CreateDbContext();
        var decision = await db.Set<AgentDecision>().SingleAsync();
        Assert.Contains("not on the watchlist", decision.BlockedActions);
    }

    [Fact]
    public async Task AllowedAndRefusedOrdersInOneTurnAreHandledIndependently()
    {
        await SeedConfigAsync();
        var broker = new ScriptedBroker(100_000m, 100_000m, true, []);

        var result = await Agent(
            broker,
            Turn(
                "Buying one, trying another.",
                ("place_order", Order("MSFT", "buy", 20)),
                ("place_order", Order("TSLA", "buy", 20))),
            Turn("Noted.")).RunCycleAsync();

        Assert.Equal(("MSFT", OrderSide.Buy, 20), Assert.Single(broker.Submitted));
        Assert.Equal("Bought 20 MSFT", result.Summary);

        await using var db = Factory.CreateDbContext();
        var decision = await db.Set<AgentDecision>().SingleAsync();
        Assert.Contains("TSLA", decision.BlockedActions);
    }

    [Fact]
    public async Task MalformedToolArgumentsAreRefusedWithoutEndingTheCycle()
    {
        await SeedConfigAsync();
        var broker = new ScriptedBroker(100_000m, 100_000m, true, []);

        // Null arguments stand in for JSON a weaker model failed to form.
        var result = await Agent(
            broker,
            Turn("Trying something.", ("place_order", null)),
            Turn("Recovered.")).RunCycleAsync();

        Assert.True(result.Ran);
        Assert.Null(result.Error);
        Assert.Empty(broker.Submitted);

        await using var db = Factory.CreateDbContext();
        var decision = await db.Set<AgentDecision>().SingleAsync();
        Assert.Contains("unparseable arguments", decision.BlockedActions);
    }

    [Fact]
    public async Task ClosingAPositionSellsExactlyWhatIsHeld()
    {
        await SeedConfigAsync();
        var broker = new ScriptedBroker(
            100_000m, 50_000m, true, [new HeldPosition("MSFT", 30, 3_000m)]);

        var result = await Agent(
            broker,
            Turn("Taking the profit.", ("close_position", new JsonObject
            {
                ["symbol"] = "MSFT",
                ["rationale"] = "Target reached.",
            })),
            Turn("Done.")).RunCycleAsync();

        Assert.Equal(("MSFT", OrderSide.Sell, 30), Assert.Single(broker.Submitted));
        Assert.Equal("Sold 30 MSFT", result.Summary);
    }

    [Fact]
    public async Task ADecidedHoldIsStillRecordedAsACycle()
    {
        await SeedConfigAsync();
        var broker = new ScriptedBroker(100_000m, 100_000m, true, []);

        var result = await Agent(broker, Turn("Nothing worth doing today.")).RunCycleAsync();

        Assert.Equal("No action", result.Summary);

        // Cycles that held must be logged too, or the record looks decisive in hindsight and hides
        // how often the answer was to wait.
        await using var db = Factory.CreateDbContext();
        var decision = await db.Set<AgentDecision>().SingleAsync();
        Assert.Contains("Nothing worth doing today.", decision.Reasoning);
        Assert.Equal("No action", decision.ActionSummary);
    }

    [Fact]
    public async Task TheModelIsNotCalledAtAllWhileTheMarketIsClosed()
    {
        await SeedConfigAsync();
        var broker = new ScriptedBroker(100_000m, 100_000m, marketOpen: false, []);

        var result = await Agent(
            broker,
            Turn("I would buy everything.", ("place_order", Order("MSFT", "buy", 10)))).RunCycleAsync();

        Assert.False(result.Ran);
        Assert.Contains("market is closed", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(broker.Submitted);

        // No decision row either: nothing was asked, so there is nothing to log.
        await using var db = Factory.CreateDbContext();
        Assert.Empty(await db.Set<AgentDecision>().ToListAsync());
    }

    [Fact]
    public async Task NothingRunsWhileTradingIsSwitchedOff()
    {
        await SeedConfigAsync(c => c.IsEnabled = false);
        var broker = new ScriptedBroker(100_000m, 100_000m, true, []);

        var result = await Agent(
            broker,
            Turn("Buy.", ("place_order", Order("MSFT", "buy", 10)))).RunCycleAsync();

        Assert.False(result.Ran);
        Assert.Empty(broker.Submitted);
    }

    [Fact]
    public async Task TheDailyLimitHoldsAcrossSeveralOrdersInOneCycle()
    {
        // The bug this guards: each order measured against the same stale count, so a single cycle
        // walks straight past the daily cap.
        await SeedConfigAsync(c => c.MaxTradesPerDay = 2);
        var broker = new ScriptedBroker(100_000m, 100_000m, true, []);

        await Agent(
            broker,
            Turn(
                "Three at once.",
                ("place_order", Order("MSFT", "buy", 10)),
                ("place_order", Order("AAPL", "buy", 10)),
                ("place_order", Order("MSFT", "buy", 10))),
            Turn("Done.")).RunCycleAsync();

        Assert.Equal(2, broker.Submitted.Count);

        await using var db = Factory.CreateDbContext();
        var decision = await db.Set<AgentDecision>().SingleAsync();
        Assert.Contains("limit is 2", decision.BlockedActions);
    }

    [Fact]
    public async Task TheFirstCycleStampsTheStartDateAndLaterCyclesLeaveItAlone()
    {
        await SeedConfigAsync();
        var broker = new ScriptedBroker(100_000m, 100_000m, true, []);

        await Agent(broker, Turn("Holding.")).RunCycleAsync();

        DateOnly? stamped;
        await using (var db = Factory.CreateDbContext())
        {
            stamped = (await db.Set<TradingConfig>().SingleAsync()).StartedOn;
            Assert.NotNull(stamped);
        }

        await Agent(broker, Turn("Still holding.")).RunCycleAsync();

        await using (var db = Factory.CreateDbContext())
        {
            // Immovable on purpose: a measurement window that can be reset is not a measurement.
            Assert.Equal(stamped, (await db.Set<TradingConfig>().SingleAsync()).StartedOn);
        }
    }

    [Fact]
    public async Task ABrokerFailureIsRecordedRatherThanLost()
    {
        await SeedConfigAsync();

        var result = await Agent(new ThrowingBroker(), Turn("Anything.")).RunCycleAsync();

        Assert.False(result.Ran);
        Assert.NotNull(result.Error);

        await using var db = Factory.CreateDbContext();
        var decision = await db.Set<AgentDecision>().SingleAsync();
        Assert.Equal("Cycle failed", decision.ActionSummary);
        Assert.Contains("broker unreachable", decision.Error!);
    }

    private sealed class ThrowingBroker() : AlpacaClient(new TradingCredentialStore())
    {
        public override bool IsConfigured => true;

        public override Task<bool> IsMarketOpenAsync(CancellationToken token = default) =>
            throw new AlpacaApiException("broker unreachable");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try
            {
                File.Delete(_dbPath + suffix);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless.
            }
        }

        GC.SuppressFinalize(this);
    }
}

using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Trading.Agent;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Trading.Tests;

file sealed class UnconfiguredBroker() : AlpacaClient(new TradingCredentialStore())
{
    public override bool IsConfigured => false;
}

file sealed class ConfiguredBroker() : AlpacaClient(new TradingCredentialStore())
{
    public override bool IsConfigured => true;

    public override Task<bool> IsMarketOpenAsync(CancellationToken token = default) =>
        Task.FromResult(false);
}

file sealed class UnconfiguredProvider : IAgentProvider
{
    public string Name => "openai-compatible";
    public bool IsConfigured => false;

    public IAgentConversation Start(
        string model, string systemPrompt, string firstUserMessage, IReadOnlyList<AgentTool> tools) =>
        throw new NotSupportedException();
}

/// <summary>
/// A blocked run has to be visible. Without a record, a schedule ticking away and achieving nothing
/// looks identical to one that never armed — the failure an unattended experiment is least able to
/// notice and most damaged by.
/// </summary>
public class BlockedCycleTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aaronos-blocked-{Guid.NewGuid():N}.db");

    private sealed class Factory(string path) : IDbContextFactory<AaronOsDbContext>
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

    private async Task SeedAsync()
    {
        var factory = new Factory(_dbPath);
        await using var db = factory.CreateDbContext();
        await SchemaBootstrapper.EnsureSchemaAsync(db);
        db.Add(new TradingConfig { IsEnabled = true, Provider = "openai-compatible" });
        await db.SaveChangesAsync();
    }

    private TradingAgent Agent(AlpacaClient broker, IAgentProvider provider) =>
        new(new Factory(_dbPath), broker, new AgentProviderRegistry([provider]));

    private async Task<List<AgentDecision>> DecisionsAsync()
    {
        await using var db = new Factory(_dbPath).CreateDbContext();
        return await db.Set<AgentDecision>().OrderBy(d => d.RanAtUtc).ToListAsync();
    }

    [Fact]
    public async Task AMissingBrokerKeyIsRecordedSoTheBlockageIsVisible()
    {
        await SeedAsync();

        var result = await Agent(new UnconfiguredBroker(), new UnconfiguredProvider()).RunCycleAsync();

        Assert.False(result.Ran);
        var decision = Assert.Single(await DecisionsAsync());
        Assert.Equal("Blocked", decision.ActionSummary);
        Assert.Contains("Alpaca", decision.Error!);
    }

    [Fact]
    public async Task AMissingModelProviderIsRecordedToo()
    {
        await SeedAsync();

        await Agent(new ConfiguredBroker(), new UnconfiguredProvider()).RunCycleAsync();

        var decision = Assert.Single(await DecisionsAsync());
        Assert.Contains("model provider", decision.Error!);
    }

    [Fact]
    public async Task RepeatingTheSameBlockDoesNotFillTheLog()
    {
        // A thirty-minute schedule would otherwise write about fifty identical rows a day, and a log
        // that has to be waded through is not a log anyone reads.
        await SeedAsync();
        var agent = Agent(new UnconfiguredBroker(), new UnconfiguredProvider());

        await agent.RunCycleAsync();
        await agent.RunCycleAsync();
        await agent.RunCycleAsync();

        Assert.Single(await DecisionsAsync());
    }

    [Fact]
    public async Task AChangedBlockReasonIsRecordedAsANewEntry()
    {
        await SeedAsync();

        await Agent(new UnconfiguredBroker(), new UnconfiguredProvider()).RunCycleAsync();

        // Broker sorted, model provider still missing: the reason moved on, so the log should say so.
        await Agent(new ConfiguredBroker(), new UnconfiguredProvider()).RunCycleAsync();

        var decisions = await DecisionsAsync();
        Assert.Equal(2, decisions.Count);
        Assert.Contains("Alpaca", decisions[0].Error!);
        Assert.Contains("model provider", decisions[1].Error!);
    }

    [Fact]
    public async Task AClosedMarketIsNotRecordedAtAll()
    {
        // The normal state for most of the day, carrying no information. Logging it would bury the
        // entries that matter.
        await SeedAsync();

        var provider = new ScriptedHoldProvider();
        var result = await Agent(new ConfiguredBroker(), provider).RunCycleAsync();

        Assert.False(result.Ran);
        Assert.Contains("closed", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await DecisionsAsync());
    }

    private sealed class ScriptedHoldProvider : IAgentProvider
    {
        public string Name => "openai-compatible";
        public bool IsConfigured => true;

        public IAgentConversation Start(
            string model, string systemPrompt, string firstUserMessage, IReadOnlyList<AgentTool> tools) =>
            throw new NotSupportedException("The market is closed, so the model must not be called.");
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

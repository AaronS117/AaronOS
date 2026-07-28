// Replays historical sessions through the real trading agent and grades the result against SPY.
//
//   dotnet run --project src/AaronOS.Trading.Backtest -- tune
//   dotnet run --project src/AaronOS.Trading.Backtest -- validate  (spends one of three looks)
//
// The windows are fixed in code on purpose. Choosing them per run is how a held-out period quietly
// becomes a tuning period.
using System.Globalization;
using System.IO;
using AaronOS.Modules.Trading.Agent;
using AaronOS.Modules.Trading.Backtest;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;

// Tune freely on the first window. The second is held out and its use is logged.
var windows = new Dictionary<string, (DateOnly From, DateOnly To)>(StringComparer.OrdinalIgnoreCase)
{
    ["tune"] = (new DateOnly(2025, 8, 1), new DateOnly(2026, 1, 31)),
    ["validate"] = (new DateOnly(2026, 2, 1), new DateOnly(2026, 7, 24)),
};

var which = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "tune";
if (!windows.TryGetValue(which, out var window))
{
    Console.WriteLine($"Unknown window '{which}'. Use: {string.Join(", ", windows.Keys)}");
    return 1;
}

var label = args.SkipWhile(a => a != "--label").Skip(1).FirstOrDefault() ?? "baseline";

var credentials = new TradingCredentialStore();
var alpaca = new AlpacaClient(credentials);
if (!alpaca.IsConfigured)
{
    Console.WriteLine("No Alpaca keys saved; historical bars need them. Add them in Settings first.");
    return 1;
}

var provider = new OpenAiCompatibleProvider(credentials);
if (!provider.IsConfigured)
{
    Console.WriteLine("No OpenAI-compatible endpoint saved. Add one in Settings first.");
    return 1;
}

// The live configuration is the thing being graded, so it is read rather than re-declared. Only the
// cadence differs: a replay decides once per session.
var live = LoadLiveConfig();
Console.WriteLine($"window   {which}: {window.From} to {window.To}");
Console.WriteLine($"label    {label}");
Console.WriteLine($"model    {live.Provider}/{live.Model}");
Console.WriteLine($"universe {live.Watchlist}   caps {live.MaxPositionPercent:0.#}% / {live.MaxInvestedPercent:0.#}%   {live.MaxTradesPerDay}/day");
Console.WriteLine();

var symbols = TradingGuardrailsSymbols(live.Watchlist).Append(PortfolioSnapshot.BenchmarkSymbol).ToList();

// Reaches back far enough for the longest lookback any strategy uses, not just far enough to fill
// against. A 30-day run-up left the 252-session trend rule permanently short of history, so it never
// traded and reported +0.00% — which reads like a finding about trend following and was a finding about
// my fetch window. Two calendar years covers a 252-session lookback with room for holidays.
Console.WriteLine("fetching bars…");
var historyStart = window.From.AddDays(-2 * 365);
var raw = await alpaca.GetDailyBarsAsync(symbols, historyStart, window.To.AddDays(7));
foreach (var (symbol, bars) in raw.OrderBy(kv => kv.Key))
{
    Console.WriteLine($"  {symbol,-6} {bars.Count,4} sessions  {bars[0].Date} .. {bars[^1].Date}");
}

var market = new ReplayMarket(
    raw.Select(kv => new KeyValuePair<string, IReadOnlyList<DailyBar>>(
        kv.Key,
        kv.Value.Select(b => new DailyBar(b.Date, b.Open, b.Close)).ToList())),
    PortfolioSnapshot.BenchmarkSymbol);

var sessions = market.DaysBetween(window.From, window.To).Count;
Console.WriteLine();
Console.WriteLine($"{sessions} sessions to replay, one decision each. This will take a while.");
Console.WriteLine();

// The mechanical strategies cost nothing to run — no model call — so they always run, and the agent's
// number is never reported without arithmetic beside it. "It lost to SPY" is a weak finding; "it lost to
// SPY and to twenty lines of rule" is an answer.
Console.WriteLine("--- mechanical baselines ---");
var baselineRunner = new BaselineRunner(market);
IBaselineStrategy[] baselines =
[
    new BuyAndHoldIndexBaseline(),
    new EqualWeightMonthlyBaseline(),
    new TrendFollowingBaseline(),
    new VolatilityTargetedBaseline(),
];

foreach (var baseline in baselines)
{
    // A fresh runner per strategy so a refusal is attributed to the strategy that caused it.
    var runner0 = new BaselineRunner(market);
    var run = runner0.Run(baseline, live, window.From, window.To);
    Console.WriteLine(
        $"  {baseline.Name,-34} {run.Performance.StrategyReturnPercent,7:+0.00;-0.00}%  " +
        $"alpha {run.Performance.AlphaPercent,7:+0.00;-0.00}  " +
        $"dd −{run.Performance.MaxDrawdownPercent,5:0.00}%  " +
        $"{run.OrdersFilled,3} fills");

    // Zero fills is ambiguous on its own — a rule that decided to stay out looks exactly like one
    // that was blocked. Say which.
    if (run.OrdersFilled == 0)
    {
        Console.WriteLine(runner0.FirstRefusal is { } refusal
            ? $"      never invested — first refusal: {refusal}"
            : "      never invested — placed no orders at all (by its own logic, not refused)");
    }
    else if (runner0.FirstRefusal is { } someRefusal)
    {
        Console.WriteLine($"      had an order refused: {someRefusal}");
    }
}

Console.WriteLine();

// The rules are free to evaluate; the agent takes half an hour. Being able to iterate on the baselines
// alone keeps that asymmetry useful.
if (args.Contains("--baselines-only"))
{
    return 0;
}

var cadence = args.SkipWhile(a => a != "--cadence").Skip(1).FirstOrDefault() switch
{
    "weekly" => DecisionCadence.Weekly,
    "monthly" => DecisionCadence.Monthly,
    _ => DecisionCadence.Daily,
};

Console.WriteLine($"agent cadence: {cadence}");
Console.WriteLine();

var dbPath = Path.Combine(Path.GetTempPath(), $"aaronos-backtest-{which}-{label}.db");
var runner = new BacktestRunner(market, provider);
var started = DateTime.UtcNow;

var result = await runner.RunAsync(
    label, live, window.From, window.To, dbPath, log: Console.WriteLine, cadence: cadence);

Console.WriteLine();
Console.WriteLine($"=== {which} / {label} ===");
Console.WriteLine($"  elapsed          {(DateTime.UtcNow - started).TotalMinutes:F1} min");
Console.WriteLine($"  sessions         {result.Sessions}");
Console.WriteLine($"  decisions made   {result.DecisionsMade}");
Console.WriteLine($"  orders filled    {result.OrdersFilled}");
Console.WriteLine($"  cycles refusing  {result.OrdersRefused}");
Console.WriteLine($"  strategy         {result.Performance.StrategyReturnPercent:+0.00;-0.00}%");
Console.WriteLine($"  SPY              {result.Performance.BenchmarkReturnPercent?.ToString("+0.00;-0.00") ?? "—"}%");
Console.WriteLine($"  alpha            {result.Performance.AlphaPercent?.ToString("+0.00;-0.00") ?? "—"} points");
Console.WriteLine($"  worst drawdown  −{result.Performance.MaxDrawdownPercent:0.00}%");
Console.WriteLine($"  closed trades    {result.Performance.ClosedTradeCount}");
Console.WriteLine($"  verdict          {result.Performance.Verdict}");
Console.WriteLine();
Console.WriteLine($"  database         {dbPath}");
return 0;

static IEnumerable<string> TradingGuardrailsSymbols(string watchlist) =>
    AaronOS.Modules.Trading.Trading.TradingGuardrails.ParseWatchlist(watchlist);

static TradingConfig LoadLiveConfig()
{
    var dbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AaronOS", "aaronos.db");

    using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
        "SELECT Watchlist, MaxPositionPercent, MaxInvestedPercent, MaxTradesPerDay, Model, Provider, " +
        "StrategyNotes, MinTradesForStats FROM TradingConfig LIMIT 1";
    using var reader = command.ExecuteReader();

    if (!reader.Read())
    {
        return new TradingConfig();
    }

    return new TradingConfig
    {
        IsEnabled = true,
        Watchlist = reader.GetString(0),
        MaxPositionPercent = decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
        MaxInvestedPercent = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
        MaxTradesPerDay = reader.GetInt32(3),
        Model = reader.GetString(4),
        Provider = reader.GetString(5),
        StrategyNotes = reader.IsDBNull(6) ? "" : reader.GetString(6),
        MinTradesForStats = reader.GetInt32(7),
        CycleIntervalMinutes = 1440,
    };
}

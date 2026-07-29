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

// Reached out to before spending half an hour discovering it one cycle at a time. A configured endpoint
// is not a reachable one, and the difference cost a full run.
var endpoint = credentials.Load()!.OpenAiBaseUrl;
try
{
    using var probe = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    using var reply = await probe.GetAsync(endpoint.TrimEnd('/') + "/models");
    Console.WriteLine($"endpoint {endpoint} reachable ({(int)reply.StatusCode})");
}
catch (Exception ex)
{
    Console.WriteLine($"endpoint {endpoint} is NOT reachable: {ex.Message}");
    Console.WriteLine("Start the model server before running a backtest.");
    return 1;
}

// The live configuration is the thing being graded, so it is read rather than re-declared. Only the
// cadence differs: a replay decides once per session.
var live = LoadLiveConfig();

// A named variant overrides the live settings for this run only. Testing a variant by editing the live
// row would change what the live paper run is doing as a side effect of a backtest, and the live run is
// the only uncontaminated evidence there is.
var variant = args.SkipWhile(a => a != "--variant").Skip(1).FirstOrDefault();
if (variant is not null)
{
    switch (variant)
    {
        case "stockpicking":
            // No index anywhere on the watchlist, so holding one is not an option and the agent has to
            // express a view or hold cash. Ten names because the 10% per-company cap means ten is the
            // fewest that still allows being fully invested — with fewer, "underperformed" would just be
            // a cash ceiling wearing a strategy's clothes, which is the mistake the 80% cap already was.
            live.Watchlist = "AAPL,MSFT,NVDA,AMZN,GOOGL,META,AVGO,TSLA,JPM,V";
            live.BroadIndexSymbols = "";
            live.StrategyNotes =
                "There is no index available to you. Hold individual names you have a reason to hold, " +
                "and remember that cash loses to a rising market. State each reason in one sentence.";
            break;

        case "news":
            live.IncludeNews = true;
            break;

        case "news-off":
            live.IncludeNews = false;
            break;

        default:
            Console.WriteLine($"Unknown variant '{variant}'.");
            return 1;
    }

    Console.WriteLine($"variant  {variant}");
}
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

// Fetched once for the whole window and filtered per session by publication time. Fetching per session
// would be thousands of calls; filtering in memory is the same information and the filter is the part
// that has to be right.
INewsSource newsSource = new NoNewsSource();
if (live.IncludeNews)
{
    Console.WriteLine("fetching headlines…");
    var headlines = await FetchHeadlinesAsync(
        credentials, TradingGuardrailsSymbols(live.Watchlist), window.From, window.To);
    var replayNews = new ReplayNewsSource(headlines);
    newsSource = replayNews;
    Console.WriteLine($"  {replayNews.TotalAvailable} headlines across the window");
    Console.WriteLine();
}

var dbPath = Path.Combine(Path.GetTempPath(), $"aaronos-backtest-{which}-{label}.db");
var runner = new BacktestRunner(market, provider, newsSource);
var started = DateTime.UtcNow;

var result = await runner.RunAsync(
    label, live, window.From, window.To, dbPath, log: Console.WriteLine, cadence: cadence);

Console.WriteLine();

// Refuses to present a return figure for a run that mostly failed. A previous run had every one of its
// 126 cycles error because the model server was down, and it reported "+0.00%, alpha −11.27" as though
// that were a strategy result. A wrong number that looks right is worse than a crash.
if (result.IsUntrustworthy)
{
    Console.WriteLine($"### RUN FAILED — {result.CyclesErrored} of {result.DecisionsAttempted} cycles errored ###");
    Console.WriteLine("  No performance figure is reported, because it would describe the harness rather");
    Console.WriteLine("  than the strategy. Check the decision log in the database for the error text.");
    Console.WriteLine($"  database  {dbPath}");
    return 2;
}

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

/// <summary>
/// Every headline for the window, paged to the end. Each carries its publication instant, which is what
/// <see cref="ReplayNewsSource"/> filters on — the correctness of the whole news experiment rests on
/// never showing the agent an article written after the session it is deciding.
/// </summary>
static async Task<List<NewsHeadline>> FetchHeadlinesAsync(
    TradingCredentialStore store, IEnumerable<string> symbols, DateOnly from, DateOnly to)
{
    var credentials = store.Load()!;
    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", credentials.AlpacaKeyId);
    http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", credentials.AlpacaSecret);

    var list = string.Join(',', symbols);
    var results = new List<NewsHeadline>();
    string? page = null;

    do
    {
        var url = $"https://data.alpaca.markets/v1beta1/news?symbols={list}" +
                  $"&start={from.AddDays(-NewsWindow.LookbackDays):yyyy-MM-dd}T00:00:00Z" +
                  $"&end={to:yyyy-MM-dd}T23:59:59Z&limit=50&sort=asc" +
                  (page is null ? "" : $"&page_token={Uri.EscapeDataString(page)}");

        using var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"  news fetch stopped: {(int)response.StatusCode}");
            break;
        }

        var json = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in json?["news"]?.AsArray() ?? [])
        {
            var created = (DateTime?)item?["created_at"];
            var headline = (string?)item?["headline"];
            if (created is null || string.IsNullOrWhiteSpace(headline))
            {
                continue;
            }

            var tickers = string.Join(',',
                (item!["symbols"]?.AsArray() ?? []).Select(s => (string?)s ?? "").Where(s => s.Length > 0));

            results.Add(new NewsHeadline(created.Value.ToUniversalTime(), tickers, headline));
        }

        page = (string?)json?["next_page_token"];
    }
    while (!string.IsNullOrEmpty(page));

    return results;
}

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

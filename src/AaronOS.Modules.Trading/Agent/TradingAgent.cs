using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using AaronOS.Core.Data;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Trading.Agent;

/// <summary>What one cycle did, for the UI to report without re-reading the database.</summary>
public readonly record struct CycleResult(bool Ran, string Summary, string? Error)
{
    public static CycleResult Skipped(string why) => new(false, why, null);
    public static CycleResult Failed(string error) => new(false, "Cycle failed", error);
}

/// <summary>
/// One decision cycle: gather the account's real state, ask the model what to do, and put whatever
/// it asks for through the guardrails before any of it reaches the broker.
///
/// The order of those steps is the design. The model never touches the broker directly; it emits
/// intent, and <see cref="TradingGuardrails"/> decides whether that intent becomes an order. A
/// refusal is fed back as a tool result so the model can react to it within the same cycle, and is
/// also written to the decision log so a blocked order is visible afterwards rather than silent.
/// </summary>
public class TradingAgent(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    AlpacaClient alpaca,
    AnthropicClient anthropic)
{
    /// <summary>Enough turns for the model to place a few orders and react to a refusal.</summary>
    private const int MaxToolTurns = 6;

    public async Task<CycleResult> RunCycleAsync(CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var config = await db.Set<TradingConfig>().FirstOrDefaultAsync(token) ?? new TradingConfig();

        if (!config.IsEnabled)
        {
            return CycleResult.Skipped("Trading is switched off.");
        }

        if (!alpaca.IsConfigured || !anthropic.IsConfigured)
        {
            return CycleResult.Skipped("Add your Alpaca and Anthropic keys in Settings first.");
        }

        var decision = new AgentDecision { RanAtUtc = DateTime.UtcNow, Model = config.Model };

        try
        {
            // Checked before the model is called rather than after: a cycle outside market hours can
            // place nothing, so paying for the reasoning would be waste.
            if (!await alpaca.IsMarketOpenAsync(token))
            {
                return CycleResult.Skipped("The market is closed.");
            }

            var account = await alpaca.GetAccountAsync(token);
            var positions = await alpaca.GetPositionsAsync(token);
            var watchlist = TradingGuardrails.ParseWatchlist(config.Watchlist);
            var quotes = await alpaca.GetQuotesAsync(watchlist, token);

            var since = DateTime.UtcNow.Date;
            var ordersToday = await db.Set<TradeOrder>().CountAsync(o => o.SubmittedAtUtc >= since, token);

            var state = new AccountState(account.Equity, account.Cash, true, positions, ordersToday);

            var result = await ConverseAsync(db, config, state, quotes, decision, token);

            decision.ActionSummary = result;
            db.Add(decision);
            await EnsureStartedOnAsync(db, config, token);
            await db.SaveChangesAsync(token);

            return new CycleResult(true, result, null);
        }
        catch (Exception ex)
        {
            decision.Error = ex.Message;
            decision.ActionSummary = "Cycle failed";
            db.Add(decision);
            await db.SaveChangesAsync(CancellationToken.None);
            return CycleResult.Failed(ex.Message);
        }
    }

    /// <summary>The tool-use loop. Returns a one-line summary of what actually happened.</summary>
    private async Task<string> ConverseAsync(
        AaronOsDbContext db,
        TradingConfig config,
        AccountState state,
        Dictionary<string, SymbolQuote> quotes,
        AgentDecision decision,
        CancellationToken token)
    {
        var messages = new JsonArray { UserMessage(BuildBrief(config, state, quotes)) };
        var reasoning = new StringBuilder();
        var actions = new List<string>();
        var blocked = new List<string>();
        var placed = 0;

        for (var turn = 0; turn < MaxToolTurns; turn++)
        {
            var response = await anthropic.SendAsync(
                config.Model, SystemPrompt, messages, Tools, token: token);

            decision.InputTokens += (int?)response["usage"]?["input_tokens"] ?? 0;
            decision.OutputTokens += (int?)response["usage"]?["output_tokens"] ?? 0;

            var content = response["content"]?.AsArray() ?? [];
            foreach (var block in content)
            {
                if ((string?)block?["type"] == "text" && (string?)block["text"] is { Length: > 0 } text)
                {
                    reasoning.AppendLine(text.Trim());
                }
            }

            var toolUses = content
                .Where(b => (string?)b?["type"] == "tool_use")
                .Select(b => b!)
                .ToList();

            if (toolUses.Count == 0)
            {
                break;
            }

            // The assistant turn has to be echoed back verbatim before its tool results, or the
            // conversation is malformed on the next request.
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() });

            var results = new JsonArray();
            foreach (var use in toolUses)
            {
                var (outcome, wasPlaced, wasBlocked) =
                    await ExecuteToolAsync(db, config, state, quotes, decision, use, token);

                if (wasPlaced is { } action)
                {
                    actions.Add(action);
                    placed++;
                    state = state with { OrdersPlacedToday = state.OrdersPlacedToday + 1 };
                }

                if (wasBlocked is { } reason)
                {
                    blocked.Add(reason);
                }

                results.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = (string?)use["id"],
                    ["content"] = outcome,
                });
            }

            messages.Add(new JsonObject { ["role"] = "user", ["content"] = results });
        }

        decision.Reasoning = reasoning.ToString().Trim();
        decision.BlockedActions = blocked.Count == 0 ? null : string.Join("; ", blocked);

        return placed == 0
            ? blocked.Count > 0 ? $"No action — {blocked.Count} order(s) refused" : "No action"
            : string.Join("; ", actions);
    }

    private async Task<(string Outcome, string? Placed, string? Blocked)> ExecuteToolAsync(
        AaronOsDbContext db,
        TradingConfig config,
        AccountState state,
        Dictionary<string, SymbolQuote> quotes,
        AgentDecision decision,
        JsonNode use,
        CancellationToken token)
    {
        var name = (string?)use["name"] ?? "";
        var input = use["input"];
        var symbol = ((string?)input?["symbol"] ?? "").Trim().ToUpperInvariant();
        var rationale = (string?)input?["rationale"];

        if (symbol.Length == 0)
        {
            return ("Refused: no symbol given.", null, "missing symbol");
        }

        OrderSide side;
        int quantity;

        switch (name)
        {
            case "place_order":
                side = string.Equals((string?)input?["side"], "sell", StringComparison.OrdinalIgnoreCase)
                    ? OrderSide.Sell
                    : OrderSide.Buy;
                quantity = (int?)input?["quantity"] ?? 0;
                break;

            case "close_position":
                side = OrderSide.Sell;
                quantity = state.Positions
                    .FirstOrDefault(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    .Quantity;
                if (quantity <= 0)
                {
                    return ($"Refused: no {symbol} held.", null, $"{symbol}: nothing held to close");
                }

                break;

            default:
                return ($"Refused: unknown tool {name}.", null, $"unknown tool {name}");
        }

        if (!quotes.TryGetValue(symbol, out var quote) || quote.Mid <= 0)
        {
            return ($"Refused: no current price for {symbol}.", null, $"{symbol}: no price");
        }

        var request = new OrderRequest(symbol, side, quantity, quote.Mid);
        var verdict = TradingGuardrails.Check(request, state, config);
        if (!verdict.Allowed)
        {
            return ($"Refused: {verdict.Reason}", null, $"{symbol}: {verdict.Reason}");
        }

        var submitted = await alpaca.PlaceMarketOrderAsync(symbol, side, quantity, token);

        db.Add(new TradeOrder
        {
            BrokerOrderId = submitted.BrokerOrderId,
            Symbol = symbol,
            Side = side,
            Quantity = quantity,
            SubmittedAtUtc = DateTime.UtcNow,
            EstimatedPrice = quote.Mid,
            Status = submitted.Status,
            AgentDecisionId = decision.Id == 0 ? null : decision.Id,
            Rationale = rationale,
        });

        var verb = side == OrderSide.Buy ? "Bought" : "Sold";
        var summary = $"{verb} {quantity} {symbol}";
        return ($"Order accepted: {summary} at about {quote.Mid:C2}.", summary, null);
    }

    /// <summary>Stamps the start date on the first cycle that runs, and never touches it again.</summary>
    private static async Task EnsureStartedOnAsync(
        AaronOsDbContext db, TradingConfig config, CancellationToken token)
    {
        if (config.StartedOn is not null || config.Id == 0)
        {
            return;
        }

        var stored = await db.Set<TradingConfig>().FirstOrDefaultAsync(c => c.Id == config.Id, token);
        if (stored is not null)
        {
            stored.StartedOn = DateOnly.FromDateTime(DateTime.Now);
        }
    }

    private static JsonObject UserMessage(string text) =>
        new() { ["role"] = "user", ["content"] = text };

    private static string BuildBrief(
        TradingConfig config, AccountState state, Dictionary<string, SymbolQuote> quotes)
    {
        var brief = new StringBuilder();
        brief.AppendLine(CultureInfo.InvariantCulture, $"Account equity: {state.Equity:C2}");
        brief.AppendLine(CultureInfo.InvariantCulture, $"Cash available: {state.Cash:C2}");
        brief.AppendLine(CultureInfo.InvariantCulture,
            $"Orders already placed today: {state.OrdersPlacedToday} of {config.MaxTradesPerDay}");
        brief.AppendLine();

        brief.AppendLine("Positions held:");
        if (state.Positions.Count == 0)
        {
            brief.AppendLine("  (none)");
        }
        else
        {
            foreach (var position in state.Positions)
            {
                brief.AppendLine(CultureInfo.InvariantCulture,
                    $"  {position.Symbol}: {position.Quantity} shares, {position.MarketValue:C2}");
            }
        }

        brief.AppendLine();
        brief.AppendLine("Current quotes:");
        foreach (var symbol in TradingGuardrails.ParseWatchlist(config.Watchlist))
        {
            brief.AppendLine(quotes.TryGetValue(symbol, out var quote)
                ? string.Create(CultureInfo.InvariantCulture,
                    $"  {symbol}: bid {quote.Bid:C2} / ask {quote.Ask:C2}")
                : $"  {symbol}: no quote available");
        }

        brief.AppendLine();
        brief.AppendLine(CultureInfo.InvariantCulture,
            $"Limits enforced by the application: no more than {config.MaxPositionPercent:0.#}% of equity in " +
            $"any one position, no more than {config.MaxInvestedPercent:0.#}% invested in total, " +
            $"{config.MaxTradesPerDay} orders a day, no borrowing and no short selling.");
        brief.AppendLine();
        brief.AppendLine("Strategy notes from the account owner:");
        brief.AppendLine(config.StrategyNotes);
        brief.AppendLine();
        brief.AppendLine("Decide what to do this cycle.");

        return brief.ToString();
    }

    /// <summary>
    /// The brief. It states plainly that doing nothing is the expected outcome most of the time,
    /// because an agent invoked every half hour and asked what to trade will find something to trade,
    /// and churn is the most reliable way to lose to the index.
    /// </summary>
    private const string SystemPrompt = """
        You manage a paper-trading account. No real money is at stake; the purpose is to find out,
        honestly, whether this approach beats simply holding the index.

        Judge yourself against buy-and-hold SPY over the same period, not against zero. Making money
        in a rising market is not a result.

        Doing nothing is usually correct. You are asked for a decision every cycle, which is far more
        often than good opportunities appear. Trade only on a specific reason you can state in one
        sentence; if you cannot, hold and say so. Frequent trading reliably underperforms.

        The application enforces position limits, an exposure cap, a daily order limit, and bans on
        borrowing and short selling. Orders that breach them are refused before reaching the broker
        and the refusal is returned to you. Work within the limits rather than probing them.

        Give a brief, plain explanation of your reasoning each cycle, including when you hold. Do not
        express more confidence than the information supports.
        """;

    private static JsonArray Tools =>
    [
        new JsonObject
        {
            ["name"] = "place_order",
            ["description"] =
                "Place a market order for a whole number of shares. Buying is limited by the "
                + "position and exposure caps; selling may only reduce a position you already hold.",
            ["input_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["symbol"] = new JsonObject { ["type"] = "string", ["description"] = "Ticker, e.g. MSFT." },
                    ["side"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray { "buy", "sell" },
                    },
                    ["quantity"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
                    ["rationale"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "One sentence on why, stored with the order.",
                    },
                },
                ["required"] = new JsonArray { "symbol", "side", "quantity", "rationale" },
            },
        },
        new JsonObject
        {
            ["name"] = "close_position",
            ["description"] = "Sell the entire holding in one symbol.",
            ["input_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["symbol"] = new JsonObject { ["type"] = "string" },
                    ["rationale"] = new JsonObject { ["type"] = "string" },
                },
                ["required"] = new JsonArray { "symbol", "rationale" },
            },
        },
    ];
}

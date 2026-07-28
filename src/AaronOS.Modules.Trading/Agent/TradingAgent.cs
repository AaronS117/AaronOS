using System.Globalization;
using System.Text;
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
    AgentProviderRegistry providers,
    TimeProvider time,
    INewsSource news)
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

        if (!alpaca.IsConfigured)
        {
            return await RecordBlockedAsync(db, "Add your Alpaca paper keys in Settings first.", time, token);
        }

        var provider = providers.Resolve(config.Provider);
        if (!provider.IsConfigured)
        {
            return await RecordBlockedAsync(
                db, $"The {provider.Name} model provider is not configured yet.", time, token);
        }

        // Stamped with the provider as well as the model, so a run in the log can be traced to what
        // actually produced it after the setting has been changed.
        var decision = new AgentDecision
        {
            RanAtUtc = time.GetUtcNow().UtcDateTime,
            Model = $"{provider.Name}/{config.Model}",
        };

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

            var since = time.GetUtcNow().UtcDateTime.Date;
            var ordersToday = await db.Set<TradeOrder>().CountAsync(o => o.SubmittedAtUtc >= since, token);

            var state = new AccountState(account.Equity, account.Cash, true, positions, ordersToday);

            var headlines = config.IncludeNews
                ? await news.AsOfAsync(watchlist, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), token)
                : [];

            var result = await ConverseAsync(db, provider, config, state, quotes, headlines, decision, token);

            decision.ActionSummary = result;
            db.Add(decision);
            await EnsureStartedOnAsync(db, config, time, token);
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

    /// <summary>
    /// Writes a visible note that the loop is alive but cannot proceed, and returns the skip.
    ///
    /// Without this, a cycle blocked on a missing key leaves no trace at all, so a run that is ticking
    /// away doing nothing looks exactly like a run that never started. That is the worst failure
    /// available to an experiment measured over months, and it is invisible by construction.
    ///
    /// Consecutive identical blocks are written once rather than every cycle. A thirty-minute schedule
    /// would otherwise add roughly fifty rows a day saying the same thing, and a log that has to be
    /// waded through is not a log anyone reads. Market-hours skips are not recorded at all: they are
    /// the normal state for most of the day and carry no information.
    /// </summary>
    private static async Task<CycleResult> RecordBlockedAsync(
        AaronOsDbContext db, string reason, TimeProvider time, CancellationToken token)
    {
        var latest = await db.Set<AgentDecision>()
            .OrderByDescending(d => d.RanAtUtc)
            .FirstOrDefaultAsync(token);

        if (latest?.ActionSummary != "Blocked" || latest.Error != reason)
        {
            db.Add(new AgentDecision
            {
                RanAtUtc = time.GetUtcNow().UtcDateTime,
                Model = "—",
                ActionSummary = "Blocked",
                Error = reason,
            });
            await db.SaveChangesAsync(token);
        }

        return CycleResult.Skipped(reason);
    }

    /// <summary>
    /// The tool-use loop, written against <see cref="IAgentConversation"/> so the same logic drives a
    /// hosted model or a local one. Returns a one-line summary of what actually happened.
    /// </summary>
    private async Task<string> ConverseAsync(
        AaronOsDbContext db,
        IAgentProvider provider,
        TradingConfig config,
        AccountState state,
        Dictionary<string, SymbolQuote> quotes,
        IReadOnlyList<NewsHeadline> headlines,
        AgentDecision decision,
        CancellationToken token)
    {
        var conversation = provider.Start(
            config.Model, SystemPrompt, BuildBrief(config, state, quotes, headlines), AgentTools.All);

        var reasoning = new StringBuilder();
        var actions = new List<string>();
        var blocked = new List<string>();
        var placed = 0;

        for (var turn = 0; turn < MaxToolTurns; turn++)
        {
            var reply = await conversation.NextAsync(token);

            decision.InputTokens += reply.InputTokens;
            decision.OutputTokens += reply.OutputTokens;

            if (reply.Text.Length > 0)
            {
                reasoning.AppendLine(reply.Text);
            }

            if (reply.ToolCalls.Count == 0)
            {
                break;
            }

            var results = new List<(string ToolCallId, string Content)>();
            foreach (var call in reply.ToolCalls)
            {
                var (outcome, wasPlaced, wasBlocked) =
                    await ExecuteToolAsync(db, config, state, quotes, decision, call, token);

                if (wasPlaced is { } action)
                {
                    actions.Add(action);
                    placed++;

                    // The running count has to advance inside the loop, or several calls in one turn
                    // would each be measured against the same stale total and slip past the daily cap.
                    state = state with { OrdersPlacedToday = state.OrdersPlacedToday + 1 };
                }

                if (wasBlocked is { } reason)
                {
                    blocked.Add(reason);
                }

                results.Add((call.Id, outcome));
            }

            conversation.AddToolResults(results);
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
        AgentToolCall call,
        CancellationToken token)
    {
        // Null arguments mean the model emitted something that would not parse, which smaller local
        // models do regularly. It is refused and explained rather than throwing, so one malformed
        // call costs a tool result instead of the whole cycle.
        if (call.Arguments is null)
        {
            return ($"Refused: arguments for {call.Name} were not valid JSON.", null,
                $"{call.Name}: unparseable arguments");
        }

        var name = call.Name;
        var input = call.Arguments;
        var symbol = (ToolArguments.StringOf(input, "symbol") ?? "").Trim().ToUpperInvariant();
        var rationale = ToolArguments.StringOf(input, "rationale");

        if (symbol.Length == 0)
        {
            return ("Refused: no symbol given.", null, "missing symbol");
        }

        // Checked here, ahead of the price lookup, so the refusal states the real reason. Quotes are
        // only fetched for watchlist symbols, so an off-watchlist order would otherwise be refused
        // for "no price" — still safely refused, but a misleading answer to hand back to a model that
        // may then retry, and a misleading line in the log afterwards.
        if (!TradingGuardrails.IsOnWatchlist(symbol, config.Watchlist))
        {
            return ($"Refused: {symbol} is not on the watchlist.", null,
                $"{symbol}: not on the watchlist");
        }

        OrderSide side;
        int quantity;

        switch (name)
        {
            case "place_order":
                side = string.Equals(ToolArguments.StringOf(input, "side"), "sell", StringComparison.OrdinalIgnoreCase)
                    ? OrderSide.Sell
                    : OrderSide.Buy;
                quantity = ToolArguments.IntOf(input, "quantity") ?? 0;
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
            SubmittedAtUtc = time.GetUtcNow().UtcDateTime,
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
        AaronOsDbContext db, TradingConfig config, TimeProvider time, CancellationToken token)
    {
        if (config.StartedOn is not null || config.Id == 0)
        {
            return;
        }

        var stored = await db.Set<TradingConfig>().FirstOrDefaultAsync(c => c.Id == config.Id, token);
        if (stored is not null)
        {
            stored.StartedOn = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        }
    }

    private static string BuildBrief(
        TradingConfig config,
        AccountState state,
        Dictionary<string, SymbolQuote> quotes,
        IReadOnlyList<NewsHeadline> headlines)
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

        if (headlines.Count > 0)
        {
            brief.AppendLine();
            brief.AppendLine(CultureInfo.InvariantCulture,
                $"Headlines from the last {NewsWindow.LookbackDays} days, oldest first:");
            foreach (var headline in headlines)
            {
                brief.AppendLine(CultureInfo.InvariantCulture,
                    $"  [{headline.CreatedUtc:MMM d}] {headline.Symbols}: {headline.Headline}");
            }

            brief.AppendLine();
            brief.AppendLine("A headline is not a reason on its own. Most news is already in the price by " +
                             "the time you read it, so act only where you can say what the market appears " +
                             "to have missed.");
        }

        brief.AppendLine();

        // Generated from the same configuration the guardrails read, and it must stay that way. An
        // earlier version recited a flat per-position cap after the code had been changed to exempt
        // broad index funds from it, and the model spent six months holding the index at a tenth of the
        // account and selling down to "comply" with a limit that no longer existed. Changing what is
        // enforced without changing what the model is told is worth ten points of underperformance.
        var indexSymbols = TradingGuardrails.ParseWatchlist(config.BroadIndexSymbols)
            .Where(s => TradingGuardrails.IsOnWatchlist(s, config.Watchlist))
            .ToList();

        brief.AppendLine(CultureInfo.InvariantCulture,
            $"Limits enforced by the application: no more than {config.MaxPositionPercent:0.#}% of equity in " +
            $"any one individual company, no more than {config.MaxInvestedPercent:0.#}% invested in total, " +
            $"{config.MaxTradesPerDay} orders a day, no borrowing and no short selling.");

        if (indexSymbols.Count > 0)
        {
            brief.AppendLine(CultureInfo.InvariantCulture,
                $"Exception: {string.Join(" and ", indexSymbols)} " +
                $"{(indexSymbols.Count == 1 ? "is a broad index fund and is" : "are broad index funds and are")} " +
                $"exempt from the per-company cap. {(indexSymbols.Count == 1 ? "It" : "They")} may be held up " +
                $"to the {config.MaxInvestedPercent:0.#}% total limit, so the index is the way to be " +
                $"substantially invested without concentrating in one company.");
        }
        brief.AppendLine();
        brief.AppendLine("Strategy notes from the account owner:");
        brief.AppendLine(config.StrategyNotes);
        brief.AppendLine();
        brief.AppendLine("Decide what to do this cycle.");

        return brief.ToString();
    }

    /// <summary>
    /// The brief.
    ///
    /// The paragraph about cash is here because its absence broke the first version completely. That
    /// brief said to judge every trade against SPY and to hold whenever no reason was evident, and the
    /// watchlist deliberately excluded SPY. A 126-session replay held cash on all 126 days and lost
    /// 11.3 points to an index that rose 11.3%, reasoning each time that "cash preservation aligns
    /// with strategy". The instruction was unsatisfiable rather than the model unwilling: the only
    /// action that could meet the stated bar had been forbidden, so inaction was the correct response
    /// to it.
    ///
    /// Two lessons are encoded below. Cash is a position, not the absence of one, and a model will not
    /// infer that. And guarding against churn is not the same as guarding against investing — an
    /// anti-churn instruction with no floor produces paralysis, which in a rising market is the worse
    /// of the two failures because it has no variance at all.
    /// </summary>
    private const string SystemPrompt = """
        You manage a paper-trading account. No real money is at stake; the purpose is to find out,
        honestly, whether this approach beats simply holding the index.

        You are measured against buy-and-hold SPY over the same period. Two things follow, and the
        second is the one that gets missed. Making money in a rising market is not a result. And
        holding cash is not neutral — it is a bet against the index, and in a rising market it loses by
        the full amount the index gained. An account sitting in cash has taken a large position, not no
        position.

        So if you have no view on individual names, the neutral action is to hold the index itself, not
        to hold cash. Buy individual names only on a specific reason you can state in one sentence.
        Churning between names reliably underperforms, and so does sitting in cash for months; avoid
        both rather than trading one for the other.

        The application enforces position limits, an exposure cap, a daily order limit, and bans on
        borrowing and short selling. Orders that breach them are refused before reaching the broker
        and the refusal is returned to you. Work within the limits rather than probing them.

        Give a brief, plain explanation of your reasoning each cycle, including when you hold. Do not
        express more confidence than the information supports.
        """;
}

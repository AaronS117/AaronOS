namespace AaronOS.Modules.Trading.Backtest;

/// <summary>One day of one symbol. Open and close are all a daily replay needs.</summary>
public readonly record struct DailyBar(DateOnly Date, decimal Open, decimal Close);

/// <summary>
/// Historical bars for a replay, indexed for lookup by symbol and date.
///
/// Trading days come from the benchmark's own bars rather than from a calendar, so holidays and early
/// closes are handled by simply not existing in the data — which is more reliable than any holiday
/// table and is guaranteed consistent with the prices being replayed.
/// </summary>
public sealed class ReplayMarket
{
    private readonly Dictionary<string, Dictionary<DateOnly, DailyBar>> _bars;

    public ReplayMarket(IEnumerable<KeyValuePair<string, IReadOnlyList<DailyBar>>> bars, string benchmarkSymbol)
    {
        _bars = bars.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ToDictionary(b => b.Date),
            StringComparer.OrdinalIgnoreCase);

        BenchmarkSymbol = benchmarkSymbol;

        if (!_bars.TryGetValue(benchmarkSymbol, out var benchmark) || benchmark.Count == 0)
        {
            throw new ArgumentException(
                $"No bars for the benchmark {benchmarkSymbol}, so no comparison would be possible.", nameof(bars));
        }

        var ordered = benchmark.Keys.OrderBy(d => d).ToList();
        TradingDays = ordered;

        // Position lookups happen once or twice per replayed session, so they get a map rather than a
        // scan. It also gives IReadOnlyList the index operation it does not have.
        _sessionIndex = ordered
            .Select((date, index) => (date, index))
            .ToDictionary(x => x.date, x => x.index);
    }

    private readonly Dictionary<DateOnly, int> _sessionIndex;

    public string BenchmarkSymbol { get; }

    public IReadOnlyList<DateOnly> TradingDays { get; }

    public IEnumerable<string> Symbols => _bars.Keys;

    public DailyBar? BarOn(string symbol, DateOnly date) =>
        _bars.TryGetValue(symbol, out var series) && series.TryGetValue(date, out var bar) ? bar : null;

    /// <summary>
    /// The next day on which this symbol traded. Fills happen here rather than on the decision day,
    /// so a symbol that did not trade the following day simply has its fill deferred.
    /// </summary>
    public DailyBar? NextBarAfter(string symbol, DateOnly date)
    {
        if (!_bars.TryGetValue(symbol, out var series))
        {
            return null;
        }

        DailyBar? best = null;
        foreach (var bar in series.Values)
        {
            if (bar.Date > date && (best is null || bar.Date < best.Value.Date))
            {
                best = bar;
            }
        }

        return best;
    }

    /// <summary>Trading days within a window, inclusive.</summary>
    public IReadOnlyList<DateOnly> DaysBetween(DateOnly from, DateOnly to) =>
        TradingDays.Where(d => d >= from && d <= to).ToList();

    /// <summary>
    /// Closing prices up to and including a date, oldest first, at most <paramref name="count"/> of
    /// them. Returns fewer when the history is short rather than padding, so a rule can decline to act
    /// until it has enough data instead of acting on invented numbers.
    /// </summary>
    public IReadOnlyList<decimal> ClosesUpTo(string symbol, DateOnly date, int count)
    {
        if (!_bars.TryGetValue(symbol, out var series) || count <= 0)
        {
            return [];
        }

        return series.Values
            .Where(b => b.Date <= date)
            .OrderByDescending(b => b.Date)
            .Take(count)
            .Select(b => b.Close)
            .Reverse()
            .ToList();
    }

    /// <summary>True on the first session of a calendar week, used to pace weekly decisions.</summary>
    public bool IsFirstSessionOfWeek(DateOnly date)
    {
        if (!_sessionIndex.TryGetValue(date, out var index))
        {
            return false;
        }

        if (index == 0)
        {
            return true;
        }

        var previous = TradingDays[index - 1];
        return System.Globalization.ISOWeek.GetWeekOfYear(date.ToDateTime(default))
               != System.Globalization.ISOWeek.GetWeekOfYear(previous.ToDateTime(default));
    }

    /// <summary>True on the first session of a calendar month.</summary>
    public bool IsFirstSessionOfMonth(DateOnly date)
    {
        if (!_sessionIndex.TryGetValue(date, out var index))
        {
            return false;
        }

        if (index == 0)
        {
            return true;
        }

        var previous = TradingDays[index - 1];
        return date.Month != previous.Month || date.Year != previous.Year;
    }
}

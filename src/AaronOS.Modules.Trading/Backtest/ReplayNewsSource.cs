using AaronOS.Modules.Trading.Agent;

namespace AaronOS.Modules.Trading.Backtest;

/// <summary>
/// Headlines for a replay, served from a set fetched once and filtered by publication time.
///
/// The filter is the only thing here that matters. Every article is fetched up front, covering the whole
/// window, and the correctness of the entire news experiment rests on never returning one published after
/// the session being replayed. Get that wrong and the strategy looks clairvoyant, with nothing in the
/// output to suggest why.
/// </summary>
public sealed class ReplayNewsSource(IEnumerable<NewsHeadline> allHeadlines) : INewsSource
{
    private readonly List<NewsHeadline> _headlines = allHeadlines
        .OrderBy(h => h.CreatedUtc)
        .ToList();

    public int TotalAvailable => _headlines.Count;

    public Task<IReadOnlyList<NewsHeadline>> AsOfAsync(
        IEnumerable<string> symbols, DateOnly session, CancellationToken token = default)
    {
        var watched = symbols
            .Select(s => s.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var earliest = NewsWindow.EarliestUtc(session);
        var cutoff = NewsWindow.CutoffUtc(session);

        var selected = _headlines
            .Where(h => h.CreatedUtc > earliest && h.CreatedUtc <= cutoff)
            .Where(h => h.Symbols
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(watched.Contains))
            .TakeLast(NewsWindow.MaxHeadlines)
            .ToList();

        return Task.FromResult<IReadOnlyList<NewsHeadline>>(selected);
    }
}

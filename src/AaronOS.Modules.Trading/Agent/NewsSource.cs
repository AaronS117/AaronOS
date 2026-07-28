namespace AaronOS.Modules.Trading.Agent;

/// <summary>One headline, with the instant it was published so it can be filtered honestly.</summary>
public readonly record struct NewsHeadline(DateTime CreatedUtc, string Symbols, string Headline);

/// <summary>
/// Headlines available to a decision, as of a given session.
///
/// The timestamp is the whole point of the abstraction. A replay that hands the agent an article
/// published after the moment being replayed is doing the thing the entire backtest was designed to
/// avoid, and it would be invisible in the results — the strategy would simply look prescient.
/// </summary>
public interface INewsSource
{
    Task<IReadOnlyList<NewsHeadline>> AsOfAsync(
        IEnumerable<string> symbols, DateOnly session, CancellationToken token = default);
}

/// <summary>The default: no headlines, so the brief is unchanged unless news is switched on.</summary>
public sealed class NoNewsSource : INewsSource
{
    public Task<IReadOnlyList<NewsHeadline>> AsOfAsync(
        IEnumerable<string> symbols, DateOnly session, CancellationToken token = default) =>
        Task.FromResult<IReadOnlyList<NewsHeadline>>([]);
}

/// <summary>Shared knobs, so the live and replay sources present the same amount of text.</summary>
public static class NewsWindow
{
    /// <summary>
    /// How far back headlines are drawn from. Two days rather than one so a Monday decision still sees
    /// the weekend, and not more because the brief has to stay short enough for a small local model to
    /// attend to all of it.
    /// </summary>
    public const int LookbackDays = 2;

    /// <summary>Cap on headlines shown. Beyond this the list stops being read and starts being skimmed.</summary>
    public const int MaxHeadlines = 10;

    /// <summary>
    /// Latest instant a decision on this session may see.
    ///
    /// End of the session's own day, matching the price information the agent already has: the replay
    /// shows it that session's close and fills at the next open, so an article published during the
    /// session is inside the information set a real end-of-day decision would have had. Anything later
    /// is not.
    /// </summary>
    public static DateTime CutoffUtc(DateOnly session) =>
        session.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);

    public static DateTime EarliestUtc(DateOnly session) =>
        session.AddDays(-LookbackDays).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}

using AaronOS.Modules.Trading.Agent;
using AaronOS.Modules.Trading.Backtest;

namespace AaronOS.Modules.Trading.Tests;

/// <summary>
/// The point-in-time filter. Everything else in the news experiment is presentation; this is the part
/// that decides whether the result means anything. An article shown a session early makes the strategy
/// look prescient, and there would be nothing in the output to suggest why.
/// </summary>
public class NewsSourceTests
{
    private static readonly DateOnly Session = new(2026, 3, 10);

    private static NewsHeadline At(int day, int hour, string symbols, string text) =>
        new(new DateTime(2026, 3, day, hour, 0, 0, DateTimeKind.Utc), symbols, text);

    private static ReplayNewsSource Source() => new(
    [
        At(6, 12, "AAPL", "four days early, outside the window"),
        At(8, 9, "AAPL", "two days before, inside the window"),
        At(9, 15, "MSFT", "yesterday, inside the window"),
        At(10, 10, "AAPL", "same session, during hours"),
        At(10, 23, "AAPL", "same session, late evening"),
        At(11, 9, "AAPL", "THE NEXT DAY — must never be shown"),
        At(20, 9, "AAPL", "ten days later — must never be shown"),
    ]);

    [Fact]
    public async Task NothingPublishedAfterTheSessionIsEverReturned()
    {
        var headlines = await Source().AsOfAsync(["AAPL", "MSFT"], Session);

        Assert.DoesNotContain(headlines, h => h.Headline.Contains("NEXT DAY"));
        Assert.DoesNotContain(headlines, h => h.Headline.Contains("ten days later"));
        Assert.All(headlines, h => Assert.True(h.CreatedUtc <= NewsWindow.CutoffUtc(Session)));
    }

    [Fact]
    public async Task TheSessionsOwnNewsIsIncluded()
    {
        // Deliberate: the agent already sees this session's close and fills at the next open, so news
        // published during the session is inside the information set an end-of-day decision would have.
        var headlines = await Source().AsOfAsync(["AAPL"], Session);

        Assert.Contains(headlines, h => h.Headline.Contains("during hours"));
        Assert.Contains(headlines, h => h.Headline.Contains("late evening"));
    }

    [Fact]
    public async Task ArticlesOlderThanTheLookbackAreDropped()
    {
        var headlines = await Source().AsOfAsync(["AAPL"], Session);

        Assert.DoesNotContain(headlines, h => h.Headline.Contains("four days early"));
    }

    [Fact]
    public async Task OnlyWatchedSymbolsAreReturned()
    {
        var headlines = await Source().AsOfAsync(["AAPL"], Session);

        Assert.DoesNotContain(headlines, h => h.Headline.Contains("yesterday"));
    }

    [Fact]
    public async Task AHeadlineTaggedWithSeveralSymbolsMatchesAnyOfThem()
    {
        var source = new ReplayNewsSource([At(9, 12, "NVDA,AVGO,MU", "chip supply story")]);

        Assert.Single(await source.AsOfAsync(["AVGO"], Session));
        Assert.Empty(await source.AsOfAsync(["AAPL"], Session));
    }

    [Fact]
    public async Task TheListIsCappedSoTheBriefStaysReadable()
    {
        var many = Enumerable.Range(0, 40)
            .Select(i => At(9, 1, "AAPL", $"story {i}"))
            .ToList();

        var headlines = await new ReplayNewsSource(many).AsOfAsync(["AAPL"], Session);

        Assert.Equal(NewsWindow.MaxHeadlines, headlines.Count);
    }

    [Fact]
    public async Task WhenCappedItKeepsTheMostRecentRatherThanTheOldest()
    {
        var source = new ReplayNewsSource(
            Enumerable.Range(1, 20).Select(i => At(9, i % 24, "AAPL", $"story {i}")).ToList());

        var headlines = await source.AsOfAsync(["AAPL"], Session);

        // Newest are the ones that matter; oldest are the ones to drop.
        Assert.Contains(headlines, h => h.Headline == "story 20");
        Assert.DoesNotContain(headlines, h => h.Headline == "story 1");
    }

    [Fact]
    public async Task TheDefaultSourceReturnsNothingSoNewsIsOptInOnly()
    {
        Assert.Empty(await new NoNewsSource().AsOfAsync(["AAPL"], Session));
    }

    [Fact]
    public void TheCutoffIsTheEndOfTheSessionsOwnDay()
    {
        var cutoff = NewsWindow.CutoffUtc(Session);

        Assert.Equal(new DateOnly(2026, 3, 10), DateOnly.FromDateTime(cutoff));
        Assert.Equal(23, cutoff.Hour);
        Assert.Equal(DateTimeKind.Utc, cutoff.Kind);
    }
}

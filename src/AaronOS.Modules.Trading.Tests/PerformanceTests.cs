using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;

namespace AaronOS.Modules.Trading.Tests;

/// <summary>
/// The measurement layer. Its job is to stop a paper account in a rising market from reading as
/// skill, so most of these tests are about what it refuses to claim.
/// </summary>
public class PerformanceTests
{
    private static PortfolioSnapshot Day(int day, decimal equity, decimal? benchmark = null) =>
        new() { Date = new DateOnly(2026, 3, day), Equity = equity, Cash = 0m, BenchmarkClose = benchmark };

    [Fact]
    public void ReturnIsMeasuredFromTheFirstSnapshotToTheLast()
    {
        var summary = PerformanceCalculator.Summarise(
            [Day(1, 100_000m), Day(2, 105_000m), Day(3, 110_000m)], 0, 0, 30);

        Assert.Equal(10m, summary.StrategyReturnPercent);
        Assert.Equal(3, summary.DayCount);
    }

    [Fact]
    public void BeatingZeroWhileLosingToTheIndexIsReportedAsLosing()
    {
        // The whole point of the module. Up twelve percent looks like a success until the index is
        // put beside it, at which point it is three points of underperformance.
        var summary = PerformanceCalculator.Summarise(
            [Day(1, 100_000m, 500m), Day(2, 112_000m, 575m)], 40, 25, 30);

        Assert.Equal(12m, summary.StrategyReturnPercent);
        Assert.Equal(15m, summary.BenchmarkReturnPercent);
        Assert.Equal(-3m, summary.AlphaPercent);
        Assert.True(summary.IsBehindBenchmark);
        Assert.Contains("Behind SPY", summary.Verdict);
    }

    [Fact]
    public void GenuineOutperformanceIsReportedAsSuch()
    {
        var summary = PerformanceCalculator.Summarise(
            [Day(1, 100_000m, 500m), Day(2, 112_000m, 525m)], 40, 25, 30);

        Assert.Equal(7m, summary.AlphaPercent);
        Assert.False(summary.IsBehindBenchmark);
        Assert.Contains("Ahead of SPY", summary.Verdict);
    }

    [Fact]
    public void LosingLessThanTheIndexStillCountsAsAhead()
    {
        var summary = PerformanceCalculator.Summarise(
            [Day(1, 100_000m, 500m), Day(2, 95_000m, 450m)], 40, 20, 30);

        Assert.Equal(-5m, summary.StrategyReturnPercent);
        Assert.Equal(-10m, summary.BenchmarkReturnPercent);
        Assert.Equal(5m, summary.AlphaPercent);
        Assert.False(summary.IsBehindBenchmark);
    }

    [Fact]
    public void WithoutBenchmarkDataNoComparisonIsInvented()
    {
        var summary = PerformanceCalculator.Summarise([Day(1, 100_000m), Day(2, 130_000m)], 40, 40, 30);

        Assert.Null(summary.BenchmarkReturnPercent);
        Assert.Null(summary.AlphaPercent);
        Assert.False(summary.IsBehindBenchmark);
        Assert.Contains("nothing to compare", summary.Verdict);
    }

    [Fact]
    public void AMissingBenchmarkAtEitherEndVoidsTheComparison()
    {
        // Silently substituting the nearest available close would shift the window in whichever
        // direction happened to help.
        var summary = PerformanceCalculator.Summarise(
            [Day(1, 100_000m, null), Day(2, 110_000m, 550m)], 40, 30, 30);

        Assert.Null(summary.AlphaPercent);
    }

    [Fact]
    public void TheWinRateIsWithheldUntilTheSampleIsUsable()
    {
        // Eight from eight is noise that reads exactly like skill.
        var summary = PerformanceCalculator.Summarise([Day(1, 100_000m), Day(2, 120_000m)], 8, 8, 30);

        Assert.Null(summary.WinRatePercent);
        Assert.False(summary.HasMeaningfulSample);
    }

    [Fact]
    public void TheWinRateAppearsOnceThereAreEnoughTrades()
    {
        var summary = PerformanceCalculator.Summarise([Day(1, 100_000m), Day(2, 120_000m)], 40, 22, 30);

        Assert.True(summary.HasMeaningfulSample);
        Assert.Equal(55m, summary.WinRatePercent);
    }

    [Fact]
    public void ASmallSampleIsCalledOutEvenWhenItIsAhead()
    {
        var summary = PerformanceCalculator.Summarise(
            [Day(1, 100_000m, 500m), Day(2, 130_000m, 505m)], 3, 3, 30);

        Assert.Contains("too few trades to mean anything", summary.Verdict);
    }

    [Fact]
    public void DrawdownIsTheDeepestFallFromAPreviousPeak()
    {
        // Peak 120,000, trough 90,000 → 25%. The endpoint alone would report a 5% gain and hide
        // that a quarter of the account disappeared along the way.
        var summary = PerformanceCalculator.Summarise(
            [Day(1, 100_000m), Day(2, 120_000m), Day(3, 90_000m), Day(4, 105_000m)], 0, 0, 30);

        Assert.Equal(25m, summary.MaxDrawdownPercent);
        Assert.Equal(5m, summary.StrategyReturnPercent);
    }

    [Fact]
    public void ACurveThatOnlyRisesHasNoDrawdown()
    {
        var summary = PerformanceCalculator.Summarise(
            [Day(1, 100_000m), Day(2, 110_000m), Day(3, 120_000m)], 0, 0, 30);

        Assert.Equal(0m, summary.MaxDrawdownPercent);
    }

    [Fact]
    public void NoSnapshotsProducesEmptyFiguresRatherThanThrowing()
    {
        var summary = PerformanceCalculator.Summarise([], 0, 0, 30);

        Assert.Equal(0, summary.DayCount);
        Assert.Equal(0m, summary.StrategyReturnPercent);
        Assert.Null(summary.AlphaPercent);
    }

    [Fact]
    public void SnapshotsOutOfOrderAreSortedBeforeMeasuring()
    {
        var summary = PerformanceCalculator.Summarise(
            [Day(3, 110_000m, 550m), Day(1, 100_000m, 500m)], 0, 0, 30);

        Assert.Equal(10m, summary.StrategyReturnPercent);
        Assert.Equal(10m, summary.BenchmarkReturnPercent);
    }
}

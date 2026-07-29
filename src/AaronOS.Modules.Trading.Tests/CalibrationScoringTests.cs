using AaronOS.Modules.Trading.Forecasting;

namespace AaronOS.Modules.Trading.Tests;

/// <summary>
/// The scoring for the prediction-market experiment. Worth pinning hard, because this is the layer that
/// decides whether a forecast gets called an edge, and the equities work established that a measurement
/// which flatters is worse than no measurement.
/// </summary>
public class CalibrationScoringTests
{
    private static ScoredForecast F(double market, double forecast, bool outcome) =>
        new("T", "q?", market, forecast, outcome);

    [Fact]
    public void APerfectForecastScoresZeroBrier()
    {
        var report = CalibrationScoring.Summarise([F(0.5, 1.0, true), F(0.5, 0.0, false)]);

        Assert.Equal(0.0, report.ForecastBrier, 10);
    }

    [Fact]
    public void ConstantFiftyFiftyScoresTheCoinFlipValue()
    {
        var report = CalibrationScoring.Summarise([F(0.5, 0.5, true), F(0.5, 0.5, false)]);

        Assert.Equal(CalibrationScoring.CoinFlipBrier, report.ForecastBrier, 10);
        Assert.False(report.BeatsCoinFlip);
    }

    [Fact]
    public void ConfidentAndWrongIsPunishedHardest()
    {
        // The property that makes Brier the right scoring rule here: it penalises confident error far
        // more than hedged error, which is exactly the failure mode the literature attributes to LLMs.
        var confident = CalibrationScoring.Summarise([F(0.5, 0.99, false)]);
        var hedged = CalibrationScoring.Summarise([F(0.5, 0.6, false)]);

        Assert.True(confident.ForecastBrier > hedged.ForecastBrier);
    }

    [Fact]
    public void EdgeIsMeasuredAgainstTheMarketNotAgainstZero()
    {
        // A forecast can be good in absolute terms and still worthless, because any bet has to be taken
        // at the market's price.
        var report = CalibrationScoring.Summarise(
            Enumerable.Range(0, 200).Select(i => F(0.90, 0.80, true)).ToList());

        Assert.True(report.BeatsCoinFlip);
        Assert.True(report.EdgeOverMarket < 0, "market was closer to the truth, so there is no edge");
        Assert.Contains("The price was the better forecast", report.Verdict);
    }

    [Fact]
    public void ABeatenMarketIsReportedAsAnEdge()
    {
        var report = CalibrationScoring.Summarise(
            Enumerable.Range(0, 200).Select(i => F(0.60, 0.85, true)).ToList());

        Assert.True(report.EdgeOverMarket > 0);
        Assert.Contains("Beat the market", report.Verdict);
    }

    [Fact]
    public void ASmallSampleRefusesToConclude()
    {
        // The equities work set a 30-trade floor and then produced runs with zero. Here the floor is
        // stated in the verdict itself so a promising number cannot be quoted without its sample size.
        var report = CalibrationScoring.Summarise(
            Enumerable.Range(0, 40).Select(i => F(0.5, 0.9, true)).ToList());

        Assert.True(report.EdgeOverMarket > 0);
        Assert.Contains("too few to conclude", report.Verdict);
    }

    [Fact]
    public void CalibrationErrorCatchesOverconfidence()
    {
        // Eighty percent confidence that comes true half the time. Accuracy looks acceptable; the
        // calibration is badly wrong, and calibration is what a stake size rests on.
        var forecasts = Enumerable.Range(0, 100)
            .Select(i => F(0.5, 0.8, i % 2 == 0))
            .ToList();

        var report = CalibrationScoring.Summarise(forecasts);

        Assert.InRange(report.ExpectedCalibrationError, 0.25, 0.35);
    }

    [Fact]
    public void APerfectlyCalibratedForecasterHasNearZeroCalibrationError()
    {
        // Thirty percent stated, thirty percent observed — right about being unsure.
        var forecasts = Enumerable.Range(0, 100)
            .Select(i => F(0.5, 0.3, i < 30))
            .ToList();

        Assert.InRange(CalibrationScoring.Summarise(forecasts).ExpectedCalibrationError, 0.0, 0.02);
    }

    [Fact]
    public void OnlyMeaningfulDisagreementsCount()
    {
        // Agreeing with the price to within a few points is not a view, and counting it as one would
        // dilute the only statistic that says whether disagreement is informed.
        var forecasts = new List<ScoredForecast>
        {
            F(0.50, 0.51, true),   // noise
            F(0.50, 0.52, false),  // noise
            F(0.50, 0.90, true),   // a real view, and correct
            F(0.50, 0.10, false),  // a real view, and correct
        };

        var report = CalibrationScoring.Summarise(forecasts);

        Assert.Equal(2, report.SubstantialDisagreements);
        Assert.Equal(1.0, report.BeatsMarketWhenDisagreeing, 10);
    }

    [Fact]
    public void NoQuestionsProducesAnHonestEmptyReport()
    {
        var report = CalibrationScoring.Summarise([]);

        Assert.Equal(0, report.Count);
        Assert.Contains("No resolved questions", report.Verdict);
    }

    [Fact]
    public void BinsRecordStatedAgainstObservedSoTheShapeOfTheErrorIsVisible()
    {
        var forecasts = Enumerable.Range(0, 100).Select(i => F(0.5, 0.95, i < 60)).ToList();

        var bin = Assert.Single(CalibrationScoring.Summarise(forecasts).Bins);

        Assert.Equal(100, bin.Count);
        Assert.Equal(0.95, bin.StatedProbability, 6);
        Assert.Equal(0.60, bin.ObservedFrequency, 6);
        Assert.True(bin.Gap > 0, "positive gap means overconfident");
    }
}

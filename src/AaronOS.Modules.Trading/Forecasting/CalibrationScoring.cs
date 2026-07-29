namespace AaronOS.Modules.Trading.Forecasting;

/// <summary>One resolved binary question, with the forecast and the market price at the same moment.</summary>
public readonly record struct ScoredForecast(
    string Ticker,
    string Question,
    double MarketProbability,
    double ForecastProbability,
    bool Outcome)
{
    public double Actual => Outcome ? 1.0 : 0.0;

    public double ForecastBrier => Math.Pow(ForecastProbability - Actual, 2);

    public double MarketBrier => Math.Pow(MarketProbability - Actual, 2);

    /// <summary>How far the forecast departs from the price. No disagreement means no possible edge.</summary>
    public double Disagreement => ForecastProbability - MarketProbability;
}

/// <summary>
/// Brier scores and calibration for a set of resolved questions.
///
/// This is why prediction markets are a better-formed experiment than equities. There, the only
/// observable was profit over time, so answering "is there an edge" needed months and a large sample of
/// trades. Here every question resolves to a zero or a one, so a forecast can be graded the moment the
/// event settles — and graded against the market price at the same instant, which is the only comparison
/// that matters. Hundreds of resolved questions give a statistically meaningful answer in an afternoon,
/// with no position taken and nothing at risk.
///
/// Lower Brier is better; 0.25 is what constant 50% guessing scores.
/// </summary>
public static class CalibrationScoring
{
    /// <summary>What guessing 50% on everything scores, and therefore the floor of usefulness.</summary>
    public const double CoinFlipBrier = 0.25;

    public static CalibrationReport Summarise(IReadOnlyList<ScoredForecast> forecasts, int buckets = 10)
    {
        if (forecasts.Count == 0)
        {
            return new CalibrationReport(0, 0, 0, 0, 0, 0, 0, 0, []);
        }

        var forecastBrier = forecasts.Average(f => f.ForecastBrier);
        var marketBrier = forecasts.Average(f => f.MarketBrier);

        // Expected calibration error: across probability buckets, how far stated confidence sits from
        // observed frequency. The research on LLM forecasting is clear that this, rather than raw
        // accuracy, is where models are weakest — and it is exactly what a bet size depends on.
        var bins = new List<CalibrationBin>();
        var weightedGap = 0.0;

        for (var i = 0; i < buckets; i++)
        {
            var low = (double)i / buckets;
            var high = (double)(i + 1) / buckets;

            var inBin = forecasts
                .Where(f => f.ForecastProbability >= low &&
                            (f.ForecastProbability < high || (i == buckets - 1 && f.ForecastProbability <= 1.0)))
                .ToList();

            if (inBin.Count == 0)
            {
                continue;
            }

            var stated = inBin.Average(f => f.ForecastProbability);
            var observed = inBin.Average(f => f.Actual);
            bins.Add(new CalibrationBin(low, high, inBin.Count, stated, observed));
            weightedGap += inBin.Count * Math.Abs(stated - observed);
        }

        // Rate of agreeing with the market's direction on the eventual outcome, which says whether any
        // disagreement is informed or merely noise.
        var disagreements = forecasts.Where(f => Math.Abs(f.Disagreement) >= 0.05).ToList();
        var rightWhenDisagreeing = disagreements.Count == 0
            ? 0.0
            : (double)disagreements.Count(f => f.ForecastBrier < f.MarketBrier) / disagreements.Count;

        return new CalibrationReport(
            Count: forecasts.Count,
            ForecastBrier: forecastBrier,
            MarketBrier: marketBrier,
            CoinFlipBrier: CoinFlipBrier,
            ExpectedCalibrationError: weightedGap / forecasts.Count,
            MeanAbsoluteDisagreement: forecasts.Average(f => Math.Abs(f.Disagreement)),
            SubstantialDisagreements: disagreements.Count,
            BeatsMarketWhenDisagreeing: rightWhenDisagreeing,
            Bins: bins);
    }
}

public readonly record struct CalibrationBin(
    double Low, double High, int Count, double StatedProbability, double ObservedFrequency)
{
    public double Gap => StatedProbability - ObservedFrequency;
}

public readonly record struct CalibrationReport(
    int Count,
    double ForecastBrier,
    double MarketBrier,
    double CoinFlipBrier,
    double ExpectedCalibrationError,
    double MeanAbsoluteDisagreement,
    int SubstantialDisagreements,
    double BeatsMarketWhenDisagreeing,
    IReadOnlyList<CalibrationBin> Bins)
{
    /// <summary>Brier points better than the market. Positive means the forecast was more accurate.</summary>
    public double EdgeOverMarket => MarketBrier - ForecastBrier;

    public bool BeatsCoinFlip => ForecastBrier < CoinFlipBrier;

    /// <summary>
    /// The verdict, and it deliberately refuses to congratulate a forecast for beating a coin flip.
    ///
    /// Beating 0.25 is trivially easy on markets that are mostly lopsided, and it says nothing about
    /// whether money could be made. Only the comparison against the market price does, because that is
    /// the price any bet would actually have to be taken at.
    /// </summary>
    public string Verdict => Count switch
    {
        0 => "No resolved questions scored.",
        < 100 => $"{Count} questions is too few to conclude anything; {EdgeOverMarket:+0.0000;-0.0000} " +
                 "Brier against the market so far.",
        _ when EdgeOverMarket <= 0 =>
            $"No edge: Brier {ForecastBrier:0.0000} against the market's {MarketBrier:0.0000}. " +
            "The price was the better forecast.",
        _ => $"Beat the market by {EdgeOverMarket:0.0000} Brier ({ForecastBrier:0.0000} vs " +
             $"{MarketBrier:0.0000}) over {Count} questions.",
    };
}

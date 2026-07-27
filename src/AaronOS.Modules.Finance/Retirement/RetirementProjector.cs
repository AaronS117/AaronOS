namespace AaronOS.Modules.Finance.Retirement;

/// <summary>Everything a projection needs, already totalled across accounts.</summary>
public readonly record struct ProjectionInput(
    decimal StartingBalance,
    decimal AnnualContribution,
    int StartAge,
    int Years,
    decimal InflationPercent,
    decimal WithdrawalRatePercent);

public readonly record struct ProjectionPoint(int YearOffset, int Age, decimal Nominal, decimal TodaysDollars);

public readonly record struct ProjectionResult(
    string ScenarioName,
    decimal AnnualReturnPercent,
    IReadOnlyList<ProjectionPoint> Points,
    decimal FinalNominal,
    decimal FinalTodaysDollars,
    decimal AnnualIncomeTodaysDollars)
{
    public decimal MonthlyIncomeTodaysDollars => AnnualIncomeTodaysDollars / 12m;
}

/// <summary>
/// Compound growth of a balance plus recurring contributions.
///
/// Two deliberate simplifications, both stated rather than hidden. Contributions are added at the
/// end of each year, which understates the result slightly compared with monthly paycheque
/// deferrals — the error is in the direction of not flattering the projection, and the model stays
/// simple enough to check by hand. And there is no Monte Carlo simulation: three labelled return
/// scenarios say "nobody knows" more honestly than a probability distribution built on an assumed
/// volatility that is equally invented.
///
/// Every figure is also reported in today's dollars. A nominal balance thirty years out is a large
/// number that means much less than it looks like, and showing only the nominal figure is the
/// single most misleading thing a retirement projection can do.
/// </summary>
public static class RetirementProjector
{
    private const int MaxYears = 80;

    /// <summary>How far the outer scenarios sit either side of the expected return.</summary>
    public const decimal ScenarioSpreadPercent = 2m;

    /// <summary>The three scenarios, pessimistic first so a chart legend reads low to high.</summary>
    public static List<ProjectionResult> ProjectScenarios(ProjectionInput input, decimal expectedReturnPercent)
    {
        return
        [
            Project(input, "Conservative", expectedReturnPercent - ScenarioSpreadPercent),
            Project(input, "Expected", expectedReturnPercent),
            Project(input, "Optimistic", expectedReturnPercent + ScenarioSpreadPercent),
        ];
    }

    public static ProjectionResult Project(ProjectionInput input, string scenarioName, decimal annualReturnPercent)
    {
        var years = Math.Clamp(input.Years, 0, MaxYears);
        var growth = 1m + (Math.Clamp(annualReturnPercent, -50m, 50m) / 100m);
        var inflation = 1m + (Math.Clamp(input.InflationPercent, -20m, 50m) / 100m);

        var balance = input.StartingBalance;
        var deflator = 1m;

        var points = new List<ProjectionPoint>(years + 1)
        {
            new(0, input.StartAge, balance, balance),
        };

        for (var year = 1; year <= years; year++)
        {
            balance = (balance * growth) + input.AnnualContribution;

            // Built up year by year rather than as a power, so the whole calculation stays in
            // decimal and never round-trips through double.
            deflator *= inflation;
            var real = deflator == 0m ? balance : balance / deflator;

            points.Add(new ProjectionPoint(year, input.StartAge + year, balance, real));
        }

        var last = points[^1];
        var income = last.TodaysDollars * Math.Clamp(input.WithdrawalRatePercent, 0m, 100m) / 100m;

        return new ProjectionResult(
            scenarioName, annualReturnPercent, points, last.Nominal, last.TodaysDollars, income);
    }
}

using AaronOS.Modules.Finance.Data;
using AaronOS.Modules.Finance.Retirement;

namespace AaronOS.Modules.Finance.Tests;

/// <summary>
/// Pins the projection arithmetic. Every figure here was worked out by hand first, which is the
/// point of the end-of-year contribution convention: a model nobody can check is a model nobody
/// should trust with a retirement decision.
/// </summary>
public class RetirementProjectorTests
{
    private static ProjectionInput Input(
        decimal starting, decimal contribution, int years,
        decimal inflation = 0m, decimal withdrawal = 4m, int startAge = 40) =>
        new(starting, contribution, startAge, years, inflation, withdrawal);

    [Fact]
    public void Compounds_ToAHandCalculatedFigure()
    {
        // 10,000 → ×1.06 = 10,600 + 6,000 = 16,600 → ×1.06 = 17,596 + 6,000 = 23,596
        var result = RetirementProjector.Project(Input(10_000m, 6_000m, 2), "Expected", 6m);

        Assert.Equal(23_596m, result.FinalNominal);
        Assert.Equal(3, result.Points.Count);
        Assert.Equal(10_000m, result.Points[0].Nominal);
        Assert.Equal(16_600m, result.Points[1].Nominal);
    }

    [Fact]
    public void AgeAdvancesOneYearPerPoint()
    {
        var result = RetirementProjector.Project(Input(1_000m, 0m, 3, startAge: 41), "Expected", 5m);

        Assert.Equal([41, 42, 43, 44], result.Points.Select(p => p.Age));
    }

    [Fact]
    public void WithNoReturn_OnlyTheContributionsAccumulate()
    {
        var result = RetirementProjector.Project(Input(0m, 1_000m, 3), "Flat", 0m);

        Assert.Equal(3_000m, result.FinalNominal);
    }

    [Fact]
    public void ZeroYears_ReturnsTheStartingBalanceUntouched()
    {
        var result = RetirementProjector.Project(Input(12_345m, 9_999m, 0), "Expected", 7m);

        Assert.Single(result.Points);
        Assert.Equal(12_345m, result.FinalNominal);
        Assert.Equal(12_345m, result.FinalTodaysDollars);
    }

    [Fact]
    public void NegativeYears_AreTreatedAsNoneRatherThanThrowing()
    {
        var result = RetirementProjector.Project(Input(500m, 100m, -5), "Expected", 6m);

        Assert.Single(result.Points);
        Assert.Equal(500m, result.FinalNominal);
    }

    [Fact]
    public void TodaysDollars_DiscountByInflationAndSitBelowTheNominalFigure()
    {
        // Two years at 10% inflation with no growth: 10,000 nominal is 10,000 / 1.21 today.
        var result = RetirementProjector.Project(Input(10_000m, 0m, 2, inflation: 10m), "Flat", 0m);

        Assert.Equal(10_000m, result.FinalNominal);
        Assert.Equal(8_264.46m, Math.Round(result.FinalTodaysDollars, 2));
        Assert.True(result.FinalTodaysDollars < result.FinalNominal);
    }

    [Fact]
    public void Income_IsTheWithdrawalRateAppliedToTodaysDollars()
    {
        var result = RetirementProjector.Project(Input(1_000_000m, 0m, 0, withdrawal: 4m), "Expected", 6m);

        Assert.Equal(40_000m, result.AnnualIncomeTodaysDollars);
        Assert.Equal(40_000m / 12m, result.MonthlyIncomeTodaysDollars);
    }

    [Fact]
    public void ThreeScenarios_StraddleTheExpectedReturnAndRankLowToHigh()
    {
        var results = RetirementProjector.ProjectScenarios(Input(50_000m, 10_000m, 20), 6m);

        Assert.Equal(3, results.Count);
        Assert.Equal(4m, results[0].AnnualReturnPercent);
        Assert.Equal(6m, results[1].AnnualReturnPercent);
        Assert.Equal(8m, results[2].AnnualReturnPercent);
        Assert.True(results[0].FinalNominal < results[1].FinalNominal);
        Assert.True(results[1].FinalNominal < results[2].FinalNominal);
    }

    [Fact]
    public void AnAbsurdReturnIsClamped_SoTheChartStaysFinite()
    {
        var result = RetirementProjector.Project(Input(1_000m, 0m, 10), "Nonsense", 5_000m);

        // Clamped to 50%: 1,000 × 1.5^10 = 57,665.04
        Assert.Equal(57_665.04m, Math.Round(result.FinalNominal, 2));
    }

    [Theory]
    [InlineData(100_000, 50, 6, 10_000, 3_000)]  // 50% match up to 6% of pay, fully earned
    [InlineData(100_000, 50, 6, 3_000, 1_500)]   // under-contributing earns only half the match
    [InlineData(100_000, 100, 4, 20_000, 4_000)] // dollar for dollar, capped at 4% of pay
    [InlineData(0, 50, 6, 10_000, 0)]            // no salary means no match to compute
    [InlineData(100_000, 0, 6, 10_000, 0)]       // no match offered
    public void EmployerMatch_NeedsBothThePercentAndTheCap(
        decimal salary, decimal matchPercent, decimal matchLimit, decimal contribution, decimal expected)
    {
        var account = new RetirementAccount
        {
            AnnualContribution = contribution,
            EmployerMatchPercent = matchPercent,
            EmployerMatchLimitPercent = matchLimit,
        };

        Assert.Equal(expected, account.EmployerMatchOn(salary));
    }

    [Theory]
    [InlineData(40, 35, false)]  // retiring before you are born again
    [InlineData(40, 40, false)]  // no years left to project
    [InlineData(40, 65, true)]
    [InlineData(5, 65, false)]   // implausible age
    public void PlanUsability_RejectsAnInvertedOrImpossibleAgePair(int age, int retireAt, bool expected)
    {
        var plan = new RetirementPlan { CurrentAge = age, TargetRetirementAge = retireAt };

        Assert.Equal(expected, plan.IsUsable);
    }

    [Fact]
    public void PlanUsability_RejectsAZeroWithdrawalRate()
    {
        // Zero would report an income of nothing from a healthy balance, which reads as a bug
        // rather than as the setting it is.
        var plan = new RetirementPlan { WithdrawalRatePercent = 0m };

        Assert.False(plan.IsUsable);
    }
}

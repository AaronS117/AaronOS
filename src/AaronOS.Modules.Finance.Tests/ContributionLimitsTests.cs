using AaronOS.Modules.Finance.Data;
using AaronOS.Modules.Finance.Retirement;

namespace AaronOS.Modules.Finance.Tests;

/// <summary>
/// The caps themselves are hand-entered IRS figures, so these tests pin the rules around them
/// rather than re-asserting the numbers for their own sake: which accounts share a cap, and how the
/// age-based catch-ups switch on and off.
/// </summary>
public class ContributionLimitsTests
{
    private static RetirementAccount Account(RetirementAccountKind kind, decimal contribution) =>
        new() { Name = kind.ToString(), Kind = kind, AnnualContribution = contribution };

    [Theory]
    [InlineData(40, 24_500)]  // no catch-up before 50
    [InlineData(49, 24_500)]
    [InlineData(50, 32_500)]  // ordinary catch-up
    [InlineData(59, 32_500)]
    [InlineData(60, 35_750)]  // SECURE 2.0's higher window replaces the ordinary catch-up
    [InlineData(63, 35_750)]
    [InlineData(64, 32_500)]  // and the window closes again at 64
    public void ElectiveDeferralCap_TracksTheCatchUpWindows(int age, decimal expected)
    {
        Assert.Equal(expected, ContributionLimits.LimitFor(ContributionLimitGroup.ElectiveDeferral, age, false));
    }

    [Theory]
    [InlineData(40, 7_500)]
    [InlineData(50, 8_600)]
    public void IraCap_AddsTheCatchUpAtFifty(int age, decimal expected)
    {
        Assert.Equal(expected, ContributionLimits.LimitFor(ContributionLimitGroup.Ira, age, false));
    }

    [Theory]
    [InlineData(40, false, 4_400)]
    [InlineData(40, true, 8_750)]
    [InlineData(55, false, 5_400)]
    [InlineData(55, true, 9_750)]
    public void HsaCap_DependsOnCoverageAndTheFiftyFiveCatchUp(int age, bool family, decimal expected)
    {
        Assert.Equal(expected, ContributionLimits.LimitFor(ContributionLimitGroup.Hsa, age, family));
    }

    [Fact]
    public void TaxableInvesting_HasNoCap()
    {
        Assert.Null(ContributionLimits.LimitFor(ContributionLimitGroup.Unlimited, 40, false));
        Assert.Equal(
            ContributionLimitGroup.Unlimited,
            ContributionLimits.GroupOf(RetirementAccountKind.TaxableBrokerage));
    }

    [Fact]
    public void ATraditionalAndARoth401k_ShareOneCapRatherThanGettingOneEach()
    {
        // The bug this guards against reports 20,000 and 10,000 as both comfortably under 24,500,
        // when together they are 5,500 over and the excess has to come back out of the plan.
        var accounts = new[]
        {
            Account(RetirementAccountKind.Traditional401k, 20_000m),
            Account(RetirementAccountKind.Roth401k, 10_000m),
        };

        var checks = ContributionLimits.Check(accounts, 40, false);

        var check = Assert.Single(checks);
        Assert.Equal(ContributionLimitGroup.ElectiveDeferral, check.Group);
        Assert.Equal(30_000m, check.Contributed);
        Assert.True(check.IsOver);
        Assert.Equal(5_500m, check.OverBy);
        Assert.Equal(0m, check.Headroom);
    }

    [Fact]
    public void ATraditionalAndARothIra_AlsoShareOneCap()
    {
        var accounts = new[]
        {
            Account(RetirementAccountKind.TraditionalIra, 4_000m),
            Account(RetirementAccountKind.RothIra, 4_000m),
        };

        var check = Assert.Single(ContributionLimits.Check(accounts, 40, false));

        Assert.Equal(8_000m, check.Contributed);
        Assert.True(check.IsOver);
        Assert.Equal(500m, check.OverBy);
    }

    [Fact]
    public void HeadroomIsReportedWhenUnderTheCap()
    {
        var accounts = new[] { Account(RetirementAccountKind.Traditional401k, 12_000m) };

        var check = Assert.Single(ContributionLimits.Check(accounts, 40, false));

        Assert.False(check.IsOver);
        Assert.Equal(12_500m, check.Headroom);
        Assert.Equal(0m, check.OverBy);
    }

    [Fact]
    public void AccountsWithNothingGoingIn_AreLeftOutOfTheChecks()
    {
        var accounts = new[]
        {
            Account(RetirementAccountKind.Traditional401k, 0m),
            Account(RetirementAccountKind.RothIra, 7_000m),
        };

        var check = Assert.Single(ContributionLimits.Check(accounts, 40, false));

        Assert.Equal(ContributionLimitGroup.Ira, check.Group);
    }

    [Fact]
    public void UncappedAccounts_ProduceNoCheckEvenWhenFunded()
    {
        var accounts = new[] { Account(RetirementAccountKind.TaxableBrokerage, 50_000m) };

        Assert.Empty(ContributionLimits.Check(accounts, 40, false));
    }
}

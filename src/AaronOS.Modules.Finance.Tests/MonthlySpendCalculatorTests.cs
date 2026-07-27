using AaronOS.Modules.Finance.Data;
using AaronOS.Modules.Finance.Sync;

namespace AaronOS.Modules.Finance.Tests;

/// <summary>
/// The average that sizes an emergency fund. Each of the three exclusions here — the partial current
/// month, internal transfers, and months with no data at all — makes the figure larger and the
/// target more demanding, which is the direction an error should never go in.
/// </summary>
public class MonthlySpendCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    private static FinanceTransaction Spend(int year, int month, int day, decimal amount, string? category = "FOOD_AND_DRINK") =>
        new() { Date = new DateOnly(year, month, day), Amount = amount, CategoryPrimary = category, Name = "test" };

    [Fact]
    public void AveragesTheCompleteMonthsAndIgnoresThePartialCurrentOne()
    {
        List<FinanceTransaction> transactions =
        [
            Spend(2026, 6, 3, 1_000m),
            Spend(2026, 5, 3, 2_000m),
            Spend(2026, 4, 3, 3_000m),
            // Mid-July: only half a month has happened, so counting it would drag the average down.
            Spend(2026, 7, 3, 50m),
        ];

        Assert.Equal(2_000m, MonthlySpendCalculator.AverageMonthlyOutflow(transactions, Today));
    }

    [Fact]
    public void TransfersBetweenYourOwnAccountsAreNotSpending()
    {
        List<FinanceTransaction> transactions =
        [
            Spend(2026, 6, 3, 1_000m),
            Spend(2026, 6, 4, 5_000m, "TRANSFER_OUT"),
            Spend(2026, 5, 3, 1_000m),
            Spend(2026, 5, 4, 5_000m, "TRANSFER_IN"),
            Spend(2026, 4, 3, 1_000m),
        ];

        Assert.Equal(1_000m, MonthlySpendCalculator.AverageMonthlyOutflow(transactions, Today));
    }

    [Fact]
    public void MoneyComingInIsNotCountedAsSpend()
    {
        // Plaid convention: a negative amount is money in. A paycheque must not offset the spend.
        List<FinanceTransaction> transactions =
        [
            Spend(2026, 6, 3, 1_500m),
            Spend(2026, 6, 1, -4_000m, "INCOME"),
        ];

        Assert.Equal(1_500m, MonthlySpendCalculator.AverageMonthlyOutflow(transactions, Today));
    }

    [Fact]
    public void MonthsWithNoDataAtAllAreSkippedRatherThanCountedAsZero()
    {
        // A bank link that only reaches back one month would otherwise average 1,000 across three
        // months and report a spend of 333, making a thin emergency fund look generous.
        List<FinanceTransaction> transactions = [Spend(2026, 6, 3, 1_000m)];

        Assert.Equal(1_000m, MonthlySpendCalculator.AverageMonthlyOutflow(transactions, Today));
    }

    [Fact]
    public void AMonthWithActivityButNoSpendStillCountsAsZero()
    {
        // Distinct from the case above: there IS data for May, it just happens to be inflow only,
        // so a genuine zero-spend month belongs in the average.
        List<FinanceTransaction> transactions =
        [
            Spend(2026, 6, 3, 1_000m),
            Spend(2026, 5, 1, -2_000m, "INCOME"),
        ];

        Assert.Equal(500m, MonthlySpendCalculator.AverageMonthlyOutflow(transactions, Today));
    }

    [Fact]
    public void NoTransactionsAtAllGivesZeroRatherThanDividingByZero()
    {
        Assert.Equal(0m, MonthlySpendCalculator.AverageMonthlyOutflow([], Today));
    }

    [Fact]
    public void CrossesTheYearBoundaryCorrectly()
    {
        List<FinanceTransaction> transactions =
        [
            Spend(2025, 12, 3, 1_000m),
            Spend(2025, 11, 3, 2_000m),
        ];

        Assert.Equal(1_500m, MonthlySpendCalculator.AverageMonthlyOutflow(transactions, new DateOnly(2026, 1, 20)));
    }

    [Theory]
    [InlineData(9_000, 3_000, 3.0)]
    [InlineData(1_500, 3_000, 0.5)]
    [InlineData(0, 3_000, 0.0)]
    public void MonthsCovered_IsTheBalanceDividedBySpend(decimal balance, decimal spend, double expected)
    {
        Assert.Equal((decimal)expected, MonthlySpendCalculator.MonthsCovered(balance, spend));
    }

    [Fact]
    public void MonthsCovered_IsUnknownRatherThanInfiniteWhenSpendIsUnknown()
    {
        Assert.Null(MonthlySpendCalculator.MonthsCovered(10_000m, 0m));
    }
}

using AaronOS.Modules.Finance.Data;
using AaronOS.Modules.Finance.Sync;

namespace AaronOS.Modules.Finance.Tests;

public class CategorySpendCalculatorTests
{
    private static FinanceTransaction Txn(int year, int month, int day, string? category, decimal amount) => new()
    {
        Date = new DateOnly(year, month, day),
        CategoryPrimary = category,
        Amount = amount
    };

    [Fact]
    public void SumsSpendByCategory_ForTheGivenMonthOnly()
    {
        var transactions = new List<FinanceTransaction>
        {
            Txn(2026, 7, 1, "FOOD_AND_DRINK", 20m),
            Txn(2026, 7, 15, "FOOD_AND_DRINK", 15m),
            Txn(2026, 7, 2, "GROCERIES", 50m),
            Txn(2026, 6, 30, "FOOD_AND_DRINK", 999m), // different month — excluded
        };

        var result = CategorySpendCalculator.SpendByCategory(transactions, 2026, 7);

        Assert.Equal(35m, result["FOOD_AND_DRINK"]);
        Assert.Equal(50m, result["GROCERIES"]);
        Assert.False(result.ContainsKey("999"));
    }

    [Fact]
    public void ExcludesTransferCategories_SoInternalMovementIsntCountedAsSpend()
    {
        var transactions = new List<FinanceTransaction>
        {
            Txn(2026, 7, 1, "TRANSFER_OUT", 500m),
            Txn(2026, 7, 1, "TRANSFER_IN", 500m),
            Txn(2026, 7, 1, "GROCERIES", 30m),
        };

        var result = CategorySpendCalculator.SpendByCategory(transactions, 2026, 7);

        Assert.Single(result);
        Assert.Equal(30m, result["GROCERIES"]);
    }

    [Fact]
    public void ExcludesNegativeAmounts_SincePlaidConventionIsPositiveEqualsSpend()
    {
        var transactions = new List<FinanceTransaction>
        {
            Txn(2026, 7, 1, "INCOME", -1000m), // a deposit/refund, not spend
            Txn(2026, 7, 1, "GROCERIES", 30m),
        };

        var result = CategorySpendCalculator.SpendByCategory(transactions, 2026, 7);

        Assert.Single(result);
        Assert.Equal(30m, result["GROCERIES"]);
    }
}

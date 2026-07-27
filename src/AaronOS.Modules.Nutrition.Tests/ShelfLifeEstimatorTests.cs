using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.ShelfLife;

namespace AaronOS.Modules.Nutrition.Tests;

public class ShelfLifeEstimatorTests
{
    private const string SampleJson = """
        [
          { "Keyword": "chicken breast", "FridgeDays": 2, "FreezerDays": 270, "PantryDays": 0 },
          { "Keyword": "chicken", "FridgeDays": 2, "FreezerDays": 270, "PantryDays": 0 },
          { "Keyword": "rice, cooked", "FridgeDays": 6, "FreezerDays": 180, "PantryDays": 0 },
          { "Keyword": "rice", "FridgeDays": 0, "FreezerDays": 0, "PantryDays": 730 }
        ]
        """;

    [Fact]
    public void FindMatch_ReturnsFirstMatchingKeyword_CaseInsensitive()
    {
        var estimator = new ShelfLifeEstimator(SampleJson);

        var match = estimator.FindMatch("Boneless Chicken Breast, raw");

        Assert.NotNull(match);
        Assert.Equal("chicken breast", match!.Keyword);
    }

    [Fact]
    public void FindMatch_PrefersMoreSpecificEarlierEntry_OverGenericLaterOne()
    {
        var estimator = new ShelfLifeEstimator(SampleJson);

        var match = estimator.FindMatch("White Rice, cooked");

        Assert.Equal("rice, cooked", match!.Keyword);
    }

    [Fact]
    public void FindMatch_ReturnsNull_WhenNothingMatches()
    {
        var estimator = new ShelfLifeEstimator(SampleJson);

        Assert.Null(estimator.FindMatch("Dragon Fruit"));
    }

    [Fact]
    public void EstimateExpiration_AddsCorrectDaysForStorageLocation()
    {
        var estimator = new ShelfLifeEstimator(SampleJson);
        var acquired = new DateOnly(2026, 7, 1);

        var fridgeEstimate = estimator.EstimateExpiration("Chicken Breast", StorageLocation.Fridge, acquired);
        var freezerEstimate = estimator.EstimateExpiration("Chicken Breast", StorageLocation.Freezer, acquired);

        Assert.Equal(new DateOnly(2026, 7, 3), fridgeEstimate);
        Assert.Equal(acquired.AddDays(270), freezerEstimate);
    }

    [Fact]
    public void EstimateExpiration_ReturnsNull_WhenNoKeywordMatches()
    {
        var estimator = new ShelfLifeEstimator(SampleJson);

        var estimate = estimator.EstimateExpiration("Dragon Fruit", StorageLocation.Fridge, new DateOnly(2026, 7, 1));

        Assert.Null(estimate);
    }
}

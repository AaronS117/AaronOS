using AaronOS.Modules.Nutrition.Calculations;
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Tests;

public class RecipeCompatibilityCheckerTests
{
    private static RecipeIngredient Line(Ingredient ingredient, string? formUsed = null) =>
        new() { Ingredient = ingredient, FormUsed = formUsed };

    [Fact]
    public void FlagsDislikedIngredient_AsBlocked()
    {
        var truffle = new Ingredient { Name = "Truffle", Rating = Rating.Dislike };
        var lines = new List<RecipeIngredient> { Line(truffle) };

        var concerns = RecipeCompatibilityChecker.CheckRecipe(lines);

        Assert.Single(concerns);
        Assert.Equal(CompatibilityLevel.Blocked, concerns[0].Level);
        Assert.Contains("Truffle", concerns[0].Message);
    }

    [Fact]
    public void FlagsUnratedIngredient_SharingTagWithDislikedIngredient_AsCaution()
    {
        var fungiTag = new Tag { Name = "fungi" };
        var mushroom = new Ingredient { Name = "Mushroom", Rating = Rating.Dislike, Tags = [fungiTag] };
        var truffle = new Ingredient { Name = "Truffle", Rating = null, Tags = [fungiTag] };
        var lines = new List<RecipeIngredient> { Line(mushroom), Line(truffle) };

        var concerns = RecipeCompatibilityChecker.CheckRecipe(lines);

        Assert.Contains(concerns, c => c.Level == CompatibilityLevel.Caution && c.Message.Contains("Truffle"));
    }

    [Fact]
    public void DoesNotFlag_UnratedIngredientWithNoSharedTags()
    {
        var mushroom = new Ingredient { Name = "Mushroom", Rating = Rating.Dislike, Tags = [new Tag { Name = "fungi" }] };
        var carrot = new Ingredient { Name = "Carrot", Rating = null, Tags = [new Tag { Name = "root-vegetable" }] };
        var lines = new List<RecipeIngredient> { Line(mushroom), Line(carrot) };

        var concerns = RecipeCompatibilityChecker.CheckRecipe(lines);

        Assert.DoesNotContain(concerns, c => c.Message.Contains("Carrot"));
    }

    [Fact]
    public void FlagsFormMismatch_AsCaution()
    {
        var chicken = new Ingredient { Name = "Chicken", PreferredForm = "fresh" };
        var lines = new List<RecipeIngredient> { Line(chicken, formUsed: "canned") };

        var concerns = RecipeCompatibilityChecker.CheckRecipe(lines);

        Assert.Contains(concerns, c => c.Level == CompatibilityLevel.Caution && c.Message.Contains("fresh") && c.Message.Contains("canned"));
    }

    [Fact]
    public void DoesNotFlagFormMismatch_WhenFormsMatch()
    {
        var chicken = new Ingredient { Name = "Chicken", PreferredForm = "fresh" };
        var lines = new List<RecipeIngredient> { Line(chicken, formUsed: "fresh") };

        var concerns = RecipeCompatibilityChecker.CheckRecipe(lines);

        Assert.Empty(concerns);
    }
}

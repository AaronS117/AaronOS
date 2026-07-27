using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Nutrition.Calculations;
using AaronOS.Modules.Nutrition.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Nutrition.ViewModels;

public partial class RecipeEditViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    private int? _recipeId;

    public ObservableCollection<RecipeIngredient> Lines { get; } = [];
    public ObservableCollection<Ingredient> AvailableIngredients { get; } = [];
    public ObservableCollection<CompatibilityConcern> Concerns { get; } = [];

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _instructions = "";

    [ObservableProperty]
    private double _servings = 1;

    [ObservableProperty]
    private Ingredient? _newLineIngredient;

    [ObservableProperty]
    private double _newLineQuantityGrams = double.NaN;

    [ObservableProperty]
    private string _newLineDisplayAmount = "";

    [ObservableProperty]
    private string _newLineFormUsed = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private RecipeNutritionTotals? _perServingTotals;

    [ObservableProperty]
    private bool _hasLines;

    [ObservableProperty]
    private bool _hasConcerns;

    [ObservableProperty]
    private string _servingsSummary = "";

    public void SetRecipeId(int? recipeId) => _recipeId = recipeId;

    partial void OnServingsChanged(double value) => Recalculate();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var ingredients = await db.Set<Ingredient>().OrderBy(i => i.Name).ToListAsync();
            AvailableIngredients.Clear();
            foreach (var ingredient in ingredients)
            {
                AvailableIngredients.Add(ingredient);
            }

            Lines.Clear();
            if (_recipeId is { } id)
            {
                var recipe = await db.Set<Recipe>()
                    .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient).ThenInclude(i => i!.Tags)
                    .FirstAsync(r => r.Id == id);

                Name = recipe.Name;
                Instructions = recipe.Instructions ?? "";
                Servings = recipe.Servings;
                foreach (var line in recipe.Ingredients)
                {
                    Lines.Add(line);
                }
            }

            Recalculate();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Recalculate()
    {
        PerServingTotals = Lines.Count == 0
            ? null
            : RecipeNutritionCalculator.CalculatePerServing(Lines, Math.Max((int)Servings, 1));

        Concerns.Clear();
        foreach (var concern in RecipeCompatibilityChecker.CheckRecipe(Lines))
        {
            Concerns.Add(concern);
        }

        HasLines = Lines.Count > 0;
        HasConcerns = Concerns.Count > 0;

        var servings = Math.Max((int)Servings, 1);
        var totalGrams = Lines.Sum(l => l.QuantityGrams);
        ServingsSummary = Lines.Count == 0
            ? ""
            : $"{totalGrams:0}g total over {servings} {(servings == 1 ? "serving" : "servings")}";
    }

    [RelayCommand]
    private void AddLine()
    {
        if (NewLineIngredient is null || double.IsNaN(NewLineQuantityGrams) || NewLineQuantityGrams <= 0)
        {
            return;
        }

        Lines.Add(new RecipeIngredient
        {
            Ingredient = NewLineIngredient,
            IngredientId = NewLineIngredient.Id,
            QuantityGrams = (decimal)NewLineQuantityGrams,
            DisplayAmount = string.IsNullOrWhiteSpace(NewLineDisplayAmount) ? null : NewLineDisplayAmount,
            FormUsed = string.IsNullOrWhiteSpace(NewLineFormUsed) ? null : NewLineFormUsed
        });

        NewLineIngredient = null;
        NewLineQuantityGrams = double.NaN;
        NewLineDisplayAmount = "";
        NewLineFormUsed = "";
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(RecipeIngredient line)
    {
        Lines.Remove(line);
        Recalculate();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || Lines.Count == 0)
        {
            StatusMessage = "Give the recipe a name and at least one ingredient.";
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            Recipe recipe;
            if (_recipeId is { } id)
            {
                recipe = await db.Set<Recipe>().Include(r => r.Ingredients).FirstAsync(r => r.Id == id);
                db.Set<RecipeIngredient>().RemoveRange(recipe.Ingredients);
                recipe.Ingredients.Clear();
            }
            else
            {
                recipe = new Recipe { Name = Name };
                db.Add(recipe);
            }

            recipe.Name = Name;
            recipe.Instructions = string.IsNullOrWhiteSpace(Instructions) ? null : Instructions;
            recipe.Servings = Math.Max((int)Servings, 1);

            foreach (var line in Lines)
            {
                recipe.Ingredients.Add(new RecipeIngredient
                {
                    IngredientId = line.IngredientId,
                    QuantityGrams = line.QuantityGrams,
                    DisplayAmount = line.DisplayAmount,
                    FormUsed = line.FormUsed
                });
            }

            await db.SaveChangesAsync();
            _recipeId = recipe.Id;
            StatusMessage = "Recipe saved.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

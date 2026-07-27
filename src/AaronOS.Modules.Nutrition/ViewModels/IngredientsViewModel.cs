using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.Usda;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Nutrition.ViewModels;

/// <summary>
/// A labelled rating choice. Binding a ComboBox straight to Rating? renders the unrated case as a
/// blank row, so the option carries its own wording and the ComboBox binds SelectedValue.
/// </summary>
public record RatingOption(string Label, Rating? Value);

public partial class IngredientsViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    UsdaApiClient usdaApiClient) : ViewModelBase
{
    private List<Ingredient> _allIngredients = [];

    public ObservableCollection<Ingredient> VisibleIngredients { get; } = [];
    public ObservableCollection<UsdaSearchResult> SearchResults { get; } = [];

    public IReadOnlyList<RatingOption> RatingOptions { get; } =
    [
        new("Unrated", null),
        new("Like", Rating.Like),
        new("Neutral", Rating.Neutral),
        new("Dislike", Rating.Dislike)
    ];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private Ingredient? _selectedIngredient;

    [ObservableProperty]
    private string _tagsText = "";

    [ObservableProperty]
    private Rating? _editRating;

    [ObservableProperty]
    private string _editPreferredForm = "";

    [ObservableProperty]
    private double _editCalories = double.NaN;

    [ObservableProperty]
    private double _editProtein = double.NaN;

    [ObservableProperty]
    private double _editFat = double.NaN;

    [ObservableProperty]
    private double _editCarbs = double.NaN;

    [ObservableProperty]
    private double _editFiber = double.NaN;

    [ObservableProperty]
    private double _editSodium = double.NaN;

    [ObservableProperty]
    private double _editCost = double.NaN;

    [ObservableProperty]
    private string _newIngredientName = "";

    [ObservableProperty]
    private string _usdaSearchQuery = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _hasSearchResults;

    [ObservableProperty]
    private int _catalogCount;

    [ObservableProperty]
    private string _catalogSummary = "";

    private static double ToDouble(decimal? value) => value is null ? double.NaN : (double)value.Value;
    private static decimal? ToDecimal(double value) => double.IsNaN(value) ? null : (decimal)value;

    partial void OnFilterTextChanged(string value) => RefreshVisible();

    partial void OnSelectedIngredientChanged(Ingredient? value)
    {
        HasSelection = value is not null;
        TagsText = value is null ? "" : string.Join(", ", value.Tags.Select(t => t.Name));
        EditRating = value?.Rating;
        EditPreferredForm = value?.PreferredForm ?? "";
        EditCalories = ToDouble(value?.CaloriesPer100g);
        EditProtein = ToDouble(value?.ProteinPer100g);
        EditFat = ToDouble(value?.FatPer100g);
        EditCarbs = ToDouble(value?.CarbsPer100g);
        EditFiber = ToDouble(value?.FiberPer100g);
        EditSodium = ToDouble(value?.SodiumMgPer100g);
        EditCost = ToDouble(value?.CostPer100g);
    }

    private void RefreshVisible()
    {
        var matches = string.IsNullOrWhiteSpace(FilterText)
            ? _allIngredients
            : _allIngredients.Where(i => i.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

        VisibleIngredients.Clear();
        foreach (var ingredient in matches.OrderBy(i => i.Name))
        {
            VisibleIngredients.Add(ingredient);
        }

        CatalogCount = _allIngredients.Count;
        var rated = _allIngredients.Count(i => i.Rating is not null);
        CatalogSummary = VisibleIngredients.Count == CatalogCount
            ? $"{CatalogCount} ingredients · {rated} rated"
            : $"{VisibleIngredients.Count} of {CatalogCount} shown · {rated} rated";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            _allIngredients = await db.Set<Ingredient>().Include(i => i.Tags).ToListAsync();
            RefreshVisible();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SearchUsdaAsync()
    {
        if (string.IsNullOrWhiteSpace(UsdaSearchQuery))
        {
            return;
        }

        IsBusy = true;
        try
        {
            SearchResults.Clear();
            var results = await usdaApiClient.SearchAsync(UsdaSearchQuery);
            foreach (var result in results)
            {
                SearchResults.Add(result);
            }
            HasSearchResults = SearchResults.Count > 0;
            StatusMessage = results.Count == 0 ? "No USDA matches found." : "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"USDA search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddFromUsdaAsync(UsdaSearchResult result)
    {
        IsBusy = true;
        try
        {
            var facts = await usdaApiClient.GetFactsAsync(result.FdcId);
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var ingredient = new Ingredient
            {
                Name = facts.Name,
                FdcId = facts.FdcId,
                CaloriesPer100g = facts.CaloriesPer100g,
                ProteinPer100g = facts.ProteinPer100g,
                FatPer100g = facts.FatPer100g,
                CarbsPer100g = facts.CarbsPer100g,
                FiberPer100g = facts.FiberPer100g,
                SodiumMgPer100g = facts.SodiumMgPer100g
            };
            db.Add(ingredient);
            await db.SaveChangesAsync();

            _allIngredients.Add(ingredient);
            RefreshVisible();
            SelectedIngredient = ingredient;
            SearchResults.Clear();
            HasSearchResults = false;
            StatusMessage = $"Added {ingredient.Name} from USDA.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't add ingredient: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddManuallyAsync()
    {
        if (string.IsNullOrWhiteSpace(NewIngredientName))
        {
            return;
        }

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var ingredient = new Ingredient { Name = NewIngredientName };
            db.Add(ingredient);
            await db.SaveChangesAsync();

            _allIngredients.Add(ingredient);
            RefreshVisible();
            SelectedIngredient = ingredient;
            NewIngredientName = "";
        }
        catch (Exception ex)
        {
            // Ingredient.Name has a unique index — a duplicate lands here rather than crashing.
            StatusMessage = $"Couldn't add ingredient: {ex.InnerException?.Message ?? ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSelectedAsync()
    {
        if (SelectedIngredient is null)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();

        var tagNames = TagsText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existingTags = await db.Set<Tag>().Where(t => tagNames.Contains(t.Name)).ToListAsync();
        var newTags = tagNames
            .Where(name => existingTags.All(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Select(name => new Tag { Name = name })
            .ToList();

        var tracked = await db.Set<Ingredient>()
            .Include(i => i.Tags)
            .FirstAsync(i => i.Id == SelectedIngredient.Id);

        tracked.Rating = EditRating;
        tracked.PreferredForm = string.IsNullOrWhiteSpace(EditPreferredForm) ? null : EditPreferredForm;
        tracked.CaloriesPer100g = ToDecimal(EditCalories);
        tracked.ProteinPer100g = ToDecimal(EditProtein);
        tracked.FatPer100g = ToDecimal(EditFat);
        tracked.CarbsPer100g = ToDecimal(EditCarbs);
        tracked.FiberPer100g = ToDecimal(EditFiber);
        tracked.SodiumMgPer100g = ToDecimal(EditSodium);
        tracked.CostPer100g = ToDecimal(EditCost);
        tracked.Tags.Clear();
        foreach (var tag in existingTags.Concat(newTags))
        {
            tracked.Tags.Add(tag);
        }

        await db.SaveChangesAsync();

        var index = _allIngredients.FindIndex(i => i.Id == tracked.Id);
        if (index >= 0)
        {
            _allIngredients[index] = tracked;
        }
        RefreshVisible();
        SelectedIngredient = tracked;
        StatusMessage = $"Saved {tracked.Name}.";
    }
}

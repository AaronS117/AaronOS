using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.ShelfLife;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Nutrition.ViewModels;

public partial class InventoryViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    ShelfLifeEstimator shelfLifeEstimator) : ViewModelBase
{
    public ObservableCollection<InventoryItem> Items { get; } = [];
    public ObservableCollection<Ingredient> AvailableIngredients { get; } = [];
    public IReadOnlyList<StorageLocation> StorageLocations { get; } = Enum.GetValues<StorageLocation>();

    [ObservableProperty]
    private Ingredient? _newIngredient;

    [ObservableProperty]
    private StorageLocation _newStorageLocation = StorageLocation.Fridge;

    [ObservableProperty]
    private DateTime? _newDateAcquired = DateTime.Now;

    [ObservableProperty]
    private DateTime? _newExpiresOn;

    [ObservableProperty]
    private string _newQuantityLabel = "";

    [ObservableProperty]
    private string _statusMessage = "";

    partial void OnNewIngredientChanged(Ingredient? value) => RefreshSuggestedExpiration();
    partial void OnNewStorageLocationChanged(StorageLocation value) => RefreshSuggestedExpiration();
    partial void OnNewDateAcquiredChanged(DateTime? value) => RefreshSuggestedExpiration();

    private void RefreshSuggestedExpiration()
    {
        if (NewIngredient is null || NewDateAcquired is null)
        {
            return;
        }

        var estimate = shelfLifeEstimator.EstimateExpiration(
            NewIngredient.Name, NewStorageLocation, DateOnly.FromDateTime(NewDateAcquired.Value));
        if (estimate is { } date)
        {
            NewExpiresOn = date.ToDateTime(TimeOnly.MinValue);
        }
    }

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

            var items = await db.Set<InventoryItem>()
                .Include(i => i.Ingredient)
                .OrderBy(i => i.ExpiresOn)
                .ToListAsync();

            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (NewIngredient is null || NewDateAcquired is null)
        {
            StatusMessage = "Pick an ingredient and a date acquired.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var item = new InventoryItem
        {
            IngredientId = NewIngredient.Id,
            StorageLocation = NewStorageLocation,
            DateAcquired = DateOnly.FromDateTime(NewDateAcquired.Value),
            ExpiresOn = NewExpiresOn is { } exp ? DateOnly.FromDateTime(exp) : null,
            QuantityLabel = string.IsNullOrWhiteSpace(NewQuantityLabel) ? null : NewQuantityLabel
        };
        db.Add(item);
        await db.SaveChangesAsync();

        item.Ingredient = NewIngredient;
        Items.Add(item);

        NewIngredient = null;
        NewExpiresOn = null;
        NewQuantityLabel = "";
        StatusMessage = "Added to inventory.";
    }

    [RelayCommand]
    private async Task DeleteAsync(InventoryItem item)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(item);
        await db.SaveChangesAsync();
        Items.Remove(item);
    }
}

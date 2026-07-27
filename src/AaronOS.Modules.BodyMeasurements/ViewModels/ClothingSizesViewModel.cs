using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.BodyMeasurements.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.BodyMeasurements.ViewModels;

public partial class ClothingSizesViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<ClothingSizeEntry> Entries { get; } = [];
    public IReadOnlyList<ClothingCategory> Categories { get; } = Enum.GetValues<ClothingCategory>();

    [ObservableProperty]
    private ClothingCategory _newCategory = ClothingCategory.Shirt;

    [ObservableProperty]
    private string _newSizeLabel = "";

    [ObservableProperty]
    private string _newBrand = "";

    [ObservableProperty]
    private string _newNotes = "";

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var entries = await db.Set<ClothingSizeEntry>()
                .OrderByDescending(e => e.Date)
                .ToListAsync();

            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
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
        if (string.IsNullOrWhiteSpace(NewSizeLabel))
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var entry = new ClothingSizeEntry
        {
            Date = DateOnly.FromDateTime(DateTime.Now),
            Category = NewCategory,
            SizeLabel = NewSizeLabel,
            Brand = string.IsNullOrWhiteSpace(NewBrand) ? null : NewBrand,
            Notes = string.IsNullOrWhiteSpace(NewNotes) ? null : NewNotes
        };
        db.Add(entry);
        await db.SaveChangesAsync();

        Entries.Insert(0, entry);
        NewSizeLabel = "";
        NewBrand = "";
        NewNotes = "";
    }

    [RelayCommand]
    private async Task DeleteAsync(ClothingSizeEntry entry)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(entry);
        await db.SaveChangesAsync();
        Entries.Remove(entry);
    }
}

using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.BodyMeasurements.Data;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.BodyMeasurements.ViewModels;

public partial class HistoryViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<BodyCheckIn> CheckIns { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var checkIns = await db.Set<BodyCheckIn>()
                .OrderByDescending(c => c.Date)
                .ToListAsync();

            CheckIns.Clear();
            foreach (var checkIn in checkIns)
            {
                CheckIns.Add(checkIn);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(BodyCheckIn checkIn)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(checkIn);
        await db.SaveChangesAsync();
        CheckIns.Remove(checkIn);
    }
}

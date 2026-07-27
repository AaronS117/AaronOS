using AaronOS.Core;
using AaronOS.Core.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS_App.ViewModels;

public partial class SettingsViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    [ObservableProperty]
    private double _heightInches = double.NaN;

    [ObservableProperty]
    private string _statusMessage = "";

    [RelayCommand]
    private async Task LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var profile = await db.UserProfiles.FirstOrDefaultAsync();
        HeightInches = profile is null ? double.NaN : (double)profile.HeightInches;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (double.IsNaN(HeightInches))
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var profile = await db.UserProfiles.FirstOrDefaultAsync();
        if (profile is null)
        {
            profile = new UserProfile();
            db.UserProfiles.Add(profile);
        }

        profile.HeightInches = (decimal)HeightInches;
        await db.SaveChangesAsync();
        StatusMessage = "Saved.";
    }
}

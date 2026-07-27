using AaronOS.Core;
using AaronOS.Core.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS_App.ViewModels;

public partial class SettingsViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    // Height is entered as feet plus inches, not as one inch count. A field labelled "inches" was given
    // 6, meaning six feet, and stored six inches. Every measurement was then compared against a
    // six-inch body, so the 3D figure inflated to its limits and BMI was meaningless. Two fields make
    // that mistake impossible to express.
    [ObservableProperty]
    private double _heightFeet = double.NaN;

    [ObservableProperty]
    private double _heightInchesPart = double.NaN;

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>The value actually stored, and what BMI and the figure are built from.</summary>
    public double TotalHeightInches =>
        (double.IsNaN(HeightFeet) ? 0 : HeightFeet * 12) +
        (double.IsNaN(HeightInchesPart) ? 0 : HeightInchesPart);

    [RelayCommand]
    private async Task LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var profile = await db.UserProfiles.FirstOrDefaultAsync();

        if (profile is null)
        {
            return;
        }

        if (!BodyMetrics.IsPlausibleHeight(profile.HeightInches))
        {
            // Left blank rather than echoed back: a stored 6 would show as 0 ft 6 in, which reads like
            // a real setting. Saying it needs re-entering is more useful than displaying bad data.
            StatusMessage =
                $"The saved height ({profile.HeightInches:N0} in) is not a possible height — please enter it again.";
            return;
        }

        var (feet, inches) = BodyMetrics.ToFeetAndInches(profile.HeightInches);
        HeightFeet = feet;
        HeightInchesPart = (double)inches;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var total = (decimal)TotalHeightInches;

        if (!BodyMetrics.IsPlausibleHeight(total))
        {
            StatusMessage = $"{total:N0} in is not a possible height. Enter feet and inches — for example 5 ft 11 in.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var profile = await db.UserProfiles.FirstOrDefaultAsync();
        if (profile is null)
        {
            profile = new UserProfile();
            db.UserProfiles.Add(profile);
        }

        profile.HeightInches = total;
        await db.SaveChangesAsync();

        var (feet, inches) = BodyMetrics.ToFeetAndInches(total);
        StatusMessage = $"Saved — {feet} ft {inches:N0} in ({total:N0} in total).";
    }
}

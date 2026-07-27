using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.BodyMeasurements.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.BodyMeasurements.ViewModels;

public partial class CheckInViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    // ponytail: NumberBox reports an empty/cleared field as NaN, not null — using that directly
    // as the "not measured" sentinel avoids a value converter for every field.
    [ObservableProperty]
    private DateTimeOffset _date = DateTimeOffset.Now;

    [ObservableProperty]
    private double _weightLb = double.NaN;

    [ObservableProperty]
    private double _neckIn = double.NaN;

    [ObservableProperty]
    private double _chestIn = double.NaN;

    [ObservableProperty]
    private double _waistIn = double.NaN;

    [ObservableProperty]
    private double _hipsIn = double.NaN;

    [ObservableProperty]
    private double _bicepLeftIn = double.NaN;

    [ObservableProperty]
    private double _bicepRightIn = double.NaN;

    [ObservableProperty]
    private double _thighLeftIn = double.NaN;

    [ObservableProperty]
    private double _thighRightIn = double.NaN;

    [ObservableProperty]
    private double _calfLeftIn = double.NaN;

    [ObservableProperty]
    private double _calfRightIn = double.NaN;

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private string _statusMessage = "";

    private static decimal? ToEntry(double value) => double.IsNaN(value) ? null : (decimal)value;

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            db.Add(new BodyCheckIn
            {
                Date = DateOnly.FromDateTime(Date.Date),
                WeightLb = ToEntry(WeightLb),
                NeckIn = ToEntry(NeckIn),
                ChestIn = ToEntry(ChestIn),
                WaistIn = ToEntry(WaistIn),
                HipsIn = ToEntry(HipsIn),
                BicepLeftIn = ToEntry(BicepLeftIn),
                BicepRightIn = ToEntry(BicepRightIn),
                ThighLeftIn = ToEntry(ThighLeftIn),
                ThighRightIn = ToEntry(ThighRightIn),
                CalfLeftIn = ToEntry(CalfLeftIn),
                CalfRightIn = ToEntry(CalfRightIn),
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes
            });
            await db.SaveChangesAsync();

            StatusMessage = "Check-in saved.";
            WeightLb = NeckIn = ChestIn = WaistIn = HipsIn = double.NaN;
            BicepLeftIn = BicepRightIn = ThighLeftIn = ThighRightIn = CalfLeftIn = CalfRightIn = double.NaN;
            Notes = "";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

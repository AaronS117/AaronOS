using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Medical.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Medical.ViewModels;

public partial class MedicationsViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<Medication> Medications { get; } = [];
    public ObservableCollection<Allergy> Allergies { get; } = [];
    public ObservableCollection<Provider> Providers { get; } = [];

    public IReadOnlyList<AllergySeverity> SeverityOptions { get; } = Enum.GetValues<AllergySeverity>();

    [ObservableProperty] private bool _hasMedications;
    [ObservableProperty] private bool _hasAllergies;
    [ObservableProperty] private string _statusMessage = "";

    // New medication
    [ObservableProperty] private string _newMedicationName = "";
    [ObservableProperty] private string _newDose = "";
    [ObservableProperty] private string _newFrequency = "";
    [ObservableProperty] private DateTime? _newStartDate = DateTime.Now;
    [ObservableProperty] private Provider? _newMedicationProvider;

    // New allergy
    [ObservableProperty] private string _newSubstance = "";
    [ObservableProperty] private string _newReaction = "";
    [ObservableProperty] private AllergySeverity _newSeverity = AllergySeverity.Unknown;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            Providers.Clear();
            foreach (var p in await db.Set<Provider>().AsNoTracking().OrderBy(p => p.Name).ToListAsync())
            {
                Providers.Add(p);
            }

            Medications.Clear();
            // Current medications first, then past ones — IsActive is computed, so ordering happens
            // in memory after materialising.
            var medications = await db.Set<Medication>().AsNoTracking()
                .Include(m => m.Provider)
                .ToListAsync();
            foreach (var m in medications.OrderByDescending(m => m.IsActive).ThenBy(m => m.Name))
            {
                Medications.Add(m);
            }

            Allergies.Clear();
            foreach (var a in await db.Set<Allergy>().AsNoTracking()
                .OrderByDescending(a => a.Severity).ThenBy(a => a.Substance).ToListAsync())
            {
                Allergies.Add(a);
            }

            HasMedications = Medications.Count > 0;
            HasAllergies = Allergies.Count > 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string? Trimmed(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [RelayCommand]
    private async Task AddMedicationAsync()
    {
        if (string.IsNullOrWhiteSpace(NewMedicationName))
        {
            StatusMessage = "Name the medication.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new Medication
        {
            Name = NewMedicationName.Trim(),
            Dose = Trimmed(NewDose),
            Frequency = Trimmed(NewFrequency),
            StartDate = NewStartDate is { } d ? DateOnly.FromDateTime(d.Date) : null,
            ProviderId = NewMedicationProvider?.Id
        });
        await db.SaveChangesAsync();

        StatusMessage = $"Added {NewMedicationName.Trim()}.";
        NewMedicationName = "";
        NewDose = "";
        NewFrequency = "";
        NewMedicationProvider = null;
        await LoadAsync();
    }

    /// <summary>Stopping a medication keeps the row and dates it, rather than deleting history.</summary>
    [RelayCommand]
    private async Task StopMedicationAsync(Medication medication)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var tracked = await db.Set<Medication>().FirstOrDefaultAsync(m => m.Id == medication.Id);
        if (tracked is null)
        {
            return;
        }

        tracked.EndDate = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        await db.SaveChangesAsync();

        StatusMessage = $"Marked {tracked.Name} as stopped.";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteMedicationAsync(Medication medication)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(medication);
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddAllergyAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSubstance))
        {
            StatusMessage = "Name the substance.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new Allergy
        {
            Substance = NewSubstance.Trim(),
            Reaction = Trimmed(NewReaction),
            Severity = NewSeverity
        });
        await db.SaveChangesAsync();

        StatusMessage = $"Added {NewSubstance.Trim()}.";
        NewSubstance = "";
        NewReaction = "";
        NewSeverity = AllergySeverity.Unknown;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAllergyAsync(Allergy allergy)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(allergy);
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}

using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Medical.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Medical.ViewModels;

public partial class HistoryViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<MedicalCondition> Conditions { get; } = [];
    public ObservableCollection<MedicalProcedure> Procedures { get; } = [];
    public ObservableCollection<Immunization> Immunizations { get; } = [];

    public IReadOnlyList<ConditionStatus> StatusOptions { get; } = Enum.GetValues<ConditionStatus>();

    [ObservableProperty] private bool _hasConditions;
    [ObservableProperty] private bool _hasProcedures;
    [ObservableProperty] private bool _hasImmunizations;
    [ObservableProperty] private string _statusMessage = "";

    // New condition
    [ObservableProperty] private string _newConditionName = "";
    [ObservableProperty] private ConditionStatus _newConditionStatus = ConditionStatus.Active;
    [ObservableProperty] private DateTime? _newConditionOnset = DateTime.Now;

    // New procedure
    [ObservableProperty] private string _newProcedureName = "";
    [ObservableProperty] private DateTime? _newProcedureDate = DateTime.Now;
    [ObservableProperty] private string _newProcedureFacility = "";

    // New immunization
    [ObservableProperty] private string _newVaccine = "";
    [ObservableProperty] private DateTime? _newVaccineDate = DateTime.Now;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            Conditions.Clear();
            foreach (var c in await db.Set<MedicalCondition>().AsNoTracking()
                .OrderBy(c => c.Status).ThenByDescending(c => c.OnsetDate).ToListAsync())
            {
                Conditions.Add(c);
            }

            Procedures.Clear();
            foreach (var p in await db.Set<MedicalProcedure>().AsNoTracking()
                .OrderByDescending(p => p.Date).ToListAsync())
            {
                Procedures.Add(p);
            }

            Immunizations.Clear();
            foreach (var i in await db.Set<Immunization>().AsNoTracking()
                .OrderByDescending(i => i.DateGiven).ToListAsync())
            {
                Immunizations.Add(i);
            }

            HasConditions = Conditions.Count > 0;
            HasProcedures = Procedures.Count > 0;
            HasImmunizations = Immunizations.Count > 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value is { } v ? DateOnly.FromDateTime(v.Date) : null;

    private static string? Trimmed(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [RelayCommand]
    private async Task AddConditionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewConditionName))
        {
            StatusMessage = "Give the condition a name.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new MedicalCondition
        {
            Name = NewConditionName.Trim(),
            Status = NewConditionStatus,
            OnsetDate = ToDateOnly(NewConditionOnset),
            ResolvedDate = NewConditionStatus == ConditionStatus.Resolved
                ? DateOnly.FromDateTime(DateTime.Now)
                : null
        });
        await db.SaveChangesAsync();

        StatusMessage = $"Added {NewConditionName.Trim()}.";
        NewConditionName = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteConditionAsync(MedicalCondition condition)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(condition);
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddProcedureAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProcedureName))
        {
            StatusMessage = "Give the procedure a name.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new MedicalProcedure
        {
            Name = NewProcedureName.Trim(),
            Date = ToDateOnly(NewProcedureDate),
            Facility = Trimmed(NewProcedureFacility)
        });
        await db.SaveChangesAsync();

        StatusMessage = $"Added {NewProcedureName.Trim()}.";
        NewProcedureName = "";
        NewProcedureFacility = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteProcedureAsync(MedicalProcedure procedure)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(procedure);
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddImmunizationAsync()
    {
        if (string.IsNullOrWhiteSpace(NewVaccine))
        {
            StatusMessage = "Name the vaccine.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new Immunization
        {
            Vaccine = NewVaccine.Trim(),
            DateGiven = ToDateOnly(NewVaccineDate)
        });
        await db.SaveChangesAsync();

        StatusMessage = $"Added {NewVaccine.Trim()}.";
        NewVaccine = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteImmunizationAsync(Immunization immunization)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(immunization);
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}

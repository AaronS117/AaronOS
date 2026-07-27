using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Medical.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Medical.ViewModels;

public partial class MedicalOverviewViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<Allergy> Allergies { get; } = [];
    public ObservableCollection<Medication> ActiveMedications { get; } = [];
    public ObservableCollection<LabResult> FlaggedLabs { get; } = [];

    [ObservableProperty]
    private int _activeConditionCount;

    [ObservableProperty]
    private int _medicationCount;

    [ObservableProperty]
    private int _flaggedLabCount;

    [ObservableProperty]
    private string _lastVisitDisplay = "No visits recorded";

    [ObservableProperty]
    private bool _hasAllergies;

    [ObservableProperty]
    private bool _hasSevereAllergy;

    [ObservableProperty]
    private bool _hasActiveMedications;

    [ObservableProperty]
    private bool _hasFlaggedLabs;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var allergies = await db.Set<Allergy>().AsNoTracking()
                .OrderByDescending(a => a.Severity)
                .ThenBy(a => a.Substance)
                .ToListAsync();
            Allergies.Clear();
            foreach (var allergy in allergies)
            {
                Allergies.Add(allergy);
            }
            HasAllergies = Allergies.Count > 0;
            HasSevereAllergy = Allergies.Any(a => a.IsSevere);

            var conditions = await db.Set<MedicalCondition>().AsNoTracking().ToListAsync();
            // IsActive is a computed property EF cannot translate, so it is applied in memory.
            ActiveConditionCount = conditions.Count(c => c.IsActive);

            var medications = await db.Set<Medication>().AsNoTracking()
                .Include(m => m.Provider)
                .OrderBy(m => m.Name)
                .ToListAsync();
            ActiveMedications.Clear();
            foreach (var medication in medications.Where(m => m.IsActive))
            {
                ActiveMedications.Add(medication);
            }
            MedicationCount = ActiveMedications.Count;
            HasActiveMedications = ActiveMedications.Count > 0;

            var labs = await db.Set<LabResult>().AsNoTracking()
                .OrderByDescending(l => l.TakenOn)
                .ToListAsync();
            FlaggedLabs.Clear();
            foreach (var lab in labs.Where(l => l.IsOutOfRange).Take(10))
            {
                FlaggedLabs.Add(lab);
            }
            FlaggedLabCount = labs.Count(l => l.IsOutOfRange);
            HasFlaggedLabs = FlaggedLabs.Count > 0;

            var lastVisit = await db.Set<MedicalVisit>().AsNoTracking()
                .Where(v => v.Date != null)
                .OrderByDescending(v => v.Date)
                .FirstOrDefaultAsync();
            LastVisitDisplay = lastVisit is null
                ? "No visits recorded"
                : $"Last visit {lastVisit.DateDisplay} · {lastVisit.TypeDisplay}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

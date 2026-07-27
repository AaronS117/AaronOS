using System.Collections.ObjectModel;
using System.IO;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Medical.Data;
using AaronOS.Modules.Medical.Import;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Medical.ViewModels;

public partial class ImportViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    private CcdaDocument? _parsed;
    private ImportPlan? _plan;

    public ObservableCollection<ImportRow> Rows { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private bool _hasFile;
    [ObservableProperty] private bool _hasParsed;
    [ObservableProperty] private bool _hasWarnings;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private int _alreadyImportedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private bool _canCommit;
    [ObservableProperty] private string _summaryText = "";

    /// <summary>Called from the page's code-behind after an OpenFileDialog.</summary>
    public void SetFile(string path)
    {
        FilePath = path;
        HasFile = !string.IsNullOrWhiteSpace(path);

        // Any previous review belongs to a different file, so clear it rather than leave a stale
        // table on screen next to a new filename.
        _parsed = null;
        _plan = null;
        Rows.Clear();
        Warnings.Clear();
        HasParsed = false;
        HasWarnings = false;
        HasError = false;
        ErrorMessage = "";
        StatusMessage = "";
        CanCommit = false;
        NewCount = AlreadyImportedCount = SkippedCount = 0;
        SummaryText = "";
    }

    [RelayCommand]
    private async Task ParseAsync()
    {
        if (!HasFile)
        {
            return;
        }

        IsBusy = true;
        HasError = false;
        ErrorMessage = "";
        try
        {
            var xml = await File.ReadAllTextAsync(FilePath);
            _parsed = CcdaParser.Parse(xml);

            var existing = await SnapshotExistingKeysAsync();
            _plan = ImportPlanner.BuildPlan(_parsed, existing);

            Rows.Clear();
            foreach (var row in _plan.Rows.OrderBy(r => r.Section).ThenBy(r => r.Description))
            {
                Rows.Add(row);
            }

            Warnings.Clear();
            foreach (var warning in _parsed.Warnings)
            {
                Warnings.Add(warning);
            }

            NewCount = _plan.NewCount;
            AlreadyImportedCount = _plan.AlreadyImportedCount;
            SkippedCount = _parsed.TotalSkipped;
            HasWarnings = Warnings.Count > 0;
            HasParsed = true;
            CanCommit = NewCount > 0;

            SummaryText = NewCount > 0
                ? $"{NewCount} new · {AlreadyImportedCount} already held · {SkippedCount} unreadable"
                : $"Nothing new to import · {AlreadyImportedCount} already held · {SkippedCount} unreadable";
        }
        catch (FormatException ex)
        {
            Fail(ex.Message);
        }
        catch (IOException ex)
        {
            Fail($"Could not read that file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Fail($"Could not read that file: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Fail(string message)
    {
        HasError = true;
        ErrorMessage = message;
        HasParsed = false;
        CanCommit = false;
        Rows.Clear();
        Warnings.Clear();
        HasWarnings = false;
        SummaryText = "";
    }

    /// <summary>
    /// Snapshots both external ids and natural keys for every record type, so the planner recognises
    /// an already-imported record whether or not the source document carried ids. The natural-key
    /// shapes come from ImportPlanner so the two sides cannot drift apart.
    /// </summary>
    private async Task<ExistingKeys> SnapshotExistingKeysAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var keys = new ExistingKeys();

        static void AddKeys(HashSet<string> set, string? externalId, string naturalKey)
        {
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                set.Add(externalId);
            }
            set.Add(naturalKey);
        }

        foreach (var c in await db.Set<MedicalCondition>().AsNoTracking()
            .Select(c => new { c.ExternalId, c.Name, c.OnsetDate }).ToListAsync())
        {
            AddKeys(keys.Conditions, c.ExternalId, ImportPlanner.NaturalKey(c.Name, c.OnsetDate));
        }

        foreach (var m in await db.Set<Medication>().AsNoTracking()
            .Select(m => new { m.ExternalId, m.Name, m.StartDate }).ToListAsync())
        {
            AddKeys(keys.Medications, m.ExternalId, ImportPlanner.NaturalKey(m.Name, m.StartDate));
        }

        foreach (var a in await db.Set<Allergy>().AsNoTracking()
            .Select(a => new { a.ExternalId, a.Substance }).ToListAsync())
        {
            AddKeys(keys.Allergies, a.ExternalId, a.Substance);
        }

        foreach (var i in await db.Set<Immunization>().AsNoTracking()
            .Select(i => new { i.ExternalId, i.Vaccine, i.DateGiven }).ToListAsync())
        {
            AddKeys(keys.Immunizations, i.ExternalId, ImportPlanner.NaturalKey(i.Vaccine, i.DateGiven));
        }

        foreach (var p in await db.Set<MedicalProcedure>().AsNoTracking()
            .Select(p => new { p.ExternalId, p.Name, p.Date }).ToListAsync())
        {
            AddKeys(keys.Procedures, p.ExternalId, ImportPlanner.NaturalKey(p.Name, p.Date));
        }

        foreach (var v in await db.Set<MedicalVisit>().AsNoTracking()
            .Select(v => new { v.ExternalId, v.Facility, v.VisitType, v.Date }).ToListAsync())
        {
            AddKeys(keys.Visits, v.ExternalId,
                ImportPlanner.NaturalKey(v.Facility ?? v.VisitType ?? "Visit", v.Date));
        }

        foreach (var l in await db.Set<LabResult>().AsNoTracking()
            .Select(l => new { l.ExternalId, l.TestName, l.TakenOn, l.Value }).ToListAsync())
        {
            AddKeys(keys.Labs, l.ExternalId,
                ImportPlanner.LabNaturalKey(l.TestName, l.TakenOn, l.Value));
        }

        return keys;
    }

    [RelayCommand]
    private async Task CommitAsync()
    {
        if (_parsed is null || _plan is null || NewCount == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            // Only the rows the review table showed as New get written, keyed exactly as the planner
            // keyed them — so what is committed can never disagree with what was reviewed.
            var newConditions = _plan.NewKeysIn("Conditions");
            foreach (var c in _parsed.Conditions.Where(c =>
                newConditions.Contains(c.ExternalId ?? ImportPlanner.NaturalKey(c.Name, c.Onset))))
            {
                db.Add(new MedicalCondition
                {
                    Name = c.Name,
                    Code = c.Code,
                    OnsetDate = c.Onset,
                    ResolvedDate = c.Resolved,
                    Status = c.IsResolved ? ConditionStatus.Resolved : ConditionStatus.Active,
                    Source = RecordSource.Imported,
                    ExternalId = c.ExternalId
                });
            }

            var newMedications = _plan.NewKeysIn("Medications");
            foreach (var m in _parsed.Medications.Where(m =>
                newMedications.Contains(m.ExternalId ?? ImportPlanner.NaturalKey(m.Name, m.Start))))
            {
                db.Add(new Medication
                {
                    Name = m.Name,
                    Dose = m.Dose,
                    Frequency = m.Frequency,
                    StartDate = m.Start,
                    EndDate = m.End,
                    Source = RecordSource.Imported,
                    ExternalId = m.ExternalId
                });
            }

            var newAllergies = _plan.NewKeysIn("Allergies");
            foreach (var a in _parsed.Allergies.Where(a =>
                newAllergies.Contains(a.ExternalId ?? a.Substance)))
            {
                db.Add(new Allergy
                {
                    Substance = a.Substance,
                    Reaction = a.Reaction,
                    Severity = MapSeverity(a.Severity),
                    Source = RecordSource.Imported,
                    ExternalId = a.ExternalId
                });
            }

            var newImmunizations = _plan.NewKeysIn("Immunizations");
            foreach (var i in _parsed.Immunizations.Where(i =>
                newImmunizations.Contains(i.ExternalId ?? ImportPlanner.NaturalKey(i.Vaccine, i.Given))))
            {
                db.Add(new Immunization
                {
                    Vaccine = i.Vaccine,
                    DateGiven = i.Given,
                    Source = RecordSource.Imported,
                    ExternalId = i.ExternalId
                });
            }

            var newProcedures = _plan.NewKeysIn("Procedures");
            foreach (var p in _parsed.Procedures.Where(p =>
                newProcedures.Contains(p.ExternalId ?? ImportPlanner.NaturalKey(p.Name, p.Date))))
            {
                db.Add(new MedicalProcedure
                {
                    Name = p.Name,
                    Date = p.Date,
                    Facility = p.Facility,
                    Source = RecordSource.Imported,
                    ExternalId = p.ExternalId
                });
            }

            var newVisits = _plan.NewKeysIn("Visits");
            foreach (var v in _parsed.Visits.Where(v => newVisits.Contains(
                v.ExternalId ?? ImportPlanner.NaturalKey(v.Facility ?? v.VisitType ?? "Visit", v.Date))))
            {
                db.Add(new MedicalVisit
                {
                    Date = v.Date,
                    VisitType = v.VisitType,
                    Facility = v.Facility,
                    Reason = v.Reason,
                    Source = RecordSource.Imported,
                    ExternalId = v.ExternalId
                });
            }

            var newLabs = _plan.NewKeysIn("Labs");
            foreach (var l in _parsed.Labs.Where(l =>
                newLabs.Contains(l.ExternalId ?? ImportPlanner.LabNaturalKey(l))))
            {
                db.Add(new LabResult
                {
                    TestName = l.TestName,
                    Value = l.Value,
                    ValueText = l.ValueText,
                    Unit = l.Unit,
                    ReferenceLow = l.Low,
                    ReferenceHigh = l.High,
                    TakenOn = l.TakenOn,
                    Source = RecordSource.Imported,
                    ExternalId = l.ExternalId
                });
            }

            var written = await db.SaveChangesAsync();

            // Re-plan against the database we just wrote. Everything should now read as already held,
            // which both proves idempotency to the user and stops the screen inviting a second import.
            // ParseAsync overwrites the counts, so the message is composed after it returns.
            await ParseAsync();
            StatusMessage = $"Imported {written} records.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static AllergySeverity MapSeverity(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "mild" => AllergySeverity.Mild,
        "moderate" => AllergySeverity.Moderate,
        "severe" => AllergySeverity.Severe,
        _ => AllergySeverity.Unknown
    };
}

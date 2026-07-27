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

    private string[] _paths = [];

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private bool _hasFile;
    [ObservableProperty] private string _sourceSummary = "";
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

    /// <summary>Called from the page's code-behind after an OpenFileDialog. Accepts several files
    /// because a record spread across health systems means one export per system.</summary>
    public void SetFiles(params string[] paths)
    {
        _paths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        HasFile = _paths.Length > 0;
        FilePath = _paths.Length switch
        {
            0 => "",
            1 => _paths[0],
            _ => $"{_paths.Length} files selected"
        };
        SourceSummary = "";

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
            // A MyChart download is a zip of many documents, so each selected file can contribute
            // several. Everything is parsed together and reviewed as one import.
            var documents = new List<string>();
            foreach (var path in _paths)
            {
                documents.AddRange(await Task.Run(() => CcdaPackage.ReadDocuments(path)));
            }

            _parsed = CcdaParser.ParseMany(documents);
            SourceSummary = _paths.Length == 1
                ? $"{_parsed.DocumentCount} document(s) read from 1 file"
                : $"{_parsed.DocumentCount} document(s) read from {_paths.Length} files";

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

            var absences = _parsed.TotalAbsenceStatements;
            var absenceNote = absences > 0 ? $" · {absences} \"none recorded\" statements ignored" : "";
            SummaryText = NewCount > 0
                ? $"{NewCount} new · {AlreadyImportedCount} already held · {SkippedCount} unreadable{absenceNote}"
                : $"Nothing new to import · {AlreadyImportedCount} already held · {SkippedCount} unreadable{absenceNote}";
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
            //
            // "Written" tracks keys already committed in this pass. Several parsed records collapse to
            // one review row, so without it every copy would be written and the database would end up
            // with far more rows than the review promised.
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool Take(string section, string key) =>
                _plan.NewKeysIn(section).Contains(key) && written.Add($"{section}|{key}");
            foreach (var c in _parsed.Conditions.Where(c =>
                Take("Conditions", ImportPlanner.NaturalKey(c.Name, c.Onset))))
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

            foreach (var m in _parsed.Medications.Where(m =>
                Take("Medications", ImportPlanner.NaturalKey(m.Name, m.Start))))
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

            foreach (var a in _parsed.Allergies.Where(a => Take("Allergies", a.Substance)))
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

            foreach (var i in _parsed.Immunizations.Where(i =>
                Take("Immunizations", ImportPlanner.NaturalKey(i.Vaccine, i.Given))))
            {
                db.Add(new Immunization
                {
                    Vaccine = i.Vaccine,
                    DateGiven = i.Given,
                    Source = RecordSource.Imported,
                    ExternalId = i.ExternalId
                });
            }

            foreach (var p in _parsed.Procedures.Where(p =>
                Take("Procedures", ImportPlanner.NaturalKey(p.Name, p.Date))))
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

            foreach (var v in _parsed.Visits.Where(v => Take(
                "Visits", ImportPlanner.NaturalKey(v.Facility ?? v.VisitType ?? "Visit", v.Date))))
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

            foreach (var l in _parsed.Labs.Where(l =>
                Take("Labs", ImportPlanner.LabNaturalKey(l))))
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

            var rowsWritten = await db.SaveChangesAsync();

            // Re-plan against the database we just wrote. Everything should now read as already held,
            // which both proves idempotency to the user and stops the screen inviting a second import.
            // ParseAsync overwrites the counts, so the message is composed after it returns.
            await ParseAsync();
            StatusMessage = $"Imported {rowsWritten} records.";
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

namespace AaronOS.Modules.Medical.Import;

public enum ImportStatus { New, AlreadyImported }

/// <summary>One line of the review table: what would be imported, and whether it is already held.</summary>
public record ImportRow(string Section, string Description, string Key, ImportStatus Status)
{
    /// <summary>Wording for the review table — the raw enum name reads like code on screen.</summary>
    public string StatusDisplay => Status == ImportStatus.New ? "New" : "Already held";
}

/// <summary>
/// A snapshot of the keys already in the database, per record type. Passed in by the ViewModel so the
/// planner stays free of EF and therefore unit-testable. Each set holds both external ids and natural
/// keys — the planner does not care which kind it matched.
/// </summary>
public record ExistingKeys
{
    public HashSet<string> Conditions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Medications { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Allergies { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Immunizations { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Procedures { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Visits { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Labs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public record ImportPlan(List<ImportRow> Rows)
{
    public int NewCount => Rows.Count(r => r.Status == ImportStatus.New);
    public int AlreadyImportedCount => Rows.Count(r => r.Status == ImportStatus.AlreadyImported);
    public IEnumerable<IGrouping<string, ImportRow>> BySection => Rows.GroupBy(r => r.Section);

    /// <summary>Keys of the rows that would actually be written, so the commit step and the review
    /// table can never disagree about what "new" meant.</summary>
    public HashSet<string> NewKeysIn(string section) =>
        Rows.Where(r => r.Section == section && r.Status == ImportStatus.New)
            .Select(r => r.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Classifies parsed records against what the database already holds, so an import can be reviewed
/// before it is committed and re-importing the same document is a no-op.
///
/// Matching prefers the document's own id. When a producer omits ids, a natural key built from the
/// record's identifying fields is used instead — without that fallback every re-import would
/// duplicate the user's entire history.
/// </summary>
public static class ImportPlanner
{
    public static ImportPlan BuildPlan(CcdaDocument parsed, ExistingKeys existing)
    {
        var rows = new List<ImportRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string section, string description, string? externalId, string naturalKey, HashSet<string> known)
        {
            // Collapse on the natural key, not the source id. A record that exists at more than one
            // health system carries a *different* id from each, so keying on the id left the same
            // childhood vaccination sitting in the review table five times over. Sections are keyed
            // separately, since one id can legitimately belong to both a condition and a lab.
            if (!seen.Add($"{section}|{naturalKey}"))
            {
                return;
            }

            // Recognised by either route: the id proves it came from a document already imported, the
            // natural key catches the same real-world record arriving under a new id.
            var known2 = (externalId is not null && known.Contains(externalId)) || known.Contains(naturalKey);

            // Key is the *dedupe* key, deliberately the natural key rather than the source id. The
            // commit step matches on this, and matching on the id instead let every id-sharing copy
            // of a deduped record through — a review saying "364 new" once wrote 1,215 rows.
            rows.Add(new ImportRow(
                section,
                description,
                naturalKey,
                known2 ? ImportStatus.AlreadyImported : ImportStatus.New));
        }

        foreach (var c in parsed.Conditions)
        {
            Add("Conditions", c.Name, c.ExternalId, NaturalKey(c.Name, c.Onset), existing.Conditions);
        }

        foreach (var m in parsed.Medications)
        {
            Add("Medications", Describe(m.Name, m.Dose), m.ExternalId, NaturalKey(m.Name, m.Start), existing.Medications);
        }

        foreach (var a in parsed.Allergies)
        {
            Add("Allergies", Describe(a.Substance, a.Reaction), a.ExternalId, a.Substance, existing.Allergies);
        }

        foreach (var i in parsed.Immunizations)
        {
            Add("Immunizations", i.Vaccine, i.ExternalId, NaturalKey(i.Vaccine, i.Given), existing.Immunizations);
        }

        foreach (var p in parsed.Procedures)
        {
            Add("Procedures", p.Name, p.ExternalId, NaturalKey(p.Name, p.Date), existing.Procedures);
        }

        foreach (var v in parsed.Visits)
        {
            Add(
                "Visits",
                Describe(v.VisitType ?? "Visit", v.Facility),
                v.ExternalId,
                NaturalKey(v.Facility ?? v.VisitType ?? "Visit", v.Date),
                existing.Visits);
        }

        foreach (var l in parsed.Labs)
        {
            Add(
                "Labs",
                Describe(l.TestName, l.Value?.ToString("0.##") ?? l.ValueText),
                l.ExternalId,
                LabNaturalKey(l),
                existing.Labs);
        }

        return new ImportPlan(rows);
    }

    // Natural keys are also built by ImportViewModel when it snapshots the database, so the two must
    // agree exactly. These helpers are the single definition of that shape.

    public static string NaturalKey(string name, DateOnly? date) => $"{name}|{date:yyyy-MM-dd}";

    public static string LabNaturalKey(ParsedLab lab) =>
        $"{lab.TestName}|{lab.TakenOn:yyyy-MM-dd}|{lab.Value?.ToString("0.##")}";

    public static string LabNaturalKey(string testName, DateOnly? takenOn, decimal? value) =>
        $"{testName}|{takenOn:yyyy-MM-dd}|{value?.ToString("0.##")}";

    private static string Describe(string head, string? tail) =>
        string.IsNullOrWhiteSpace(tail) ? head : $"{head} · {tail}";
}

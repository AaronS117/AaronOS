namespace AaronOS.Modules.Medical.Import;

// Parsed records, in the shape the review table and the planner both need. Deliberately separate
// from the EF entities in Data/: nothing may touch the database until the user has seen what was
// found and confirmed it, so parsing produces plain values rather than trackable entities.

public record ParsedCondition(
    string Name, string? Code, DateOnly? Onset, DateOnly? Resolved, bool IsResolved, string? ExternalId);

public record ParsedMedication(
    string Name, string? Dose, string? Frequency, DateOnly? Start, DateOnly? End, string? ExternalId);

public record ParsedAllergy(string Substance, string? Reaction, string? Severity, string? ExternalId);

public record ParsedImmunization(string Vaccine, DateOnly? Given, string? ExternalId);

public record ParsedProcedure(string Name, DateOnly? Date, string? Facility, string? ExternalId);

public record ParsedVisit(
    DateOnly? Date, string? VisitType, string? Facility, string? Reason, string? ExternalId);

public record ParsedLab(
    string TestName, decimal? Value, string? ValueText, string? Unit,
    decimal? Low, decimal? High, DateOnly? TakenOn, string? ExternalId);

/// <summary>
/// Everything a document yielded, plus an honest account of what it did not. The skip counts and
/// warnings are as much a part of the result as the records: an import that quietly discarded a
/// third of a document would be worse than one that says so.
/// </summary>
public record CcdaDocument
{
    public List<ParsedCondition> Conditions { get; init; } = [];
    public List<ParsedMedication> Medications { get; init; } = [];
    public List<ParsedAllergy> Allergies { get; init; } = [];
    public List<ParsedImmunization> Immunizations { get; init; } = [];
    public List<ParsedProcedure> Procedures { get; init; } = [];
    public List<ParsedVisit> Visits { get; init; } = [];
    public List<ParsedLab> Labs { get; init; } = [];

    /// <summary>How many source documents contributed. A single MyChart download holds several.</summary>
    public int DocumentCount { get; set; } = 1;

    /// <summary>Entries present in the document that could not be read, keyed by section name.</summary>
    public Dictionary<string, int> SkippedBySection { get; init; } = [];

    /// <summary>"No known active allergies"-style assertions passed over, keyed by section. Tracked
    /// separately from skips because nothing failed — they are simply not records.</summary>
    public Dictionary<string, int> AbsenceStatements { get; init; } = [];

    public int TotalAbsenceStatements => AbsenceStatements.Values.Sum();

    /// <summary>Readable notes about anything unusual, surfaced on the review screen.</summary>
    public List<string> Warnings { get; init; } = [];

    public int TotalParsed => Conditions.Count + Medications.Count + Allergies.Count
        + Immunizations.Count + Procedures.Count + Visits.Count + Labs.Count;

    public int TotalSkipped => SkippedBySection.Values.Sum();
}

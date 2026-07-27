using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace AaronOS.Modules.Medical.Import;

/// <summary>
/// Reads a C-CDA R2.1 document into plain parsed records. Pure — no file I/O, no database, no
/// network — so it is fully testable against fixture documents.
///
/// Written defensively on purpose. Real exports vary in how completely they populate the standard:
/// values live in coded attributes or only in the narrative table, template IDs are sometimes
/// versioned or missing, dates arrive at several precisions, and <c>nullFlavor</c> stands in for
/// absent data. Each entry is therefore parsed inside a try/catch — one unreadable entry increments a
/// skip count and records a warning rather than costing the other few hundred good ones.
/// </summary>
public static partial class CcdaParser
{
    private static readonly XNamespace V = "urn:hl7-org:v3";

    // Section template IDs (C-CDA R2.1), each paired with the LOINC section code used as a fallback
    // because some producers omit or version the templateId.
    private const string ProblemsTemplate = "2.16.840.1.113883.10.20.22.2.5.1";
    private const string MedicationsTemplate = "2.16.840.1.113883.10.20.22.2.1.1";
    private const string AllergiesTemplate = "2.16.840.1.113883.10.20.22.2.6.1";
    private const string ImmunizationsTemplate = "2.16.840.1.113883.10.20.22.2.2.1";
    private const string ResultsTemplate = "2.16.840.1.113883.10.20.22.2.3.1";
    private const string ProceduresTemplate = "2.16.840.1.113883.10.20.22.2.7.1";
    private const string EncountersTemplate = "2.16.840.1.113883.10.20.22.2.22.1";
    private const string VitalSignsTemplate = "2.16.840.1.113883.10.20.22.2.4.1";

    /// <summary>
    /// LOINC codes for body weight and height, excluded deliberately. BodyMeasurements owns those
    /// numbers, modules may not write to each other's tables, and two sources of truth for the same
    /// measurement would be worse than one.
    /// </summary>
    private static readonly HashSet<string> ExcludedVitalCodes =
        ["29463-7", "3141-9", "8350-1", "8302-2", "3137-7"];

    /// <summary>
    /// Parses several documents as one result, because a single MyChart download contains a folder of
    /// them and the user is importing "their record", not "document 4 of 8".
    ///
    /// The same records repeat heavily across documents in a real export — 1,302 parsed records
    /// collapsed to 378 unique ones in testing — so de-duplication is left to ImportPlanner, which
    /// already keys on the source id with a natural-key fallback. Merging here would duplicate that
    /// logic in a second place.
    ///
    /// One unreadable document does not sink the batch: it is counted and named in the warnings.
    /// </summary>
    public static CcdaDocument ParseMany(IEnumerable<string> xmlDocuments)
    {
        var merged = new CcdaDocument();
        var index = 0;
        var parsed = 0;

        foreach (var xml in xmlDocuments)
        {
            index++;
            CcdaDocument one;
            try
            {
                one = Parse(xml);
            }
            catch (FormatException ex)
            {
                Skip(merged, "Document", $"document {index} could not be read ({ex.Message})");
                continue;
            }

            parsed++;
            merged.Conditions.AddRange(one.Conditions);
            merged.Medications.AddRange(one.Medications);
            merged.Allergies.AddRange(one.Allergies);
            merged.Immunizations.AddRange(one.Immunizations);
            merged.Procedures.AddRange(one.Procedures);
            merged.Visits.AddRange(one.Visits);
            merged.Labs.AddRange(one.Labs);

            foreach (var (section, count) in one.SkippedBySection)
            {
                merged.SkippedBySection[section] = merged.SkippedBySection.GetValueOrDefault(section) + count;
            }

            foreach (var (section, count) in one.AbsenceStatements)
            {
                merged.AbsenceStatements[section] = merged.AbsenceStatements.GetValueOrDefault(section) + count;
            }

            foreach (var warning in one.Warnings.Where(w => !merged.Warnings.Contains(w)))
            {
                merged.Warnings.Add(warning);
            }
        }

        if (parsed == 0 && index > 0)
        {
            throw new FormatException("None of the documents in that file could be read as health records.");
        }

        merged.DocumentCount = parsed;
        return merged;
    }

    public static CcdaDocument Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new FormatException("That file is not valid XML.", ex);
        }

        if (doc.Root is null || doc.Root.Name != V + "ClinicalDocument")
        {
            throw new FormatException(
                "That file is not a C-CDA document. In MyChart, use Sharing → Download My Record.");
        }

        var result = new CcdaDocument();

        ParseSection(doc, ProblemsTemplate, "11450-4", "Problems", result, ParseProblem);
        ParseSection(doc, MedicationsTemplate, "10160-0", "Medications", result, ParseMedication);
        ParseSection(doc, AllergiesTemplate, "48765-2", "Allergies", result, ParseAllergy);
        ParseSection(doc, ImmunizationsTemplate, "11369-6", "Immunizations", result, ParseImmunization);
        ParseSection(doc, ResultsTemplate, "30954-2", "Results", result, ParseObservations);
        ParseSection(doc, ProceduresTemplate, "47519-4", "Procedures", result, ParseProcedure);
        ParseSection(doc, EncountersTemplate, "46240-8", "Encounters", result, ParseEncounter);
        ParseSection(doc, VitalSignsTemplate, "8716-3", "Vital signs", result, ParseObservations);

        return result;
    }

    private static void ParseSection(
        XDocument doc,
        string templateRoot,
        string loincCode,
        string label,
        CcdaDocument result,
        Action<XElement, Dictionary<string, string>, CcdaDocument> parseEntry)
    {
        // Every matching section, not just the first. One document legitimately carries more than one
        // section under the same template — a real "Active Problems" list alongside Epic's separate
        // "no known active problems" assertion, or "Encounter Details" next to "Encounters". Taking
        // only the first silently dropped every genuine diagnosis whenever the negation section
        // happened to come first.
        foreach (var section in FindSections(doc, templateRoot, loincCode))
        {
            var narrative = BuildNarrativeIndex(section);

            foreach (var entry in section.Elements(V + "entry"))
            {
                try
                {
                    parseEntry(entry, narrative, result);
                }
                catch (Exception ex)
                {
                    Skip(result, label, ex.Message);
                }
            }
        }
    }

    private static List<XElement> FindSections(XDocument doc, string templateRoot, string loincCode)
    {
        var byTemplate = doc.Descendants(V + "section")
            .Where(s => s.Elements(V + "templateId").Any(t => Attr(t, "root") == templateRoot))
            .ToList();
        if (byTemplate.Count > 0)
        {
            return byTemplate;
        }

        // Fallback on the section's own LOINC code when the templateId is missing or versioned.
        return doc.Descendants(V + "section")
            .Where(s => Attr(s.Element(V + "code"), "code") == loincCode)
            .ToList();
    }

    /// <summary>
    /// Maps narrative ids to their text. C-CDA frequently carries the human-readable value only in
    /// the section's narrative table, with the entry pointing at it via
    /// <c>&lt;text&gt;&lt;reference value="#id"/&gt;&lt;/text&gt;</c> — without resolving that, names
    /// come back empty for a large share of real documents.
    /// </summary>
    private static Dictionary<string, string> BuildNarrativeIndex(XElement section)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var text = section.Element(V + "text");
        if (text is null)
        {
            return index;
        }

        foreach (var element in text.Descendants())
        {
            // CDA narrative uses the XML-standard uppercase ID attribute.
            var id = Attr(element, "ID");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var value = Flatten(element);
            if (!string.IsNullOrWhiteSpace(value))
            {
                index[id] = value;
            }
        }

        return index;
    }

    private static string Flatten(XElement element) =>
        string.Join(
            " ",
            element.DescendantNodes().OfType<XText>()
                .Select(t => t.Value.Trim())
                .Where(t => t.Length > 0))
            .Trim();

    private static string? Narrative(Dictionary<string, string> index, XElement? owner)
    {
        var reference = owner?.Element(V + "text")?.Element(V + "reference");
        var key = Attr(reference, "value")?.TrimStart('#');

        return key is not null && index.TryGetValue(key, out var value) ? Normalize(value) : null;
    }

    // ---- per-section entry parsers -----------------------------------------------------------

    private static void ParseProblem(XElement entry, Dictionary<string, string> narrative, CcdaDocument result)
    {
        var act = entry.Element(V + "act");
        var observation = (act ?? entry).Descendants(V + "observation").FirstOrDefault()
            ?? throw new FormatException("a problem entry contained no observation");

        var value = observation.Element(V + "value");
        var name = DisplayName(value)
            ?? Narrative(narrative, observation)
            ?? throw new FormatException("a problem had no readable name");

        if (IsAbsenceStatement(name))
        {
            Absence(result, "Problems");
            return;
        }

        var effective = observation.Element(V + "effectiveTime");
        var onset = Hl7Time.ParseDate(Attr(effective?.Element(V + "low"), "value"));
        var resolved = Hl7Time.ParseDate(Attr(effective?.Element(V + "high"), "value"));

        result.Conditions.Add(new ParsedCondition(
            name,
            Attr(value, "code"),
            onset,
            resolved,
            resolved is not null,
            ExternalId(act) ?? ExternalId(observation)));
    }

    private static void ParseMedication(XElement entry, Dictionary<string, string> narrative, CcdaDocument result)
    {
        var admin = entry.Descendants(V + "substanceAdministration").FirstOrDefault()
            ?? throw new FormatException("a medication entry contained no substanceAdministration");

        var material = admin.Descendants(V + "manufacturedMaterial").FirstOrDefault();
        var name = DisplayName(material?.Element(V + "code"))
            ?? Narrative(narrative, admin)
            ?? throw new FormatException("a medication had no readable name");

        if (IsAbsenceStatement(name))
        {
            Absence(result, "Medications");
            return;
        }

        // Two effectiveTime elements are normal and mean different things: IVL_TS carries the date
        // range, PIVL_TS the dosing frequency. They are told apart by shape rather than by xsi:type,
        // since not every producer stamps the type. Reading the wrong one yields nonsense dates.
        var interval = admin.Elements(V + "effectiveTime").FirstOrDefault(
            e => e.Element(V + "low") is not null || e.Element(V + "high") is not null);
        var period = admin.Elements(V + "effectiveTime")
            .Select(e => e.Element(V + "period"))
            .FirstOrDefault(p => p is not null);

        var dose = admin.Element(V + "doseQuantity");
        var doseText = Attr(dose, "value") is { } dv
            ? $"{dv} {Attr(dose, "unit")}".Trim()
            : null;

        var frequency = Attr(period, "value") is { } pv
            ? $"every {pv} {Attr(period, "unit")}".Trim()
            : null;

        result.Medications.Add(new ParsedMedication(
            name,
            doseText,
            frequency,
            Hl7Time.ParseDate(Attr(interval?.Element(V + "low"), "value")),
            Hl7Time.ParseDate(Attr(interval?.Element(V + "high"), "value")),
            ExternalId(admin) ?? ExternalId(entry.Element(V + "act"))));
    }

    private static void ParseAllergy(XElement entry, Dictionary<string, string> narrative, CcdaDocument result)
    {
        var observation = entry.Descendants(V + "observation").FirstOrDefault()
            ?? throw new FormatException("an allergy entry contained no observation");

        var substance = DisplayName(
                observation.Descendants(V + "playingEntity").FirstOrDefault()?.Element(V + "code"))
            ?? Narrative(narrative, observation)
            ?? throw new FormatException("an allergy had no readable substance");

        if (IsAbsenceStatement(substance))
        {
            Absence(result, "Allergies");
            return;
        }

        // Reaction and severity are both nested observations; only the severity one is coded SEV.
        var nested = observation.Descendants(V + "observation").ToList();

        var severity = nested
            .Where(o => Attr(o.Element(V + "code"), "code") == "SEV")
            .Select(o => DisplayName(o.Element(V + "value")))
            .FirstOrDefault(s => s is not null);

        var reaction = nested
            .Where(o => Attr(o.Element(V + "code"), "code") != "SEV")
            .Select(o => DisplayName(o.Element(V + "value")))
            .FirstOrDefault(s => s is not null);

        result.Allergies.Add(new ParsedAllergy(
            substance, reaction, severity, ExternalId(observation) ?? ExternalId(entry.Element(V + "act"))));
    }

    private static void ParseImmunization(XElement entry, Dictionary<string, string> narrative, CcdaDocument result)
    {
        var admin = entry.Descendants(V + "substanceAdministration").FirstOrDefault()
            ?? throw new FormatException("an immunization entry contained no substanceAdministration");

        var material = admin.Descendants(V + "manufacturedMaterial").FirstOrDefault();
        var vaccine = DisplayName(material?.Element(V + "code"))
            ?? Narrative(narrative, admin)
            ?? throw new FormatException("an immunization had no readable vaccine");

        if (IsAbsenceStatement(vaccine))
        {
            Absence(result, "Immunizations");
            return;
        }

        vaccine = StripNarrativeDates(vaccine);

        var effective = admin.Element(V + "effectiveTime");
        var given = Hl7Time.ParseDate(Attr(effective, "value"))
            ?? Hl7Time.ParseDate(Attr(effective?.Element(V + "low"), "value"));

        result.Immunizations.Add(new ParsedImmunization(vaccine, given, ExternalId(admin)));
    }

    /// <summary>
    /// Handles the Results and Vital Signs sections, which share an organizer/observation shape and
    /// both land in LabResult. Individual observations that cannot be named are skipped quietly
    /// rather than failing the whole entry — a panel with one unnamed analyte should still import the
    /// rest of its analytes.
    /// </summary>
    private static void ParseObservations(XElement entry, Dictionary<string, string> narrative, CcdaDocument result)
    {
        var observations = entry.Descendants(V + "observation").ToList();
        if (observations.Count == 0)
        {
            throw new FormatException("a result entry contained no observations");
        }

        foreach (var observation in observations)
        {
            var code = observation.Element(V + "code");
            if (ExcludedVitalCodes.Contains(Attr(code, "code") ?? string.Empty))
            {
                continue; // body weight or height — see ExcludedVitalCodes
            }

            var name = DisplayName(code) ?? Narrative(narrative, observation);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var value = observation.Element(V + "value");
            var numeric = ParseDecimal(Attr(value, "value"));

            string? text = null;
            if (numeric is null)
            {
                text = DisplayName(value) ?? (value?.Value.Trim() is { Length: > 0 } v ? v : null);
            }

            var range = observation.Descendants(V + "observationRange")
                .Select(r => r.Element(V + "value"))
                .FirstOrDefault(v => v is not null);

            var effective = observation.Element(V + "effectiveTime");

            result.Labs.Add(new ParsedLab(
                name,
                numeric,
                text,
                Attr(value, "unit"),
                ParseDecimal(Attr(range?.Element(V + "low"), "value")),
                ParseDecimal(Attr(range?.Element(V + "high"), "value")),
                Hl7Time.ParseDate(Attr(effective, "value"))
                    ?? Hl7Time.ParseDate(Attr(effective?.Element(V + "low"), "value")),
                ExternalId(observation)));
        }
    }

    private static void ParseProcedure(XElement entry, Dictionary<string, string> narrative, CcdaDocument result)
    {
        // The Procedures section legitimately uses procedure, observation or act.
        var element = entry.Element(V + "procedure")
            ?? entry.Element(V + "observation")
            ?? entry.Element(V + "act")
            ?? throw new FormatException("a procedure entry was empty");

        var name = DisplayName(element.Element(V + "code"))
            ?? Narrative(narrative, element)
            ?? throw new FormatException("a procedure had no readable name");

        var effective = element.Element(V + "effectiveTime");
        var date = Hl7Time.ParseDate(Attr(effective, "value"))
            ?? Hl7Time.ParseDate(Attr(effective?.Element(V + "low"), "value"));

        result.Procedures.Add(new ParsedProcedure(name, date, FacilityName(element), ExternalId(element)));
    }

    private static void ParseEncounter(XElement entry, Dictionary<string, string> narrative, CcdaDocument result)
    {
        var encounter = entry.Element(V + "encounter")
            ?? entry.Descendants(V + "encounter").FirstOrDefault()
            ?? throw new FormatException("an encounter entry contained no encounter");

        var effective = encounter.Element(V + "effectiveTime");
        var date = Hl7Time.ParseDate(Attr(effective, "value"))
            ?? Hl7Time.ParseDate(Attr(effective?.Element(V + "low"), "value"));

        result.Visits.Add(new ParsedVisit(
            date,
            DisplayName(encounter.Element(V + "code")) ?? Narrative(narrative, encounter),
            FacilityName(encounter),
            null,
            ExternalId(encounter)));
    }

    // ---- shared helpers ----------------------------------------------------------------------

    private static string? Attr(XElement? element, string name) =>
        element?.Attribute(name)?.Value is { Length: > 0 } v ? v : null;

    /// <summary>
    /// True for the "nothing to report" assertions Epic emits as ordinary coded entries — "No known
    /// active allergies", "No known active problems", "No current medications". They are statements of
    /// absence, not records, and importing them produces an allergy list whose first row claims there
    /// are no allergies. Found in real Froedtert exports, where every one of 21 documents carried one.
    /// </summary>
    private static bool IsAbsenceStatement(string text) =>
        NegationPrefixes.Any(p => text.TrimStart().StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] NegationPrefixes =
        ["no known", "no active", "none known", "no current", "no reported", "not on file"];

    /// <summary>
    /// Trims the administration dates that get appended when a vaccine's name comes from the narrative
    /// table rather than a coded displayName. Different systems in the same export do it differently:
    /// "DTAP (Infanrix) (Given 3/4/2003, 1/24/2000, …)" from one, and a bare
    /// "DTAP (Infanrix) 03/04/2003 , 01/24/2000 , …" from another.
    ///
    /// The dates belong in the date field, and one narrative cell can list five of them, so they cannot
    /// be split into separate records reliably. Keeping the clean name is the honest reading — and it
    /// is what lets the same childhood vaccination from two health systems collapse to one row.
    /// </summary>
    private static string StripNarrativeDates(string name)
    {
        var marker = name.IndexOf("(Given", StringComparison.OrdinalIgnoreCase);
        if (marker > 0)
        {
            name = name[..marker];
        }

        var date = FirstDateInText().Match(name);
        if (date.Success && date.Index > 0)
        {
            name = name[..date.Index];
        }

        return name.TrimEnd(' ', ',', ';', '-', '(').TrimEnd();
    }

    [GeneratedRegex(@"\d{1,2}/\d{1,2}/\d{2,4}")]
    private static partial Regex FirstDateInText();

    /// <summary>
    /// Collapses whitespace runs and trims. Narrative cells arrive with newlines and padding, and
    /// without this "Influenza Vaccine, Multidose Vial" and the same value with a trailing space count
    /// as two different records for de-duplication.
    /// </summary>
    private static string Normalize(string value) => WhitespaceRuns().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();

    /// <summary>A coded element's human-readable text, respecting nullFlavor.</summary>
    private static string? DisplayName(XElement? coded)
    {
        if (coded is null || Attr(coded, "nullFlavor") is not null)
        {
            return null;
        }

        if (Attr(coded, "displayName") is { } display)
        {
            return Normalize(display);
        }

        var original = coded.Element(V + "originalText")?.Value;
        return string.IsNullOrWhiteSpace(original) ? null : Normalize(original);
    }

    private static string? ExternalId(XElement? owner)
    {
        var id = owner?.Element(V + "id");
        return id is null ? null : Attr(id, "extension") ?? Attr(id, "root");
    }

    private static string? FacilityName(XElement owner)
    {
        var named = owner.Descendants(V + "playingEntity")
            .Select(p => p.Element(V + "name")?.Value.Trim())
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        if (named is not null)
        {
            return named;
        }

        return owner.Descendants(V + "participantRole")
            .Select(p => DisplayName(p.Element(V + "code")))
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>
    /// Records that an absence statement was passed over. Counted separately from Skip: nothing failed
    /// to parse, so calling it "unreadable" would be misleading — but dropping it with no trace at all
    /// would hide a decision from the user.
    /// </summary>
    private static void Absence(CcdaDocument result, string section)
    {
        result.AbsenceStatements[section] = result.AbsenceStatements.GetValueOrDefault(section) + 1;
    }

    private static void Skip(CcdaDocument result, string section, string reason)
    {
        result.SkippedBySection[section] = result.SkippedBySection.GetValueOrDefault(section) + 1;

        var warning = $"{section}: {reason}";
        if (!result.Warnings.Contains(warning))
        {
            result.Warnings.Add(warning);
        }
    }
}

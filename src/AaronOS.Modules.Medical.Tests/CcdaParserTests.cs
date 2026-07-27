using AaronOS.Modules.Medical.Import;
using AaronOS.Modules.Medical.Tests.Fixtures;

namespace AaronOS.Modules.Medical.Tests;

public class CcdaParserTests
{
    [Fact]
    public void ParsesCondition_NamedByCodedDisplayName()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ProblemsSection));

        var hypertension = doc.Conditions.Single(c => c.ExternalId == "cond-1");
        Assert.Equal("Essential hypertension", hypertension.Name);
        Assert.Equal(new DateOnly(2020, 1, 15), hypertension.Onset);
        Assert.Null(hypertension.Resolved);
        Assert.False(hypertension.IsResolved);
    }

    [Fact]
    public void ParsesCondition_NamedOnlyByNarrativeReference_AndResolvedByItsHighDate()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ProblemsSection));

        var resolved = doc.Conditions.Single(c => c.ExternalId == "cond-2");
        Assert.Equal("Seasonal allergic rhinitis", resolved.Name); // via <reference value="#prob1"/>
        Assert.Equal(new DateOnly(2018, 3, 1), resolved.Onset);
        Assert.Equal(new DateOnly(2019, 6, 1), resolved.Resolved);
        Assert.True(resolved.IsResolved);
    }

    [Fact]
    public void FindsSection_ByLoincCode_WhenTemplateIdIsAbsent()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ProblemsSectionWithoutTemplateId));

        Assert.Equal("Diabetes mellitus", Assert.Single(doc.Conditions).Name);
    }

    [Fact]
    public void ParsesMedication_TakingDatesFromTheIntervalNotTheFrequency()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.MedicationsSection));

        var med = Assert.Single(doc.Medications);
        Assert.Equal("Lisinopril 10 MG", med.Name);
        Assert.Equal("10 mg", med.Dose);
        Assert.Equal(new DateOnly(2024, 1, 1), med.Start);
        Assert.Equal(new DateOnly(2025, 1, 1), med.End);
        Assert.Equal("med-1", med.ExternalId);
    }

    [Fact]
    public void ParsesMedication_FrequencyFromThePeriod()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.MedicationsSection));

        Assert.Contains("1", Assert.Single(doc.Medications).Frequency);
    }

    [Fact]
    public void ParsesAllergy_SubstanceReactionAndSeverity()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.AllergiesSection));

        var allergy = Assert.Single(doc.Allergies);
        Assert.Equal("Penicillin G", allergy.Substance);
        Assert.Equal("Hives", allergy.Reaction);
        Assert.Equal("Moderate", allergy.Severity);
    }

    [Fact]
    public void ParsesImmunization()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ImmunizationsSection));

        var shot = Assert.Single(doc.Immunizations);
        Assert.Equal("Influenza vaccine", shot.Vaccine);
        Assert.Equal(new DateOnly(2021, 3, 10), shot.Given);
    }

    [Fact]
    public void ParsesLabs_NumericWithRange_AndTextualWithout()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ResultsSection));

        var hb = doc.Labs.Single(l => l.TestName == "Hemoglobin");
        Assert.Equal(14.2m, hb.Value);
        Assert.Equal("g/dL", hb.Unit);
        Assert.Equal(13.5m, hb.Low);
        Assert.Equal(17.5m, hb.High);
        Assert.Equal(new DateOnly(2026, 1, 1), hb.TakenOn);

        var antigen = doc.Labs.Single(l => l.TestName == "Hepatitis B surface antigen");
        Assert.Null(antigen.Value);
        Assert.Equal("Negative", antigen.ValueText);
        Assert.Null(antigen.Low);
        Assert.Null(antigen.High);
    }

    [Fact]
    public void ParsesProcedure()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ProceduresSection));

        var procedure = Assert.Single(doc.Procedures);
        Assert.Equal("Appendectomy", procedure.Name);
        Assert.Equal(new DateOnly(2018, 4, 12), procedure.Date);
    }

    [Fact]
    public void ParsesVisit_IncludingItsFacility()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.EncountersSection));

        var visit = Assert.Single(doc.Visits);
        Assert.Equal("Office visit", visit.VisitType);
        Assert.Equal("Springfield Clinic", visit.Facility);
        Assert.Equal(new DateOnly(2026, 1, 15), visit.Date);
    }

    [Fact]
    public void VitalSigns_ImportClinicalObservationsButNotBodyWeight()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.VitalSignsSection));

        Assert.Contains(doc.Labs, l => l.TestName.Contains("Systolic", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(doc.Labs, l => l.TestName.Contains("Heart rate", StringComparison.OrdinalIgnoreCase));

        // Body weight belongs to BodyMeasurements. Modules may not write to each other's tables, and
        // two sources of truth for the same number would be worse than one.
        Assert.DoesNotContain(doc.Labs, l => l.TestName.Contains("weight", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CountsSkippedEntries_RatherThanThrowingOrDroppingSilently()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.MalformedProblemsSection));

        Assert.Empty(doc.Conditions);
        Assert.True(doc.TotalSkipped >= 1);
        Assert.NotEmpty(doc.Warnings);
        Assert.Contains("Problems", doc.SkippedBySection.Keys);
    }

    [Fact]
    public void ParsesGoodEntriesAlongsideBadOnesInTheSameDocument()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(
            CcdaFixtures.MalformedProblemsSection, CcdaFixtures.ResultsSection));

        Assert.Empty(doc.Conditions);
        Assert.Equal(2, doc.Labs.Count);   // one bad section must not cost another its records
        Assert.True(doc.TotalSkipped >= 1);
    }

    [Fact]
    public void AbsentSectionIsNotAnError()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ProblemsSection));

        Assert.Empty(doc.Labs);
        Assert.Empty(doc.Medications);
        Assert.Empty(doc.Allergies);
        Assert.Empty(doc.Visits);
    }

    [Fact]
    public void ParsesEverySectionTogether()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(
            CcdaFixtures.ProblemsSection, CcdaFixtures.MedicationsSection, CcdaFixtures.AllergiesSection,
            CcdaFixtures.ImmunizationsSection, CcdaFixtures.ResultsSection, CcdaFixtures.ProceduresSection,
            CcdaFixtures.EncountersSection, CcdaFixtures.VitalSignsSection));

        Assert.Equal(2, doc.Conditions.Count);
        Assert.Single(doc.Medications);
        Assert.Single(doc.Allergies);
        Assert.Single(doc.Immunizations);
        Assert.Single(doc.Procedures);
        Assert.Single(doc.Visits);
        Assert.Equal(4, doc.Labs.Count);  // 2 results + systolic + heart rate, weight excluded
        Assert.Equal(0, doc.TotalSkipped);
    }

    [Fact]
    public void ThrowsFormatException_OnXmlThatIsNotACcda()
    {
        var ex = Assert.Throws<FormatException>(() => CcdaParser.Parse("<html><body>nope</body></html>"));
        Assert.Contains("C-CDA", ex.Message);
    }

    [Fact]
    public void ThrowsFormatException_OnMalformedXml() =>
        Assert.Throws<FormatException>(() => CcdaParser.Parse("<not closed"));
}

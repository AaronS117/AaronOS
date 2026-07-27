using AaronOS.Modules.Medical.Data;

namespace AaronOS.Modules.Medical.Tests;

public class EntityLogicTests
{
    private static LabResult Lab(decimal? value, decimal? low, decimal? high, string? text = null) =>
        new() { TestName = "T", Value = value, ReferenceLow = low, ReferenceHigh = high, ValueText = text };

    [Fact]
    public void LabIsOutOfRange_WhenAboveHigh() => Assert.True(Lab(200, 100, 150).IsOutOfRange);

    [Fact]
    public void LabIsOutOfRange_WhenBelowLow() => Assert.True(Lab(50, 100, 150).IsOutOfRange);

    [Fact]
    public void LabInRange_WhenBetween() => Assert.False(Lab(120, 100, 150).IsOutOfRange);

    [Fact]
    public void LabInRange_OnBothBoundaries()
    {
        Assert.False(Lab(100, 100, 150).IsOutOfRange);
        Assert.False(Lab(150, 100, 150).IsOutOfRange);
    }

    [Fact]
    public void LabOneSidedRange_LowOnly()
    {
        Assert.True(Lab(90, 100, null).IsOutOfRange);
        Assert.False(Lab(110, 100, null).IsOutOfRange);
    }

    [Fact]
    public void LabOneSidedRange_HighOnly()
    {
        Assert.True(Lab(160, null, 150).IsOutOfRange);
        Assert.False(Lab(140, null, 150).IsOutOfRange);
    }

    [Fact]
    public void LabNeverOutOfRange_WithNoRange() => Assert.False(Lab(999, null, null).IsOutOfRange);

    [Fact]
    public void LabNeverOutOfRange_WithNoNumericValue() =>
        Assert.False(Lab(null, 1, 2, "Negative").IsOutOfRange);

    [Fact]
    public void LabValueDisplay_PrefersNumericThenTextThenDash()
    {
        Assert.Equal("14.2 g/dL", new LabResult { TestName = "T", Value = 14.2m, Unit = "g/dL" }.ValueDisplay);
        Assert.Equal("14.2", new LabResult { TestName = "T", Value = 14.2m }.ValueDisplay);
        Assert.Equal("Negative", new LabResult { TestName = "T", ValueText = "Negative" }.ValueDisplay);
        Assert.Equal("—", new LabResult { TestName = "T" }.ValueDisplay);
    }

    [Fact]
    public void LabRangeDisplay_CoversTwoSidedOneSidedAndNone()
    {
        Assert.Equal("100–150", Lab(1, 100, 150).RangeDisplay);
        Assert.Equal("≥ 100", Lab(1, 100, null).RangeDisplay);
        Assert.Equal("≤ 150", Lab(1, null, 150).RangeDisplay);
        Assert.Equal("—", Lab(1, null, null).RangeDisplay);
    }

    [Fact]
    public void MedicationIsActive_UntilItsEndDateHasPassed()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        Assert.True(new Medication { Name = "M" }.IsActive);                             // no end date
        Assert.True(new Medication { Name = "M", EndDate = today }.IsActive);            // ends today
        Assert.True(new Medication { Name = "M", EndDate = today.AddDays(30) }.IsActive);
        Assert.False(new Medication { Name = "M", EndDate = today.AddDays(-1) }.IsActive);
    }

    [Fact]
    public void MedicationDoseDisplay_JoinsWhatIsPresentAndDashesWhenEmpty()
    {
        Assert.Equal("10 mg · every 1 d", new Medication { Name = "M", Dose = "10 mg", Frequency = "every 1 d" }.DoseDisplay);
        Assert.Equal("10 mg", new Medication { Name = "M", Dose = "10 mg" }.DoseDisplay);
        Assert.Equal("—", new Medication { Name = "M" }.DoseDisplay);
    }

    [Fact]
    public void ConditionIsActive_UnlessResolved()
    {
        Assert.True(new MedicalCondition { Name = "C", Status = ConditionStatus.Active }.IsActive);
        Assert.True(new MedicalCondition { Name = "C", Status = ConditionStatus.Chronic }.IsActive);
        Assert.False(new MedicalCondition { Name = "C", Status = ConditionStatus.Resolved }.IsActive);
    }

    [Fact]
    public void AllergySeverityDisplay_HidesUnknownBehindADash()
    {
        Assert.Equal("—", new Allergy { Substance = "S" }.SeverityDisplay);
        Assert.Equal("Severe", new Allergy { Substance = "S", Severity = AllergySeverity.Severe }.SeverityDisplay);
        Assert.True(new Allergy { Substance = "S", Severity = AllergySeverity.Severe }.IsSevere);
    }

    [Fact]
    public void ImportedFlagFollowsSource()
    {
        Assert.False(new LabResult { TestName = "T" }.IsImported);
        Assert.True(new LabResult { TestName = "T", Source = RecordSource.Imported }.IsImported);
    }

    [Fact]
    public void DocumentReportsMissingFile()
    {
        var doc = new MedicalDocument
        {
            Title = "Scan",
            FilePath = @"C:\definitely\not\here\scan.pdf",
            AddedOn = new DateOnly(2026, 7, 27)
        };

        Assert.False(doc.FileExists);
        Assert.True(doc.IsMissing);
        Assert.Equal("File missing", doc.StatusDisplay);
    }

    [Fact]
    public void DocumentReportsPresentFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aaronos-medical-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "x");
        try
        {
            var doc = new MedicalDocument { Title = "Scan", FilePath = path, AddedOn = default };
            Assert.True(doc.FileExists);
            Assert.Equal("OK", doc.StatusDisplay);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

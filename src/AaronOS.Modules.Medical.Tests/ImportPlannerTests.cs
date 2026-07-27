using AaronOS.Modules.Medical.Import;

namespace AaronOS.Modules.Medical.Tests;

public class ImportPlannerTests
{
    private static CcdaDocument WithConditions(params ParsedCondition[] conditions) =>
        new() { Conditions = [.. conditions] };

    [Fact]
    public void UnseenExternalId_IsNew()
    {
        var plan = ImportPlanner.BuildPlan(
            WithConditions(new ParsedCondition("Hypertension", null, null, null, false, "cond-1")),
            new ExistingKeys());

        var row = Assert.Single(plan.Rows);
        Assert.Equal(ImportStatus.New, row.Status);
        Assert.Equal(1, plan.NewCount);
    }

    [Fact]
    public void KnownExternalId_IsAlreadyImported()
    {
        var existing = new ExistingKeys();
        existing.Conditions.Add("cond-1");

        var plan = ImportPlanner.BuildPlan(
            WithConditions(new ParsedCondition("Hypertension", null, null, null, false, "cond-1")),
            existing);

        Assert.Equal(ImportStatus.AlreadyImported, Assert.Single(plan.Rows).Status);
        Assert.Equal(0, plan.NewCount);
        Assert.Equal(1, plan.AlreadyImportedCount);
    }

    [Fact]
    public void WithoutAnExternalId_FallsBackToANaturalKey()
    {
        // Some producers omit ids entirely. Without a natural-key fallback every re-import would
        // duplicate everything, so this is the case that actually protects the user's history.
        var existing = new ExistingKeys();
        existing.Conditions.Add("Hypertension|2020-01-15");

        var plan = ImportPlanner.BuildPlan(
            WithConditions(new ParsedCondition("Hypertension", null, new DateOnly(2020, 1, 15), null, false, null)),
            existing);

        Assert.Equal(ImportStatus.AlreadyImported, Assert.Single(plan.Rows).Status);
    }

    [Fact]
    public void NaturalKeyDistinguishesTheSameNameOnDifferentDates()
    {
        var existing = new ExistingKeys();
        existing.Conditions.Add("Hypertension|2020-01-15");

        var plan = ImportPlanner.BuildPlan(
            WithConditions(new ParsedCondition("Hypertension", null, new DateOnly(2023, 6, 1), null, false, null)),
            existing);

        Assert.Equal(ImportStatus.New, Assert.Single(plan.Rows).Status);
    }

    [Fact]
    public void TheSameRecordTwiceInOneDocument_CollapsesToOneRow()
    {
        var plan = ImportPlanner.BuildPlan(
            WithConditions(
                new ParsedCondition("Hypertension", null, null, null, false, "cond-1"),
                new ParsedCondition("Hypertension", null, null, null, false, "cond-1")),
            new ExistingKeys());

        Assert.Single(plan.Rows);
        Assert.Equal(1, plan.NewCount);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var existing = new ExistingKeys();
        existing.Conditions.Add("COND-1");

        var plan = ImportPlanner.BuildPlan(
            WithConditions(new ParsedCondition("Hypertension", null, null, null, false, "cond-1")),
            existing);

        Assert.Equal(ImportStatus.AlreadyImported, Assert.Single(plan.Rows).Status);
    }

    [Fact]
    public void SectionsAreKeyedSeparately_SoASharedIdDoesNotCollide()
    {
        // The same source id can legitimately appear for a condition and a lab.
        var existing = new ExistingKeys();
        existing.Conditions.Add("shared-1");

        var plan = ImportPlanner.BuildPlan(
            new CcdaDocument
            {
                Conditions = [new ParsedCondition("C", null, null, null, false, "shared-1")],
                Labs = [new ParsedLab("A1c", 5.7m, null, "%", null, null, null, "shared-1")]
            },
            existing);

        Assert.Equal(ImportStatus.AlreadyImported, plan.Rows.Single(r => r.Section == "Conditions").Status);
        Assert.Equal(ImportStatus.New, plan.Rows.Single(r => r.Section == "Labs").Status);
    }

    [Fact]
    public void PlansEverySectionAndGroupsThemForReview()
    {
        var plan = ImportPlanner.BuildPlan(
            new CcdaDocument
            {
                Conditions = [new ParsedCondition("C", null, null, null, false, "c1")],
                Medications = [new ParsedMedication("M", "10 mg", null, null, null, "m1")],
                Allergies = [new ParsedAllergy("Penicillin", "Hives", "Moderate", "a1")],
                Immunizations = [new ParsedImmunization("Flu", null, "i1")],
                Procedures = [new ParsedProcedure("Appendectomy", null, null, "p1")],
                Visits = [new ParsedVisit(null, "Office visit", "Clinic", null, "v1")],
                Labs = [new ParsedLab("A1c", 5.7m, null, "%", null, null, null, "l1")]
            },
            new ExistingKeys());

        Assert.Equal(7, plan.Rows.Count);
        Assert.Equal(7, plan.NewCount);
        Assert.Equal(7, plan.BySection.Count());
    }

    [Fact]
    public void LabNaturalKeySeparatesRepeatedTestsOverTime()
    {
        var existing = new ExistingKeys();
        existing.Labs.Add("A1c|2026-01-01|5.7");

        var plan = ImportPlanner.BuildPlan(
            new CcdaDocument
            {
                Labs =
                [
                    new ParsedLab("A1c", 5.7m, null, "%", null, null, new DateOnly(2026, 1, 1), null),
                    new ParsedLab("A1c", 6.1m, null, "%", null, null, new DateOnly(2026, 6, 1), null)
                ]
            },
            existing);

        Assert.Equal(ImportStatus.AlreadyImported, plan.Rows[0].Status);
        Assert.Equal(ImportStatus.New, plan.Rows[1].Status);
    }

    [Fact]
    public void EmptyDocumentPlansNothing()
    {
        var plan = ImportPlanner.BuildPlan(new CcdaDocument(), new ExistingKeys());

        Assert.Empty(plan.Rows);
        Assert.Equal(0, plan.NewCount);
    }
}

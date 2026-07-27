namespace AaronOS.Modules.Finance.Data;

/// <summary>
/// The single row of planning assumptions every projection is built from.
///
/// Age lives here rather than in Core's UserProfile deliberately: the Medical module is being
/// developed against UserProfile concurrently, and a retirement age is a planning input rather
/// than a fact about the person. If a shared birth date lands in Core later, this becomes a
/// derived value and the column can go.
/// </summary>
public class RetirementPlan
{
    public int Id { get; set; }

    /// <summary>Gross annual pay. Drives the employer match and the savings rate.</summary>
    public decimal AnnualSalary { get; set; }

    public int CurrentAge { get; set; } = 40;
    public int TargetRetirementAge { get; set; } = 65;

    /// <summary>Nominal annual return for the middle scenario. The other two are derived from it.</summary>
    public decimal ExpectedReturnPercent { get; set; } = 6m;

    public decimal InflationPercent { get; set; } = 2.5m;

    /// <summary>Percent of the final balance drawn each year in retirement.</summary>
    public decimal WithdrawalRatePercent { get; set; } = 4m;

    /// <summary>HSA limits differ for family versus self-only coverage.</summary>
    public bool HasFamilyHsaCoverage { get; set; }

    public int YearsToRetirement => Math.Max(0, TargetRetirementAge - CurrentAge);

    /// <summary>
    /// True when the assumptions are usable. Zero salary is allowed — it only removes the match
    /// and the savings rate — but an inverted or absurd age pair produces a meaningless chart.
    /// </summary>
    public bool IsUsable =>
        CurrentAge is >= 14 and <= 99 &&
        TargetRetirementAge > CurrentAge &&
        TargetRetirementAge <= 100 &&
        ExpectedReturnPercent is >= -20m and <= 30m &&
        InflationPercent is >= -5m and <= 25m &&
        WithdrawalRatePercent is > 0m and <= 20m;
}

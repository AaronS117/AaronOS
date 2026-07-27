using AaronOS.Modules.Finance.Data;

namespace AaronOS.Modules.Finance.Retirement;

/// <summary>
/// Which annual cap an account counts against. Accounts do not each get their own limit — a
/// traditional and a Roth 401(k) share one elective-deferral cap, and a traditional and a Roth IRA
/// share one IRA cap. Checking each account separately would report headroom that does not exist.
/// </summary>
public enum ContributionLimitGroup
{
    ElectiveDeferral,
    Ira,
    Hsa,
    Unlimited,
}

/// <summary>One group's planned contributions measured against its cap.</summary>
public readonly record struct LimitCheck(
    ContributionLimitGroup Group, string Label, decimal Contributed, decimal Limit)
{
    public bool IsOver => Contributed > Limit;
    public decimal OverBy => Math.Max(0, Contributed - Limit);
    public decimal Headroom => Math.Max(0, Limit - Contributed);
}

/// <summary>
/// IRS annual contribution caps.
///
/// The figures are for <see cref="Year"/> and were taken from irs.gov: the 401(k) and IRA numbers
/// from "401(k) limit increases to $24,500 for 2026, IRA limit increases to $7,500", the HSA
/// numbers from Rev. Proc. 2025-19. They are hardcoded because there is no free API for them, which
/// means they go stale every January — the year is exposed so the UI can say which year it is
/// quoting rather than presenting a stale cap as current.
/// </summary>
public static class ContributionLimits
{
    public const int Year = 2026;

    private const decimal ElectiveDeferralBase = 24_500m;
    private const decimal ElectiveDeferralCatchUp50 = 8_000m;

    /// <summary>SECURE 2.0's higher catch-up, which applies only while turning 60 through 63.</summary>
    private const decimal ElectiveDeferralCatchUp60To63 = 11_250m;

    private const decimal IraBase = 7_500m;
    private const decimal IraCatchUp50 = 1_100m;

    private const decimal HsaSelfOnly = 4_400m;
    private const decimal HsaFamily = 8_750m;
    private const decimal HsaCatchUp55 = 1_000m;

    public static ContributionLimitGroup GroupOf(RetirementAccountKind kind) => kind switch
    {
        RetirementAccountKind.Traditional401k or RetirementAccountKind.Roth401k => ContributionLimitGroup.ElectiveDeferral,
        RetirementAccountKind.TraditionalIra or RetirementAccountKind.RothIra => ContributionLimitGroup.Ira,
        RetirementAccountKind.Hsa => ContributionLimitGroup.Hsa,
        _ => ContributionLimitGroup.Unlimited,
    };

    public static string LabelOf(ContributionLimitGroup group) => group switch
    {
        ContributionLimitGroup.ElectiveDeferral => "401(k) contributions",
        ContributionLimitGroup.Ira => "IRA contributions",
        ContributionLimitGroup.Hsa => "HSA contributions",
        _ => "Taxable investing",
    };

    /// <summary>The cap for a group at a given age, or null when the group has none.</summary>
    public static decimal? LimitFor(ContributionLimitGroup group, int age, bool familyHsaCoverage) => group switch
    {
        ContributionLimitGroup.ElectiveDeferral => ElectiveDeferralBase + age switch
        {
            // The 60–63 window replaces the ordinary catch-up rather than stacking on it, and it
            // ends at 64 — the amount goes back down, which is easy to get wrong.
            >= 60 and <= 63 => ElectiveDeferralCatchUp60To63,
            >= 50 => ElectiveDeferralCatchUp50,
            _ => 0m,
        },
        ContributionLimitGroup.Ira => IraBase + (age >= 50 ? IraCatchUp50 : 0m),
        ContributionLimitGroup.Hsa =>
            (familyHsaCoverage ? HsaFamily : HsaSelfOnly) + (age >= 55 ? HsaCatchUp55 : 0m),
        _ => null,
    };

    /// <summary>
    /// One check per capped group that has any planned contribution. Groups with nothing going into
    /// them are omitted, so the UI shows warnings about accounts you actually fund.
    /// </summary>
    public static List<LimitCheck> Check(
        IEnumerable<RetirementAccount> accounts, int age, bool familyHsaCoverage)
    {
        return accounts
            .GroupBy(a => GroupOf(a.Kind))
            .Select(g => (Group: g.Key, Contributed: g.Sum(a => a.AnnualContribution)))
            .Where(x => x.Contributed > 0)
            .Select(x => (x.Group, x.Contributed, Limit: LimitFor(x.Group, age, familyHsaCoverage)))
            .Where(x => x.Limit is not null)
            .Select(x => new LimitCheck(x.Group, LabelOf(x.Group), x.Contributed, x.Limit!.Value))
            .OrderBy(c => c.Label)
            .ToList();
    }
}

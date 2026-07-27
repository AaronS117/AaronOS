namespace AaronOS.Modules.Finance.Data;

/// <summary>
/// Tax treatment of a retirement account. This drives which annual limit applies, so the values
/// are not merely labels — see <see cref="Retirement.ContributionLimits"/>.
/// </summary>
public enum RetirementAccountKind
{
    Traditional401k,
    Roth401k,
    TraditionalIra,
    RothIra,
    Hsa,
    TaxableBrokerage,
}

/// <summary>
/// One retirement or long-term investment account.
///
/// The balance comes from a linked Plaid account when one is available and from
/// <see cref="ManualBalance"/> when it is not. Most 401(k) and HSA providers are not in the
/// existing bank link, so manual entry is the normal case rather than the fallback.
/// </summary>
public class RetirementAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public RetirementAccountKind Kind { get; set; }

    /// <summary>Linked <see cref="FinanceAccount"/>, when Plaid can report this balance.</summary>
    public int? FinanceAccountId { get; set; }

    /// <summary>Balance as last entered by hand. Ignored when <see cref="FinanceAccountId"/> is set.</summary>
    public decimal? ManualBalance { get; set; }

    /// <summary>What you plan to put in over a year, excluding anything the employer adds.</summary>
    public decimal AnnualContribution { get; set; }

    /// <summary>Percent of salary the employer matches, e.g. 50 for fifty cents on the dollar.</summary>
    public decimal EmployerMatchPercent { get; set; }

    /// <summary>Percent of salary the match stops at, e.g. 6 for "up to 6% of pay".</summary>
    public decimal EmployerMatchLimitPercent { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// The employer's annual contribution given a salary.
    ///
    /// A match is expressed as two numbers because both matter: a 50% match up to 6% of pay is
    /// worth 3% of salary, not 50% and not 6%. Collapsing them into one figure is the usual way
    /// this gets estimated wrong.
    /// </summary>
    public decimal EmployerMatchOn(decimal annualSalary)
    {
        if (annualSalary <= 0 || EmployerMatchPercent <= 0 || EmployerMatchLimitPercent <= 0)
        {
            return 0;
        }

        // The employee has to actually defer that much of pay to earn the full match.
        var eligiblePay = annualSalary * Math.Min(EmployerMatchLimitPercent, 100) / 100m;
        var deferred = Math.Min(AnnualContribution, eligiblePay);
        return deferred * Math.Min(EmployerMatchPercent, 100) / 100m;
    }
}

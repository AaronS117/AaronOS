using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Finance.Data;
using AaronOS.Modules.Finance.Retirement;
using AaronOS.Modules.Finance.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace AaronOS.Modules.Finance.ViewModels;

/// <summary>An option in a kind dropdown. The enum member's own name is not what a person reads.</summary>
public record KindOption<T>(T Value, string Label);

/// <summary>A cap and what is going into it, formatted for display.</summary>
public record LimitRow(string Label, string Detail, bool IsOver);

/// <summary>Headline numbers for one return scenario.</summary>
public record ScenarioRow(string Name, decimal ReturnPercent, decimal TodaysDollars, decimal MonthlyIncome);

public partial class RetirementViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    private static readonly SKColor ReactorCyan = new(0x4C, 0xC2, 0xFF);
    private static readonly SKColor Conservative = new(0x9A, 0xA3, 0xB2);
    private static readonly SKColor Optimistic = new(0x5F, 0xD9, 0x8C);
    private static readonly SKColor AxisLabel = new(0x9A, 0xA3, 0xB2);
    private static readonly SKColor Separator = new(0x2A, 0x2A, 0x30);

    private const double MaxBarWidth = 320;

    /// <summary>Bound directly so the editors read and write the stored assumptions in place.</summary>
    [ObservableProperty]
    private RetirementPlan _plan = new();

    [ObservableProperty]
    private decimal _totalBalance;

    [ObservableProperty]
    private decimal _annualContributionTotal;

    [ObservableProperty]
    private decimal _annualEmployerMatch;

    [ObservableProperty]
    private decimal _savingsRatePercent;

    [ObservableProperty]
    private decimal _averageMonthlySpend;

    [ObservableProperty]
    private decimal _emergencyBalance;

    [ObservableProperty]
    private decimal _emergencyTarget;

    [ObservableProperty]
    private string _emergencyMonthsDisplay = "—";

    [ObservableProperty]
    private double _emergencyBarWidth;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasProjection;

    [ObservableProperty]
    private bool _hasEmergencyGoal;

    [ObservableProperty]
    private bool _hasAccounts;

    [ObservableProperty]
    private bool _hasGoals;

    [ObservableProperty]
    private bool _hasLimitWarning;

    [ObservableProperty]
    private string _projectionCaption = "";

    public ObservableCollection<RetirementAccount> Accounts { get; } = [];
    public ObservableCollection<SavingsGoal> Goals { get; } = [];
    public ObservableCollection<LimitRow> LimitRows { get; } = [];
    public ObservableCollection<ScenarioRow> Scenarios { get; } = [];
    public ObservableCollection<FinanceAccount> LinkableAccounts { get; } = [];

    public List<ISeries> ProjectionSeries { get; } = [];
    public List<ICartesianAxis> ProjectionXAxes { get; } = [];
    public List<ICartesianAxis> ProjectionYAxes { get; } = [];

    public IReadOnlyList<KindOption<RetirementAccountKind>> AccountKinds { get; } =
    [
        new(RetirementAccountKind.Traditional401k, "Traditional 401(k)"),
        new(RetirementAccountKind.Roth401k, "Roth 401(k)"),
        new(RetirementAccountKind.TraditionalIra, "Traditional IRA"),
        new(RetirementAccountKind.RothIra, "Roth IRA"),
        new(RetirementAccountKind.Hsa, "HSA"),
        new(RetirementAccountKind.TaxableBrokerage, "Taxable brokerage"),
    ];

    public IReadOnlyList<KindOption<SavingsGoalKind>> GoalKinds { get; } =
    [
        new(SavingsGoalKind.EmergencyFund, "Emergency fund"),
        new(SavingsGoalKind.TargetPurchase, "Savings target"),
    ];

    public string LimitYearCaption =>
        $"Caps shown are the IRS {ContributionLimits.Year} figures, entered by hand. Verify at irs.gov each January.";

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            Plan = await db.Set<RetirementPlan>().FirstOrDefaultAsync() ?? new RetirementPlan();

            var accounts = await db.Set<RetirementAccount>().OrderBy(a => a.Name).ToListAsync();
            Accounts.Clear();
            foreach (var account in accounts)
            {
                Accounts.Add(account);
            }
            HasAccounts = accounts.Count > 0;

            var goals = await db.Set<SavingsGoal>()
                .Where(g => !g.IsArchived)
                .OrderBy(g => g.Name)
                .ToListAsync();
            Goals.Clear();
            foreach (var goal in goals)
            {
                Goals.Add(goal);
            }
            HasGoals = goals.Count > 0;

            var financeAccounts = await db.Set<FinanceAccount>().OrderBy(a => a.Name).ToListAsync();
            LinkableAccounts.Clear();
            foreach (var account in financeAccounts)
            {
                LinkableAccounts.Add(account);
            }

            var transactions = await db.Set<FinanceTransaction>().ToListAsync();

            Recalculate(financeAccounts, transactions);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Everything derived, in one pass. Called on load and after each save rather than on every
    /// keystroke: the totals depend on values still being typed, and a figure that jitters while
    /// you edit a field is harder to read than one that settles when you commit.
    /// </summary>
    private void Recalculate(List<FinanceAccount> financeAccounts, List<FinanceTransaction> transactions)
    {
        var balanceById = financeAccounts.ToDictionary(a => a.Id, a => a.CurrentBalance ?? 0);

        decimal BalanceOf(int? linkedId, decimal? manual) =>
            linkedId is { } id && balanceById.TryGetValue(id, out var live) ? live : manual ?? 0;

        TotalBalance = Accounts.Sum(a => BalanceOf(a.FinanceAccountId, a.ManualBalance));
        AnnualContributionTotal = Accounts.Sum(a => a.AnnualContribution);
        AnnualEmployerMatch = Accounts.Sum(a => a.EmployerMatchOn(Plan.AnnualSalary));

        var annualSavingsContribution = Goals.Sum(g => g.MonthlyContribution) * 12m;
        SavingsRatePercent = Plan.AnnualSalary > 0
            ? Math.Round((AnnualContributionTotal + annualSavingsContribution) / Plan.AnnualSalary * 100m, 1)
            : 0m;

        AverageMonthlySpend = MonthlySpendCalculator.AverageMonthlyOutflow(
            transactions, DateOnly.FromDateTime(DateTime.Now));

        BuildEmergencyFund(BalanceOf);
        BuildLimitRows();
        BuildProjection();
    }

    private void BuildEmergencyFund(Func<int?, decimal?, decimal> balanceOf)
    {
        var fund = Goals.FirstOrDefault(g => g.Kind == SavingsGoalKind.EmergencyFund);
        HasEmergencyGoal = fund is not null;
        if (fund is null)
        {
            EmergencyBalance = 0;
            EmergencyTarget = 0;
            EmergencyMonthsDisplay = "—";
            EmergencyBarWidth = 0;
            return;
        }

        EmergencyBalance = balanceOf(fund.FinanceAccountId, fund.ManualBalance);

        // Months of real spending, not a figure typed in — that is the whole point of deriving it
        // from the transactions. An explicit TargetAmount still wins if one was entered.
        var months = fund.TargetMonthsOfExpenses ?? 3;
        EmergencyTarget = fund.TargetAmount ?? AverageMonthlySpend * months;

        var covered = MonthlySpendCalculator.MonthsCovered(EmergencyBalance, AverageMonthlySpend);
        EmergencyMonthsDisplay = covered is null
            ? "—"
            : $"{covered.Value:0.0} months of expenses covered";

        EmergencyBarWidth = EmergencyTarget <= 0
            ? 0
            : MaxBarWidth * Math.Clamp((double)(EmergencyBalance / EmergencyTarget), 0, 1);
    }

    private void BuildLimitRows()
    {
        LimitRows.Clear();
        foreach (var check in ContributionLimits.Check(Accounts, Plan.CurrentAge, Plan.HasFamilyHsaCoverage))
        {
            var detail = check.IsOver
                ? $"{check.Contributed:C0} planned against a {check.Limit:C0} cap — over by {check.OverBy:C0}"
                : $"{check.Contributed:C0} of {check.Limit:C0} — {check.Headroom:C0} of room left";
            LimitRows.Add(new LimitRow(check.Label, detail, check.IsOver));
        }

        HasLimitWarning = LimitRows.Any(r => r.IsOver);
    }

    private void BuildProjection()
    {
        ProjectionSeries.Clear();
        ProjectionXAxes.Clear();
        ProjectionYAxes.Clear();
        Scenarios.Clear();

        HasProjection = Plan.IsUsable && Plan.YearsToRetirement > 0 && (TotalBalance > 0 || AnnualContributionTotal > 0);
        if (!HasProjection)
        {
            ProjectionCaption = "Enter your age, target retirement age and at least one account balance to see a projection.";
            return;
        }

        var input = new ProjectionInput(
            StartingBalance: TotalBalance,
            AnnualContribution: AnnualContributionTotal + AnnualEmployerMatch,
            StartAge: Plan.CurrentAge,
            Years: Plan.YearsToRetirement,
            InflationPercent: Plan.InflationPercent,
            WithdrawalRatePercent: Plan.WithdrawalRatePercent);

        var results = RetirementProjector.ProjectScenarios(input, Plan.ExpectedReturnPercent);

        // Plotted in today's dollars. The nominal line is a bigger number that means less, and
        // showing both invites reading the wrong one.
        var colours = new[] { Conservative, ReactorCyan, Optimistic };
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var colour = colours[i];
            ProjectionSeries.Add(new LineSeries<ObservablePoint>
            {
                Values = result.Points
                    .Select(p => new ObservablePoint(p.Age, (double)p.TodaysDollars))
                    .ToArray(),
                Name = $"{result.ScenarioName} ({result.AnnualReturnPercent:0.#}%)",
                Stroke = new SolidColorPaint(colour) { StrokeThickness = i == 1 ? 2.8f : 1.6f },
                GeometrySize = 0,
                Fill = null,
                LineSmoothness = 0.2,
            });

            Scenarios.Add(new ScenarioRow(
                result.ScenarioName,
                result.AnnualReturnPercent,
                result.FinalTodaysDollars,
                result.MonthlyIncomeTodaysDollars));
        }

        ProjectionXAxes.Add(new Axis
        {
            Name = "Age",
            NamePaint = new SolidColorPaint(AxisLabel),
            NameTextSize = 12,
            LabelsPaint = new SolidColorPaint(AxisLabel),
            TextSize = 12,
            SeparatorsPaint = new SolidColorPaint(Separator) { StrokeThickness = 1 },
        });

        // Both settings here are corrections to what LiveCharts does by default on a dark card: it
        // draws separators in a near-white that outshines the data, and it labels a balance axis as
        // a raw "500000" beside a shortened "1.5 M".
        ProjectionYAxes.Add(new Axis
        {
            Labeler = FormatMoneyAxis,
            LabelsPaint = new SolidColorPaint(AxisLabel),
            TextSize = 12,
            SeparatorsPaint = new SolidColorPaint(Separator) { StrokeThickness = 1 },
            MinLimit = 0,
        });

        ProjectionCaption =
            $"In today's dollars, at {Plan.InflationPercent:0.#}% inflation, drawing " +
            $"{Plan.WithdrawalRatePercent:0.#}% a year. An estimate, not a promise.";
    }

    /// <summary>Axis labels in one consistent shorthand, so the scale reads at a glance.</summary>
    public static string FormatMoneyAxis(double value) => value switch
    {
        >= 1_000_000 or <= -1_000_000 => $"${value / 1_000_000:0.#}M",
        >= 1_000 or <= -1_000 => $"${value / 1_000:0}k",
        _ => $"${value:0}",
    };

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Plan.IsUsable)
        {
            StatusMessage = "Check the ages and rates — retirement age has to be later than your current age.";
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var stored = await db.Set<RetirementPlan>().FirstOrDefaultAsync();
            if (stored is null)
            {
                db.Add(Plan);
            }
            else
            {
                stored.AnnualSalary = Plan.AnnualSalary;
                stored.CurrentAge = Plan.CurrentAge;
                stored.TargetRetirementAge = Plan.TargetRetirementAge;
                stored.ExpectedReturnPercent = Plan.ExpectedReturnPercent;
                stored.InflationPercent = Plan.InflationPercent;
                stored.WithdrawalRatePercent = Plan.WithdrawalRatePercent;
                stored.HasFamilyHsaCoverage = Plan.HasFamilyHsaCoverage;
            }

            await SaveRowsAsync(db);
            await db.SaveChangesAsync();

            StatusMessage = $"Saved {DateTime.Now:h:mm tt}.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Writes the edited rows back. The collections hold detached entities loaded by a context that
    /// is already disposed, so each row is attached to the new context by Id rather than tracked
    /// across contexts — new rows have Id 0 and get inserted.
    /// </summary>
    private async Task SaveRowsAsync(AaronOsDbContext db)
    {
        foreach (var account in Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.Name))
            {
                continue;
            }

            if (account.Id == 0)
            {
                db.Add(account);
                continue;
            }

            var stored = await db.Set<RetirementAccount>().FirstOrDefaultAsync(a => a.Id == account.Id);
            if (stored is null)
            {
                continue;
            }

            stored.Name = account.Name;
            stored.Kind = account.Kind;
            stored.FinanceAccountId = account.FinanceAccountId;
            stored.ManualBalance = account.ManualBalance;
            stored.AnnualContribution = account.AnnualContribution;
            stored.EmployerMatchPercent = account.EmployerMatchPercent;
            stored.EmployerMatchLimitPercent = account.EmployerMatchLimitPercent;
            stored.Notes = account.Notes;
        }

        foreach (var goal in Goals)
        {
            if (string.IsNullOrWhiteSpace(goal.Name))
            {
                continue;
            }

            if (goal.Id == 0)
            {
                db.Add(goal);
                continue;
            }

            var stored = await db.Set<SavingsGoal>().FirstOrDefaultAsync(g => g.Id == goal.Id);
            if (stored is null)
            {
                continue;
            }

            stored.Name = goal.Name;
            stored.Kind = goal.Kind;
            stored.TargetAmount = goal.TargetAmount;
            stored.TargetMonthsOfExpenses = goal.TargetMonthsOfExpenses;
            stored.TargetDate = goal.TargetDate;
            stored.FinanceAccountId = goal.FinanceAccountId;
            stored.ManualBalance = goal.ManualBalance;
            stored.MonthlyContribution = goal.MonthlyContribution;
            stored.IsArchived = goal.IsArchived;
        }
    }

    [RelayCommand]
    private void AddAccount()
    {
        Accounts.Add(new RetirementAccount { Name = "New account", Kind = RetirementAccountKind.Traditional401k });
        HasAccounts = true;
    }

    [RelayCommand]
    private async Task RemoveAccountAsync(RetirementAccount? account)
    {
        if (account is null)
        {
            return;
        }

        Accounts.Remove(account);
        HasAccounts = Accounts.Count > 0;

        if (account.Id != 0)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var stored = await db.Set<RetirementAccount>().FirstOrDefaultAsync(a => a.Id == account.Id);
            if (stored is not null)
            {
                db.Remove(stored);
                await db.SaveChangesAsync();
            }
        }

        await LoadAsync();
    }

    [RelayCommand]
    private void AddGoal()
    {
        var isFirstEmergencyFund = Goals.All(g => g.Kind != SavingsGoalKind.EmergencyFund);
        Goals.Add(new SavingsGoal
        {
            Name = isFirstEmergencyFund ? "Emergency fund" : "New goal",
            Kind = isFirstEmergencyFund ? SavingsGoalKind.EmergencyFund : SavingsGoalKind.TargetPurchase,
            TargetMonthsOfExpenses = isFirstEmergencyFund ? 3 : null,
        });
        HasGoals = true;
    }

    [RelayCommand]
    private async Task RemoveGoalAsync(SavingsGoal? goal)
    {
        if (goal is null)
        {
            return;
        }

        Goals.Remove(goal);
        HasGoals = Goals.Count > 0;

        if (goal.Id != 0)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var stored = await db.Set<SavingsGoal>().FirstOrDefaultAsync(g => g.Id == goal.Id);
            if (stored is not null)
            {
                db.Remove(stored);
                await db.SaveChangesAsync();
            }
        }

        await LoadAsync();
    }
}

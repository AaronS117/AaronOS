namespace AaronOS.Modules.Finance.Data;

public enum SavingsGoalKind
{
    /// <summary>Target is months of real spending rather than a figure you pick.</summary>
    EmergencyFund,
    TargetPurchase,
}

/// <summary>A savings bucket with something you are saving towards.</summary>
public class SavingsGoal
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public SavingsGoalKind Kind { get; set; }

    /// <summary>Target figure. Null for an emergency fund, whose target is derived from spending.</summary>
    public decimal? TargetAmount { get; set; }

    /// <summary>Emergency fund only: how many months of expenses to hold. Three to six is usual.</summary>
    public int? TargetMonthsOfExpenses { get; set; }

    public DateOnly? TargetDate { get; set; }

    public int? FinanceAccountId { get; set; }
    public decimal? ManualBalance { get; set; }
    public decimal MonthlyContribution { get; set; }
    public bool IsArchived { get; set; }
}

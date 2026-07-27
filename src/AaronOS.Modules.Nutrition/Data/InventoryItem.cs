namespace AaronOS.Modules.Nutrition.Data;

public class InventoryItem
{
    /// <summary>Anything inside this many days of its ExpiresOn counts as "use it up".</summary>
    public const int ExpiringSoonWithinDays = 3;

    public int Id { get; set; }
    public int IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public StorageLocation StorageLocation { get; set; }
    public DateOnly DateAcquired { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? QuantityLabel { get; set; }
    public string? Notes { get; set; }

    // Read-only computed display members below. EF ignores getter-only properties, so they need no
    // [NotMapped]. Same convention as FinanceTransaction.DateDisplay/IsInflow: compute the shape the
    // UI wants here, so XAML binds one plain property instead of running a value converter.

    public int? DaysLeft => ExpiresOn is { } on
        ? on.DayNumber - DateOnly.FromDateTime(DateTime.Now).DayNumber
        : null;

    public bool IsExpired => DaysLeft is < 0;

    public bool IsExpiringSoon => DaysLeft is >= 0 and <= ExpiringSoonWithinDays;

    public string DateAcquiredDisplay => DateAcquired.ToString("MMM d");

    public string ExpiresDisplay => ExpiresOn?.ToString("MMM d") ?? "—";

    /// <summary>Plain-language freshness, e.g. "expired yesterday", "today", "in 2 days".</summary>
    public string FreshnessText => DaysLeft switch
    {
        null => "no date",
        < -1 => $"expired {-DaysLeft.Value} days ago",
        -1 => "expired yesterday",
        0 => "today",
        1 => "tomorrow",
        < 14 => $"in {DaysLeft.Value} days",
        < 60 => $"in {DaysLeft.Value / 7} weeks",
        _ => $"in {DaysLeft.Value / 30} months"
    };
}

namespace AaronOS.Modules.Trading.Data;

public enum OrderSide
{
    Buy,
    Sell,
}

/// <summary>
/// An order as this app recorded it. The broker holds its own copy; this one exists so the decision
/// that produced the order, the reasoning behind it and the eventual fill all live in one place that
/// survives the broker's retention policy.
/// </summary>
public class TradeOrder
{
    public int Id { get; set; }

    /// <summary>The broker's identifier, so a fill can be reconciled back to this row.</summary>
    public string BrokerOrderId { get; set; } = "";

    public string Symbol { get; set; } = "";
    public OrderSide Side { get; set; }
    public int Quantity { get; set; }

    public DateTime SubmittedAtUtc { get; set; }
    public decimal? EstimatedPrice { get; set; }
    public decimal? FilledPrice { get; set; }
    public DateTime? FilledAtUtc { get; set; }

    /// <summary>Broker status verbatim (new, filled, canceled, rejected …).</summary>
    public string Status { get; set; } = "new";

    /// <summary>The cycle that produced this order, so an order always has its reasoning attached.</summary>
    public int? AgentDecisionId { get; set; }

    /// <summary>Why the agent said it placed this order, in one line.</summary>
    public string? Rationale { get; set; }
}

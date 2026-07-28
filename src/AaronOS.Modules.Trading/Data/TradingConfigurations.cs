using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Trading.Data;

public class TradingConfigConfiguration : IEntityTypeConfiguration<TradingConfig>
{
    public void Configure(EntityTypeBuilder<TradingConfig> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Watchlist).HasMaxLength(500).IsRequired();
        builder.Property(c => c.Model).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Provider).HasMaxLength(32).IsRequired();
        builder.Property(c => c.StrategyNotes).HasMaxLength(4000);
        builder.Property(c => c.MaxPositionPercent).HasPrecision(6, 2);
        builder.Property(c => c.MaxInvestedPercent).HasPrecision(6, 2);
    }
}

public class TradeOrderConfiguration : IEntityTypeConfiguration<TradeOrder>
{
    public void Configure(EntityTypeBuilder<TradeOrder> builder)
    {
        builder.HasKey(o => o.Id);
        builder.HasIndex(o => o.BrokerOrderId).IsUnique();
        builder.HasIndex(o => o.SubmittedAtUtc);
        builder.Property(o => o.BrokerOrderId).HasMaxLength(64).IsRequired();
        builder.Property(o => o.Symbol).HasMaxLength(16).IsRequired();
        builder.Property(o => o.Side).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(o => o.Status).HasMaxLength(32).IsRequired();
        builder.Property(o => o.Rationale).HasMaxLength(1000);
        builder.Property(o => o.EstimatedPrice).HasPrecision(18, 4);
        builder.Property(o => o.FilledPrice).HasPrecision(18, 4);
    }
}

public class PortfolioSnapshotConfiguration : IEntityTypeConfiguration<PortfolioSnapshot>
{
    public void Configure(EntityTypeBuilder<PortfolioSnapshot> builder)
    {
        builder.HasKey(s => s.Id);

        // One row per day, enforced by the database rather than by the code that writes it: a
        // duplicated day would silently distort both the equity curve and the drawdown.
        builder.HasIndex(s => s.Date).IsUnique();
        builder.Property(s => s.Equity).HasPrecision(18, 2);
        builder.Property(s => s.Cash).HasPrecision(18, 2);
        builder.Property(s => s.BenchmarkClose).HasPrecision(18, 4);
    }
}

public class AgentDecisionConfiguration : IEntityTypeConfiguration<AgentDecision>
{
    public void Configure(EntityTypeBuilder<AgentDecision> builder)
    {
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => d.RanAtUtc);
        builder.Property(d => d.Model).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Reasoning).HasMaxLength(20000);
        builder.Property(d => d.ActionSummary).HasMaxLength(500).IsRequired();
        builder.Property(d => d.BlockedActions).HasMaxLength(2000);
        builder.Property(d => d.Error).HasMaxLength(2000);
    }
}

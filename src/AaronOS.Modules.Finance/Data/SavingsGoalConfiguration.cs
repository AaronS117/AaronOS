using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Finance.Data;

public class SavingsGoalConfiguration : IEntityTypeConfiguration<SavingsGoal>
{
    public void Configure(EntityTypeBuilder<SavingsGoal> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(200).IsRequired();
        builder.Property(g => g.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(g => g.TargetAmount).HasPrecision(14, 2);
        builder.Property(g => g.ManualBalance).HasPrecision(14, 2);
        builder.Property(g => g.MonthlyContribution).HasPrecision(14, 2);
    }
}

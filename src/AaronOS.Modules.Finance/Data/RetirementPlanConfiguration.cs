using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Finance.Data;

public class RetirementPlanConfiguration : IEntityTypeConfiguration<RetirementPlan>
{
    public void Configure(EntityTypeBuilder<RetirementPlan> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.AnnualSalary).HasPrecision(14, 2);
        builder.Property(p => p.ExpectedReturnPercent).HasPrecision(6, 2);
        builder.Property(p => p.InflationPercent).HasPrecision(6, 2);
        builder.Property(p => p.WithdrawalRatePercent).HasPrecision(6, 2);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Finance.Data;

public class RetirementAccountConfiguration : IEntityTypeConfiguration<RetirementAccount>
{
    public void Configure(EntityTypeBuilder<RetirementAccount> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();

        // Stored as text, not as the enum's ordinal: the column stays readable in the database and
        // inserting a new member into the middle of the enum cannot silently reinterpret old rows.
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(a => a.ManualBalance).HasPrecision(14, 2);
        builder.Property(a => a.AnnualContribution).HasPrecision(14, 2);
        builder.Property(a => a.EmployerMatchPercent).HasPrecision(6, 2);
        builder.Property(a => a.EmployerMatchLimitPercent).HasPrecision(6, 2);
        builder.Property(a => a.Notes).HasMaxLength(500);
    }
}

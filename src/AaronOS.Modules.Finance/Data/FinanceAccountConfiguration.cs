using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Finance.Data;

public class FinanceAccountConfiguration : IEntityTypeConfiguration<FinanceAccount>
{
    public void Configure(EntityTypeBuilder<FinanceAccount> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.PlaidAccountId).IsUnique();
        builder.Property(a => a.PlaidAccountId).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Mask).HasMaxLength(8);
        builder.Property(a => a.Type).HasMaxLength(32).IsRequired();
        builder.Property(a => a.Subtype).HasMaxLength(32);
        builder.Property(a => a.CurrentBalance).HasPrecision(14, 2);
        builder.Property(a => a.AvailableBalance).HasPrecision(14, 2);
        builder.Property(a => a.IsoCurrencyCode).HasMaxLength(3).IsRequired();
    }
}

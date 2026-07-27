using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Finance.Data;

public class FinanceTransactionConfiguration : IEntityTypeConfiguration<FinanceTransaction>
{
    public void Configure(EntityTypeBuilder<FinanceTransaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => t.PlaidTransactionId).IsUnique();
        builder.HasIndex(t => t.Date);
        builder.Property(t => t.PlaidTransactionId).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(300).IsRequired();
        builder.Property(t => t.Amount).HasPrecision(14, 2);
        builder.Property(t => t.CategoryPrimary).HasMaxLength(64);
        builder.Property(t => t.CategoryDetailed).HasMaxLength(64);
        builder.Property(t => t.IsoCurrencyCode).HasMaxLength(3).IsRequired();
    }
}

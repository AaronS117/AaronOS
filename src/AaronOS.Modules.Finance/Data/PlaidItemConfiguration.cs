using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Finance.Data;

public class PlaidItemConfiguration : IEntityTypeConfiguration<PlaidItem>
{
    public void Configure(EntityTypeBuilder<PlaidItem> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.ItemId).IsUnique();
        builder.Property(p => p.ItemId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.InstitutionId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.InstitutionName).HasMaxLength(200).IsRequired();
    }
}

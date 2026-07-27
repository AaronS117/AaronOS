using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.BodyMeasurements.Data;

public class ClothingSizeEntryConfiguration : IEntityTypeConfiguration<ClothingSizeEntry>
{
    public void Configure(EntityTypeBuilder<ClothingSizeEntry> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.SizeLabel).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Brand).HasMaxLength(64);
    }
}

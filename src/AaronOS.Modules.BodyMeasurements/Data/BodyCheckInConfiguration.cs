using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.BodyMeasurements.Data;

public class BodyCheckInConfiguration : IEntityTypeConfiguration<BodyCheckIn>
{
    public void Configure(EntityTypeBuilder<BodyCheckIn> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.Date);

        foreach (var property in builder.Metadata.GetProperties())
        {
            if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
            {
                property.SetPrecision(6);
                property.SetScale(2);
            }
        }
    }
}

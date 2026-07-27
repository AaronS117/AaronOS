using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class RoutineConfiguration : IEntityTypeConfiguration<Routine>
{
    public void Configure(EntityTypeBuilder<Routine> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.IsActive);
        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Category).HasConversion<int>();
        builder.Property(r => r.PreferredDaysOfWeek).HasConversion<int?>();
        builder.Ignore(r => r.IsIntervalDriven);
    }
}

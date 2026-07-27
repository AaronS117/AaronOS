using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class ScheduleExceptionConfiguration : IEntityTypeConfiguration<ScheduleException>
{
    public void Configure(EntityTypeBuilder<ScheduleException> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Date);
        builder.Property(e => e.Label).HasMaxLength(120);
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.Kind).HasConversion<int?>();
        builder.Ignore(e => e.IsStandalone);
    }
}

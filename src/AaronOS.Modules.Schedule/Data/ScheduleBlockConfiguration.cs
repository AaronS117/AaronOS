using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class ScheduleBlockConfiguration : IEntityTypeConfiguration<ScheduleBlock>
{
    public void Configure(EntityTypeBuilder<ScheduleBlock> builder)
    {
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => b.IsActive);
        builder.Property(b => b.Label).HasMaxLength(120).IsRequired();
        builder.Property(b => b.Kind).HasConversion<int>();
        builder.Property(b => b.DaysOfWeek).HasConversion<int>();
        builder.Ignore(b => b.WrapsMidnight);
    }
}

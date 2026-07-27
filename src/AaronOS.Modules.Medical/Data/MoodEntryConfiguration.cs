using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Medical.Data;

public class MoodEntryConfiguration : IEntityTypeConfiguration<MoodEntry>
{
    public void Configure(EntityTypeBuilder<MoodEntry> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.SleepHours).HasPrecision(4, 1);

        // One entry per day, enforced by the database rather than only by the save path.
        builder.HasIndex(m => m.Date).IsUnique();
    }
}

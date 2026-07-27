using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Medical.Data;

public class SleepNightConfiguration : IEntityTypeConfiguration<SleepNight>
{
    public void Configure(EntityTypeBuilder<SleepNight> builder)
    {
        builder.HasKey(s => s.Id);

        // One night per wake date, enforced by the database. Re-running a sync over a range that was
        // already imported must update rows rather than pile up duplicates.
        builder.HasIndex(s => s.Date).IsUnique();
    }
}

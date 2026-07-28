using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class ExternalEventConfiguration : IEntityTypeConfiguration<ExternalEvent>
{
    public void Configure(EntityTypeBuilder<ExternalEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.ExternalCalendarId, e.ExternalUid }).IsUnique();
        builder.HasIndex(e => e.StartsAt);
        builder.Property(e => e.ExternalUid).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Location).HasMaxLength(500);
        builder.HasOne<ExternalCalendar>()
            .WithMany()
            .HasForeignKey(e => e.ExternalCalendarId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

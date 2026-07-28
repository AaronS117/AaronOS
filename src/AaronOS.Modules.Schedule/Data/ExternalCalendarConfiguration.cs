using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class ExternalCalendarConfiguration : IEntityTypeConfiguration<ExternalCalendar>
{
    public void Configure(EntityTypeBuilder<ExternalCalendar> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Provider).HasConversion<int>();
        builder.Property(c => c.IcsUrl).HasMaxLength(1000);
        builder.Property(c => c.RemoteCalendarId).HasMaxLength(200);
        builder.Property(c => c.LastError).HasMaxLength(2000);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class RoutineCompletionConfiguration : IEntityTypeConfiguration<RoutineCompletion>
{
    public void Configure(EntityTypeBuilder<RoutineCompletion> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.RoutineId, c.CompletedAt });
        builder.Property(c => c.Note).HasMaxLength(500);
        builder.HasOne<Routine>()
            .WithMany()
            .HasForeignKey(c => c.RoutineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

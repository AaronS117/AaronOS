using Microsoft.EntityFrameworkCore;

namespace AaronOS.Core.Data;

/// <summary>
/// The single shared DbContext for the whole app. Each module supplies its own
/// IEntityTypeConfiguration classes in its own assembly; this context discovers
/// them from the registered IAppModule list rather than referencing module types
/// directly, so Core never depends on any module.
/// </summary>
public class AaronOsDbContext(DbContextOptions<AaronOsDbContext> options, IEnumerable<IAppModule> modules) : DbContext(options)
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserProfileConfiguration());

        foreach (var assembly in modules.Select(m => m.GetType().Assembly).Distinct())
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }
}

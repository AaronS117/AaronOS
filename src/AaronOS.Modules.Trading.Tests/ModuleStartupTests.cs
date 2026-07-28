using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Trading.Tests;

/// <summary>
/// The startup hook is what makes the run unattended, so it gets tested rather than assumed. If it
/// silently failed to arm, the experiment would look like it was running and would quietly be doing
/// nothing — the worst possible failure for something measured over months.
/// </summary>
public class ModuleStartupTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aaronos-startup-{Guid.NewGuid():N}.db");

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AaronOsDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        services.AddSingleton<IAppModule, TradingModule>();
        new TradingModule().RegisterServices(services);
        return services.BuildServiceProvider();
    }

    private async Task SeedAsync(bool enabled)
    {
        var services = BuildServices();
        var factory = services.GetRequiredService<IDbContextFactory<AaronOsDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await SchemaBootstrapper.EnsureSchemaAsync(db);
        db.Add(new TradingConfig { IsEnabled = enabled, CycleIntervalMinutes = 60 });
        await db.SaveChangesAsync();
        await services.DisposeAsync();
    }

    [Fact]
    public async Task WithTradingEnabledTheScheduleArmsWithoutAnyPageBeingOpened()
    {
        await SeedAsync(enabled: true);

        var services = BuildServices();
        var scheduler = services.GetRequiredService<TradingScheduler>();
        Assert.False(scheduler.IsRunning);

        await new TradingModule().OnStartupAsync(services);

        Assert.True(scheduler.IsRunning);

        scheduler.Stop();
        await services.DisposeAsync();
    }

    [Fact]
    public async Task WithTradingSwitchedOffNothingIsArmed()
    {
        // The switch in the database is the whole off-ramp: stopping the experiment must not depend on
        // remembering never to press Start.
        await SeedAsync(enabled: false);

        var services = BuildServices();
        var scheduler = services.GetRequiredService<TradingScheduler>();

        await new TradingModule().OnStartupAsync(services);

        Assert.False(scheduler.IsRunning);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task WithNoConfigurationAtAllStartupIsAQuietNoOp()
    {
        var services = BuildServices();
        var factory = services.GetRequiredService<IDbContextFactory<AaronOsDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await SchemaBootstrapper.EnsureSchemaAsync(db);
        }

        // A first-ever launch has no row; that must not throw on the way to showing the window.
        await new TradingModule().OnStartupAsync(services);

        Assert.False(services.GetRequiredService<TradingScheduler>().IsRunning);
        await services.DisposeAsync();
    }

    [Fact]
    public void TheSchedulerIsASingletonSoRepeatedStartsCannotOrphanTimers()
    {
        var services = BuildServices();

        Assert.Same(
            services.GetRequiredService<TradingScheduler>(),
            services.GetRequiredService<TradingScheduler>());

        services.Dispose();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try
            {
                File.Delete(_dbPath + suffix);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless.
            }
        }

        GC.SuppressFinalize(this);
    }
}

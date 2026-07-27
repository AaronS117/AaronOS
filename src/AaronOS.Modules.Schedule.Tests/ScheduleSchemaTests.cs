using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.Tests;

public class ScheduleSchemaTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aaronos-sched-{Guid.NewGuid():N}.db");

    private AaronOsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AaronOsDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        IAppModule[] modules = [new ScheduleModule()];
        return new AaronOsDbContext(options, modules);
    }

    [Fact]
    public async Task ScheduleBlock_RoundTrips()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.Add(new ScheduleBlock
        {
            Kind = ScheduleBlockKind.Work,
            Label = "Core hours",
            DaysOfWeek = DayOfWeekFlags.Monday | DayOfWeekFlags.Friday,
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(17, 0, 0),
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var loaded = await db.Set<ScheduleBlock>().SingleAsync();
        Assert.Equal(ScheduleBlockKind.Work, loaded.Kind);
        Assert.Equal(DayOfWeekFlags.Monday | DayOfWeekFlags.Friday, loaded.DaysOfWeek);
        Assert.Equal(new TimeSpan(8, 0, 0), loaded.StartTime);
        Assert.Null(loaded.EffectiveTo);
    }

    [Fact]
    public void DayOfWeekFlags_MapsEveryDayOfWeek()
    {
        Assert.Equal(DayOfWeekFlags.Sunday, DayOfWeekFlagsExtensions.From(DayOfWeek.Sunday));
        Assert.Equal(DayOfWeekFlags.Wednesday, DayOfWeekFlagsExtensions.From(DayOfWeek.Wednesday));
        Assert.Equal(DayOfWeekFlags.Saturday, DayOfWeekFlagsExtensions.From(DayOfWeek.Saturday));
    }

    [Fact]
    public async Task ScheduleException_RoundTripsBothShapes()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        var block = new ScheduleBlock
        {
            Kind = ScheduleBlockKind.Work,
            Label = "Core hours",
            DaysOfWeek = DayOfWeekFlags.Weekdays,
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(17, 0, 0),
            EffectiveFrom = new DateOnly(2026, 1, 1),
        };
        db.Add(block);
        await db.SaveChangesAsync();

        // A cancellation of a template block (PTO).
        db.Add(new ScheduleException
        {
            Date = new DateOnly(2026, 7, 3),
            ScheduleBlockId = block.Id,
            IsCancelled = true,
            Note = "PTO",
        });
        // A standalone one-off entry with no parent block (a late night).
        db.Add(new ScheduleException
        {
            Date = new DateOnly(2026, 7, 6),
            Kind = ScheduleBlockKind.Work,
            Label = "Deploy window",
            StartTime = new TimeSpan(20, 0, 0),
            EndTime = new TimeSpan(23, 0, 0),
        });
        await db.SaveChangesAsync();

        var loaded = await db.Set<ScheduleException>().OrderBy(e => e.Date).ToListAsync();
        Assert.True(loaded[0].IsCancelled);
        Assert.Equal(block.Id, loaded[0].ScheduleBlockId);
        Assert.Null(loaded[1].ScheduleBlockId);
        Assert.Equal("Deploy window", loaded[1].Label);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}

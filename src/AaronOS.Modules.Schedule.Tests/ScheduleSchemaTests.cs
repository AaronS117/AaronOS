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
        await using (var db = CreateContext())
        {
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
        }

        // Fresh context against the same file: forces a genuine reload, so the assertions
        // below check what SQLite actually stored, not the objects this test constructed.
        await using var verify = CreateContext();
        var loaded = await verify.Set<ScheduleBlock>().SingleAsync();
        Assert.Equal(ScheduleBlockKind.Work, loaded.Kind);
        Assert.Equal("Core hours", loaded.Label);
        Assert.Equal(DayOfWeekFlags.Monday | DayOfWeekFlags.Friday, loaded.DaysOfWeek);
        Assert.Equal(new TimeSpan(8, 0, 0), loaded.StartTime);
        Assert.Equal(new TimeSpan(17, 0, 0), loaded.EndTime);
        Assert.Equal(new DateOnly(2026, 1, 1), loaded.EffectiveFrom);
        Assert.Null(loaded.EffectiveTo);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public void DayOfWeekFlags_MapsEveryDayOfWeek()
    {
        // Named flags, independent of the shift expression this test is guarding.
        DayOfWeekFlags[] expectedByOrdinal =
        [
            DayOfWeekFlags.Sunday, DayOfWeekFlags.Monday, DayOfWeekFlags.Tuesday, DayOfWeekFlags.Wednesday,
            DayOfWeekFlags.Thursday, DayOfWeekFlags.Friday, DayOfWeekFlags.Saturday,
        ];

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            Assert.Equal(expectedByOrdinal[(int)day], DayOfWeekFlagsExtensions.From(day));
        }
    }

    [Fact]
    public async Task ScheduleException_RoundTripsBothShapes()
    {
        int blockId;
        await using (var db = CreateContext())
        {
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

            blockId = block.Id;
        }

        // Fresh context against the same file: forces a genuine reload, so the assertions
        // below check what SQLite actually stored, not the objects this test constructed.
        await using var verify = CreateContext();
        var loaded = await verify.Set<ScheduleException>().OrderBy(e => e.Date).ToListAsync();
        Assert.True(loaded[0].IsCancelled);
        Assert.Equal(blockId, loaded[0].ScheduleBlockId);
        Assert.Null(loaded[1].ScheduleBlockId);
        Assert.Equal("Deploy window", loaded[1].Label);
    }

    [Fact]
    public async Task DeletingABlock_RemovesOnlyItsDependentExceptions()
    {
        // There is deliberately no FK between ScheduleException.ScheduleBlockId and ScheduleBlock,
        // so WeekViewModel.DeleteBlockAsync reclaims dependent rows by hand. This test pins that
        // contract at the data layer.
        int blockId;
        await using (var db = CreateContext())
        {
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
            blockId = block.Id;

            // Dependent on the block that will be deleted.
            db.Add(new ScheduleException
            {
                Date = new DateOnly(2026, 7, 6),
                ScheduleBlockId = blockId,
                IsCancelled = true,
                Note = "PTO",
            });
            // Unrelated standalone exception (no ScheduleBlockId) — must survive.
            db.Add(new ScheduleException
            {
                Date = new DateOnly(2026, 7, 7),
                Kind = ScheduleBlockKind.Work,
                Label = "Deploy window",
                StartTime = new TimeSpan(20, 0, 0),
                EndTime = new TimeSpan(23, 0, 0),
            });
            await db.SaveChangesAsync();
        }

        // Mirrors WeekViewModel.DeleteBlockAsync exactly: dependents removed by hand, then the
        // block, in one SaveChangesAsync.
        await using (var db = CreateContext())
        {
            var dependents = await db.Set<ScheduleException>()
                .Where(e => e.ScheduleBlockId == blockId)
                .ToListAsync();
            db.RemoveRange(dependents);

            db.Remove(await db.Set<ScheduleBlock>().SingleAsync(b => b.Id == blockId));
            await db.SaveChangesAsync();
        }

        // Fresh context: forces a genuine reload, so this checks what SQLite actually stored.
        await using var verify = CreateContext();
        var survivor = Assert.Single(await verify.Set<ScheduleException>().ToListAsync());
        Assert.Null(survivor.ScheduleBlockId);
        Assert.Equal("Deploy window", survivor.Label);
        Assert.Equal(0, await verify.Set<ScheduleBlock>().CountAsync());
    }

    [Fact]
    public async Task Routine_CascadeDeletesItsCompletions()
    {
        int routineId;
        await using (var seed = CreateContext())
        {
            await seed.Database.EnsureCreatedAsync();
            var litter = new Routine
            {
                Name = "Scoop litter box",
                Category = RoutineCategory.LitterBox,
                IntervalDays = 2,
                EstimatedMinutes = 5,
            };
            seed.Add(litter);
            await seed.SaveChangesAsync();
            routineId = litter.Id;

            seed.Add(new RoutineCompletion { RoutineId = routineId, CompletedAt = new DateTime(2026, 7, 6, 21, 0, 0) });
            await seed.SaveChangesAsync();
        }

        // Verify the completion was inserted
        await using (var verifySeeded = CreateContext())
        {
            Assert.Equal(1, await verifySeeded.Set<RoutineCompletion>().CountAsync());
        }

        // Delete the routine in a fresh context that has no completions tracked.
        // This forces the delete to rely on the ON DELETE CASCADE in the SQLite schema,
        // not on EF's client-side cascade of tracked entities.
        await using (var deleter = CreateContext())
        {
            await deleter.Set<Routine>().Where(r => r.Id == routineId).ExecuteDeleteAsync();
        }

        // Verify both routine and its completions were deleted
        await using (var verifyDeleted = CreateContext())
        {
            Assert.Equal(0, await verifyDeleted.Set<RoutineCompletion>().CountAsync());
            Assert.Equal(0, await verifyDeleted.Set<Routine>().CountAsync());
        }
    }

    [Fact]
    public async Task Routine_StoresAWeekdayPinnedShape()
    {
        await using (var db = CreateContext())
        {
            await db.Database.EnsureCreatedAsync();

            db.Add(new Routine
            {
                Name = "Take out trash",
                Category = RoutineCategory.Trash,
                PreferredDaysOfWeek = DayOfWeekFlags.Tuesday,
                PreferredTimeOfDay = new TimeSpan(20, 0, 0),
            });
            await db.SaveChangesAsync();
        }

        // Fresh context against the same file: forces a genuine reload, so the assertions
        // below check what SQLite actually stored, not the objects this test constructed.
        await using var verify = CreateContext();
        var loaded = await verify.Set<Routine>().SingleAsync();
        Assert.Null(loaded.IntervalDays);
        Assert.Equal(DayOfWeekFlags.Tuesday, loaded.PreferredDaysOfWeek);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}

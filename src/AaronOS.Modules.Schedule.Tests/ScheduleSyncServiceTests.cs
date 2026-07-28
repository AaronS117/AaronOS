using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.Tests;

/// <summary>
/// Covers the fail-soft contract, not the merge decision itself (that's ExternalEventMergerTests).
/// "A failed fetch must never empty the cache" is the single most important guarantee in this
/// plan — if it regresses, the user's whole day disappears from Today with no error dialog. Task 9
/// checks this too, but manually against a live feed, so this is the only automated coverage on any
/// machine without credentials.
/// </summary>
public class ScheduleSyncServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aaronos-sync-{Guid.NewGuid():N}.db");
    private readonly TestContextFactory _factory;

    public ScheduleSyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<AaronOsDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _factory = new TestContextFactory(options);

        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    private sealed class TestContextFactory(DbContextOptions<AaronOsDbContext> options)
        : IDbContextFactory<AaronOsDbContext>
    {
        private static readonly IAppModule[] Modules = [new ScheduleModule()];

        public AaronOsDbContext CreateDbContext() => new(options, Modules);
    }

    private sealed class FakeSource(CalendarProvider provider, Func<IReadOnlyList<ExternalEventDto>> fetch)
        : IExternalCalendarSource
    {
        public CalendarProvider Provider => provider;

        public Task<IReadOnlyList<ExternalEventDto>> FetchAsync(
            ExternalCalendar calendar, DateOnly from, DateOnly to, CancellationToken cancellationToken)
            => Task.FromResult(fetch());
    }

    [Fact]
    public async Task FailedFetch_KeepsCachedEventsAndRecordsTheError()
    {
        int calendarId;
        await using (var db = _factory.CreateDbContext())
        {
            var priorSync = new DateTime(2026, 7, 1, 8, 0, 0);
            var calendar = new ExternalCalendar
            {
                Provider = CalendarProvider.OutlookIcs, DisplayName = "Work", IcsUrl = "https://example/a.ics",
                LastSyncedAt = priorSync,
            };
            db.Add(calendar);
            await db.SaveChangesAsync();
            calendarId = calendar.Id;

            db.AddRange(
                new ExternalEvent
                {
                    ExternalCalendarId = calendarId, ExternalUid = "uid-1", Title = "Standup",
                    StartsAt = new DateTime(2026, 7, 6, 9, 30, 0), EndsAt = new DateTime(2026, 7, 6, 10, 0, 0),
                    LastSeenAt = new DateTime(2026, 7, 6, 8, 0, 0),
                },
                new ExternalEvent
                {
                    ExternalCalendarId = calendarId, ExternalUid = "uid-2", Title = "Retro",
                    StartsAt = new DateTime(2026, 7, 6, 15, 0, 0), EndsAt = new DateTime(2026, 7, 6, 16, 0, 0),
                    LastSeenAt = new DateTime(2026, 7, 6, 8, 0, 0),
                });
            await db.SaveChangesAsync();
        }

        var source = new FakeSource(CalendarProvider.OutlookIcs,
            () => throw new InvalidOperationException("feed unreachable"));
        var service = new ScheduleSyncService(_factory, [source]);

        await service.SyncOneAsync(calendarId, CancellationToken.None);

        await using var verify = _factory.CreateDbContext();
        Assert.Equal(2, await verify.Set<ExternalEvent>().CountAsync(e => e.ExternalCalendarId == calendarId));
        var calendarAfter = await verify.Set<ExternalCalendar>().SingleAsync(c => c.Id == calendarId);
        Assert.NotNull(calendarAfter.LastError);
        Assert.Contains("feed unreachable", calendarAfter.LastError);
        // Seeded with a known prior value (not just null) so this distinguishes "not advanced" from
        // "never set".
        Assert.Equal(new DateTime(2026, 7, 1, 8, 0, 0), calendarAfter.LastSyncedAt);
    }

    [Fact]
    public async Task OneBrokenCalendarDoesNotStopTheOthers()
    {
        int brokenId, healthyId;
        await using (var db = _factory.CreateDbContext())
        {
            var broken = new ExternalCalendar
            {
                Provider = CalendarProvider.OutlookIcs, DisplayName = "Work", IcsUrl = "https://example/a.ics",
            };
            var healthy = new ExternalCalendar
            {
                Provider = CalendarProvider.GoogleCalendar, DisplayName = "Personal", RemoteCalendarId = "primary",
            };
            db.AddRange(broken, healthy);
            await db.SaveChangesAsync();
            brokenId = broken.Id;
            healthyId = healthy.Id;

            db.Add(new ExternalEvent
            {
                ExternalCalendarId = brokenId, ExternalUid = "uid-1", Title = "Standup",
                StartsAt = new DateTime(2026, 7, 6, 9, 30, 0), EndsAt = new DateTime(2026, 7, 6, 10, 0, 0),
                LastSeenAt = new DateTime(2026, 7, 6, 8, 0, 0),
            });
            await db.SaveChangesAsync();
        }

        var today = DateTime.Now.Date;
        var brokenSource = new FakeSource(CalendarProvider.OutlookIcs,
            () => throw new InvalidOperationException("feed unreachable"));
        var healthySource = new FakeSource(CalendarProvider.GoogleCalendar,
            () => new List<ExternalEventDto>
            {
                new("uid-9", "Dentist", today.AddHours(13), today.AddHours(14), false, null, true),
            });
        var service = new ScheduleSyncService(_factory, [brokenSource, healthySource]);

        var succeeded = await service.SyncAllAsync(CancellationToken.None);

        Assert.Equal(1, succeeded);

        await using var verify = _factory.CreateDbContext();
        var healthyEvent = await verify.Set<ExternalEvent>().SingleAsync(e => e.ExternalCalendarId == healthyId);
        Assert.Equal("Dentist", healthyEvent.Title);

        Assert.Equal(1, await verify.Set<ExternalEvent>().CountAsync(e => e.ExternalCalendarId == brokenId));
        var brokenCalendar = await verify.Set<ExternalCalendar>().SingleAsync(c => c.Id == brokenId);
        Assert.NotNull(brokenCalendar.LastError);
    }

    [Fact]
    public async Task SuccessfulSyncClearsAPreviousError()
    {
        int calendarId;
        await using (var db = _factory.CreateDbContext())
        {
            var calendar = new ExternalCalendar
            {
                Provider = CalendarProvider.GoogleCalendar, DisplayName = "Personal", RemoteCalendarId = "primary",
                LastError = "previous failure: feed unreachable",
            };
            db.Add(calendar);
            await db.SaveChangesAsync();
            calendarId = calendar.Id;
        }

        var today = DateTime.Now.Date;
        var source = new FakeSource(CalendarProvider.GoogleCalendar,
            () => new List<ExternalEventDto>
            {
                new("uid-1", "Dentist", today.AddHours(13), today.AddHours(14), false, null, true),
            });
        var service = new ScheduleSyncService(_factory, [source]);

        await service.SyncOneAsync(calendarId, CancellationToken.None);

        await using var verify = _factory.CreateDbContext();
        var calendarAfter = await verify.Set<ExternalCalendar>().SingleAsync(c => c.Id == calendarId);
        Assert.Null(calendarAfter.LastError);
        Assert.NotNull(calendarAfter.LastSyncedAt);
    }

    [Fact]
    public async Task EventStraddlingWindowStart_UpdatesInsteadOfCollidingOnInsert()
    {
        // A long-running event (a multi-week holiday, an all-day "Project X" block) that started
        // before the sync window but is still in progress when the window opens. IcsFeedClient
        // returns it — Ical.Net's GetOccurrences(windowStart) includes an occurrence still running
        // at windowStart even though it began earlier. A StartsAt-only "existing" query would miss
        // this cached row, the merger would plan it as an insert, and the insert would collide with
        // the composite unique index on (ExternalCalendarId, ExternalUid) — permanently failing
        // every sync for as long as the event runs. Dates are derived from DateTime.Now, matching
        // what the service itself reads, rather than hard-coded.
        var today = DateTime.Now.Date;
        var longEventStart = today.AddDays(-30); // well before the 14-day-back window edge
        var longEventEnd = today.AddDays(-10); // after the window edge — still "ongoing" at windowStart

        int calendarId;
        await using (var db = _factory.CreateDbContext())
        {
            var calendar = new ExternalCalendar
            {
                Provider = CalendarProvider.OutlookIcs, DisplayName = "Work", IcsUrl = "https://example/a.ics",
            };
            db.Add(calendar);
            await db.SaveChangesAsync();
            calendarId = calendar.Id;

            db.Add(new ExternalEvent
            {
                ExternalCalendarId = calendarId, ExternalUid = "uid-long", Title = "Holiday",
                StartsAt = longEventStart, EndsAt = longEventEnd, IsAllDay = true,
                LastSeenAt = today.AddDays(-31),
            });
            await db.SaveChangesAsync();
        }

        var source = new FakeSource(CalendarProvider.OutlookIcs,
            () => new List<ExternalEventDto>
            {
                new("uid-long", "Holiday (extended)", longEventStart, longEventEnd, true, null, true),
            });
        var service = new ScheduleSyncService(_factory, [source]);

        var succeeded = await service.SyncAllAsync(CancellationToken.None);

        Assert.Equal(1, succeeded);

        await using var verify = _factory.CreateDbContext();
        var calendarAfter = await verify.Set<ExternalCalendar>().SingleAsync(c => c.Id == calendarId);
        Assert.Null(calendarAfter.LastError);

        var row = await verify.Set<ExternalEvent>().SingleAsync(e => e.ExternalUid == "uid-long");
        Assert.Equal("Holiday (extended)", row.Title);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}

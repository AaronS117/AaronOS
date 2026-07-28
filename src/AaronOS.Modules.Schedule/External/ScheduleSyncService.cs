using System.Diagnostics;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// Fetch, merge, persist, record outcome — once per enabled calendar. Every failure is contained to
/// the calendar that caused it: one broken feed must not stop the others, and must not throw into
/// the UI or take down the background tick.
/// </summary>
public sealed class ScheduleSyncService(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    IEnumerable<IExternalCalendarSource> sources)
{
    /// <summary>How far back and forward to sync. Two weeks back keeps recent history for context;
    /// eight weeks forward comfortably covers the suggestion engine's 7-day lookahead.</summary>
    private const int DaysBack = 14;
    private const int DaysForward = 56;

    /// <returns>How many calendars synced successfully.</returns>
    public async Task<int> SyncAllAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var calendars = await db.Set<ExternalCalendar>().Where(c => c.IsEnabled).ToListAsync(cancellationToken);

        var succeeded = 0;
        foreach (var calendar in calendars)
        {
            // Checked once per calendar so shutdown is deterministic regardless of what any inner
            // catch below does — the guarantee doesn't depend on every future catch being correct.
            cancellationToken.ThrowIfCancellationRequested();
            if (await SyncCalendarAsync(db, calendar, cancellationToken)) succeeded++;
        }

        return succeeded;
    }

    public async Task SyncOneAsync(int calendarId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var calendar = await db.Set<ExternalCalendar>().SingleOrDefaultAsync(c => c.Id == calendarId, cancellationToken);
        if (calendar is null) return;

        await SyncCalendarAsync(db, calendar, cancellationToken);
    }

    private async Task<bool> SyncCalendarAsync(
        AaronOsDbContext db,
        ExternalCalendar calendar,
        CancellationToken cancellationToken)
    {
        var source = sources.FirstOrDefault(s => s.Provider == calendar.Provider);
        if (source is null)
        {
            await RecordFailureAsync(db, calendar, $"No source registered for {calendar.Provider}.", cancellationToken);
            return false;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var from = today.AddDays(-DaysBack);
        var to = today.AddDays(DaysForward);

        IReadOnlyList<ExternalEventDto> fetched;
        try
        {
            fetched = await source.FetchAsync(calendar, from, to, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // shutdown, not a calendar failure — don't record it as one
        }
        catch (Exception ex)
        {
            // The cache is deliberately left untouched: a failed fetch is not evidence that
            // anything was cancelled, and merging against it would delete real events.
            await RecordFailureAsync(db, calendar, Describe(ex), cancellationToken);
            return false;
        }

        try
        {
            // Overlap test, not a StartsAt-only filter: a source can return an event that started
            // before the window but is still running at windowStart (Ical.Net's
            // GetOccurrences(windowStart) includes an in-progress occurrence). A StartsAt-only
            // query would exclude that cached row from `existing`, the merger would then plan it as
            // an insert, and the insert would collide with the composite unique index on
            // (ExternalCalendarId, ExternalUid) — failing every sync for as long as the event runs.
            var windowStart = from.ToDateTime(TimeOnly.MinValue);
            var windowEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var existing = await db.Set<ExternalEvent>()
                .Where(e => e.ExternalCalendarId == calendar.Id
                            && e.StartsAt < windowEnd && e.EndsAt > windowStart)
                .ToListAsync(cancellationToken);

            var plan = ExternalEventMerger.Plan(existing, fetched);
            var seenAt = DateTime.Now;

            foreach (var dto in plan.ToInsert)
            {
                var entity = new ExternalEvent
                {
                    ExternalCalendarId = calendar.Id,
                    ExternalUid = dto.ExternalUid,
                };
                ExternalEventMerger.CopyInto(dto, entity, seenAt);
                db.Add(entity);
            }

            foreach (var (target, incoming) in plan.ToUpdate)
            {
                ExternalEventMerger.CopyInto(incoming, target, seenAt);
            }

            db.RemoveRange(plan.ToDelete);

            calendar.LastSyncedAt = seenAt;
            calendar.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(db, calendar, Describe(ex), cancellationToken);
            return false;
        }
    }

    private static async Task RecordFailureAsync(
        AaronOsDbContext db,
        ExternalCalendar calendar,
        string message,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine($"ScheduleSyncService: calendar {calendar.Id} ({calendar.DisplayName}) failed: {message}");

        try
        {
            // Discard whatever partial state the failed attempt left tracked, so writing the error
            // doesn't accidentally commit half a merge.
            db.ChangeTracker.Clear();

            var fresh = await db.Set<ExternalCalendar>()
                .SingleOrDefaultAsync(c => c.Id == calendar.Id, cancellationToken);
            if (fresh is null) return;

            fresh.LastError = message.Length > 2000 ? message[..2000] : message;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // shutdown, not a failure to record a failure — let the caller observe it
        }
        catch (Exception ex)
        {
            // Failing to record a failure must not itself throw — there is nothing useful left to do.
            Debug.WriteLine($"ScheduleSyncService: could not record failure: {ex.Message}");
        }
    }

    /// <summary>Includes the inner exception, because an HttpRequestException's own message is
    /// often just "An error occurred while sending the request" with the real cause one level down.</summary>
    private static string Describe(Exception ex) =>
        ex.InnerException is null ? ex.Message : $"{ex.Message} ({ex.InnerException.Message})";
}

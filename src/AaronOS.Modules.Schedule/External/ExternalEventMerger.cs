using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.External;

/// <param name="ToUpdate">Pairs of the tracked entity and the incoming values. The caller applies
/// them with <see cref="ExternalEventMerger.CopyInto"/> so EF change tracking and the row's Id
/// survive, rather than the entity being replaced by a fresh untracked instance.</param>
public sealed record MergePlan(
    IReadOnlyList<ExternalEventDto> ToInsert,
    IReadOnlyList<(ExternalEvent Existing, ExternalEventDto Incoming)> ToUpdate,
    IReadOnlyList<ExternalEvent> ToDelete);

/// <summary>
/// Pure merge logic for one calendar's full-window fetch, mirroring
/// AaronOS.Modules.Finance.Sync.TransactionSyncMerger — no DbContext or IO dependency, so the
/// algorithm is testable against plain lists (see ExternalEventMergerTests).
///
/// Deletion is driven by absence from the fetch, which is only sound because the caller passes a
/// *successful* full-window fetch. A partial or failed fetch must never reach this method, or it
/// would delete events that still exist.
/// </summary>
public static class ExternalEventMerger
{
    public static MergePlan Plan(
        IReadOnlyList<ExternalEvent> existing,
        IReadOnlyList<ExternalEventDto> fetched)
    {
        // This method plans one calendar's fetch at a time (see class doc). ExternalUid is only
        // unique per calendar, so a mixed-calendar `existing` list is invalid input regardless of
        // whether any UID actually collides: rows whose UIDs happen to differ would pass straight
        // through ToDictionary and get planned as if they belonged to one calendar, silently wrong
        // rather than failing loudly. This check rejects all mixed-calendar input at the seam,
        // rather than relying on a UID collision to surface it as a dictionary exception.
        if (existing.DistinctBy(e => e.ExternalCalendarId).Count() > 1)
        {
            throw new ArgumentException(
                "Plan() merges one calendar's fetch at a time, but 'existing' contains rows from " +
                "more than one ExternalCalendarId. Scope 'existing' to a single calendar before calling.",
                nameof(existing));
        }

        // Within one calendar, ExternalUid is unique (composite unique index on ExternalCalendarId
        // + ExternalUid), so a plain ToDictionary is correct here and throws on a genuine
        // duplicate rather than silently keeping one row and dropping the other.
        var existingByUid = existing.ToDictionary(e => e.ExternalUid);

        // Last wins on a duplicated UID: a malformed feed shouldn't fail the whole sync.
        var fetchedByUid = new Dictionary<string, ExternalEventDto>();
        foreach (var dto in fetched) fetchedByUid[dto.ExternalUid] = dto;

        var toInsert = new List<ExternalEventDto>();
        var toUpdate = new List<(ExternalEvent, ExternalEventDto)>();

        foreach (var (uid, dto) in fetchedByUid)
        {
            if (existingByUid.TryGetValue(uid, out var match))
            {
                toUpdate.Add((match, dto));
            }
            else
            {
                toInsert.Add(dto);
            }
        }

        var toDelete = existing.Where(e => !fetchedByUid.ContainsKey(e.ExternalUid)).ToList();

        return new MergePlan(toInsert, toUpdate, toDelete);
    }

    /// <summary>Copies a DTO's fields onto an existing entity in place, so a tracked EF Core entity
    /// keeps its identity and Id. Used for updates and, after construction, for inserts — one code
    /// path, so the two cannot drift apart.</summary>
    public static void CopyInto(ExternalEventDto dto, ExternalEvent target, DateTime seenAt)
    {
        target.Title = dto.Title;
        target.StartsAt = dto.StartsAt;
        target.EndsAt = dto.EndsAt;
        target.IsAllDay = dto.IsAllDay;
        target.Location = dto.Location;
        target.IsBusy = dto.IsBusy;
        target.LastSeenAt = seenAt;
    }
}

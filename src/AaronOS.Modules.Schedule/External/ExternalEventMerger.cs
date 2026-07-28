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
        var existingByUid = existing
            .GroupBy(e => e.ExternalUid)
            .ToDictionary(g => g.Key, g => g.First());

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

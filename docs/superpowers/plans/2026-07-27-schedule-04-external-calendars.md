# Schedule Module — Plan 4: External Calendars

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Read the work Outlook calendar from a published ICS feed and the personal Google Calendar from the Calendar API, cache both into local tables, and merge them into the agenda — read-only, failing soft.

**Architecture:** Two sources behind one `IExternalCalendarSource` interface, a pure `ExternalEventMerger` that decides the resulting row set, and a `ScheduleSyncService` that orchestrates fetch → merge → persist per calendar and records success or failure on the row. Google's OAuth token lands in the app database under DPAPI via a small `IDataStore`, matching how Plaid and USDA credentials are already stored.

**Tech Stack:** Adds `Ical.Net` 5.2.3, `Google.Apis.Calendar.v3` 1.75.0, `Google.Apis.Auth`, and `System.Security.Cryptography.ProtectedData` — the last of these is added by **Task 7**, the first task that needs DPAPI. Plan 1 deliberately does not reference it.

**Spec:** `docs/superpowers/specs/2026-07-27-schedule-module-design.md` — this plan covers phases 7 and 8.

**Prerequisite:** Plans 1–3 complete. `AgendaBuilder.Build` already accepts an `IReadOnlyList<ExternalEventEntry>` parameter and merges busy events — that path is tested and unused until this plan fills it.

## Global Constraints

- Target framework `net8.0-windows`; `LangVersion` `13.0`; `Nullable` `enable`.
- **Never use the partial-property `[ObservableProperty]` form.**
- **Read-only. Never write to an external calendar.** No create, update, delete, or RSVP against Outlook or Google, at any point, for any reason.
- **Every external call fails soft.** A fetch or parse failure records `ExternalCalendar.LastError` and leaves cached rows untouched. Nothing may throw into the UI or take down the background tick.
- Secrets are DPAPI-protected with `DataProtectionScope.CurrentUser`, following `Finance.Plaid.AccessTokenProtector`. **Never** store a token, refresh token, or client secret in plaintext, in a settings file, or in source.
- Times from external sources arrive with time-zone information; convert to **local wall clock** at the boundary and store `DateTime` local, never `DateTimeOffset`. `AgendaBuilder` works in local wall-clock `TimeSpan`s.
- Pure services (`ExternalEventMerger`) take values and return values. No `DbContext`, no `HttpClient`, no clock.
- Run tests with `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`.
- **Database-backed tests must write and read through separate `DbContext` instances.** EF Core's
  identity resolution returns already-tracked entities, so a query issued on the same context that
  performed the insert asserts against the objects the test just constructed — not what SQLite
  actually stored. That defeats the entire reason these tests use a real file-backed database: an
  asymmetric value converter, or a nullable `TimeSpan?`/`DateOnly?` that reads back as zero instead
  of null, would pass. Write in one context, dispose it, then verify through a fresh one against the
  same `_dbPath`:

  ```csharp
  await using (var db = CreateContext())
  {
      await db.Database.EnsureCreatedAsync();
      db.Add(/* ... */);
      await db.SaveChangesAsync();
  }

  // Fresh context, same file: the assertions below now check what SQLite stored.
  await using var verify = CreateContext();
  var loaded = await verify.Set<T>().SingleAsync();
  ```

  **This constraint governs the task bodies below.** Where a task's test code still shows a single
  `db` used for both the write and the read, restructure it to the shape above. If an assertion then
  fails, that is a real mapping bug the single-context pattern was hiding — report it; do not adjust
  the assertion to match what you observe.

## Task 0 (do this before writing any code): confirm the Outlook path exists

**This is a gate, not a formality.** Phase 7's entire design assumes the `wemautomation.com` tenant permits publishing a calendar, and tenants frequently disable it. Find out first.

- [ ] In Outlook on the web, signed in with the work account, go to **Settings → Calendar → Shared calendars → Publish a calendar**.
- [ ] Select the calendar, choose the **Can view all details** permission, and publish.
- [ ] Copy the **ICS** link (not the HTML link).
- [ ] Verify it actually serves data from this machine: `curl -sS "<the ics url>" | head -20`
      Expected: text beginning `BEGIN:VCALENDAR`. An HTML error page, a 403, or a login redirect means the link is not anonymously fetchable.

**If publishing is unavailable or the URL does not serve `BEGIN:VCALENDAR`:** stop and report it. Do Tasks 1, 2, 3, and 6 (entities, merger, the sync service, and the agenda wiring) plus Tasks 7–9 (Google), and skip Tasks 4 and 5 (the ICS source and its settings row). The work calendar then stays manual until a Microsoft Graph app registration is approved in the tenant, which is a separate piece of work — `IExternalCalendarSource` is the seam a Graph provider drops into, and nothing else in the module is blocked. Record the outcome in the commit message so the next person does not re-run this gate blind.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/AaronOS.Modules.Schedule/Data/ExternalCalendar.cs` (+`Configuration`) | One configured calendar and its sync state |
| `src/AaronOS.Modules.Schedule/Data/ExternalEvent.cs` (+`Configuration`) | One cached event |
| `src/AaronOS.Modules.Schedule/External/ExternalEventDto.cs` | What a source returns, before persistence |
| `src/AaronOS.Modules.Schedule/External/IExternalCalendarSource.cs` | The fetch seam (ICS today, Graph later) |
| `src/AaronOS.Modules.Schedule/External/ExternalEventMerger.cs` | Pure upsert/delete decision |
| `src/AaronOS.Modules.Schedule/External/IcsFeedClient.cs` | HTTP GET + Ical.Net parse |
| `src/AaronOS.Modules.Schedule/External/GoogleCalendarClient.cs` | Google Calendar API read |
| `src/AaronOS.Modules.Schedule/External/DpapiDataStore.cs` | DPAPI-backed Google token store |
| `src/AaronOS.Modules.Schedule/External/GoogleCredentialProvider.cs` | OAuth consent + credential caching |
| `src/AaronOS.Modules.Schedule/External/ScheduleSyncService.cs` | Orchestration, error recording |
| `src/AaronOS.Modules.Schedule/ViewModels/ScheduleSettingsViewModel.cs` | Settings section state |
| `src/AaronOS.Modules.Schedule/Views/ScheduleSettingsSection.xaml(.cs)` | `UserControl` contributed to app Settings |
| `src/AaronOS.Modules.Schedule.Tests/ExternalEventMergerTests.cs` | Merge tests |
| `src/AaronOS.Modules.Schedule.Tests/IcsFeedClientTests.cs` | Parse tests |
| `src/AaronOS.Modules.Schedule.Tests/Fixtures/sample-calendar.ics` | Checked-in ICS fixture |

---

## Task 1: External calendar entities

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Data/ExternalCalendar.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/ExternalCalendarConfiguration.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/ExternalEvent.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/ExternalEventConfiguration.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs`

**Interfaces:**
- Consumes: `CalendarProvider` from Plan 1's `ScheduleEnums.cs`.
- Produces: `ExternalCalendar` (`Id`, `Provider`, `DisplayName`, `IcsUrl`, `RemoteCalendarId`, `EncryptedToken`, `IsEnabled`, `LastSyncedAt`, `LastError`), `ExternalEvent` (`Id`, `ExternalCalendarId`, `ExternalUid`, `Title`, `StartsAt`, `EndsAt`, `IsAllDay`, `Location`, `IsBusy`, `LastSeenAt`).

- [ ] **Step 1: Write the failing test**

Add to `ScheduleSchemaTests`:

> ⚠️ **The test code below uses one `db` for both the write and the read. That is stale — restructure it before running.** EF Core's identity resolution returns the already-tracked entity, so asserting through the context that performed the insert checks the object the test constructed, not what SQLite stored: a broken value converter would pass. Write in one context, dispose it, then verify through a fresh `CreateContext()` against the same `_dbPath`. And where a test deletes to prove a cascade, the deleting context must not load or track the children — otherwise it proves EF's client-side cascade rather than the database foreign key. Use `ExecuteDeleteAsync` or a key-only attached stub there.

```csharp
    [Fact]
    public async Task ExternalEvent_UidIsUniquePerCalendar_ButSharedAcrossCalendars()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        var work = new ExternalCalendar { Provider = CalendarProvider.OutlookIcs, DisplayName = "Work", IcsUrl = "https://example/a.ics" };
        var personal = new ExternalCalendar { Provider = CalendarProvider.GoogleCalendar, DisplayName = "Personal", RemoteCalendarId = "primary" };
        db.AddRange(work, personal);
        await db.SaveChangesAsync();

        db.Add(new ExternalEvent
        {
            ExternalCalendarId = work.Id, ExternalUid = "uid-1", Title = "Standup",
            StartsAt = new DateTime(2026, 7, 6, 9, 30, 0), EndsAt = new DateTime(2026, 7, 6, 10, 0, 0),
            IsBusy = true, LastSeenAt = new DateTime(2026, 7, 6, 8, 0, 0),
        });
        // The same UID on a different calendar is legitimate — two feeds can carry the same event.
        db.Add(new ExternalEvent
        {
            ExternalCalendarId = personal.Id, ExternalUid = "uid-1", Title = "Standup (personal copy)",
            StartsAt = new DateTime(2026, 7, 6, 9, 30, 0), EndsAt = new DateTime(2026, 7, 6, 10, 0, 0),
            IsBusy = true, LastSeenAt = new DateTime(2026, 7, 6, 8, 0, 0),
        });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Set<ExternalEvent>().CountAsync());

        // A duplicate UID on the SAME calendar must be rejected — that index is what makes
        // re-syncing idempotent rather than accumulating duplicates.
        db.Add(new ExternalEvent
        {
            ExternalCalendarId = work.Id, ExternalUid = "uid-1", Title = "Duplicate",
            StartsAt = new DateTime(2026, 7, 6, 9, 30, 0), EndsAt = new DateTime(2026, 7, 6, 10, 0, 0),
            LastSeenAt = new DateTime(2026, 7, 6, 8, 0, 0),
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ExternalCalendar_CascadeDeletesItsEvents()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        var calendar = new ExternalCalendar { Provider = CalendarProvider.OutlookIcs, DisplayName = "Work", IcsUrl = "https://example/a.ics" };
        db.Add(calendar);
        await db.SaveChangesAsync();

        db.Add(new ExternalEvent
        {
            ExternalCalendarId = calendar.Id, ExternalUid = "uid-1", Title = "Standup",
            StartsAt = new DateTime(2026, 7, 6, 9, 30, 0), EndsAt = new DateTime(2026, 7, 6, 10, 0, 0),
            LastSeenAt = new DateTime(2026, 7, 6, 8, 0, 0),
        });
        await db.SaveChangesAsync();

        db.Remove(calendar);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.Set<ExternalEvent>().CountAsync());
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `ExternalCalendar` and `ExternalEvent`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Data/ExternalCalendar.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// One configured external calendar, plus the outcome of its last sync. Both the success timestamp
/// and the error text live on the row rather than in a log, so the Settings page can show what
/// happened without the user going looking.
/// </summary>
public class ExternalCalendar
{
    public int Id { get; set; }
    public CalendarProvider Provider { get; set; }
    public string DisplayName { get; set; } = "";

    /// <summary>The published-calendar ICS URL. <see cref="CalendarProvider.OutlookIcs"/> only.</summary>
    public string? IcsUrl { get; set; }

    /// <summary>Google's calendar id, usually "primary". <see cref="CalendarProvider.GoogleCalendar"/> only.</summary>
    public string? RemoteCalendarId { get; set; }

    /// <summary>DPAPI-protected OAuth token blob (current-user scope). Google only; null for ICS,
    /// which is anonymous. Never store this value in plaintext.</summary>
    public byte[]? EncryptedToken { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Null after a successful sync; the failure message otherwise.</summary>
    public string? LastError { get; set; }
}
```

`src/AaronOS.Modules.Schedule/Data/ExternalCalendarConfiguration.cs`:

```csharp
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
```

`src/AaronOS.Modules.Schedule/Data/ExternalEvent.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// A cached external event. Cached rather than fetched live because the suggestion engine has to
/// reason about tomorrow's commitments offline, and a published-ICS feed is slow enough that
/// re-fetching on every navigation would make the UI feel broken.
///
/// Times are local wall clock, converted at the source boundary.
/// </summary>
public class ExternalEvent
{
    public int Id { get; set; }
    public int ExternalCalendarId { get; set; }

    /// <summary>The source's own identifier. Unique per calendar — that index is what makes
    /// re-syncing idempotent.</summary>
    public string ExternalUid { get; set; } = "";

    public string Title { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsAllDay { get; set; }
    public string? Location { get; set; }

    /// <summary>False for free/FYI events. AgendaBuilder excludes those entirely: a free event
    /// should not consume a gap or move the recommended bedtime.</summary>
    public bool IsBusy { get; set; } = true;

    /// <summary>When the last sync last saw this event. Diagnostic only — deletion is driven by
    /// absence from a full-window fetch, not by this timestamp going stale.</summary>
    public DateTime LastSeenAt { get; set; }
}
```

`src/AaronOS.Modules.Schedule/Data/ExternalEventConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class ExternalEventConfiguration : IEntityTypeConfiguration<ExternalEvent>
{
    public void Configure(EntityTypeBuilder<ExternalEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.ExternalCalendarId, e.ExternalUid }).IsUnique();
        builder.HasIndex(e => e.StartsAt);
        builder.Property(e => e.ExternalUid).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Location).HasMaxLength(500);
        builder.HasOne<ExternalCalendar>()
            .WithMany()
            .HasForeignKey(e => e.ExternalCalendarId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 72`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Data src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs
git commit -m "Add ExternalCalendar and ExternalEvent entities"
```

---

## Task 2: ExternalEventMerger

**Files:**
- Create: `src/AaronOS.Modules.Schedule/External/ExternalEventDto.cs`
- Create: `src/AaronOS.Modules.Schedule/External/ExternalEventMerger.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/ExternalEventMergerTests.cs`

**Interfaces:**
- Produces:
  - `record ExternalEventDto(string ExternalUid, string Title, DateTime StartsAt, DateTime EndsAt, bool IsAllDay, string? Location, bool IsBusy)`
  - `record MergePlan(IReadOnlyList<ExternalEventDto> ToInsert, IReadOnlyList<(ExternalEvent Existing, ExternalEventDto Incoming)> ToUpdate, IReadOnlyList<ExternalEvent> ToDelete)`
  - `static MergePlan ExternalEventMerger.Plan(IReadOnlyList<ExternalEvent> existing, IReadOnlyList<ExternalEventDto> fetched)`
  - `static void ExternalEventMerger.CopyInto(ExternalEventDto dto, ExternalEvent target, DateTime seenAt)`

**Why a plan rather than a returned list.** `TransactionSyncMerger` in the Finance module returns the final set for tests but does its real persistence separately with `CopyFieldsInto`, so tracked EF entities keep their identity. This does the same thing more directly: `Plan` names the three operations, the caller applies them to tracked entities, and both paths share `CopyInto` so they cannot drift.

- [ ] **Step 1: Write the failing tests**

Create `src/AaronOS.Modules.Schedule.Tests/ExternalEventMergerTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;

namespace AaronOS.Modules.Schedule.Tests;

public class ExternalEventMergerTests
{
    private static readonly DateTime SeenAt = new(2026, 7, 6, 8, 0, 0);

    private static ExternalEvent Existing(string uid, string title, int startHour) => new()
    {
        Id = uid.GetHashCode(), ExternalCalendarId = 1, ExternalUid = uid, Title = title,
        StartsAt = new DateTime(2026, 7, 6, startHour, 0, 0),
        EndsAt = new DateTime(2026, 7, 6, startHour + 1, 0, 0),
        IsBusy = true, LastSeenAt = SeenAt.AddDays(-1),
    };

    private static ExternalEventDto Fetched(string uid, string title, int startHour, bool isBusy = true) =>
        new(uid, title, new DateTime(2026, 7, 6, startHour, 0, 0), new DateTime(2026, 7, 6, startHour + 1, 0, 0),
            IsAllDay: false, Location: null, isBusy);

    [Fact]
    public void NewUid_IsInserted()
    {
        var plan = ExternalEventMerger.Plan([], [Fetched("uid-1", "Standup", 9)]);

        Assert.Equal("uid-1", Assert.Single(plan.ToInsert).ExternalUid);
        Assert.Empty(plan.ToUpdate);
        Assert.Empty(plan.ToDelete);
    }

    [Fact]
    public void KnownUid_IsUpdatedInPlace_NotReinserted()
    {
        var existing = Existing("uid-1", "Standup", 9);

        var plan = ExternalEventMerger.Plan([existing], [Fetched("uid-1", "Standup (moved)", 10)]);

        Assert.Empty(plan.ToInsert);
        Assert.Empty(plan.ToDelete);
        var (target, incoming) = Assert.Single(plan.ToUpdate);
        Assert.Same(existing, target); // the tracked entity, so its Id survives
        Assert.Equal("Standup (moved)", incoming.Title);
    }

    [Fact]
    public void UidAbsentFromTheFetch_IsDeleted()
    {
        var cancelled = Existing("uid-2", "Cancelled meeting", 14);

        var plan = ExternalEventMerger.Plan(
            [Existing("uid-1", "Standup", 9), cancelled],
            [Fetched("uid-1", "Standup", 9)]);

        Assert.Same(cancelled, Assert.Single(plan.ToDelete));
    }

    [Fact]
    public void MergingTheSameBatchTwice_IsIdempotent()
    {
        var existing = Existing("uid-1", "Standup", 9);
        var fetched = Fetched("uid-1", "Standup", 9);

        // Apply the first plan the way the caller would, then re-plan against the result.
        var first = ExternalEventMerger.Plan([existing], [fetched]);
        foreach (var (target, incoming) in first.ToUpdate) ExternalEventMerger.CopyInto(incoming, target, SeenAt);

        var second = ExternalEventMerger.Plan([existing], [fetched]);

        Assert.Empty(second.ToInsert);
        Assert.Empty(second.ToDelete);
        Assert.Single(second.ToUpdate); // an unchanged event still "updates", but to identical values
        Assert.Equal("Standup", existing.Title);
        Assert.Equal(new DateTime(2026, 7, 6, 9, 0, 0), existing.StartsAt);
    }

    [Fact]
    public void DuplicateUidsInOneFetch_KeepTheLastAndDoNotThrow()
    {
        // A malformed feed can repeat a UID. Throwing would fail the whole sync over one bad row.
        var plan = ExternalEventMerger.Plan([], [Fetched("uid-1", "First", 9), Fetched("uid-1", "Second", 11)]);

        var inserted = Assert.Single(plan.ToInsert);
        Assert.Equal("Second", inserted.Title);
    }

    [Fact]
    public void EmptyFetch_DeletesEverything()
    {
        // A calendar that has genuinely been cleared must clear locally too — but note the caller
        // only ever passes a successful full-window fetch here, never a failed one.
        var plan = ExternalEventMerger.Plan([Existing("uid-1", "Standup", 9)], []);

        Assert.Single(plan.ToDelete);
        Assert.Empty(plan.ToInsert);
    }

    [Fact]
    public void CopyInto_OverwritesEveryMutableFieldAndStampsSeenAt()
    {
        var target = Existing("uid-1", "Old title", 9);
        var dto = new ExternalEventDto("uid-1", "New title",
            new DateTime(2026, 7, 6, 15, 0, 0), new DateTime(2026, 7, 6, 16, 0, 0),
            IsAllDay: true, Location: "Room 2", IsBusy: false);

        ExternalEventMerger.CopyInto(dto, target, SeenAt);

        Assert.Equal("New title", target.Title);
        Assert.Equal(new DateTime(2026, 7, 6, 15, 0, 0), target.StartsAt);
        Assert.Equal(new DateTime(2026, 7, 6, 16, 0, 0), target.EndsAt);
        Assert.True(target.IsAllDay);
        Assert.Equal("Room 2", target.Location);
        Assert.False(target.IsBusy);
        Assert.Equal(SeenAt, target.LastSeenAt);
        // Identity must survive — that is the whole reason for copying rather than replacing.
        Assert.Equal("uid-1", target.ExternalUid);
        Assert.Equal(1, target.ExternalCalendarId);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `ExternalEventMerger`, `ExternalEventDto`, `MergePlan`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/External/ExternalEventDto.cs`:

```csharp
namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// One event as a source returned it. Times are already converted to local wall clock by the
/// source, so nothing downstream has to know about time zones.
/// </summary>
public sealed record ExternalEventDto(
    string ExternalUid,
    string Title,
    DateTime StartsAt,
    DateTime EndsAt,
    bool IsAllDay,
    string? Location,
    bool IsBusy);
```

`src/AaronOS.Modules.Schedule/External/ExternalEventMerger.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 79`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/External src/AaronOS.Modules.Schedule.Tests/ExternalEventMergerTests.cs
git commit -m "Add pure ExternalEventMerger with insert/update/delete planning"
```

---

## Task 3: The source interface and sync orchestration

**Files:**
- Create: `src/AaronOS.Modules.Schedule/External/IExternalCalendarSource.cs`
- Create: `src/AaronOS.Modules.Schedule/External/ScheduleSyncService.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `ExternalEventMerger.Plan`, `ExternalEventMerger.CopyInto`, `ExternalCalendar`, `ExternalEvent`.
- Produces:
  - `interface IExternalCalendarSource` with `CalendarProvider Provider { get; }` and `Task<IReadOnlyList<ExternalEventDto>> FetchAsync(ExternalCalendar calendar, DateOnly from, DateOnly to, CancellationToken ct)`
  - `ScheduleSyncService` with `Task<int> SyncAllAsync(CancellationToken ct)` returning the number of calendars synced successfully, and `Task SyncOneAsync(int calendarId, CancellationToken ct)`

- [ ] **Step 1: Write the interface and service**

No unit test: the merge decision is covered by 7 `ExternalEventMergerTests`, and what remains here is EF persistence plus error recording, verified end to end in Task 5 and Task 9.

`src/AaronOS.Modules.Schedule/External/IExternalCalendarSource.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// One way of reading an external calendar. The seam exists because the work calendar's transport
/// is uncertain: a published ICS feed today, potentially Microsoft Graph later if the tenant
/// requires it. A Graph provider implements this and is registered alongside; nothing else changes.
///
/// Implementations are read-only. Never write to an external calendar.
/// </summary>
public interface IExternalCalendarSource
{
    CalendarProvider Provider { get; }

    /// <summary>
    /// Returns every event in [<paramref name="from"/>, <paramref name="to"/>] — a full-window
    /// fetch, because <see cref="ExternalEventMerger"/> deletes local rows absent from the result.
    /// Throw on failure; the caller records the message and leaves the cache alone.
    /// </summary>
    Task<IReadOnlyList<ExternalEventDto>> FetchAsync(
        ExternalCalendar calendar,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
```

`src/AaronOS.Modules.Schedule/External/ScheduleSyncService.cs`:

```csharp
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
            var existing = await db.Set<ExternalEvent>()
                .Where(e => e.ExternalCalendarId == calendar.Id
                            && e.StartsAt >= from.ToDateTime(TimeOnly.MinValue)
                            && e.StartsAt <= to.ToDateTime(TimeOnly.MaxValue))
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
```

- [ ] **Step 2: Register the service**

In `ScheduleModule.RegisterServices`:

```csharp
        services.AddSingleton<ScheduleSyncService>();
```

with `using AaronOS.Modules.Schedule.External;`. Sources are registered in Tasks 4 and 7; `IEnumerable<IExternalCalendarSource>` resolves to an empty sequence until then, which `SyncCalendarAsync` handles by recording "No source registered".

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build AaronOS.slnx --nologo`
Expected: `Build succeeded`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 79`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add IExternalCalendarSource seam and ScheduleSyncService orchestration"
```

---

## Task 4: ICS feed client

**Files:**
- Modify: `src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj`
- Create: `src/AaronOS.Modules.Schedule/External/IcsFeedClient.cs`
- Create: `src/AaronOS.Modules.Schedule.Tests/Fixtures/sample-calendar.ics`
- Create: `src/AaronOS.Modules.Schedule.Tests/IcsFeedClientTests.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Produces: `IcsFeedClient : IExternalCalendarSource` (`Provider => CalendarProvider.OutlookIcs`) plus a separately testable `static IReadOnlyList<ExternalEventDto> IcsFeedClient.Parse(string icsText, DateOnly from, DateOnly to)`.

**API confidence, stated plainly.** `Ical.Net` 5.x changed its occurrence-expansion API from 4.x, and I have not verified 5.2.3's exact signatures. The code below is the expected shape; **treat the first compile as the source of truth**, not this document. Two facts are reliable: `Calendar.Load(string)` parses, and occurrences come back as objects carrying a `Period` with start and end. If the member names differ, find the real ones without guessing:

```bash
# List the public types and members containing "Occurrence" or "Period"
strings ~/.nuget/packages/ical.net/5.2.3/lib/net*/Ical.Net.dll | grep -iE 'occurrence|getoccurrences|period' | sort -u | head -40
```

Then write the call and let the compiler error (`CS1061: 'X' does not contain a definition for 'Y'`) point at the wrong member. That loop resolves in seconds and beats reading release notes.

- [ ] **Step 1: Add the package and the fixture**

Add to the module csproj's package `ItemGroup`:

```xml
    <PackageReference Include="Ical.Net" Version="5.2.3" />
```

Create `src/AaronOS.Modules.Schedule.Tests/Fixtures/sample-calendar.ics`. This fixture is the parse contract — a simple event, a weekly recurrence, an all-day event, a free/transparent event, and a VTIMEZONE:

```
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//AaronOS//Schedule Test Fixture//EN
CALSCALE:GREGORIAN
METHOD:PUBLISH
BEGIN:VTIMEZONE
TZID:America/New_York
BEGIN:STANDARD
DTSTART:20261101T020000
TZOFFSETFROM:-0400
TZOFFSETTO:-0500
TZNAME:EST
END:STANDARD
BEGIN:DAYLIGHT
DTSTART:20260308T020000
TZOFFSETFROM:-0500
TZOFFSETTO:-0400
TZNAME:EDT
END:DAYLIGHT
END:VTIMEZONE
BEGIN:VEVENT
UID:simple-event@aaronos.test
DTSTAMP:20260701T120000Z
DTSTART;TZID=America/New_York:20260706T093000
DTEND;TZID=America/New_York:20260706T100000
SUMMARY:Standup
LOCATION:Room 1
TRANSP:OPAQUE
END:VEVENT
BEGIN:VEVENT
UID:weekly-event@aaronos.test
DTSTAMP:20260701T120000Z
DTSTART;TZID=America/New_York:20260707T140000
DTEND;TZID=America/New_York:20260707T150000
RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3
SUMMARY:Weekly sync
TRANSP:OPAQUE
END:VEVENT
BEGIN:VEVENT
UID:allday-event@aaronos.test
DTSTAMP:20260701T120000Z
DTSTART;VALUE=DATE:20260709
DTEND;VALUE=DATE:20260710
SUMMARY:Company holiday
TRANSP:OPAQUE
END:VEVENT
BEGIN:VEVENT
UID:free-event@aaronos.test
DTSTAMP:20260701T120000Z
DTSTART;TZID=America/New_York:20260708T110000
DTEND;TZID=America/New_York:20260708T120000
SUMMARY:FYI only
TRANSP:TRANSPARENT
END:VEVENT
BEGIN:VEVENT
UID:outside-window@aaronos.test
DTSTAMP:20260701T120000Z
DTSTART;TZID=America/New_York:20270101T090000
DTEND;TZID=America/New_York:20270101T100000
SUMMARY:Next year
TRANSP:OPAQUE
END:VEVENT
END:VCALENDAR
```

Make the fixture available to the test run. Add to the **test** csproj:

```xml
  <ItemGroup>
    <None Update="Fixtures\sample-calendar.ics" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

Create `src/AaronOS.Modules.Schedule.Tests/IcsFeedClientTests.cs`:

```csharp
using AaronOS.Modules.Schedule.External;

namespace AaronOS.Modules.Schedule.Tests;

public class IcsFeedClientTests
{
    private static string FixtureText() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-calendar.ics"));

    private static readonly DateOnly From = new(2026, 7, 6);
    private static readonly DateOnly To = new(2026, 7, 31);

    [Fact]
    public void Parses_ASimpleEvent()
    {
        var events = IcsFeedClient.Parse(FixtureText(), From, To);

        var standup = Assert.Single(events, e => e.Title == "Standup");
        Assert.Equal(new DateTime(2026, 7, 6, 9, 30, 0), standup.StartsAt);
        Assert.Equal(new DateTime(2026, 7, 6, 10, 0, 0), standup.EndsAt);
        Assert.Equal("Room 1", standup.Location);
        Assert.True(standup.IsBusy);
        Assert.False(standup.IsAllDay);
    }

    [Fact]
    public void ExpandsARecurringEvent_IntoDistinctOccurrences()
    {
        var events = IcsFeedClient.Parse(FixtureText(), From, To);

        var weekly = events.Where(e => e.Title == "Weekly sync").OrderBy(e => e.StartsAt).ToList();

        // COUNT=3 from Tue 7 July: the 7th, 14th, and 21st.
        Assert.Equal(3, weekly.Count);
        Assert.Equal([new DateTime(2026, 7, 7, 14, 0, 0), new DateTime(2026, 7, 14, 14, 0, 0), new DateTime(2026, 7, 21, 14, 0, 0)],
            weekly.Select(e => e.StartsAt));

        // Each occurrence needs its own UID or the unique index collapses them into one row.
        Assert.Equal(3, weekly.Select(e => e.ExternalUid).Distinct().Count());
    }

    [Fact]
    public void MarksAnAllDayEvent()
    {
        var events = IcsFeedClient.Parse(FixtureText(), From, To);

        var holiday = Assert.Single(events, e => e.Title == "Company holiday");
        Assert.True(holiday.IsAllDay);
        Assert.Equal(new DateOnly(2026, 7, 9), DateOnly.FromDateTime(holiday.StartsAt));
    }

    [Fact]
    public void MarksATransparentEventAsFree()
    {
        var events = IcsFeedClient.Parse(FixtureText(), From, To);

        var fyi = Assert.Single(events, e => e.Title == "FYI only");
        Assert.False(fyi.IsBusy);
    }

    [Fact]
    public void ExcludesEventsOutsideTheWindow()
    {
        var events = IcsFeedClient.Parse(FixtureText(), From, To);

        Assert.DoesNotContain(events, e => e.Title == "Next year");
    }

    [Fact]
    public void MalformedInput_ThrowsRatherThanSilentlyReturningNothing()
    {
        // A truncated or HTML error-page response must surface as a failure the sync service can
        // record, not as "the calendar is empty" — which would delete every cached event.
        Assert.ThrowsAny<Exception>(() => IcsFeedClient.Parse("<html>Sign in</html>", From, To));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `IcsFeedClient`.

- [ ] **Step 4: Write the implementation, then reconcile with the real API**

`src/AaronOS.Modules.Schedule/External/IcsFeedClient.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;
using Ical.Net;
using Ical.Net.CalendarComponents;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// Reads a published-calendar ICS feed. Anonymous HTTP GET plus a parse — no OAuth, no app
/// registration. The trade is freshness: published Outlook calendars can lag by hours.
///
/// Parsing uses Ical.Net rather than hand-rolled string work. RRULE expansion, VTIMEZONE
/// resolution, line unfolding, and value escaping are considerably more code to get right than a
/// package reference, and getting them subtly wrong yields a calendar that is quietly incorrect.
/// </summary>
public sealed class IcsFeedClient(IHttpClientFactory httpClientFactory) : IExternalCalendarSource
{
    public CalendarProvider Provider => CalendarProvider.OutlookIcs;

    public async Task<IReadOnlyList<ExternalEventDto>> FetchAsync(
        ExternalCalendar calendar,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(calendar.IcsUrl))
        {
            throw new InvalidOperationException("This calendar has no ICS URL configured.");
        }

        var http = httpClientFactory.CreateClient(nameof(IcsFeedClient));
        using var response = await http.GetAsync(calendar.IcsUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(text, from, to);
    }

    /// <summary>
    /// Separated from the HTTP fetch so it is testable against a checked-in fixture
    /// (see IcsFeedClientTests) with no network involved.
    /// </summary>
    public static IReadOnlyList<ExternalEventDto> Parse(string icsText, DateOnly from, DateOnly to)
    {
        // Throws on input that isn't a calendar — which is the desired behaviour: a login redirect
        // or error page must surface as a failure, never as an empty calendar, because an empty
        // successful fetch legitimately deletes every cached event.
        var calendar = Calendar.Load(icsText);

        var windowStart = from.ToDateTime(TimeOnly.MinValue);
        var windowEnd = to.ToDateTime(TimeOnly.MaxValue);

        var results = new List<ExternalEventDto>();

        foreach (var occurrence in calendar.GetOccurrences(windowStart, windowEnd))
        {
            if (occurrence.Source is not CalendarEvent source) continue;

            var start = occurrence.Period.StartTime.AsSystemLocal;
            var end = occurrence.Period.EndTime?.AsSystemLocal ?? start.AddHours(1);

            if (start > windowEnd || end < windowStart) continue;

            var isAllDay = !occurrence.Period.StartTime.HasTime;

            // TRANSP:TRANSPARENT means the time is not blocked. Anything else — including a
            // missing TRANSP — is treated as busy, which is the ICS default.
            var isBusy = !string.Equals(source.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase);

            // Each occurrence of a recurring event needs its own stable identity: the bare UID
            // repeats across occurrences, and the unique (calendar, uid) index would collapse a
            // weekly meeting into a single row.
            var uid = $"{source.Uid}#{start:yyyyMMddTHHmmss}";

            results.Add(new ExternalEventDto(
                uid,
                string.IsNullOrWhiteSpace(source.Summary) ? "(untitled)" : source.Summary,
                start,
                end,
                isAllDay,
                source.Location,
                isBusy));
        }

        return results;
    }
}
```

**Reconcile against the installed package.** Every member marked below is the expected 5.x shape and must be confirmed:

- `Calendar.Load(string)` — reliable.
- `calendar.GetOccurrences(DateTime, DateTime)` — 5.x may take `CalDateTime` arguments, or a single start plus a `TakeWhileBefore(...)` extension. Adjust the call, keeping the same window semantics.
- `occurrence.Source` — may be named `Source` or expose the component differently.
- `occurrence.Period.StartTime` / `.EndTime` — `CalDateTime`. The local-time conversion may be `AsSystemLocal`, `AsDateTimeOffset.LocalDateTime`, or `Value`; use whichever the type actually offers, and keep the result a **local** `DateTime`.
- `CalDateTime.HasTime` — the all-day discriminator. If absent, a `VALUE=DATE` start has a zero time component and a whole-day duration; use that instead.
- `source.Transparency` — may be a string or an enum. Compare accordingly.

Run `dotnet build` after each adjustment and let the compiler drive. The six tests are the acceptance criteria; if they pass, the API is being used correctly regardless of which member names were involved.

- [ ] **Step 5: Register the client**

`IcsFeedClient` needs `IHttpClientFactory`. In `ScheduleModule.RegisterServices`:

```csharp
        services.AddHttpClient(nameof(IcsFeedClient), client =>
        {
            // A published feed that hangs must not hold up a sync pass.
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IExternalCalendarSource, IcsFeedClient>();
```

`AddHttpClient` comes from `Microsoft.Extensions.Http`, which the app already has transitively via `Host.CreateDefaultBuilder`. If it does not resolve, add `<PackageReference Include="Microsoft.Extensions.Http" Version="8.0.1" />` to the module csproj and `using Microsoft.Extensions.DependencyInjection;`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 85`

If `Parses_ASimpleEvent` reports a time offset by hours, the local-time conversion is wrong — the fixture is authored in `America/New_York` and converts to whatever this machine's zone is, so assert against the converted value rather than the literal `09:30` if the machine is not Eastern. Adjust the test to compute the expectation from the fixture's zone, and say so in a comment.

- [ ] **Step 7: Commit**

```bash
git add src/AaronOS.Modules.Schedule src/AaronOS.Modules.Schedule.Tests
git commit -m "Add ICS feed client parsing published Outlook calendars"
```

---

## Task 5: Settings section and first real Outlook sync

**Files:**
- Create: `src/AaronOS.Modules.Schedule/ViewModels/ScheduleSettingsViewModel.cs`
- Create: `src/AaronOS.Modules.Schedule/Views/ScheduleSettingsSection.xaml`
- Create: `src/AaronOS.Modules.Schedule/Views/ScheduleSettingsSection.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `ScheduleSyncService`, `ExternalCalendar`.
- Produces: `ScheduleSettingsViewModel` with `ObservableCollection<ExternalCalendar> Calendars`, `LoadCommand`, `AddIcsCalendarCommand`, `SyncNowCommand`, `SyncAllCommand`, `RemoveCalendarCommand`, `ToggleEnabledCommand`. `ScheduleModule.SettingsContentType => typeof(ScheduleSettingsSection)`.

- [ ] **Step 1: Write the ViewModel**

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class ScheduleSettingsViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    ScheduleSyncService syncService) : ViewModelBase
{
    public ObservableCollection<ExternalCalendar> Calendars { get; } = [];

    [ObservableProperty]
    private string _newIcsUrl = "";

    [ObservableProperty]
    private string _newIcsName = "Work (Outlook)";

    [ObservableProperty]
    private string? _statusMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var calendars = await db.Set<ExternalCalendar>().ToListAsync();

            Calendars.Clear();
            foreach (var calendar in calendars.OrderBy(c => c.Provider).ThenBy(c => c.DisplayName))
            {
                Calendars.Add(calendar);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddIcsCalendarAsync()
    {
        StatusMessage = null;

        if (string.IsNullOrWhiteSpace(NewIcsUrl))
        {
            StatusMessage = "Paste the published calendar's ICS URL.";
            return;
        }
        if (!Uri.TryCreate(NewIcsUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            StatusMessage = "The ICS URL must be an absolute https:// address.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new ExternalCalendar
        {
            Provider = CalendarProvider.OutlookIcs,
            DisplayName = string.IsNullOrWhiteSpace(NewIcsName) ? "Outlook" : NewIcsName.Trim(),
            IcsUrl = uri.ToString(),
            IsEnabled = true,
        });
        await db.SaveChangesAsync();

        NewIcsUrl = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SyncNowAsync(ExternalCalendar calendar)
    {
        IsBusy = true;
        StatusMessage = $"Syncing {calendar.DisplayName}…";
        try
        {
            await syncService.SyncOneAsync(calendar.Id, CancellationToken.None);
            await LoadAsync();

            var refreshed = Calendars.FirstOrDefault(c => c.Id == calendar.Id);
            StatusMessage = refreshed?.LastError is { } error
                ? $"{calendar.DisplayName} failed: {error}"
                : $"{calendar.DisplayName} synced.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SyncAllAsync()
    {
        IsBusy = true;
        StatusMessage = "Syncing…";
        try
        {
            var succeeded = await syncService.SyncAllAsync(CancellationToken.None);
            await LoadAsync();
            StatusMessage = $"{succeeded} of {Calendars.Count(c => c.IsEnabled)} calendar(s) synced.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(ExternalCalendar calendar)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var tracked = await db.Set<ExternalCalendar>().SingleAsync(c => c.Id == calendar.Id);
        tracked.IsEnabled = !tracked.IsEnabled;
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveCalendarAsync(ExternalCalendar calendar)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        // Cascade removes its cached events too.
        db.Remove(await db.Set<ExternalCalendar>().SingleAsync(c => c.Id == calendar.Id));
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}
```

- [ ] **Step 2: Write the UserControl**

A `UserControl`, not a `Page`, because the app's Settings page composes several of these inline — the same contract `Finance.LinkAccountSection` satisfies.

`src/AaronOS.Modules.Schedule/Views/ScheduleSettingsSection.xaml`:

```xml
<UserControl
    x:Class="AaronOS.Modules.Schedule.Views.ScheduleSettingsSection"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    mc:Ignorable="d">

    <StackPanel>
        <ui:TextBlock Text="Calendars" FontTypography="BodyStrong" Margin="0,0,0,4" />
        <TextBlock TextWrapping="Wrap" Opacity="0.75" Margin="0,0,0,8"
                   Text="Read-only. AaronOS never writes to your calendars." />

        <ItemsControl ItemsSource="{Binding Calendars}" Margin="0,0,0,8">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <ui:Card Margin="0,0,0,8">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column="0">
                                <ui:TextBlock Text="{Binding DisplayName}" FontTypography="BodyStrong" />
                                <TextBlock>
                                    <Run Text="{Binding Provider, Mode=OneWay}" />
                                    <Run Text="{Binding LastSyncedAt, StringFormat=' · last synced {0:g}', TargetNullValue=' · never synced', Mode=OneWay}" />
                                </TextBlock>
                                <TextBlock Text="{Binding LastError}" TextWrapping="Wrap"
                                           Foreground="{DynamicResource SystemFillColorCriticalBrush}" />
                            </StackPanel>
                            <ui:Button Grid.Column="1" Content="Sync" Click="Sync_Click" Margin="0,0,8,0" />
                            <ui:Button Grid.Column="2" Content="{Binding IsEnabled}" Click="Toggle_Click" Margin="0,0,8,0" />
                            <ui:Button Grid.Column="3" Content="Remove" Click="Remove_Click" />
                        </Grid>
                    </ui:Card>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <ui:TextBlock Text="Add an Outlook published calendar" FontTypography="BodyStrong" Margin="0,8,0,4" />
        <TextBlock TextWrapping="Wrap" Opacity="0.75" Margin="0,0,0,8"
                   Text="In Outlook on the web: Settings → Calendar → Shared calendars → Publish a calendar. Choose 'Can view all details' and copy the ICS link." />
        <ui:TextBox PlaceholderText="Name" Text="{Binding NewIcsName, Mode=TwoWay}" Margin="0,0,0,8" />
        <ui:TextBox PlaceholderText="https://outlook.office365.com/owa/calendar/.../calendar.ics"
                    Text="{Binding NewIcsUrl, Mode=TwoWay}" Margin="0,0,0,8" />
        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
            <ui:Button Content="Add calendar" Appearance="Primary" Command="{Binding AddIcsCalendarCommand}" Margin="0,0,8,0" />
            <ui:Button Content="Sync all now" Command="{Binding SyncAllCommand}" />
        </StackPanel>
        <ui:TextBlock Text="{Binding StatusMessage}" TextWrapping="Wrap" />
    </StackPanel>
</UserControl>
```

`src/AaronOS.Modules.Schedule/Views/ScheduleSettingsSection.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class ScheduleSettingsSection : UserControl
{
    public ScheduleSettingsViewModel ViewModel { get; }

    public ScheduleSettingsSection()
    {
        ViewModel = AppServices.Provider.GetRequiredService<ScheduleSettingsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExternalCalendar calendar })
        {
            _ = ViewModel.SyncNowCommand.ExecuteAsync(calendar);
        }
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExternalCalendar calendar })
        {
            _ = ViewModel.ToggleEnabledCommand.ExecuteAsync(calendar);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExternalCalendar calendar })
        {
            _ = ViewModel.RemoveCalendarCommand.ExecuteAsync(calendar);
        }
    }
}
```

- [ ] **Step 3: Expose it and register the ViewModel**

In `ScheduleModule`:

```csharp
    /// <summary>Calendar configuration is one-time setup, so it belongs in Settings rather than in
    /// this module's own sub-navigation — the same reasoning as Finance's bank linking.</summary>
    public Type? SettingsContentType => typeof(ScheduleSettingsSection);
```

and in `RegisterServices`:

```csharp
        services.AddTransient<ScheduleSettingsViewModel>();
```

- [ ] **Step 4: Verify against the real feed**

Run: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`

1. Open the app's Settings page. A **Calendars** section appears with the Outlook instructions.
2. Paste the ICS URL captured in Task 0 and click **Add calendar**. The row appears reading "OutlookIcs · never synced".
3. Click **Sync**. Expected: the status reads "<name> synced", the row shows a last-synced timestamp, and no error text appears.
4. Click **Sync** again. Expected: the same result, and no duplicate events — verified in the next step.
5. Confirm the cached row count is stable across two syncs:

```bash
sqlite3 "$LOCALAPPDATA/AaronOS/aaronos.db" "SELECT COUNT(*) FROM ExternalEvents;"
```

Run it after the first sync and again after the second. The two numbers must match. A growing count means the unique index or the merge is wrong.

6. Paste a deliberately broken URL (`https://example.com/not-a-calendar.ics`) as a second calendar and sync it. Expected: red error text on that row, the working calendar unaffected, and **no** error dialog. This is the fail-soft requirement, verified rather than assumed.
7. Remove the broken calendar.

Close the app.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add Schedule settings section with Outlook ICS calendar sync"
```

---

## Task 6: Merge cached events into the agenda

**Files:**
- Create: `src/AaronOS.Modules.Schedule/External/ExternalEventProjector.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/ExternalEventProjectorTests.cs`
- Modify: `src/AaronOS.Modules.Schedule/ViewModels/TodayViewModel.cs`
- Modify: `src/AaronOS.Modules.Schedule/ViewModels/WeekViewModel.cs`
- Modify: `src/AaronOS.Modules.Schedule/ViewModels/SleepViewModel.cs`
- Modify: `src/AaronOS.Modules.Schedule/Notifications/ScheduleBackgroundWorker.cs`

**Interfaces:**
- Produces: `static IReadOnlyList<ExternalEventEntry> ExternalEventProjector.ToAgendaEntries(IReadOnlyList<ExternalEvent> events)` — maps stored rows to the plain record `AgendaBuilder` already accepts, splitting any event that spans midnight.

- [ ] **Step 1: Write the failing tests**

Create `src/AaronOS.Modules.Schedule.Tests/ExternalEventProjectorTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;

namespace AaronOS.Modules.Schedule.Tests;

public class ExternalEventProjectorTests
{
    private static ExternalEvent Event(DateTime start, DateTime end, string title = "Meeting", bool allDay = false, bool busy = true) =>
        new()
        {
            Id = 1, ExternalCalendarId = 1, ExternalUid = "uid", Title = title,
            StartsAt = start, EndsAt = end, IsAllDay = allDay, IsBusy = busy, LastSeenAt = start,
        };

    [Fact]
    public void SingleDayEvent_MapsToOneEntry()
    {
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 6, 9, 30, 0), new DateTime(2026, 7, 6, 10, 0, 0))]);

        var only = Assert.Single(entries);
        Assert.Equal(new DateOnly(2026, 7, 6), only.Date);
        Assert.Equal(new TimeSpan(9, 30, 0), only.Start);
        Assert.Equal(new TimeSpan(10, 0, 0), only.End);
        Assert.True(only.IsBusy);
    }

    [Fact]
    public void EventSpanningMidnight_IsSplitPerDay()
    {
        // AgendaBuilder works in per-day wall-clock spans, so a 22:00-02:00 event must arrive as
        // two entries or its second half silently vanishes.
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 6, 22, 0, 0), new DateTime(2026, 7, 7, 2, 0, 0), "Deploy")])
            .OrderBy(e => e.Date).ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal((new DateOnly(2026, 7, 6), new TimeSpan(22, 0, 0), new TimeSpan(24, 0, 0)),
            (entries[0].Date, entries[0].Start, entries[0].End));
        Assert.Equal((new DateOnly(2026, 7, 7), TimeSpan.Zero, new TimeSpan(2, 0, 0)),
            (entries[1].Date, entries[1].Start, entries[1].End));
    }

    [Fact]
    public void MultiDayAllDayEvent_CoversEveryDayInFull()
    {
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 9), new DateTime(2026, 7, 11), "Holiday", allDay: true)])
            .OrderBy(e => e.Date).ToList();

        // DTEND is exclusive for all-day events, so 9th-11th covers the 9th and 10th.
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e =>
        {
            Assert.Equal(TimeSpan.Zero, e.Start);
            Assert.Equal(new TimeSpan(24, 0, 0), e.End);
        });
    }

    [Fact]
    public void FreeEvent_KeepsItsIsBusyFlag_ForAgendaBuilderToFilter()
    {
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 6, 11, 0, 0), new DateTime(2026, 7, 6, 12, 0, 0), busy: false)]);

        Assert.False(Assert.Single(entries).IsBusy);
    }

    [Fact]
    public void ZeroLengthEvent_IsSkipped()
    {
        // A reminder-style event with identical start and end would produce a zero-width entry that
        // muddles gap computation for no benefit.
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 6, 9, 0, 0), new DateTime(2026, 7, 6, 9, 0, 0))]);

        Assert.Empty(entries);
    }

    [Fact]
    public void AbsurdlyLongEvent_IsCappedRatherThanExpandingForever()
    {
        // A malformed feed can carry a decade-long event; expanding it per-day would allocate
        // thousands of entries. Cap at 60 days.
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 1, 1), new DateTime(2036, 1, 1), "Broken", allDay: true)]);

        Assert.Equal(60, entries.Count);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `ExternalEventProjector`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/External/ExternalEventProjector.cs`:

```csharp
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// Maps stored <see cref="ExternalEvent"/> rows onto the plain <see cref="ExternalEventEntry"/>
/// record that <see cref="AgendaBuilder"/> accepts. This mapping is why the agenda logic carries no
/// dependency on the external-calendar tables.
///
/// An event that spans midnight is split per day, because AgendaBuilder works in per-day wall-clock
/// spans and would otherwise drop everything after the first day boundary.
/// </summary>
public static class ExternalEventProjector
{
    /// <summary>Guard against a malformed feed carrying a multi-year event.</summary>
    private const int MaxDaysPerEvent = 60;

    private static readonly TimeSpan DayEnd = new(24, 0, 0);

    public static IReadOnlyList<ExternalEventEntry> ToAgendaEntries(IReadOnlyList<ExternalEvent> events)
    {
        var entries = new List<ExternalEventEntry>();

        foreach (var e in events)
        {
            if (e.EndsAt <= e.StartsAt) continue; // zero-length or inverted: nothing to show

            var firstDay = DateOnly.FromDateTime(e.StartsAt);

            // All-day DTEND is exclusive, so an event ending at midnight ends on the previous day.
            var lastMoment = e.EndsAt.TimeOfDay == TimeSpan.Zero ? e.EndsAt.AddTicks(-1) : e.EndsAt;
            var lastDay = DateOnly.FromDateTime(lastMoment);

            var dayCount = Math.Min(lastDay.DayNumber - firstDay.DayNumber + 1, MaxDaysPerEvent);

            for (var i = 0; i < dayCount; i++)
            {
                var date = firstDay.AddDays(i);
                var start = i == 0 ? e.StartsAt.TimeOfDay : TimeSpan.Zero;
                var end = date == lastDay ? lastMoment.TimeOfDay : DayEnd;

                // An all-day event, or any middle day of a multi-day event, covers the full day.
                if (e.IsAllDay)
                {
                    start = TimeSpan.Zero;
                    end = DayEnd;
                }

                if (end <= start) continue;

                entries.Add(new ExternalEventEntry(date, start, end, e.Title, e.IsBusy));
            }
        }

        return entries;
    }
}
```

- [ ] **Step 4: Wire it into every agenda caller**

There are four. In each, load the cached events for the same window already being queried and pass the projection instead of `[]`.

**`TodayViewModel.LoadAsync`** — replace the `AgendaBuilder.Build(...)` call's last argument:

```csharp
            var externalRows = await db.Set<ExternalEvent>()
                .Where(e => e.StartsAt >= today.AddDays(-1).ToDateTime(TimeOnly.MinValue)
                            && e.StartsAt <= tomorrowDate.ToDateTime(TimeOnly.MaxValue))
                .ToListAsync();

            var agenda = AgendaBuilder.Build(
                today, tomorrowDate, blocks, exceptions, ExternalEventProjector.ToAgendaEntries(externalRows));
```

**`WeekViewModel.LoadAsync`** — same shape, over the week's range:

```csharp
            var externalRows = await db.Set<ExternalEvent>()
                .Where(e => e.StartsAt >= WeekStart.AddDays(-1).ToDateTime(TimeOnly.MinValue)
                            && e.StartsAt <= end.ToDateTime(TimeOnly.MaxValue))
                .ToListAsync();

            foreach (var day in AgendaBuilder.Build(
                WeekStart, end, blocks, exceptions, ExternalEventProjector.ToAgendaEntries(externalRows)))
            {
                Days.Add(day);
            }
```

**`SleepViewModel.LoadAsync`** — tomorrow's first commitment must account for a real meeting, which is the whole point of this integration:

```csharp
            var externalRows = await db.Set<ExternalEvent>()
                .Where(e => e.StartsAt >= today.ToDateTime(TimeOnly.MinValue)
                            && e.StartsAt <= tomorrowDate.ToDateTime(TimeOnly.MaxValue))
                .ToListAsync();

            var tomorrow = AgendaBuilder.Build(
                tomorrowDate, tomorrowDate, blocks, exceptions,
                ExternalEventProjector.ToAgendaEntries(externalRows)).Single();
```

**`ScheduleBackgroundWorker.PlanAsync`** — identical to `TodayViewModel`:

```csharp
        var externalRows = await db.Set<ExternalEvent>()
            .Where(e => e.StartsAt >= today.AddDays(-1).ToDateTime(TimeOnly.MinValue)
                        && e.StartsAt <= tomorrowDate.ToDateTime(TimeOnly.MaxValue))
            .ToListAsync(cancellationToken);

        var agenda = AgendaBuilder.Build(
            today, tomorrowDate, blocks, exceptions, ExternalEventProjector.ToAgendaEntries(externalRows));
```

Add `using AaronOS.Modules.Schedule.External;` to each file.

- [ ] **Step 5: Add a periodic sync to the background tick**

In `ScheduleBackgroundWorker`, inject `ScheduleSyncService` and sync every 30 ticks (half-hourly), which is well inside a published feed's own refresh lag:

```csharp
public sealed class ScheduleBackgroundWorker(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    INotificationSink sink,
    ScheduleSyncService syncService) : IDisposable
{
    private const int TicksPerSync = 30;
    private int _ticksSinceSync = TicksPerSync; // sync on the first tick
```

and at the top of `TickSafelyAsync`'s `try` block, before planning:

```csharp
            if (++_ticksSinceSync >= TicksPerSync)
            {
                _ticksSinceSync = 0;
                // SyncAllAsync records its own per-calendar failures and never throws for them.
                await syncService.SyncAllAsync(cancellationToken);
            }
```

- [ ] **Step 6: Run the tests and verify in the app**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 91`

Run: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`

Confirm:
1. Today shows real Outlook meetings interleaved with the template blocks, in start order.
2. A meeting inside a free gap has split that gap — the "Free time" list no longer shows one long uninterrupted span where a meeting sits.
3. Week shows meetings on the correct days.
4. Sleep's recommended bedtime accounts for tomorrow's earliest meeting if it is earlier than the work block's start.
5. A meeting you have declined or that is marked free does **not** appear (it arrives with `IsBusy = false` and `AgendaBuilder` filters it).

Close the app.

- [ ] **Step 7: Commit**

```bash
git add src/AaronOS.Modules.Schedule src/AaronOS.Modules.Schedule.Tests
git commit -m "Merge cached external events into the agenda and sync periodically"
```

---

## Task 7: DPAPI-backed Google token store

**Files:**
- Modify: `src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj`
- Create: `src/AaronOS.Modules.Schedule/External/TokenProtector.cs`
- Create: `src/AaronOS.Modules.Schedule/External/DpapiDataStore.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/TokenProtectorTests.cs`

**Interfaces:**
- Produces: `static byte[] TokenProtector.Protect(string)` / `static string TokenProtector.Unprotect(byte[])`, and `DpapiDataStore : Google.Apis.Util.Store.IDataStore`.

- [ ] **Step 1: Add the packages**

```xml
    <PackageReference Include="Google.Apis.Calendar.v3" Version="1.75.0.4206" />
    <PackageReference Include="Google.Apis.Auth" Version="1.75.0" />
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.10" />
```

Confirm the `Google.Apis.Auth` version resolves; if NuGet reports it missing, use the version that `Google.Apis.Calendar.v3` brings in transitively and drop the explicit reference.

**This task owns the `ProtectedData` reference.** Plan 1 deliberately does not include it — an unused package reference on a module that has no secrets to protect is speculative configuration, and a review correctly rejected carrying it early. `TokenProtector` below is the first code that needs it, which is why it arrives here. The version matches what `Finance` and `Nutrition` already use for the same purpose.

- [ ] **Step 2: Write the failing test**

Create `src/AaronOS.Modules.Schedule.Tests/TokenProtectorTests.cs`, mirroring `AccessTokenProtectorTests` in the Finance test project:

```csharp
using AaronOS.Modules.Schedule.External;

namespace AaronOS.Modules.Schedule.Tests;

public class TokenProtectorTests
{
    [Fact]
    public void RoundTrips_ThroughDpapi()
    {
        const string token = """{"access_token":"ya29.example","refresh_token":"1//example"}""";

        var encrypted = TokenProtector.Protect(token);
        var decrypted = TokenProtector.Unprotect(encrypted);

        Assert.Equal(token, decrypted);
        // The point of the exercise: the stored bytes must not be the plaintext.
        Assert.NotEqual(token, System.Text.Encoding.UTF8.GetString(encrypted));
        Assert.DoesNotContain("refresh_token", System.Text.Encoding.UTF8.GetString(encrypted));
    }

    [Fact]
    public void Unprotect_ThrowsOnCorruptInput()
    {
        // A copied database or a changed Windows account yields undecryptable bytes. That must be
        // a clear failure the caller turns into "needs re-authorization", not silent garbage.
        Assert.ThrowsAny<Exception>(() => TokenProtector.Unprotect([1, 2, 3, 4]));
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `TokenProtector`.

- [ ] **Step 4: Write the implementation**

`src/AaronOS.Modules.Schedule/External/TokenProtector.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// DPAPI (current-user scope) protection for the Google OAuth token blob, mirroring
/// AaronOS.Modules.Finance.Plaid.AccessTokenProtector and
/// AaronOS.Modules.Nutrition.Usda.ApiKeyProtector — duplicated rather than shared, since modules
/// can't reference each other's internals and one small DPAPI helper isn't worth promoting to Core
/// for three callers yet.
/// </summary>
public static class TokenProtector
{
    public static byte[] Protect(string value) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value), optionalEntropy: null, DataProtectionScope.CurrentUser);

    public static string Unprotect(byte[] encrypted) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser));
}
```

`src/AaronOS.Modules.Schedule/External/DpapiDataStore.cs`:

```csharp
using System.Text.Json;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using Google.Apis.Util.Store;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// Google's token cache, backed by <see cref="ExternalCalendar.EncryptedToken"/> under DPAPI
/// instead of the library's default FileDataStore. Two reasons: the token lives with the rest of
/// the app's data rather than in a stray folder under %AppData%, and it is encrypted at rest to
/// the current Windows user, consistent with how Plaid and USDA credentials are already stored.
/// </summary>
public sealed class DpapiDataStore(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    int externalCalendarId) : IDataStore
{
    public async Task StoreAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var calendar = await db.Set<ExternalCalendar>().SingleOrDefaultAsync(c => c.Id == externalCalendarId);
        if (calendar is null) return;

        calendar.EncryptedToken = TokenProtector.Protect(json);
        await db.SaveChangesAsync();
    }

    public async Task<T> GetAsync<T>(string key)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var calendar = await db.Set<ExternalCalendar>().SingleOrDefaultAsync(c => c.Id == externalCalendarId);

        if (calendar?.EncryptedToken is not { Length: > 0 } encrypted) return default!;

        try
        {
            return JsonSerializer.Deserialize<T>(TokenProtector.Unprotect(encrypted))!;
        }
        catch (Exception)
        {
            // Undecryptable (copied database, different Windows account) or unparseable. Returning
            // default makes Google's auth layer treat it as "no cached token" and re-prompt, which
            // is exactly the desired recovery — better than throwing from a token read.
            return default!;
        }
    }

    public async Task DeleteAsync<T>(string key) => await ClearAsync();

    public async Task ClearAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var calendar = await db.Set<ExternalCalendar>().SingleOrDefaultAsync(c => c.Id == externalCalendarId);
        if (calendar is null) return;

        calendar.EncryptedToken = null;
        await db.SaveChangesAsync();
    }
}
```

`IDataStore`'s `key` parameter is ignored: one store instance serves exactly one calendar row, so there is only ever one value to hold. Confirm the interface's exact member list against the installed `Google.Apis.Core` — if it declares more members, implement them with the same single-value semantics.

- [ ] **Step 5: Run the tests and commit**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 93`

```bash
git add src/AaronOS.Modules.Schedule src/AaronOS.Modules.Schedule.Tests
git commit -m "Add DPAPI-backed token store for Google credentials"
```

---

## Task 8: Google Calendar source

**Files:**
- Create: `src/AaronOS.Modules.Schedule/External/GoogleCredentialProvider.cs`
- Create: `src/AaronOS.Modules.Schedule/External/GoogleCalendarClient.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`
- Modify: `src/AaronOS.Modules.Schedule/ViewModels/ScheduleSettingsViewModel.cs`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleSettingsSection.xaml(.cs)`

**Interfaces:**
- Produces: `GoogleCredentialProvider` with `Task<UserCredential> AuthorizeAsync(int calendarId, IEnumerable<string> scopes, CancellationToken ct)`, and `GoogleCalendarClient : IExternalCalendarSource` (`Provider => CalendarProvider.GoogleCalendar`). `ScheduleSettingsViewModel` gains `ConnectGoogleCommand`.

**Prerequisite outside the codebase.** A Google Cloud project with the Calendar API enabled and an **OAuth client of type Desktop app**, whose `client_secret_*.json` is downloaded. Keep the file outside the repository and point at it with an environment variable — a client secret must never be committed. In testing mode the consent screen needs the Google account added as a test user.

- [ ] **Step 1: Write the credential provider**

```csharp
using AaronOS.Core.Data;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// Runs Google's installed-app OAuth flow (a loopback redirect in the default browser) and caches
/// the resulting token in the app database under DPAPI via <see cref="DpapiDataStore"/>.
///
/// The client secret is read from a file whose path comes from the AARONOS_GOOGLE_CLIENT_SECRET
/// environment variable. It is deliberately not embedded, not committed, and not stored in the
/// database: a desktop-app client secret is not a true secret, but committing one is still a
/// mistake that is tedious to undo.
/// </summary>
public sealed class GoogleCredentialProvider(IDbContextFactory<AaronOsDbContext> dbContextFactory)
{
    private const string ClientSecretPathVariable = "AARONOS_GOOGLE_CLIENT_SECRET";

    public async Task<UserCredential> AuthorizeAsync(
        int calendarId,
        IEnumerable<string> scopes,
        CancellationToken cancellationToken)
    {
        var path = Environment.GetEnvironmentVariable(ClientSecretPathVariable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Set {ClientSecretPathVariable} to the path of your Google OAuth desktop-app client secret JSON file.");
        }

        await using var stream = File.OpenRead(path);
        var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken)).Secrets;

        // Opens the system browser and completes on a loopback redirect. "user" is an arbitrary
        // key: the data store holds one token per calendar row, so it is never consulted.
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            scopes,
            "user",
            cancellationToken,
            new DpapiDataStore(dbContextFactory, calendarId));
    }
}
```

Confirm `GoogleClientSecrets.FromStreamAsync` and the `GoogleWebAuthorizationBroker.AuthorizeAsync` overload against the installed package; if a signature differs, the compiler names the mismatch. Do not switch to a different auth entry point to make it compile — `GoogleWebAuthorizationBroker` is the installed-app flow, and substituting a service-account or device flow would change the security model.

- [ ] **Step 2: Write the calendar client**

```csharp
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// Reads a Google calendar over the Calendar API. Read-only by construction: the requested scope is
/// CalendarReadonly, so the credential cannot write even if a future change tried to.
/// </summary>
public sealed class GoogleCalendarClient(
    GoogleCredentialProvider credentialProvider,
    IDbContextFactory<AaronOsDbContext> dbContextFactory) : IExternalCalendarSource
{
    public static readonly string[] Scopes = [CalendarService.Scope.CalendarReadonly];

    public CalendarProvider Provider => CalendarProvider.GoogleCalendar;

    public async Task<IReadOnlyList<ExternalEventDto>> FetchAsync(
        ExternalCalendar calendar,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var credential = await credentialProvider.AuthorizeAsync(calendar.Id, Scopes, cancellationToken);

        using var service = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AaronOS",
        });

        var request = service.Events.List(calendar.RemoteCalendarId ?? "primary");
        request.TimeMinDateTimeOffset = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue));
        request.TimeMaxDateTimeOffset = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue));
        // singleEvents expands recurrences server-side, so this client needs no RRULE logic.
        request.SingleEvents = true;
        request.MaxResults = 2500;
        request.ShowDeleted = false;

        var results = new List<ExternalEventDto>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);

            foreach (var item in response.Items ?? [])
            {
                if (Map(item) is { } dto) results.Add(dto);
            }

            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken) && !cancellationToken.IsCancellationRequested);

        return results;
    }

    private static ExternalEventDto? Map(Event item)
    {
        if (item.Status == "cancelled") return null;

        var isAllDay = item.Start?.Date is not null;

        DateTime start, end;
        if (isAllDay)
        {
            if (!DateTime.TryParse(item.Start!.Date, out start)) return null;
            end = DateTime.TryParse(item.End?.Date, out var parsedEnd) ? parsedEnd : start.AddDays(1);
        }
        else
        {
            if (item.Start?.DateTimeDateTimeOffset is not { } startOffset) return null;
            start = startOffset.LocalDateTime;
            end = item.End?.DateTimeDateTimeOffset?.LocalDateTime ?? start.AddHours(1);
        }

        // "transparent" means the event doesn't block time. Google also reports declined
        // invitations, which shouldn't consume a free gap either.
        var declined = item.Attendees?.Any(a => a.Self == true && a.ResponseStatus == "declined") == true;
        var isBusy = item.Transparency != "transparent" && !declined;

        // singleEvents=true gives each occurrence its own id, so no suffix is needed here.
        var uid = item.Id ?? item.ICalUID;
        if (string.IsNullOrWhiteSpace(uid)) return null;

        return new ExternalEventDto(
            uid,
            string.IsNullOrWhiteSpace(item.Summary) ? "(untitled)" : item.Summary,
            start,
            end,
            isAllDay,
            item.Location,
            isBusy);
    }
}
```

The `DateTimeDateTimeOffset` and `TimeMinDateTimeOffset` property names are the current generated-client spelling (the older `DateTime`/`TimeMin` properties are obsolete). Confirm against the installed package and follow the compiler if they differ.

- [ ] **Step 3: Register and expose a connect action**

In `ScheduleModule.RegisterServices`:

```csharp
        services.AddSingleton<GoogleCredentialProvider>();
        services.AddSingleton<IExternalCalendarSource, GoogleCalendarClient>();
```

Two `IExternalCalendarSource` registrations coexist; `ScheduleSyncService` picks by `Provider`.

Add to `ScheduleSettingsViewModel`:

```csharp
    [RelayCommand]
    private async Task ConnectGoogleAsync()
    {
        StatusMessage = "Opening Google sign-in…";
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var calendar = await db.Set<ExternalCalendar>()
                .FirstOrDefaultAsync(c => c.Provider == CalendarProvider.GoogleCalendar);

            if (calendar is null)
            {
                calendar = new ExternalCalendar
                {
                    Provider = CalendarProvider.GoogleCalendar,
                    DisplayName = "Personal (Google)",
                    RemoteCalendarId = "primary",
                    IsEnabled = true,
                };
                db.Add(calendar);
                await db.SaveChangesAsync();
            }

            // Authorizing writes the token via DpapiDataStore, so the sync path afterwards is
            // non-interactive.
            await credentialProvider.AuthorizeAsync(calendar.Id, GoogleCalendarClient.Scopes, CancellationToken.None);

            await SyncOneAsync(calendar);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Google sign-in failed: {ex.Message}";
        }
    }
```

Add `GoogleCredentialProvider credentialProvider` to the ViewModel's constructor parameters and `using AaronOS.Modules.Schedule.External;`.

In `ScheduleSettingsSection.xaml`, add next to the existing buttons:

```xml
            <ui:Button Content="Connect Google Calendar" Command="{Binding ConnectGoogleCommand}" Margin="8,0,0,0" />
```

- [ ] **Step 4: Verify against the real account**

Set the environment variable and launch — it must be set for the process, so set it in the same shell:

```bash
export AARONOS_GOOGLE_CLIENT_SECRET="/c/Users/aaron/secrets/google-oauth-desktop.json"
dotnet run --project src/AaronOS.App/AaronOS.App.csproj
```

Confirm:
1. Settings → **Connect Google Calendar** opens the system browser to Google's consent screen.
2. Granting access returns to the app and the status reads "Personal (Google) synced."
3. A Google calendar row appears with a last-synced timestamp and no error.
4. Today and Week show personal events alongside work meetings and template blocks.
5. Close and relaunch the app, then click **Sync** on the Google row. Expected: it succeeds **without** opening the browser — the DPAPI-stored refresh token is being used.
6. Confirm the token is not stored in plaintext:

```bash
sqlite3 "$LOCALAPPDATA/AaronOS/aaronos.db" "SELECT hex(EncryptedToken) IS NOT NULL, instr(CAST(EncryptedToken AS TEXT), 'refresh_token') FROM ExternalCalendars WHERE Provider = 1;"
```

Expected: `1|0` — a token exists and the literal string `refresh_token` does not appear in it.

7. Unset `AARONOS_GOOGLE_CLIENT_SECRET`, relaunch, and click **Connect Google Calendar**. Expected: the status explains that the variable must be set, and the app does not crash.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add Google Calendar source with DPAPI-cached OAuth credentials"
```

**Do not commit the client secret file.** Confirm with `git status` that no `client_secret*.json` is staged, and add `client_secret*.json` to `.gitignore` as a guard.

---

## Task 9: Failure-path verification

**Files:** none. This task changes nothing; it confirms the fail-soft requirement holds against both providers, which no unit test can establish.

- [ ] **Step 1: Network failure**

Disconnect the network (or set the ICS URL to `https://127.0.0.1:9/none.ics`), then click **Sync all now**.

Expected: each affected row shows red error text, cached events **remain visible** on Today and Week, and no dialog appears. Reconnect and sync again; the error clears and `LastSyncedAt` updates.

The critical assertion is the second one: a failed fetch must never empty the cache. If Today goes blank, `SyncCalendarAsync` is merging against a failed fetch — the `catch` must return before reaching the merge.

- [ ] **Step 2: Revoked Google access**

At <https://myaccount.google.com/permissions>, remove access for the OAuth app, then click **Sync** on the Google row.

Expected: an error on the row, cached events retained, no crash. Clicking **Connect Google Calendar** re-prompts and recovers.

- [ ] **Step 3: Corrupted token**

Simulate a copied database:

```bash
sqlite3 "$LOCALAPPDATA/AaronOS/aaronos.db" "UPDATE ExternalCalendars SET EncryptedToken = X'01020304' WHERE Provider = 1;"
```

Launch and click **Sync** on the Google row. Expected: `DpapiDataStore.GetAsync` treats the undecryptable blob as "no token", so the flow re-prompts for consent rather than throwing. Complete it and confirm sync works again.

- [ ] **Step 4: Background tick resilience**

With a broken ICS URL configured, leave the app open for at least 31 minutes so the tick's periodic sync runs and fails.

Expected: the app keeps running, notifications still fire for overdue routines, and the only evidence of the failure is the error text on the Settings row plus `Debug.WriteLine` output. A tick that throws would silently stop all notifications for the rest of the session — that is the failure this step exists to rule out.

- [ ] **Step 5: Record the outcome**

No code changed unless a step surfaced a fix. Commit any fix with a message naming what the verification caught, and note in the commit whether the Task 0 gate passed — whether this build actually has a working Outlook feed, or is Google-only pending a Graph app registration.

---

## Definition of done for Plan 4

- `dotnet build AaronOS.slnx --nologo` succeeds.
- `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo` reports 93 passing tests, 0 failing.
- Outlook (if the Task 0 gate passed) and Google events appear on Today, Week, and in the bedtime calculation.
- Re-syncing does not duplicate events; a cancelled event disappears; a declined or free event never consumes a free gap.
- Every failure path in Task 9 leaves cached data intact and surfaces as row-level error text, never as a dialog or a crash.
- No plaintext token exists in the database, and no client secret is committed.

## Deferred to Plan 5

Gmail scanning and Claude-based extraction. Also deliberately not built here: writing to any external calendar, Microsoft Graph as an alternative Outlook transport (the `IExternalCalendarSource` seam is ready for it), and selecting a non-primary Google calendar (`RemoteCalendarId` supports it; the UI hard-codes `"primary"`).

# Schedule Module — Design

## Context

AaronOS is a modular WPF desktop app (see `docs/MODULE_GUIDELINES.md`) with three modules today:
`AaronOS.Modules.BodyMeasurements`, `AaronOS.Modules.Finance`, and `AaronOS.Modules.Nutrition`.
The user wants a fourth module that holds the shape of their week: work hours, off-work time, sleep,
recurring chores (gym, house cleaning, cat litter, trash), goals, and upcoming release dates — and
that makes suggestions about what to do when, informed by their real calendars.

Two calendars matter. Work lives in Outlook under a `wemautomation.com` account; everything personal
lives in Google. The machine has only the new Outlook MSIX wrapper (no classic Outlook, no Outlook
profiles, no local `.ost`) and is neither Entra-joined nor domain-joined, so there is no local
Outlook data source to read. Work calendar data has to come over the network.

This module follows `docs/MODULE_GUIDELINES.md` exactly: a compiled-in `IAppModule`, its own entities
discovered automatically by the shared `AaronOsDbContext`, its own ViewModels and Pages, one project
reference from `AaronOS.App`, one line appended to the module array in `App.xaml.cs`.

## Scope

This spec covers the whole feature area in one module, at the user's explicit direction after being
shown a decomposed alternative. The implementation phases (see "Build order") are ordered so the
local, dependency-free parts land and become usable before any OAuth work begins — a tenant
restriction on the calendar side cannot strand the rest of the module.

In scope:

- A recurring weekly schedule template (work, sleep, personal blocks) with dated exceptions for PTO,
  overtime, travel, and one-off changes. The user's work hours are fixed weekly with occasional
  exceptions, so the template plus exceptions model fits directly.
- Recurring routines with an interval or a fixed weekday, completion logging, and next-due /
  overdue-by-N-days computation.
- A configurable nightly sleep target and a recommended bedtime derived from the next day's first
  commitment. Hours actually slept are **not** recorded here — see "Sleep is forward-looking only".
- Goals with optional target dates, progress, and milestones.
- Release tracking for media (games, movies, shows) and products (hardware launches, restocks) in one
  dated-record table with a category.
- A ranked suggestion list surfaced on a Today panel, plus native Windows notifications.
- Read-only ingestion of the work Outlook calendar via a published-calendar ICS feed.
- Read-only ingestion of the personal Google Calendar via the Google Calendar API.
- Gmail scanning for dated things that never made it onto a calendar, extracted via the Claude API
  into a review queue that the user approves before anything joins the schedule.

Explicitly out of scope, noted so it is not silently forgotten:

- **Writing to any external calendar.** Both integrations are read-only.
- **Microsoft Graph.** See "Outlook access" below — the calendar layer is designed so a Graph
  provider can be added later without reworking the module, but Graph is not built here.
- **Toasts while the app is closed.** Notifications require the app to be running; a Windows
  Scheduled Task that wakes something up is a separate concern.
- **Any record of hours slept.** The Medical module already owns that twice over: `MoodEntry` holds a
  self-report and `SleepNight` holds measured hours imported from a Withings sleep pad, with
  `MoodStatistics.SleepFor` deciding which to show. A third store here would mean entering sleep in
  two modules and could disagree with the pad.
- **Sleep debt.** It needs actual hours, which live in Medical, and `MODULE_GUIDELINES.md` forbids
  reading another module's entities. Adding it means first promoting the nightly-sleep shape into
  `AaronOS.Core` so both modules share one definition — deliberately its own piece of work.
- **Body-composition goals.** Those already live in `BodyMeasurements`, and `MODULE_GUIDELINES.md`
  forbids reaching across module boundaries. `Goal` here is a generic dated-goal record; the two
  coexist without referencing each other.
- **Metric units and time-zone travel.** Local time only, consistent with the rest of the app.

## Confidence and open questions

Stated plainly, because these shape the build:

- **Whether the work tenant permits publishing a calendar is unknown.** Outlook Web offers
  Settings → Calendar → Shared calendars → Publish a calendar, which yields an anonymous `.ics` URL,
  but tenants frequently disable it. This is verified at the start of phase 7, not before. If it is
  disabled, phases 1–6 and 8–9 are unaffected and the work calendar stays manual until a Graph app
  registration is approved.
- **Published ICS feeds refresh slowly** — often hours behind. The work calendar will not be
  real-time. This is a property of the transport, not a bug to fix.
- **Mail extraction will be imperfect.** Every extracted item lands in a review queue rather than on
  the schedule. False positives cost a dismissal; false negatives are invisible. That trade is
  accepted deliberately.
- **Sleep recommendations are arithmetic on the user's own numbers**, not a clinical determination.
  The module computes a bedtime from the next day's commitments and a debt figure against a target
  the user sets. It does not determine how much sleep the user personally needs; nothing beyond the
  general 7–9 hour adult range would have any basis.

## Module shape

`AaronOS.Modules.Schedule`, a class library exactly like the existing three:

```csharp
public class ScheduleModule : IAppModule
{
    public string Id => "schedule";
    public string DisplayName => "Schedule";
    public string IconGlyph => "CalendarLtr24"; // confirm exact Wpf.Ui.Controls.SymbolRegular member at implementation time
    public Type HomePageType => typeof(ScheduleShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        // ViewModels transient, services singleton — see RegisterServices below
    }
}
```

`ScheduleShellPage` carries a button row and an internal `Frame`, following
`BodyMeasurementsShellPage`, and navigates between the module's own pages with
`Frame.Navigate(new SomePage())`. The shell knows only about `ScheduleShellPage`.

Pages: **Today**, **Week**, **Routines**, **Sleep**, **Goals & Releases**, **Review Inbox**,
**Settings**. One ViewModel per page, registered transient, derived from `AaronOS.Core.ViewModelBase`,
resolved in each page's constructor via `AppServices.Provider.GetRequiredService<T>()` with
`DataContext` set explicitly and load work kicked off from the `Loaded` event.

### csproj

Per `MODULE_GUIDELINES.md`: `net8.0-windows`, `UseWPF`, `LangVersion 13.0`, `Nullable enable`. Plus
`<UseWindowsForms>true</UseWindowsForms>` for `NotifyIcon` (see "Notifications"). Project reference to
`AaronOS.Core`; package references to `WPF-UI`, `Ical.Net` 5.2.3, `Google.Apis.Calendar.v3` 1.75.0,
`Google.Apis.Gmail.v1` 1.74.0, `Google.Apis.Auth`, and `Anthropic`.

`Ical.Net` is a deliberate dependency rather than a hand-rolled parser: RRULE expansion, VTIMEZONE
handling, line unfolding, and value escaping are substantially more code to get right than a package
reference, and getting them subtly wrong produces a calendar that is quietly incorrect.

Note the CommunityToolkit.Mvvm gotcha from the guidelines: use the field-backed
`[ObservableProperty] private bool _x;` form, not partial properties, and ignore `MVVMTK0045`.

## Data model

All entities live under `Data/` with a matching `IEntityTypeConfiguration<T>`. Names are
domain-specific enough that EF Core's default pluralized table naming cannot collide with another
module.

### Schedule

**`ScheduleBlock`** — the recurring template.

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | int | |
| `Kind` | `ScheduleBlockKind` enum | `Work`, `Sleep`, `Personal` |
| `Label` | string | e.g. "Core hours" |
| `DaysOfWeek` | `DayOfWeekFlags` enum (flags) | Stored as int |
| `StartTime` / `EndTime` | `TimeSpan` | Local wall-clock. `EndTime < StartTime` means it wraps midnight (sleep blocks) |
| `EffectiveFrom` | `DateOnly` | |
| `EffectiveTo` | `DateOnly?` | Null = open-ended |
| `IsActive` | bool | |

The user's fixed weekly hours are a small number of rows here.

**`ScheduleException`** — a dated override, so reality does not require editing the template.

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | int | |
| `Date` | `DateOnly` | |
| `ScheduleBlockId` | int? | Null = a standalone one-off block on that date |
| `IsCancelled` | bool | True = the referenced block does not occur (PTO, holiday) |
| `Kind` | `ScheduleBlockKind?` | For standalone entries |
| `Label` | string? | |
| `StartTime` / `EndTime` | `TimeSpan?` | Replacement times, or the times of a standalone entry |
| `Note` | string? | |

Index on `Date`.

### Routines

**`Routine`**

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | int | |
| `Name` | string | |
| `Category` | `RoutineCategory` enum | `Gym`, `Cleaning`, `LitterBox`, `Trash`, `Other` |
| `IntervalDays` | int? | Null when the routine is weekday-pinned instead |
| `PreferredDaysOfWeek` | `DayOfWeekFlags?` | Trash night is a fixed weekday; the litter box is an interval |
| `PreferredTimeOfDay` | `TimeSpan?` | A hint for the suggestion ranking, not a hard slot |
| `EstimatedMinutes` | int? | Used to match a routine against an actual free gap |
| `IsActive` | bool | |

Exactly one of `IntervalDays` and `PreferredDaysOfWeek` must be set; validated in the ViewModel and
asserted in `RoutineScheduler`.

The Routines page enforces the exclusivity structurally rather than by checking it after the fact.
Seven day checkboxes sit beside the interval box, mirroring how the Week page picks a block's days;
ticking any day disables the interval box, and the save path writes `PreferredDaysOfWeek` with
`IntervalDays` null, or the reverse. Both-set is therefore unreachable, and the one remaining
validation is that at least one mode is filled in — which matters because
`RoutineScheduler.Evaluate` throws on a routine with neither set and `EvaluateAll` propagates that,
failing the whole page load rather than one row.

**`RoutineCompletion`** — `Id`, `RoutineId` (FK, cascade delete), `CompletedAt` (`DateTime`),
`Note` (string?). Index on `(RoutineId, CompletedAt)`.

Next-due is computed from the most recent completion, never stored. Storing a "next due" column
would need rewriting on every completion and would silently drift if a completion were edited or
deleted.

### Sleep

**Sleep is forward-looking only.** This module stores a target and computes a bedtime from it. It
does not store hours slept, and there is no `SleepLog` entity.

The Medical module already records nightly sleep twice: `MoodEntry` carries a self-reported figure
alongside mood and energy, and `SleepNight` carries measured hours, stages, heart rate and a sleep
score imported from a Withings sleep pad, with `MoodStatistics.SleepFor` resolving which to display.
Duplicating that here would ask for the same data in two places and produce a second set of numbers
that could contradict the pad. So Schedule keeps only the half Medical lacks — "given tomorrow's
first commitment, when should I be in bed?" — which reads the agenda and needs no history at all.

The cost is that sleep debt is out of scope, since debt needs actual hours. Closing that gap means
promoting the nightly-sleep shape into `AaronOS.Core` so both modules depend on one definition, per
the cross-module rule in `MODULE_GUIDELINES.md`. That is a separate design decision, not something to
improvise while building this module.

**`SleepSettings`** — single-row table, following the `UserProfile` pattern but kept in this module
since nothing else needs it: `Id`, `TargetHours` (`decimal`, default 8.0), `SleepOnsetMinutes` (int,
default 15), `MorningRoutineMinutes` (int, default 45), `WindDownLeadMinutes` (int, default 30).

### Goals and releases

**`Goal`** — `Id`, `Title`, `Description` (string?), `TargetDate` (`DateOnly?`), `ProgressPercent`
(int 0–100), `Status` (`GoalStatus`: `Active`, `Paused`, `Done`, `Abandoned`), `CreatedAt`,
`CompletedAt` (`DateTime?`).

**`GoalMilestone`** — `Id`, `GoalId` (FK, cascade delete), `Title`, `DueDate` (`DateOnly?`),
`IsDone` (bool), `SortOrder` (int).

**`Release`** — `Id`, `Title`, `Category` (`ReleaseCategory`: `Media`, `Product`), `ReleaseDate`
(`DateOnly`), `IsDateEstimated` (bool), `Url` (string?), `Notes` (string?), `IsDismissed` (bool).
Index on `ReleaseDate`.

One table for both media and product launches, per the user's choice — they differ only by category
and by whether the date implies an action.

### External calendars and mail

**`ExternalCalendar`**

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | int | |
| `Provider` | `CalendarProvider` enum | `OutlookIcs`, `GoogleCalendar` |
| `DisplayName` | string | |
| `IcsUrl` | string? | `OutlookIcs` only |
| `RemoteCalendarId` | string? | `GoogleCalendar` only |
| `EncryptedToken` | byte[]? | DPAPI-protected; Google only |
| `IsEnabled` | bool | |
| `LastSyncedAt` | `DateTime?` | |
| `LastError` | string? | Null on success |

**`ExternalEvent`** — `Id`, `ExternalCalendarId` (FK, cascade delete), `ExternalUid` (string),
`Title`, `StartsAt` (`DateTime`), `EndsAt` (`DateTime`), `IsAllDay` (bool), `Location` (string?),
`IsBusy` (bool), `LastSeenAt` (`DateTime`). **Unique index on `(ExternalCalendarId, ExternalUid)`** —
this is what makes re-syncing idempotent.

External events are cached into local tables rather than fetched live per page load. The suggestion
engine has to reason about tomorrow's commitments while offline, and the published-ICS feed is slow
enough that re-fetching on navigation would make the UI feel broken.

**`InboxItem`** — mail-derived candidates awaiting review.

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | int | |
| `SourceMessageId` | string | Gmail message id; unique index |
| `DetectedTitle` | string | |
| `DetectedDate` | `DateOnly?` | Null when extraction found no usable date |
| `Kind` | `InboxItemKind` enum | `Appointment`, `Delivery`, `Release`, `Deadline`, `Other` |
| `Confidence` | decimal | 0–1, as reported by the extractor |
| `RawSubject` / `RawSnippet` | string | Shown in review so the user can judge |
| `Status` | `InboxItemStatus` enum | `Pending`, `Accepted`, `Dismissed` |
| `CreatedAt` | `DateTime` | |

Accepting an item creates a `Release`, a `ScheduleException`, or a `Goal` depending on `Kind`, and
flips the item to `Accepted`. Nothing from mail reaches the schedule without that step.

## Calendar views

Revised 2026-07-28, after the first version shipped. The original design gave the module a Today
list, a Week list and a Routines page. In use, the day-list layout turned out to be the wrong shape:
every entry renders the same height regardless of duration, so a fifteen-minute check-in and a
three-hour block look identical, and there is no sense of a day filling up. That is precisely what
time blocking needs to convey. So the calendar becomes a **time grid modelled on Outlook's**, and the
Today view is removed — week and month are the only views.

### The two views

**Week** is the default. A fixed time gutter on the left, seven day columns, hour lines across. Each
item is positioned by its real start and sized by its real duration, so the shape of the day is
visible at a glance. Opens scrolled to 07:00 rather than midnight, because otherwise the working day
starts below the fold; the full 00:00–24:00 range remains reachable by scrolling.

**Month** is a six-by-seven cell grid. No time positioning — Outlook does not do it either at this
density and it would be illegible. Each cell lists its items in start order, capped, with a
"+N more" affordance; clicking a day switches to Week focused on that week.

### The all-day band

Outlook puts all-day items in a separate strip above the grid, and copying that is not cosmetic — it
is required. `ExternalEventProjector` maps an all-day event to `00:00`–`24:00`, which in a time grid
is a block occupying the entire column height. A single all-day event would otherwise bury the whole
day's real meetings behind it. So any item spanning the full day is lifted out of the grid into the
band. This is the first design consequence of the projector's existing behaviour, and it applies
equally to a `Sleep` block that wraps midnight — those already arrive pre-split per day by
`AgendaBuilder`, so they sit in the grid, correctly, as two partial-day blocks.

### Overlap layout is a pure function

Two meetings at the same time must sit side by side, each at half width, exactly as Outlook does.
The rule is interval-graph lane assignment, and it belongs in a pure service along
`AgendaBuilder` — no `DbContext`, no visual types:

`IReadOnlyList<PositionedItem> TimeGridLayout.Assign(IReadOnlyList<CalendarItem> itemsForOneDay)`

returning each item with a `Lane` and the `LaneCount` of its overlap cluster. Sort by start; assign
each item to the first lane whose previous item has already ended; the lane count is computed **per
cluster of mutually overlapping items, not per day** — otherwise one overlapping pair at 09:00
shrinks every unrelated block in the day to half width, which is wrong and looks broken. The view
then derives geometry arithmetically: `Left = Lane / LaneCount * columnWidth`,
`Width = columnWidth / LaneCount`, `Top = Start.TotalMinutes * pixelsPerMinute`.

Keeping this pure is what makes the hard part testable. Lane assignment has genuine edge cases —
identical spans, an item entirely enclosed by another, a chain where A overlaps B and B overlaps C
but A and C do not touch — and none of them need a running application to verify.

### Click a slot to add

Clicking empty grid space opens the existing add-block form pre-filled with that day and a start time
snapped to the nearest fifteen minutes. The pixel-to-time conversion is arithmetic and lives with the
layout service, not in the code-behind, so it is testable too.

Drag-to-create and drag-to-resize are deliberately **not** in this revision. They are most of the
cost of a calendar control — hit-testing, snapping, live preview, undo — and the grid is what
delivers the Outlook feel. They are the obvious next increment once the grid is in use.

### `CalendarItem`: the seam for other modules

The grid renders a plain record and knows nothing about this module's entities:

```csharp
public sealed record CalendarItem(
    DateOnly Date, TimeSpan Start, TimeSpan End,
    string Label, CalendarItemKind Kind, string? Detail);
```

The Schedule module maps its `AgendaEntry` values into this. The point is that a calendar is
plausibly useful to other modules — Medical's appointments, Nutrition's meal times — and
`MODULE_GUIDELINES.md` forbids one module reading another's entities, so the shared shape would have
to live in `AaronOS.Core` with a contribution point beside `SettingsContentType`.

That contribution point is **not** being built yet. Building it now would be guessing at what other
modules need, and it changes a contract all five modules compile against. Rendering from
`CalendarItem` rather than `AgendaEntry` is the whole preparation required: when a second module
actually has something to show, the record moves to Core and an interface is added, without touching
the grid. This is a deliberate YAGNI call, recorded so the next person knows it was a decision and
not an oversight.

### What this replaces

The Today page is removed. Its content — today's remaining items, overdue routines, the free-time
readout, and the ranked suggestion list that Plan 2 was going to put there — becomes a narrow rail on
the right of the week view. `TodayViewModel` is largely reusable as that rail's ViewModel, so little
of the shipped work is wasted. Plans 2 and 3 both reference the Today page and need amending.

## Services

Five pure services — they take values and return values, touch no database, and are where the tests
live. Plus the I/O-bound clients, kept deliberately thin.

### `AgendaBuilder` (pure)

`IReadOnlyList<AgendaDay> Build(DateOnly from, DateOnly to, IReadOnlyList<ScheduleBlock> blocks,
IReadOnlyList<ScheduleException> exceptions, IReadOnlyList<ExternalEventEntry> events)`

The last parameter takes `ExternalEventEntry` — a flat record of the fields the builder actually
needs — rather than the `ExternalEvent` entity, so the builder stays pure and testable without
constructing persistence objects. An all-day event maps to `00:00`–`24:00`; mapping its end to
`00:00` would be discarded as a zero-duration span.

Expands blocks across the range honouring `DaysOfWeek` and the effective-date window, applies
exceptions (cancellations remove, time overrides replace, standalone entries add), merges external
events, and returns days each holding an ordered list of `AgendaEntry` (start, end, kind, label,
source) plus the computed free gaps between committed entries. Midnight-wrapping sleep blocks are
split across the day boundary so a day's entries are always sorted and non-wrapping.

Everything downstream consumes this. It is the single place recurrence is interpreted.

One consequence of splitting wrapping blocks is worth stating outright, because it is a choice
rather than a fallout. Cancelling a block on date D removes that date's occurrence — both its
evening segment on D and the morning tail it would carry into D+1 — but not the tail that arrived
on D from D-1's occurrence. For a night shift, "cancel Tuesday's shift" therefore means the shift
that starts Tuesday evening, and Tuesday morning still shows the tail of Monday night's shift.
That reading matches how someone requesting a day off would describe it, and the alternative
(clearing the calendar day) would silently truncate the previous day's shift.

### `RoutineScheduler` (pure)

For each routine plus its completion history, returns `NextDueDate` and `OverdueByDays`. Interval
routines are due at `lastCompleted + IntervalDays` (or immediately if never completed);
weekday-pinned routines are due on the next matching weekday not already covered by a completion.

### `SleepPlanner` (pure)

- `RecommendedBedtime(DateOnly tonight, AgendaDay tomorrow, SleepSettings settings)` — works backward
  from tomorrow's first committed entry: minus `MorningRoutineMinutes`, minus `TargetHours`, minus
  `SleepOnsetMinutes`. When tomorrow has no commitments, falls back to the active `Sleep`
  `ScheduleBlock`.
`RecommendedBedtime` is the whole service — one method, no result record. There is no debt or average
computation, because this module holds no sleep history to compute them from.

### `SuggestionEngine` (pure)

Takes the agenda's free gaps, routine due states, tonight's recommended bedtime, upcoming releases,
and goal milestone dates. Returns a ranked `IReadOnlyList<Suggestion>` (title, reason, suggested time
window, urgency). Ranking rules, in order:

1. Overdue routines rank above merely-due ones, by days overdue.
2. A routine whose `EstimatedMinutes` fits an actual free gap ranks above one that does not.
3. A routine whose `PreferredTimeOfDay` falls inside a free gap ranks above one placed elsewhere.
4. Releases and milestones within 7 days appear as informational entries, never as chores.
5. Tonight's bedtime always appears, pinned last.

Deterministic and pure, so the ranking is directly testable.

### `ExternalEventMerger` (pure)

Upserts a fetched batch against the cached rows for one calendar, keyed on `ExternalUid`: new UIDs
insert, known UIDs update in place, and UIDs absent from a full-window fetch are deleted. Mirrors the
existing `TransactionSyncMerger` in the Finance module.

### I/O clients

- **`IcsFeedClient`** — `HttpClient` GET plus `Ical.Net` parse, expanding occurrences across the sync
  window into `ExternalEvent` DTOs. Behind an `IExternalCalendarSource` interface so a future Graph
  provider drops in beside it.
- **`GoogleCalendarClient`** / **`GmailClient`** — `Google.Apis`, using the installed-app loopback
  OAuth flow. `GoogleCalendarClient` reads events over a window; `GmailClient` lists messages matching
  a configured query and returns subject + snippet only.
- **`DpapiDataStore`** — a small `IDataStore` implementation backed by `ExternalCalendar.EncryptedToken`
  so Google's token cache lands in the app database under DPAPI rather than in a `FileDataStore`.
  Uses the same `ProtectedData.Protect` / `Unprotect` shape as
  `Finance.Plaid.AccessTokenProtector` and `Nutrition.Usda.ApiKeyProtector`.
- **`MailEventExtractor`** — see below.
- **`ScheduleSyncService`** — orchestrates fetch → merge → persist per enabled calendar, records
  `LastSyncedAt` / `LastError`, and is invoked from Settings and from the background tick.
- **`NotificationService`** — see below.

### `RegisterServices`

ViewModels transient (a fresh instance per navigation, per the guidelines); the I/O clients
singleton. The pure services register nothing: `AgendaBuilder` and `RoutineScheduler` shipped as
static classes, because a service holding no state and reading no clock has nothing to inject.
`SleepPlanner` and `SuggestionEngine` should follow that same shape rather than leaving the module
with an arbitrary mix. Database access through the injected
`IDbContextFactory<AaronOsDbContext>` with a short-lived context per unit of work:
`await using var db = await _dbContextFactory.CreateDbContextAsync();`

## Mail extraction via the Claude API

`MailEventExtractor` sends the subject and snippet of a candidate message to the Claude API and
receives a structured object back.

- **Model:** `claude-opus-5`.
- **Structured output:** `OutputConfig.Format = new JsonOutputFormat { Schema = ... }` with a schema
  covering `title` (string), `date` (string, ISO date or null), `kind` (enum matching
  `InboxItemKind`), and `confidence` (number 0–1). Using the schema rather than parsing free text
  means the model retries on mismatch at the API layer instead of producing something the module has
  to defensively parse.
- **Effort:** `OutputConfig.Effort = Effort.Low`. This is mechanical extraction from two short
  strings.
- **Thinking:** left at the default (on) with generous `MaxTokens` headroom, since `MaxTokens` caps
  thinking plus response together. Disabling thinking on this model has documented failure modes and
  is not worth the token saving on calls this small.
- **Refusals:** a declined request returns HTTP 200 with `StopReason == "refusal"`. The extractor
  checks `StopReason` before reading `Content`, and records the item as unextracted rather than
  throwing.
- **Credentials:** the API key is stored DPAPI-protected in a single-row credential table, copying
  `Nutrition.Usda.UsdaCredentialStore` exactly. Absent a key, phase 9 is inert and the rest of the
  module is unaffected.

Cost: subject plus snippet is roughly 150 input tokens and 100 output, so about **$0.003 per email
scanned** at Opus 5's $5 / $25 per million tokens — a few dollars a month at fifty scanned messages a
day. Only messages matching the configured Gmail query are scanned, and each message id is scanned
at most once thanks to the unique index on `InboxItem.SourceMessageId`.

## Notifications

`NotificationService` uses `System.Windows.Forms.NotifyIcon` and `ShowBalloonTip`, which ships with
the .NET Windows Desktop SDK. Windows 10/11 routes balloon tips through the Action Center as real
toasts, so this needs no NuGet package, no Start-menu shortcut carrying an AUMID, and no COM
activator class.

The ceiling, stated so the upgrade path is known: title-and-text only, no action buttons, no
click-to-navigate, and a tray icon must exist while notifications are wanted. If actionable toasts
become worth the setup cost, `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 plus the AUMID shortcut and
COM activator is the replacement, and it swaps in behind `NotificationService` without touching
callers.

A single `PeriodicTimer` ticking every minute while the app runs drives three things: overdue-routine
notifications (at most one per routine per day), the nightly wind-down reminder at
`RecommendedBedtime − WindDownLeadMinutes`, and a periodic external-calendar sync. One timer, not a
scheduling framework. Notifications fired are recorded in memory for the session so a tick storm
cannot produce duplicates.

## Error handling

Every path that leaves the machine fails soft:

- A failed ICS fetch, Google API call, or Claude call records the message on
  `ExternalCalendar.LastError` (or the credential row for Claude), leaves the cached data untouched,
  and is surfaced on the Settings page next to `LastSyncedAt`. Nothing throws into the UI.
- A DPAPI `Unprotect` failure (a copied database, a changed Windows account) is treated as
  "not authorized": the calendar is shown as needing re-authorization rather than crashing.
- A malformed ICS payload fails that one calendar's sync and leaves other calendars alone.
- The local schedule, routines, sleep, goals, and releases all work with every integration disabled.
  That is the whole point of the phase ordering.

**Schema caveat.** `AaronOS.Core.Data.SchemaBootstrapper` creates *missing tables* at startup but does
not alter tables that already exist. Adding this module to an existing database is therefore safe and
requires no deletion — which matters, because the database holds linked bank connections that can
only be re-established through an OAuth flow. However, any later change to a column on one of these
entities needs a hand-written `ALTER TABLE` or a dropped local database
(`%LocalAppData%\AaronOS\aaronos.db`). Worth getting the entity shapes right in phase 1.

## Build order

Phases 1–6 have no external dependencies and produce a usable module on their own. Phases 7–9 add the
integrations.

1. **Scaffold and agenda.** Project, `IAppModule`, `ScheduleShellPage`, `ScheduleBlock` /
   `ScheduleException` entities and configurations, `AgendaBuilder`, Today and Week pages, block
   editing UI. Register in `AaronOS.App`. Usable immediately.
2. **Routines.** `Routine`, `RoutineCompletion`, `RoutineScheduler`, Routines page with completion
   logging and next-due display.
3. **Sleep.** `SleepSettings`, `SleepPlanner`, Sleep page with target configuration and the
   recommended bedtime. No logging, no debt figure.
4. **Goals and releases.** `Goal`, `GoalMilestone`, `Release`, the Goals & Releases page.
5. **Suggestions.** `SuggestionEngine` wired into the Today panel.
6. **Notifications.** `NotificationService`, tray icon, `PeriodicTimer` tick for overdue routines and
   the wind-down reminder.
7. **Outlook ICS sync.** Verify the tenant permits calendar publishing *first*. Then
   `ExternalCalendar` / `ExternalEvent`, `IExternalCalendarSource`, `IcsFeedClient`,
   `ExternalEventMerger`, `ScheduleSyncService`, Settings UI, and merging external events into the
   agenda. If publishing is disabled, stop here and record that the Graph provider is the remaining
   option — nothing else in the module is blocked.
8. **Google Calendar.** `DpapiDataStore`, `GoogleCalendarClient`, OAuth consent flow (reusing the
   `OAuthPopupWindow` pattern from Finance where it fits), sync wired into `ScheduleSyncService`.
9. **Gmail scan.** `GmailClient`, `MailEventExtractor`, Claude credential storage, `InboxItem`, the
   Review Inbox page with accept/dismiss, and accept-to-entity conversion.

## Testing

`AaronOS.Modules.Schedule.Tests`, mirroring `AaronOS.Modules.Finance.Tests` and
`AaronOS.Modules.Nutrition.Tests`. The pure services carry the coverage:

- **`AgendaBuilder`** — weekday expansion; effective-date windows; cancellation exceptions; time-override
  exceptions; standalone one-off entries; midnight-wrapping sleep blocks split correctly; external
  events merged in start order; free-gap computation, including a fully-booked day (no gaps) and an
  empty day (one gap).
- **`RoutineScheduler`** — never-completed routine is due now; interval routine due date after one and
  several completions; overdue-by-days arithmetic; weekday-pinned routine skips a weekday already
  covered by a completion.
- **`SleepPlanner`** — bedtime derived from tomorrow's first commitment; fallback to the sleep block
  when tomorrow is empty; debt across a mixed 14-night window; a long night does not offset a short
  one.
- **`SuggestionEngine`** — overdue outranks due; a routine that fits a gap outranks one that does not;
  preferred-time placement; bedtime pinned last; releases appear as informational, not as chores.
- **`ExternalEventMerger`** — insert, update-in-place, delete-when-absent, and idempotency (merging
  the same batch twice is a no-op).
- **`IcsFeedClient` parsing** — against a checked-in `.ics` fixture covering a simple event, a
  recurring event with an RRULE, an all-day event, and an event carrying a VTIMEZONE. Parsing is
  tested; the HTTP fetch is not.

The network clients (`GoogleCalendarClient`, `GmailClient`, `MailEventExtractor`) are not unit-tested.
Their logic is thin by design; the value lives in the pure services above.

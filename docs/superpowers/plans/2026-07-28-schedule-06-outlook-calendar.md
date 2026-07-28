# Schedule Module — Plan 6: Outlook-style calendar (week + month time grid)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the day-list calendar with an Outlook-style time grid — week and month views, items positioned and sized by their real times, overlapping items side by side, an all-day band, and click-a-slot to add. Remove the Today page and move its content to a rail on the week view.

**Architecture:** All geometry and layout decisions are pure functions in `Calendar/` — `CalendarItemMapper` shapes data, `TimeGridLayout` assigns overlap lanes and converts between pixels and time. The views own no arithmetic beyond calling those. A small custom `Panel` arranges positioned items, because that is the one place WPF genuinely needs imperative layout. The grid renders a provider-agnostic `CalendarItem` record, never this module's entities.

**Tech Stack:** Unchanged — .NET 8 `net8.0-windows`, WPF, WPF-UI 4.3.0, EF Core 8 + SQLite, CommunityToolkit.Mvvm, xUnit. **No new NuGet package.** If a task seems to need one, stop and report it.

**Spec:** `docs/superpowers/specs/2026-07-27-schedule-module-design.md`, the `## Calendar views` section. Read that section before starting — it records *why* each decision was made, which this plan does not repeat.

**Prerequisite:** Plans 1 and 4 complete. `AgendaBuilder.Build` returns `AgendaDay` values holding `AgendaEntry` and `FreeGap` lists; `ExternalEventProjector` feeds external events in. 69 tests pass.

## Global Constraints

Every task's requirements implicitly include this section. These repeat earlier plans' constraints because a task's implementer may not have read them, and every one of them has already caused a real defect in this project.

- Target framework `net8.0-windows`; `UseWPF` true; `LangVersion` `13.0`; `Nullable` `enable`.
- **Never use the partial-property `[ObservableProperty]` form.** The generator does not run in this environment. Always write `[ObservableProperty] private bool _x;` and ignore `MVVMTK0045` — that warning is expected and must not be "fixed".
- **Every `<Run Text="{Binding ...}">` must specify `Mode=OneWay`.** `Run.Text` is registered `BindsTwoWayByDefault` and throws at runtime against a get-only property. `TextBlock.Text` does not need it.
- WPF `Grid`/`StackPanel` have no `Spacing`/`Padding` — use explicit `Margin` on children.
- `ui:NumberBox.Value` is `double?` on WPF-UI 4.3.0; `null`, not `double.NaN`, means "not entered".
- **Never hard-code a colour.** Use `DynamicResource` theme brushes so light and dark both work. A literal `#RRGGBB` for a block fill will look correct in whichever theme you happened to test and wrong in the other. This is a new constraint for this plan and the most likely thing to get wrong.
- Pure services take dates and sizes as parameters. **Never read `DateTime.Now` inside a pure service.**
- Times are `TimeSpan` (wall clock) or `DateTime` (local), never `DateTimeOffset`.
- ViewModels derive from `AaronOS.Core.ViewModelBase` and get data through the injected `IDbContextFactory<AaronOsDbContext>`, one short-lived context per operation.
- Pages/UserControls have a public parameterless constructor, resolve their ViewModel via `AaronOS.Core.AppServices.Provider.GetRequiredService<T>()`, set `DataContext`, then `InitializeComponent()`, then hook `Loaded`.
- Per-item buttons in a `DataTemplate` use a code-behind `Click` handler reading `DataContext`.
- **Any query for `ExternalEvent` rows must use an interval-overlap test** (`StartsAt < windowEnd && EndsAt > windowStart`), never a `StartsAt` range. A `StartsAt` filter omits a multi-day event that began before the window and is still running; in the sync path that produced a permanently failing calendar. Match the existing queries in `TodayViewModel`/`WeekViewModel`.
- Database-backed tests write through one `DbContext` and read through a **separate** one. EF identity resolution returns the tracked instance, so a same-context read asserts against the object the test constructed.
- Run tests with `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`.
- Test-count expectations in this plan are **deltas**, not totals. Absolute counts have gone stale three times in this project.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/AaronOS.Modules.Schedule/Calendar/CalendarItem.cs` | Provider-agnostic item record + `CalendarItemKind` |
| `src/AaronOS.Modules.Schedule/Calendar/CalendarItemMapper.cs` | `AgendaEntry` → `CalendarItem`; splits all-day from timed |
| `src/AaronOS.Modules.Schedule/Calendar/TimeGridLayout.cs` | Overlap lane assignment + pixel/time arithmetic |
| `src/AaronOS.Modules.Schedule/Calendar/PositionedItem.cs` | An item plus its lane and cluster lane count |
| `src/AaronOS.Modules.Schedule/Views/TimeGridPanel.cs` | Custom `Panel` arranging positioned items |
| `src/AaronOS.Modules.Schedule/ViewModels/CalendarWeekViewModel.cs` | Week grid state, replaces `WeekViewModel`'s display role |
| `src/AaronOS.Modules.Schedule/ViewModels/CalendarMonthViewModel.cs` | Month cell state |
| `src/AaronOS.Modules.Schedule/ViewModels/AgendaRailViewModel.cs` | The right-hand rail (was `TodayViewModel`) |
| `src/AaronOS.Modules.Schedule/Views/CalendarWeekPage.xaml(.cs)` | Week grid + all-day band + rail |
| `src/AaronOS.Modules.Schedule/Views/CalendarMonthPage.xaml(.cs)` | Month grid |
| `src/AaronOS.Modules.Schedule.Tests/TimeGridLayoutTests.cs` | Lane assignment and geometry tests |
| `src/AaronOS.Modules.Schedule.Tests/CalendarItemMapperTests.cs` | Mapping and all-day banding tests |

---

## Task 1: `CalendarItem` and the mapper

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Calendar/CalendarItem.cs`
- Create: `src/AaronOS.Modules.Schedule/Calendar/CalendarItemMapper.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/CalendarItemMapperTests.cs`

**Interfaces:**
- Consumes: `AgendaEntry`, `ScheduleBlockKind`, `AgendaEntrySource` from Plan 1.
- Produces:
  - `record CalendarItem(DateOnly Date, TimeSpan Start, TimeSpan End, string Label, CalendarItemKind Kind, string? Detail)` with `bool IsAllDay => Start == TimeSpan.Zero && End == TimeSpan.FromHours(24)`
  - `enum CalendarItemKind { Work, Sleep, Personal, Meeting, Other }`
  - `record DayItems(IReadOnlyList<CalendarItem> AllDay, IReadOnlyList<CalendarItem> Timed)`
  - `static DayItems CalendarItemMapper.ForDay(AgendaDay day)`
  - `static CalendarItemKind CalendarItemMapper.KindOf(ScheduleBlockKind kind, AgendaEntrySource source)`

**Why a separate kind enum.** `ScheduleBlockKind` describes what a *block* is; `CalendarItemKind` describes how the calendar should *present* an item, and it needs a `Meeting` value that no block kind supplies — an external event arrives as `Kind = Personal, Source = External`, which should read as a meeting, not as personal time. Mapping through a function keeps that judgement in one tested place instead of scattered across XAML.

- [ ] **Step 1: Write the failing tests**

Create `src/AaronOS.Modules.Schedule.Tests/CalendarItemMapperTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Calendar;
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Tests;

public class CalendarItemMapperTests
{
    private static readonly DateOnly Day = new(2026, 7, 28);

    private static AgendaEntry Entry(int fromHour, int toHour, ScheduleBlockKind kind, string label,
        AgendaEntrySource source = AgendaEntrySource.Block) =>
        new(new TimeSpan(fromHour, 0, 0), new TimeSpan(toHour, 0, 0), kind, label, source);

    private static AgendaDay DayWith(params AgendaEntry[] entries) => new(Day, entries, []);

    [Fact]
    public void TimedEntriesStayInTheGrid()
    {
        var result = CalendarItemMapper.ForDay(DayWith(Entry(9, 10, ScheduleBlockKind.Work, "Standup")));

        Assert.Empty(result.AllDay);
        var item = Assert.Single(result.Timed);
        Assert.Equal(Day, item.Date);
        Assert.Equal(new TimeSpan(9, 0, 0), item.Start);
        Assert.Equal(new TimeSpan(10, 0, 0), item.End);
        Assert.Equal("Standup", item.Label);
    }

    [Fact]
    public void AFullDaySpanIsLiftedIntoTheAllDayBand()
    {
        // 00:00-24:00 in a time grid is a block filling the whole column, which would bury every
        // real meeting behind it. It belongs in the band above the grid instead.
        var result = CalendarItemMapper.ForDay(
            DayWith(Entry(0, 24, ScheduleBlockKind.Personal, "Company holiday", AgendaEntrySource.External)));

        Assert.Empty(result.Timed);
        var item = Assert.Single(result.AllDay);
        Assert.True(item.IsAllDay);
        Assert.Equal("Company holiday", item.Label);
    }

    [Fact]
    public void AWrappedSleepTailIsNotTreatedAsAllDay()
    {
        // AgendaBuilder splits a midnight-wrapping block, so a sleep tail arrives as 00:00-07:00.
        // That is a partial day and must stay in the grid: banding it would lose its actual hours.
        var result = CalendarItemMapper.ForDay(DayWith(Entry(0, 7, ScheduleBlockKind.Sleep, "Sleep")));

        Assert.Empty(result.AllDay);
        var item = Assert.Single(result.Timed);
        Assert.Equal(new TimeSpan(7, 0, 0), item.End);
    }

    [Fact]
    public void AnEveningBlockRunningToMidnightStaysInTheGrid()
    {
        // 18:00-24:00 ends at the day boundary but is not an all-day item. Keying the band on the
        // END alone rather than the whole span would wrongly lift this out of the grid.
        var result = CalendarItemMapper.ForDay(DayWith(Entry(18, 24, ScheduleBlockKind.Work, "Late shift")));

        Assert.Empty(result.AllDay);
        Assert.Single(result.Timed);
    }

    [Fact]
    public void AnExternalEntryPresentsAsAMeetingRatherThanPersonalTime()
    {
        var result = CalendarItemMapper.ForDay(
            DayWith(Entry(11, 12, ScheduleBlockKind.Personal, "1:1", AgendaEntrySource.External)));

        Assert.Equal(CalendarItemKind.Meeting, Assert.Single(result.Timed).Kind);
    }

    [Theory]
    [InlineData(ScheduleBlockKind.Work, CalendarItemKind.Work)]
    [InlineData(ScheduleBlockKind.Sleep, CalendarItemKind.Sleep)]
    [InlineData(ScheduleBlockKind.Personal, CalendarItemKind.Personal)]
    public void ABlockKeepsItsOwnKind(ScheduleBlockKind blockKind, CalendarItemKind expected)
    {
        Assert.Equal(expected, CalendarItemMapper.KindOf(blockKind, AgendaEntrySource.Block));
    }
}
```

> `ScheduleBlockKind` has exactly three members — `Work`, `Sleep`, `Personal` — verified against `src/AaronOS.Modules.Schedule/Data/ScheduleEnums.cs`. Do not add a member to that enum to give the calendar more colours.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `CalendarItem`, `CalendarItemMapper`, `CalendarItemKind`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Calendar/CalendarItem.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Calendar;

/// <summary>
/// One thing on the calendar, in terms the grid can render without knowing where it came from.
///
/// Deliberately NOT AgendaEntry. A calendar is plausibly useful to other modules — Medical's
/// appointments, Nutrition's meal times — and MODULE_GUIDELINES.md forbids one module reading
/// another's entities, so the shared shape would have to move to AaronOS.Core. Rendering from this
/// record is the whole preparation for that; nothing else in the grid needs to change when it happens.
/// </summary>
/// <param name="Detail">Optional secondary line — a location, or a source name. May be null.</param>
public sealed record CalendarItem(
    DateOnly Date,
    TimeSpan Start,
    TimeSpan End,
    string Label,
    CalendarItemKind Kind,
    string? Detail)
{
    private static readonly TimeSpan DayEnd = TimeSpan.FromHours(24);

    /// <summary>
    /// True only for a span covering the entire day. Both ends matter: an evening block running
    /// 18:00-24:00 also ends at the boundary but is not an all-day item.
    /// </summary>
    public bool IsAllDay => Start == TimeSpan.Zero && End == DayEnd;

    public int Minutes => (int)(End - Start).TotalMinutes;
}

/// <summary>
/// How the calendar presents an item. Distinct from ScheduleBlockKind, which describes what a block
/// IS — this describes how it should read, and adds Meeting, which no block kind supplies.
///
/// Deliberately NOT one value per imaginable activity. ScheduleBlockKind is only
/// { Work, Sleep, Personal }, and routine categories (Gym, LitterBox, Trash...) are not placed on the
/// calendar by this plan, so a Gym or Chore value here would be a kind nothing can produce. Add one
/// when something actually maps to it.
/// </summary>
public enum CalendarItemKind { Work, Sleep, Personal, Meeting, Other }
```

`src/AaronOS.Modules.Schedule/Calendar/CalendarItemMapper.cs`:

```csharp
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Calendar;

/// <summary>Items for one day, already separated into the band above the grid and the grid itself.</summary>
public sealed record DayItems(IReadOnlyList<CalendarItem> AllDay, IReadOnlyList<CalendarItem> Timed);

/// <summary>
/// Turns an AgendaDay into what the grid draws. Pure: no clock, no DbContext.
/// </summary>
public static class CalendarItemMapper
{
    public static DayItems ForDay(AgendaDay day)
    {
        var allDay = new List<CalendarItem>();
        var timed = new List<CalendarItem>();

        foreach (var entry in day.Entries)
        {
            var item = new CalendarItem(
                day.Date,
                entry.Start,
                entry.End,
                entry.Label,
                KindOf(entry.Kind, entry.Source),
                entry.Source == AgendaEntrySource.External ? "from calendar" : null);

            (item.IsAllDay ? allDay : timed).Add(item);
        }

        return new DayItems(allDay, timed);
    }

    /// <summary>
    /// An external event arrives as Personal/External because AgendaBuilder has no better kind for
    /// it; presenting that as personal time is wrong, so source wins over kind for externals.
    /// </summary>
    public static CalendarItemKind KindOf(ScheduleBlockKind kind, AgendaEntrySource source)
    {
        if (source == AgendaEntrySource.External) return CalendarItemKind.Meeting;

        return kind switch
        {
            ScheduleBlockKind.Work => CalendarItemKind.Work,
            ScheduleBlockKind.Sleep => CalendarItemKind.Sleep,
            ScheduleBlockKind.Personal => CalendarItemKind.Personal,
            _ => CalendarItemKind.Other,
        };
    }
}
```

> The `_ => Other` arm exists so a future `ScheduleBlockKind` member renders as something rather than throwing. Do not invent a `CalendarItemKind` per block kind.

- [ ] **Step 4: Run the tests to verify they pass**

Expected: `Passed!` with 0 failures and 8 more passing tests than before this task.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Calendar src/AaronOS.Modules.Schedule.Tests/CalendarItemMapperTests.cs
git commit -m "Add CalendarItem and the agenda-to-calendar mapper"
```

---

## Task 2: Overlap lane assignment

This is the task with the real algorithm. Everything else in the plan is plumbing.

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Calendar/PositionedItem.cs`
- Create: `src/AaronOS.Modules.Schedule/Calendar/TimeGridLayout.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/TimeGridLayoutTests.cs`

**Interfaces:**
- Produces:
  - `record PositionedItem(CalendarItem Item, int Lane, int LaneCount)`
  - `static IReadOnlyList<PositionedItem> TimeGridLayout.Assign(IReadOnlyList<CalendarItem> itemsForOneDay)`

**The rule, precisely.** Sort items by start, then by end descending so a longer item takes the left lane. Walk them, assigning each to the lowest-numbered lane whose previously-assigned item has already ended. `LaneCount` is the number of lanes used by that item's **cluster** — a maximal run of items connected by overlap — and **not** the number used by the whole day. Getting that wrong is the single most visible defect available here: one overlapping pair at 09:00 would halve the width of every unrelated block in the day.

Note that "cluster" is about connectivity, not mutual overlap. If A is 09:00–10:00, B is 09:30–11:00 and C is 10:30–11:30, then A and C never touch, but all three are one cluster because B bridges them, and all three must share a lane count of 2. A test below pins exactly this.

- [ ] **Step 1: Write the failing tests**

Create `src/AaronOS.Modules.Schedule.Tests/TimeGridLayoutTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Calendar;

namespace AaronOS.Modules.Schedule.Tests;

public class TimeGridLayoutTests
{
    private static readonly DateOnly Day = new(2026, 7, 28);

    private static CalendarItem At(string label, int fromHour, int fromMin, int toHour, int toMin) =>
        new(Day, new TimeSpan(fromHour, fromMin, 0), new TimeSpan(toHour, toMin, 0),
            label, CalendarItemKind.Meeting, null);

    private static PositionedItem Find(IReadOnlyList<PositionedItem> all, string label) =>
        all.Single(p => p.Item.Label == label);

    [Fact]
    public void NonOverlappingItemsAllTakeTheFullWidth()
    {
        var result = TimeGridLayout.Assign([At("A", 9, 0, 10, 0), At("B", 11, 0, 12, 0)]);

        Assert.All(result, p => Assert.Equal(0, p.Lane));
        Assert.All(result, p => Assert.Equal(1, p.LaneCount));
    }

    [Fact]
    public void TwoOverlappingItemsSitSideBySide()
    {
        var result = TimeGridLayout.Assign([At("A", 9, 0, 10, 0), At("B", 9, 30, 10, 30)]);

        Assert.Equal(0, Find(result, "A").Lane);
        Assert.Equal(1, Find(result, "B").Lane);
        Assert.All(result, p => Assert.Equal(2, p.LaneCount));
    }

    [Fact]
    public void AnUnrelatedItemKeepsFullWidthWhenAnotherPairOverlaps()
    {
        // The defect this exists to catch: computing LaneCount per DAY would make C half width too.
        var result = TimeGridLayout.Assign(
            [At("A", 9, 0, 10, 0), At("B", 9, 30, 10, 30), At("C", 14, 0, 15, 0)]);

        Assert.Equal(2, Find(result, "A").LaneCount);
        Assert.Equal(2, Find(result, "B").LaneCount);
        Assert.Equal(1, Find(result, "C").LaneCount);
        Assert.Equal(0, Find(result, "C").Lane);
    }

    [Fact]
    public void ABridgedChainIsOneClusterEvenThoughTheEndsDoNotTouch()
    {
        // A and C do not overlap each other, but B overlaps both, so all three share a lane count.
        // A per-pair lane count would give C a count of 2 but A a count of 2 and leave a gap; a
        // naive "reset when the day is clear" approach would split them into separate clusters.
        var result = TimeGridLayout.Assign(
            [At("A", 9, 0, 10, 0), At("B", 9, 30, 11, 0), At("C", 10, 30, 11, 30)]);

        Assert.All(result, p => Assert.Equal(2, p.LaneCount));
        Assert.Equal(0, Find(result, "A").Lane);
        Assert.Equal(1, Find(result, "B").Lane);
        Assert.Equal(0, Find(result, "C").Lane); // A has ended, so lane 0 is free again
    }

    [Fact]
    public void ThreeSimultaneousItemsEachTakeAThird()
    {
        var result = TimeGridLayout.Assign(
            [At("A", 9, 0, 10, 0), At("B", 9, 0, 10, 0), At("C", 9, 0, 10, 0)]);

        Assert.All(result, p => Assert.Equal(3, p.LaneCount));
        Assert.Equal([0, 1, 2], result.Select(p => p.Lane).OrderBy(l => l).ToArray());
    }

    [Fact]
    public void AnEnclosedItemStillGetsItsOwnLane()
    {
        // B sits entirely inside A. This is the case a naive "does it start after the last end"
        // check gets wrong, because B starts and ends within A's span.
        var result = TimeGridLayout.Assign([At("A", 9, 0, 12, 0), At("B", 10, 0, 11, 0)]);

        Assert.Equal(0, Find(result, "A").Lane);
        Assert.Equal(1, Find(result, "B").Lane);
        Assert.All(result, p => Assert.Equal(2, p.LaneCount));
    }

    [Fact]
    public void AdjacentItemsDoNotCountAsOverlapping()
    {
        // A ends exactly when B starts. Back-to-back meetings must each keep full width, or a normal
        // day of consecutive meetings would render as a column of half-width slivers.
        var result = TimeGridLayout.Assign([At("A", 9, 0, 10, 0), At("B", 10, 0, 11, 0)]);

        Assert.All(result, p => Assert.Equal(1, p.LaneCount));
        Assert.All(result, p => Assert.Equal(0, p.Lane));
    }

    [Fact]
    public void InputOrderDoesNotChangeTheResult()
    {
        var forward = TimeGridLayout.Assign([At("A", 9, 0, 10, 0), At("B", 9, 30, 10, 30)]);
        var reversed = TimeGridLayout.Assign([At("B", 9, 30, 10, 30), At("A", 9, 0, 10, 0)]);

        Assert.Equal(Find(forward, "A").Lane, Find(reversed, "A").Lane);
        Assert.Equal(Find(forward, "B").Lane, Find(reversed, "B").Lane);
    }

    [Fact]
    public void EmptyInputReturnsEmpty() => Assert.Empty(TimeGridLayout.Assign([]));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Expected: `CS0246` for `TimeGridLayout` and `PositionedItem`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Calendar/PositionedItem.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Calendar;

/// <summary>
/// An item plus where it sits horizontally. <paramref name="LaneCount"/> is the lane count of this
/// item's overlap CLUSTER, not of the whole day — so an isolated block keeps full width even when
/// another pair overlaps elsewhere in the same day.
/// </summary>
public sealed record PositionedItem(CalendarItem Item, int Lane, int LaneCount);
```

`src/AaronOS.Modules.Schedule/Calendar/TimeGridLayout.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Calendar;

/// <summary>
/// Where each item sits in a day column, and how pixels map to times. Pure: takes values, returns
/// values, no clock and no visual types — which is what makes the awkward cases testable.
/// </summary>
public static class TimeGridLayout
{
    public static IReadOnlyList<PositionedItem> Assign(IReadOnlyList<CalendarItem> itemsForOneDay)
    {
        if (itemsForOneDay.Count == 0) return [];

        // Longest-first on a tie so a long block takes the left lane and short ones stack to its
        // right, which is what Outlook does and reads better than the reverse.
        var ordered = itemsForOneDay
            .OrderBy(i => i.Start)
            .ThenByDescending(i => i.End)
            .ThenBy(i => i.Label, StringComparer.Ordinal) // total order: same input -> same output
            .ToList();

        var result = new List<PositionedItem>(ordered.Count);

        var laneEnds = new List<TimeSpan>();   // when the item currently in each lane finishes
        var cluster = new List<int>();         // indices into `result` for the open cluster
        var clusterEnd = TimeSpan.MinValue;    // latest end seen in the open cluster

        foreach (var item in ordered)
        {
            // A gap with nothing running closes the cluster: its lane count is now known.
            if (item.Start >= clusterEnd && cluster.Count > 0)
            {
                Close(result, cluster, laneEnds.Count);
                laneEnds.Clear();
                cluster.Clear();
                clusterEnd = TimeSpan.MinValue;
            }

            // Lowest lane whose occupant has finished. Strictly `<=` so back-to-back items share a
            // lane rather than being treated as an overlap.
            var lane = laneEnds.FindIndex(end => end <= item.Start);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(item.End);
            }
            else
            {
                laneEnds[lane] = item.End;
            }

            cluster.Add(result.Count);
            result.Add(new PositionedItem(item, lane, 1)); // LaneCount patched when the cluster closes
            if (item.End > clusterEnd) clusterEnd = item.End;
        }

        Close(result, cluster, laneEnds.Count);
        return result;

        static void Close(List<PositionedItem> all, List<int> indices, int laneCount)
        {
            foreach (var i in indices) all[i] = all[i] with { LaneCount = laneCount };
        }
    }
}
```

Read that `clusterEnd` comparison carefully: the cluster closes only when an item starts at or after **the latest end in the cluster**, not after the previous item's end. That is what makes a bridged chain one cluster.

- [ ] **Step 4: Run the tests to verify they pass**

Expected: `Passed!` with 0 failures and 9 more passing tests than before this task.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Calendar src/AaronOS.Modules.Schedule.Tests/TimeGridLayoutTests.cs
git commit -m "Add overlap lane assignment for the calendar time grid"
```

---

## Task 3: Pixel and time arithmetic

**Files:**
- Modify: `src/AaronOS.Modules.Schedule/Calendar/TimeGridLayout.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/TimeGridLayoutTests.cs`

**Interfaces:**
- Produces, on `TimeGridLayout`:
  - `const double HourHeight = 48d` and `const double PixelsPerMinute = HourHeight / 60d`
  - `static double TopFor(TimeSpan time)` / `static double HeightFor(CalendarItem item)`
  - `static TimeSpan TimeAt(double y, int snapMinutes = 15)`
  - `const double DayHeight = 24 * HourHeight`

**Why 48px per hour.** It is what Outlook uses, it makes a 30-minute meeting 24px — tall enough to show a label — and it puts a full day at 1152px, which scrolls rather than compressing. A vertically scaling grid was considered and rejected: it makes a 15-minute block unreadable on a short window.

- [ ] **Step 1: Add the failing tests**

Append to `TimeGridLayoutTests.cs`:

```csharp
    [Fact]
    public void TopIsProportionalToTheStartTime()
    {
        Assert.Equal(0d, TimeGridLayout.TopFor(TimeSpan.Zero));
        Assert.Equal(TimeGridLayout.HourHeight, TimeGridLayout.TopFor(new TimeSpan(1, 0, 0)));
        Assert.Equal(TimeGridLayout.HourHeight * 9.5, TimeGridLayout.TopFor(new TimeSpan(9, 30, 0)));
    }

    [Fact]
    public void HeightIsProportionalToDuration()
    {
        var half = new CalendarItem(new DateOnly(2026, 7, 28), new TimeSpan(9, 0, 0),
            new TimeSpan(9, 30, 0), "x", CalendarItemKind.Other, null);

        Assert.Equal(TimeGridLayout.HourHeight / 2, TimeGridLayout.HeightFor(half));
    }

    [Fact]
    public void AFullDayIsTheWholeGridHeight()
    {
        Assert.Equal(TimeGridLayout.DayHeight, TimeGridLayout.TopFor(TimeSpan.FromHours(24)));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(48, 1, 0)]        // exactly 09:00-equivalent: one hour down
    [InlineData(52, 1, 0)]        // 5 minutes in, snaps back to the hour
    [InlineData(58, 1, 15)]       // 12.5 minutes in, snaps forward to :15
    public void TimeAtSnapsToTheNearestQuarterHour(double y, int expectedHours, int expectedMinutes)
    {
        Assert.Equal(new TimeSpan(expectedHours, expectedMinutes, 0), TimeGridLayout.TimeAt(y));
    }

    [Fact]
    public void TimeAtIsClampedToTheDay()
    {
        // A click below the last row, or a negative y from a transform, must not produce a time
        // outside the day — a block starting at 25:00 would silently never render.
        Assert.Equal(TimeSpan.Zero, TimeGridLayout.TimeAt(-40));
        Assert.Equal(new TimeSpan(23, 45, 0), TimeGridLayout.TimeAt(TimeGridLayout.DayHeight + 500));
    }
```

> Work out the expected values in the `[Theory]` from `HourHeight = 48` before running: 48px is one hour, so 4px is 5 minutes and one 15-minute snap step is 12px. If an expectation here disagrees with that arithmetic, the expectation is wrong — fix it rather than bending the implementation.

- [ ] **Step 2: Run to verify they fail**

Expected: `CS0117`/`CS0246` for the new members.

- [ ] **Step 3: Add the implementation**

Add to `TimeGridLayout`:

```csharp
    /// <summary>Row height for one hour. Outlook's value; makes a 30-minute item 24px, which is the
    /// smallest that still fits a readable label.</summary>
    public const double HourHeight = 48d;

    public const double PixelsPerMinute = HourHeight / 60d;
    public const double DayHeight = 24 * HourHeight;

    public static double TopFor(TimeSpan time) => time.TotalMinutes * PixelsPerMinute;

    public static double HeightFor(CalendarItem item) => item.Minutes * PixelsPerMinute;

    /// <summary>
    /// The time at a vertical offset, snapped to <paramref name="snapMinutes"/>. Clamped into the day:
    /// a click below the last row or a negative offset from a transform must not yield a time outside
    /// 00:00-23:45, because a block starting at 25:00 would silently never render.
    /// </summary>
    public static TimeSpan TimeAt(double y, int snapMinutes = 15)
    {
        var minutes = y / PixelsPerMinute;
        var snapped = Math.Round(minutes / snapMinutes) * snapMinutes;
        var clamped = Math.Clamp(snapped, 0, 24 * 60 - snapMinutes);
        return TimeSpan.FromMinutes(clamped);
    }
```

- [ ] **Step 4: Run to verify they pass**

Expected: `Passed!` with 0 failures and 8 more passing tests than before this task (the `[Theory]` contributes 4).

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Calendar src/AaronOS.Modules.Schedule.Tests/TimeGridLayoutTests.cs
git commit -m "Add pixel and time arithmetic for the calendar grid"
```

---

## Task 4: The `TimeGridPanel`

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Views/TimeGridPanel.cs`

**Interfaces:**
- Produces: `TimeGridPanel : Panel`, used as an `ItemsPanelTemplate` for a day column. It arranges each child by reading the `PositionedItem` from that child's `DataContext`.

**Why a custom panel rather than bindings.** Vertical position comes from a fixed scale, so it could be bound. Horizontal position cannot: a lane's width is a fraction of the column's *actual* width, which is not known until layout runs. Doing that with a `MultiBinding` on `ActualWidth` plus a converter works but spreads the geometry across XAML, a converter and the layout service, and re-entrant `ActualWidth` bindings are a known source of layout loops. A ~30-line panel keeps all of it in one place and resizes correctly for free.

- [ ] **Step 1: Write the panel**

No unit test: `MeasureOverride`/`ArrangeOverride` need a live visual tree, and the arithmetic they call is already covered by `TimeGridLayoutTests`. What this class adds beyond that is WPF plumbing, verified in the app in Task 5.

`src/AaronOS.Modules.Schedule/Views/TimeGridPanel.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using AaronOS.Modules.Schedule.Calendar;

namespace AaronOS.Modules.Schedule.Views;

/// <summary>
/// Arranges one day column's items by time and lane. Each child's DataContext is a PositionedItem;
/// vertical geometry comes from TimeGridLayout, horizontal from the lane fraction of the panel's own
/// width — which is why this is a Panel and not a set of bindings.
/// </summary>
public sealed class TimeGridPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            // Height is dictated by the item's duration, so give the child exactly that and let it
            // clip its own content rather than letting a long label stretch the row.
            var item = ItemOf(child);
            var height = item is null ? 0 : TimeGridLayout.HeightFor(item.Item);
            child.Measure(new Size(availableSize.Width, height));
        }

        // Always the full day tall: the column must scroll as one with the time gutter beside it,
        // so its height must not depend on how many items happen to be present.
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, TimeGridLayout.DayHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var positioned = ItemOf(child);
            if (positioned is null) { child.Arrange(new Rect(0, 0, 0, 0)); continue; }

            var laneWidth = finalSize.Width / Math.Max(positioned.LaneCount, 1);

            child.Arrange(new Rect(
                positioned.Lane * laneWidth,
                TimeGridLayout.TopFor(positioned.Item.Start),
                laneWidth,
                TimeGridLayout.HeightFor(positioned.Item)));
        }

        return new Size(finalSize.Width, TimeGridLayout.DayHeight);
    }

    /// <summary>ItemsControl wraps each item in a container, so read through to the DataContext.</summary>
    private static PositionedItem? ItemOf(UIElement child) =>
        (child as FrameworkElement)?.DataContext as PositionedItem;
}
```

- [ ] **Step 2: Confirm it compiles**

Run: `dotnet build src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj --nologo`
Expected: 0 errors. Test count unchanged.

- [ ] **Step 3: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Views/TimeGridPanel.cs
git commit -m "Add TimeGridPanel to arrange calendar items by time and lane"
```

---

## Task 5: The week grid page

The biggest task. Read the spec's `## Calendar views` section first.

**Files:**
- Create: `src/AaronOS.Modules.Schedule/ViewModels/CalendarWeekViewModel.cs`
- Create: `src/AaronOS.Modules.Schedule/Views/CalendarWeekPage.xaml`
- Create: `src/AaronOS.Modules.Schedule/Views/CalendarWeekPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml(.cs)`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `AgendaBuilder.Build`, `CalendarItemMapper.ForDay`, `TimeGridLayout.Assign`, `ExternalEventProjector.ToAgendaEntries`.
- Produces: `CalendarWeekViewModel` with `ObservableCollection<CalendarDayColumn> Columns`, `WeekHeading`, `LoadCommand`, `PreviousWeekCommand`, `NextWeekCommand`, `ThisWeekCommand`.
- Produces: `record CalendarDayColumn(DateOnly Date, string Header, bool IsToday, IReadOnlyList<CalendarItem> AllDay, IReadOnlyList<PositionedItem> Timed)`.

- [ ] **Step 1: Write the ViewModel**

No unit test: the arithmetic is covered by `TimeGridLayoutTests` and `CalendarItemMapperTests`; what remains here is a database read plus calls into those.

`src/AaronOS.Modules.Schedule/ViewModels/CalendarWeekViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Calendar;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

/// <summary>One day column, already laid out. The view binds and draws; it computes nothing.</summary>
public sealed record CalendarDayColumn(
    DateOnly Date,
    string Header,
    bool IsToday,
    IReadOnlyList<CalendarItem> AllDay,
    IReadOnlyList<PositionedItem> Timed);

public partial class CalendarWeekViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<CalendarDayColumn> Columns { get; } = [];

    /// <summary>Hour labels for the gutter. Built once — 00 to 23 never changes.</summary>
    public IReadOnlyList<string> HourLabels { get; } =
        Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();

    [ObservableProperty]
    private DateOnly _weekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Now));

    [ObservableProperty]
    private string _weekHeading = "";

    /// <summary>True when any day in the visible week has an all-day item, so the band can collapse
    /// to nothing rather than leaving an empty strip across the top.</summary>
    [ObservableProperty]
    private bool _hasAllDayItems;

    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7)); // Monday-first

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var end = WeekStart.AddDays(6);
            var today = DateOnly.FromDateTime(DateTime.Now);
            WeekHeading = $"{WeekStart:MMM d} – {end:MMM d, yyyy}";

            await using var db = await dbContextFactory.CreateDbContextAsync();
            var blocks = await db.Set<ScheduleBlock>().Where(b => b.IsActive).ToListAsync();

            // One day back: AgendaBuilder expands a warm-up day so a block wrapping past midnight
            // carries its tail forward, and a cancellation the night before must suppress that tail.
            var exceptions = await db.Set<ScheduleException>()
                .Where(e => e.Date >= WeekStart.AddDays(-1) && e.Date <= end)
                .ToListAsync();

            // Overlap test, NOT a StartsAt range — a multi-day event that began before this window
            // and is still running must appear. See the Global Constraints.
            var windowStart = WeekStart.AddDays(-1).ToDateTime(TimeOnly.MinValue);
            var windowEnd = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var externalRows = await db.Set<ExternalEvent>()
                .Where(e => e.StartsAt < windowEnd && e.EndsAt > windowStart)
                .ToListAsync();

            var days = AgendaBuilder.Build(
                WeekStart, end, blocks, exceptions, ExternalEventProjector.ToAgendaEntries(externalRows));

            Columns.Clear();
            var anyAllDay = false;
            foreach (var day in days)
            {
                var items = CalendarItemMapper.ForDay(day);
                if (items.AllDay.Count > 0) anyAllDay = true;

                Columns.Add(new CalendarDayColumn(
                    day.Date,
                    $"{day.Date:ddd d}",
                    day.Date == today,
                    items.AllDay,
                    TimeGridLayout.Assign(items.Timed)));
            }

            HasAllDayItems = anyAllDay;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        WeekStart = WeekStart.AddDays(-7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        WeekStart = WeekStart.AddDays(7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ThisWeekAsync()
    {
        WeekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Now));
        await LoadAsync();
    }
}
```

- [ ] **Step 2: Write the page**

`src/AaronOS.Modules.Schedule/Views/CalendarWeekPage.xaml`. Structure, in order: a header row with navigation; the all-day band; then a single `ScrollViewer` containing the gutter and the seven day columns so they scroll together.

**Colour comes from a resource lookup keyed on `CalendarItemKind`, never a literal.** Define the brushes in the page's `Resources` using `DynamicResource` references to WPF-UI's palette, and select one with a `Style` + `DataTrigger` per kind — the same trigger technique used for the calendar toggle button in `ScheduleSettingsSection.xaml`.

```xml
<Page
    x:Class="AaronOS.Modules.Schedule.Views.CalendarWeekPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    xmlns:cal="clr-namespace:AaronOS.Modules.Schedule.Calendar"
    xmlns:views="clr-namespace:AaronOS.Modules.Schedule.Views"
    mc:Ignorable="d">

    <Page.Resources>
        <!-- One template for an item, styled by kind through triggers. -->
        <DataTemplate x:Key="CalendarItemTemplate">
            <Border CornerRadius="3" Margin="1,0,1,1" Padding="4,2" ClipToBounds="True">
                <Border.Style>
                    <Style TargetType="Border">
                        <Setter Property="Background" Value="{DynamicResource AccentControlElevationBorderBrush}" />
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding Item.Kind}" Value="Sleep">
                                <Setter Property="Background" Value="{DynamicResource ControlAltFillColorTertiaryBrush}" />
                            </DataTrigger>
                            <DataTrigger Binding="{Binding Item.Kind}" Value="Personal">
                                <Setter Property="Background" Value="{DynamicResource SystemFillColorSuccessBackgroundBrush}" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Border.Style>
                <StackPanel>
                    <TextBlock Text="{Binding Item.Label}" FontSize="11" TextTrimming="CharacterEllipsis" />
                    <TextBlock Text="{Binding Item.Detail}" FontSize="10" Opacity="0.7"
                               TextTrimming="CharacterEllipsis" />
                </StackPanel>
            </Border>
        </DataTemplate>
    </Page.Resources>

    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />   <!-- header + nav -->
            <RowDefinition Height="Auto" />   <!-- day headers -->
            <RowDefinition Height="Auto" />   <!-- all-day band -->
            <RowDefinition Height="*" />      <!-- scrolling grid -->
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
            <ui:Button Content="&#x25C0;" Command="{Binding PreviousWeekCommand}" Margin="0,0,4,0" />
            <ui:Button Content="Today" Command="{Binding ThisWeekCommand}" Margin="0,0,4,0" />
            <ui:Button Content="&#x25B6;" Command="{Binding NextWeekCommand}" Margin="0,0,12,0" />
            <ui:TextBlock Text="{Binding WeekHeading}" FontTypography="Subtitle" VerticalAlignment="Center" />
        </StackPanel>

        <!-- Day headers and the two item areas all share this column layout: a fixed gutter plus
             seven equal columns. Keep the gutter width identical in all three or they will not line up. -->
        <ItemsControl Grid.Row="1" ItemsSource="{Binding Columns}" Margin="56,0,0,0">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><UniformGrid Rows="1" /></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding Header}" HorizontalAlignment="Center" Margin="0,0,0,4">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsToday}" Value="True">
                                        <Setter Property="FontWeight" Value="Bold" />
                                        <Setter Property="Foreground" Value="{DynamicResource AccentTextFillColorPrimaryBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <ItemsControl Grid.Row="2" ItemsSource="{Binding Columns}" Margin="56,0,0,4"
                      Visibility="{Binding HasAllDayItems, Converter={StaticResource BooleanToVisibilityConverter}}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><UniformGrid Rows="1" /></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <ItemsControl ItemsSource="{Binding AllDay}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Background="{DynamicResource AccentControlElevationBorderBrush}"
                                        CornerRadius="3" Margin="1" Padding="4,2">
                                    <TextBlock Text="{Binding Label}" FontSize="11"
                                               TextTrimming="CharacterEllipsis" />
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <ScrollViewer Grid.Row="3" VerticalScrollBarVisibility="Auto" x:Name="GridScroller">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="56" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>

                <ItemsControl Grid.Column="0" ItemsSource="{Binding HourLabels}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding}" Height="48" FontSize="10" Opacity="0.6"
                                       HorizontalAlignment="Right" Margin="0,-6,8,0" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <ItemsControl Grid.Column="1" ItemsSource="{Binding Columns}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate><UniformGrid Rows="1" /></ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                                    BorderThickness="1,0,0,0" Background="Transparent"
                                    MouseLeftButtonUp="DayColumn_Click">
                                <ItemsControl ItemsSource="{Binding Timed}"
                                              ItemTemplate="{StaticResource CalendarItemTemplate}">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <views:TimeGridPanel />
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                </ItemsControl>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Grid>
        </ScrollViewer>
    </Grid>
</Page>
```

Two things that will not work if you change them casually. `Background="Transparent"` on the day-column `Border` is required — a `null` background is not hit-testable, so clicks would not register in Task 6. And the gutter width appears three times (`Margin="56,..."` twice, `ColumnDefinition Width="56"` once); those must agree or the day headers will sit off by 56px from their columns.

Hour lines are deliberately not drawn in this task. Add them only if the grid reads badly without them — the hour labels plus the column separators may be enough, and a line per hour per column is 168 extra elements.

`BooleanToVisibilityConverter` is a WPF built-in but is **not** automatically in scope. Add it to `Page.Resources`:
`<BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />`

- [ ] **Step 3: Write the code-behind**

```csharp
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class CalendarWeekPage : Page
{
    public CalendarWeekViewModel ViewModel { get; }

    public CalendarWeekPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<CalendarWeekViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            // Open on the working day. Without this the grid starts at midnight and every real
            // commitment is below the fold.
            GridScroller.ScrollToVerticalOffset(Calendar.TimeGridLayout.HourHeight * 7);
        };
    }
}
```

The `DayColumn_Click` handler is added in Task 6; add an empty one now so the XAML compiles.

- [ ] **Step 4: Register and route to it**

In `ScheduleModule.RegisterServices`, add `services.AddTransient<CalendarWeekViewModel>();`.
In `ScheduleShellPage.xaml`, replace the `Today` and `Week` buttons with a single `Week` button, and in the code-behind navigate to `new CalendarWeekPage()`. Leave the `Routines` button alone.

- [ ] **Step 5: Verify in the app**

Run: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`

Confirm, and report anything that differs:
1. The week grid renders with a time gutter and seven columns, opening around 07:00.
2. Real calendar meetings appear at their correct times and heights, in the correct columns.
3. Two meetings at the same time appear side by side, each about half width.
4. A meeting elsewhere in the same day is still full width.
5. The all-day band is absent when there are no all-day items.
6. Both light and dark theme are legible — switch in Settings and check.

Close the app.

- [ ] **Step 6: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add the Outlook-style week grid"
```

---

## Task 6: Click a slot to add a block

**Files:**
- Modify: `src/AaronOS.Modules.Schedule/Views/CalendarWeekPage.xaml(.cs)`
- Modify: `src/AaronOS.Modules.Schedule/ViewModels/CalendarWeekViewModel.cs`

**Interfaces:**
- Produces: `CalendarWeekViewModel.BeginAddCommand` taking a `(DateOnly Date, TimeSpan Start)` tuple, plus `NewLabel`, `NewKind`, `NewStartText`, `NewEndText`, `ValidationMessage`, `SaveBlockCommand`, `CancelAddCommand`, and `bool IsAddingBlock`.

- [ ] **Step 1: Convert the click into a date and time**

In `CalendarWeekPage.xaml.cs`:

```csharp
    private void DayColumn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarDayColumn column } element) return;

        // Y within the column, not the window: the grid is inside a ScrollViewer, so a raw screen
        // coordinate would be wrong by the scroll offset.
        var y = e.GetPosition(element).Y;

        ViewModel.BeginAddCommand.Execute((column.Date, Calendar.TimeGridLayout.TimeAt(y)));
    }
```

Add `using System.Windows;` and `using AaronOS.Modules.Schedule.ViewModels;`.

- [ ] **Step 2: Add the editor to the ViewModel**

Carry over the block-saving logic from the existing `WeekViewModel.SaveBlockAsync` — including its validation, which is load-bearing:

```csharp
    [ObservableProperty] private bool _isAddingBlock;
    [ObservableProperty] private string _newLabel = "";
    [ObservableProperty] private ScheduleBlockKind _newKind = ScheduleBlockKind.Work;
    [ObservableProperty] private string _newStartText = "";
    [ObservableProperty] private string _newEndText = "";
    [ObservableProperty] private string? _validationMessage;
    [ObservableProperty] private DateOnly _newDate;

    public IReadOnlyList<ScheduleBlockKind> Kinds { get; } = Enum.GetValues<ScheduleBlockKind>();

    [RelayCommand]
    private void BeginAdd((DateOnly Date, TimeSpan Start) slot)
    {
        ValidationMessage = null;
        NewDate = slot.Date;
        NewStartText = $"{slot.Start:hh\\:mm}";
        // Default to an hour, clamped so a click late in the day cannot propose an end past midnight.
        var end = slot.Start + TimeSpan.FromHours(1);
        NewEndText = end >= TimeSpan.FromHours(24) ? "23:45" : $"{end:hh\\:mm}";
        IsAddingBlock = true;
    }

    [RelayCommand]
    private void CancelAdd() => IsAddingBlock = false;
```

`SaveBlockAsync` mirrors `WeekViewModel`'s, with two differences: `DaysOfWeek` is the single weekday of `NewDate` — `DayOfWeekFlagsExtensions.From(NewDate.DayOfWeek)`, which is a static method on the extensions class, NOT a member of the enum, and `EffectiveFrom` is `NewDate`. Keep **all** of the existing validation, in particular the range check — `TimeSpan.TryParse` reads a bare `"8"` as eight *days*, and without that check a mistyped time persists as a corrupt block that no UI can edit.

- [ ] **Step 3: Add the form to the page**

A `ui:Card` in `Grid.Row="0"`, right-aligned, with `Visibility` bound to `IsAddingBlock` through the `BooleanToVisibilityConverter`: a label `ui:TextBox`, a `ComboBox` bound to `Kinds`, two `ui:TextBox`es for the times, the validation `ui:TextBlock` in `SystemFillColorCriticalBrush`, and Save/Cancel buttons.

- [ ] **Step 4: Verify in the app**

Confirm: clicking an empty 10:00 slot on Wednesday opens the form pre-filled with `10:00`–`11:00`; saving adds a block that appears immediately in that column at that position; clicking near 23:50 proposes an end of `23:45` rather than something past midnight; typing `8` in a time box is rejected with the range message.

- [ ] **Step 5: Commit**

```bash
git commit -am "Add click-a-slot block creation to the week grid"
```

---

## Task 7: The agenda rail

**Files:**
- Create: `src/AaronOS.Modules.Schedule/ViewModels/AgendaRailViewModel.cs`
- Modify: `src/AaronOS.Modules.Schedule/Views/CalendarWeekPage.xaml(.cs)`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`
- Delete: `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml(.cs)`
- Delete: `src/AaronOS.Modules.Schedule/ViewModels/TodayViewModel.cs`

**Interfaces:**
- Produces: `AgendaRailViewModel` with `ObservableCollection<AgendaEntry> Remaining`, `ObservableCollection<RoutineRow> DueRoutines`, `string FreeTimeSummary`, `LoadCommand`.

**Start from `TodayViewModel`.** Most of what the rail needs is already there — it loads a single day's agenda, free gaps, and routine due states. Copy it, then narrow: the rail shows only entries **still ahead** of the current time, so it needs `DateTime.Now`. That is fine in a ViewModel; it is only the pure services that must not read the clock.

- [ ] **Step 1: Write the rail ViewModel**

Adapt `TodayViewModel` as described. Filter `Remaining` on `entry.End > TimeSpan.FromMinutes(DateTime.Now.TimeOfDay.TotalMinutes)`. Set `FreeTimeSummary` from the remaining free gaps — e.g. `"3h 20m free before 18:00"` — or `"nothing free left today"` when there is none.

- [ ] **Step 2: Add the rail to the page**

Wrap the existing content in a two-column `Grid`: `*` for the calendar, `260` for the rail, with a left border on the rail using `ControlStrokeColorDefaultBrush`. The rail hosts its own `AgendaRailViewModel`, resolved in the page constructor and assigned to that panel's `DataContext` — do not merge it into `CalendarWeekViewModel`, which is about the week, not today.

- [ ] **Step 3: Delete the Today page and ViewModel**

Remove both files, drop `services.AddTransient<TodayViewModel>()`, and remove the `Today` navigation button and handler from `ScheduleShellPage`. Search the module for remaining references before building: `grep -rn "TodayPage\|TodayViewModel" src/`.

- [ ] **Step 4: Verify**

Run the tests, build the solution, then run the app. Confirm the rail shows today's remaining items and overdue routines, that it is empty-but-not-broken late in the day, and that no navigation still points at a Today page.

Expected: `Passed!` with 0 failures and the same test count as before this task.

- [ ] **Step 5: Commit**

```bash
git add -A src/AaronOS.Modules.Schedule
git commit -m "Replace the Today page with a rail on the week view"
```

---

## Task 8: The month view

**Files:**
- Create: `src/AaronOS.Modules.Schedule/ViewModels/CalendarMonthViewModel.cs`
- Create: `src/AaronOS.Modules.Schedule/Views/CalendarMonthPage.xaml(.cs)`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml(.cs)`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/CalendarItemMapperTests.cs`

**Interfaces:**
- Produces: `record MonthCell(DateOnly Date, bool IsCurrentMonth, bool IsToday, IReadOnlyList<CalendarItem> Visible, int HiddenCount)`.
- Produces: `static IReadOnlyList<MonthCell> CalendarItemMapper.ToMonthCells(IReadOnlyList<AgendaDay> days, DateOnly month, DateOnly today, int maxPerCell = 3)`.

**Always 42 cells.** Six rows of seven, starting on the Monday on or before the 1st. A month grid that changes height between months makes the whole page jump, and some months genuinely need six rows.

- [ ] **Step 1: Write the failing tests**

Add to `CalendarItemMapperTests.cs`:

```csharp
    [Fact]
    public void MonthCellsAlwaysCoverSixWeeksStartingOnAMonday()
    {
        var cells = CalendarItemMapper.ToMonthCells([], new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 28));

        Assert.Equal(42, cells.Count);
        Assert.Equal(DayOfWeek.Monday, cells[0].Date.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 6, 29), cells[0].Date); // Monday before 1 July 2026
    }

    [Fact]
    public void CellsOutsideTheMonthAreMarkedSoTheViewCanDimThem()
    {
        var cells = CalendarItemMapper.ToMonthCells([], new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 28));

        Assert.False(cells[0].IsCurrentMonth);                       // 29 June
        Assert.True(cells.Single(c => c.Date.Day == 15 && c.IsCurrentMonth).IsCurrentMonth);
        Assert.True(cells.Single(c => c.Date == new DateOnly(2026, 7, 28)).IsToday);
    }

    [Fact]
    public void ACellCapsItsItemsAndReportsHowManyAreHidden()
    {
        var day = new DateOnly(2026, 7, 15);
        var entries = Enumerable.Range(8, 5)
            .Select(h => new AgendaEntry(new TimeSpan(h, 0, 0), new TimeSpan(h + 1, 0, 0),
                ScheduleBlockKind.Work, $"M{h}", AgendaEntrySource.Block))
            .ToArray();

        var cells = CalendarItemMapper.ToMonthCells(
            [new AgendaDay(day, entries, [])], new DateOnly(2026, 7, 1), day);

        var cell = cells.Single(c => c.Date == day);
        Assert.Equal(3, cell.Visible.Count);
        Assert.Equal(2, cell.HiddenCount);
        Assert.Equal("M8", cell.Visible[0].Label); // earliest first
    }
```

- [ ] **Step 2: Run to verify they fail** — `CS0246` for `MonthCell` / `ToMonthCells`.

- [ ] **Step 3: Implement `ToMonthCells`**

Walk 42 dates from the Monday on or before the 1st. For each, look up that date's `AgendaDay` (absent means an empty cell), take all its items — all-day first, then timed by start — cap at `maxPerCell`, and record the remainder as `HiddenCount`.

- [ ] **Step 4: Write the ViewModel and page**

`CalendarMonthViewModel` loads the same way `CalendarWeekViewModel` does, over the 42-day range, with the same overlap query for external events. The page is a `UniformGrid Rows="6" Columns="7"` of cells: date number, dimmed when `IsCurrentMonth` is false, accented when `IsToday`, then the item list and a `+N more` line bound to `HiddenCount`. Clicking a cell navigates to the week view for that date — expose it as an event the shell handles, rather than having the month page construct a `CalendarWeekPage` itself.

- [ ] **Step 5: Add the Month button** to `ScheduleShellPage`, register the ViewModel, and verify in the app that a month renders, that adjacent-month days are dimmed, that a busy day shows `+N more`, and that clicking a day opens that week.

Expected: `Passed!` with 0 failures and 3 more passing tests than before this task.

- [ ] **Step 6: Commit**

```bash
git add -A src/AaronOS.Modules.Schedule src/AaronOS.Modules.Schedule.Tests
git commit -m "Add the month calendar view"
```

---

## Task 9: Amend Plans 2 and 3 for the removed Today page

**Files:**
- Modify: `docs/superpowers/plans/2026-07-27-schedule-02-sleep-goals-suggestions.md`
- Modify: `docs/superpowers/plans/2026-07-27-schedule-03-notifications.md`

No code. Those plans were written against a Today page that no longer exists — plan 2's Task 7 is titled "Wire suggestions into the Today page", and plan 3's worker references it. Left alone, whoever executes them next will either recreate the page or stall.

- [ ] **Step 1: Find every reference**

```bash
grep -n 'TodayPage\|TodayViewModel\|Today page\|Today panel' \
  docs/superpowers/plans/2026-07-27-schedule-02-sleep-goals-suggestions.md \
  docs/superpowers/plans/2026-07-27-schedule-03-notifications.md
```

- [ ] **Step 2: Redirect them to the rail**

In plan 2, retitle Task 7 to "Wire suggestions into the agenda rail" and change its target to `AgendaRailViewModel` / the rail section of `CalendarWeekPage.xaml`. The `SuggestionEngine` interface is unchanged — only where the list is displayed moves. In plan 2's `SleepViewModel`, the external-event query and `AgendaBuilder` call stay exactly as they are.

In plan 3, change the Today references to the rail. The notification worker never touched the page — it builds its own agenda — so only prose changes there.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/plans
git commit -m "Point Plans 2 and 3 at the agenda rail instead of the Today page"
```

---

## Definition of done for Plan 6

- `dotnet build AaronOS.slnx --nologo` succeeds.
- `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo` reports 0 failures, with 28 more passing tests than the 69 in place before this plan.
- Week and Month are the only calendar views; no Today page or `TodayViewModel` remains anywhere in `src/`.
- Real Outlook meetings render at their correct times and durations; two at once sit side by side; an unrelated block in the same day keeps full width.
- Clicking an empty slot creates a block at that day and time.
- The all-day band appears only when there is an all-day item.
- Both light and dark themes are legible, and no `#RRGGBB` literal appears in any calendar XAML.

## Deferred, deliberately

**Drag to create, move and resize.** Most of the cost of a calendar control — hit-testing, snapping, live preview while dragging, undo. The grid is what delivers the Outlook feel; dragging is the next increment once it is in use.

**The `AaronOS.Core` contribution point** that would let other modules put items on this calendar. Rendering from `CalendarItem` rather than `AgendaEntry` is the whole preparation; the interface itself waits until a second module actually has something to show, so it is designed against a real need instead of a guess.

**Hour lines inside the columns.** Left out unless the grid reads badly without them: a line per hour per column is 168 extra elements for something the gutter labels may already convey.

**Editing an existing block from the grid.** Delete-and-recreate remains the only path, as it was before this plan.

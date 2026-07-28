# Schedule Module — Plan 2: Sleep, Goals, Releases, and Suggestions

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a bedtime recommendation derived from tomorrow's first commitment, generic goals with milestones, dated release tracking, and a ranked suggestion list surfaced on the Today page.

**Architecture:** Same shape as Plan 1 — two more pure static services (`SleepPlanner`, `SuggestionEngine`) that take materialised lists and return values, plus four entities and two pages. `SuggestionEngine` is the one place that combines agenda gaps, routine due states, releases, and milestones; nothing else ranks anything.

**Sleep scope — read this before Task 1.** This module does NOT store sleep history. The Medical module already owns it, twice: `MoodEntry` carries self-reported nightly hours, `SleepNight` carries measured hours imported from a Withings sleep pad, and `MoodStatistics.SleepFor` resolves which one to display. Adding a third store here would mean entering sleep in two modules and would produce a second set of numbers that could disagree with the pad.

So Schedule keeps only the half Medical does not have: a target-hours setting and the forward-looking question "given tomorrow's first commitment, when should I be in bed?" That needs no history at all — it reads the agenda, not the past.

The consequence, stated plainly because it drops a feature the spec originally listed: there is **no sleep-debt tracking** in this plan. Debt requires actual hours slept, which live in Medical, and `docs/MODULE_GUIDELINES.md` forbids reading another module's entities. Adding debt later means promoting the nightly-sleep shape into `AaronOS.Core` so both modules share it — a deliberate, separate piece of work, not something to improvise inside this plan. Do not add a `SleepLog` entity, and do not reach into Medical's tables.

**Tech Stack:** Unchanged from Plan 1 — .NET 8 `net8.0-windows`, WPF, WPF-UI 4.3.0, EF Core 8 + SQLite, CommunityToolkit.Mvvm, xUnit 2.5.3.

**Spec:** `docs/superpowers/specs/2026-07-27-schedule-module-design.md` — this plan covers phases 3, 4, and 5.

**Prerequisite:** Plans 1 and 4 complete, in that order — `AaronOS.Modules.Schedule` exists and is registered, `AgendaBuilder.Build` and `RoutineScheduler.EvaluateAll` are in place, and `ExternalEventProjector` exists. Plan 4 (external calendars) was moved ahead of this one, so `SleepViewModel` must project cached external events into tomorrow's agenda when it computes a bedtime.

## Global Constraints

Every task's requirements implicitly include this section. These repeat Plan 1's constraints because a task's implementer may not have read Plan 1.

- Target framework `net8.0-windows`; `UseWPF` true; `LangVersion` `13.0`; `Nullable` `enable`; `ImplicitUsings` `enable`.
- **Never use the partial-property `[ObservableProperty]` form.** The generator does not run in this environment. Always write `[ObservableProperty] private bool _x;` and ignore `MVVMTK0045`.
- ViewModels transient, services singleton.
- Pages have a public parameterless constructor, resolve their ViewModel via `AaronOS.Core.AppServices.Provider.GetRequiredService<T>()`, set `DataContext` explicitly, then `InitializeComponent()`, then hook `Loaded`.
- `Frame.Navigate` takes an instance: `ContentFrame.Navigate(new SleepPage())`.
- Never reference another module's entities. `Goal` here is unrelated to `BodyMeasurements` goals and must not read them.
- **`Goal` must map to the table `ScheduleGoal`.** Every module shares one SQLite file, and BodyMeasurements already owns a `Goal` table. This does not fail loudly: `SchemaBootstrapper` creates only MISSING tables, so it would find `Goal` present, skip it, and every Schedule goal query would then run against BodyMeasurements' columns. `ToTable("ScheduleGoal")` in `GoalConfiguration`, plus the guard test in Task 4. Of the eight entities plans 2, 4 and 5 add, `Goal` is the only name that collides — checked against the live database — so the others keep their default table names.
- WPF `Grid`/`StackPanel` have no `Spacing`/`Padding` — use explicit `Margin` on children.
- `ui:NumberBox.Value` is declared `double?` on the installed WPF-UI 4.3.0; a cleared box reports `null`, not `double.NaN`. Bind it to a `double?`, use `null` as the not-entered sentinel, convert at save time, no value converter. A non-nullable `double` target silently fails to update on clear — WPF drops the `null`→`double` TwoWay conversion instead of throwing — so a `double.IsNaN` guard against it is unreachable.
- `DatePicker.SelectedDate` is `DateTime?`.
- Per-item buttons in a `DataTemplate` use a code-behind `Click` handler reading `DataContext`.
- Pure services take `today` (or an explicit date) as a parameter. **Never read `DateTime.Now` inside a pure service** — it makes the rules untestable at an arbitrary date.
- Imperial units, local time only. Times are `TimeSpan` (wall clock) or `DateTime` (local), never `DateTimeOffset`.
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
- **Sleep advice is arithmetic, not diagnosis.** `SleepPlanner` computes a bedtime by working backwards from the next day's first commitment against a user-set target. It must not infer, recommend, or hard-code a target beyond the 8.0-hour default. No UI copy may imply a clinical recommendation.
- **No sleep history in this module.** Do not create a `SleepLog` entity, do not compute sleep debt, and do not read the Medical module's `MoodEntry` or `SleepNight` tables — cross-module entity access is forbidden by `docs/MODULE_GUIDELINES.md`. See the sleep-scope note in the header for why.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/AaronOS.Modules.Schedule/Data/SleepSettings.cs` (+`Configuration`) | Single-row target and lead times |
| `src/AaronOS.Modules.Schedule/Data/Goal.cs` (+`Configuration`) | Generic dated goal |
| `src/AaronOS.Modules.Schedule/Data/GoalMilestone.cs` (+`Configuration`) | A goal's checklist item |
| `src/AaronOS.Modules.Schedule/Data/Release.cs` (+`Configuration`) | Media or product release date |
| `src/AaronOS.Modules.Schedule/Sleep/SleepPlanner.cs` | Pure bedtime computation |
| `src/AaronOS.Modules.Schedule/Suggestions/Suggestion.cs` | Result record, one ranked item |
| `src/AaronOS.Modules.Schedule/Suggestions/SuggestionEngine.cs` | Pure ranking across every input |
| `src/AaronOS.Modules.Schedule/ViewModels/SleepViewModel.cs` | Sleep page state |
| `src/AaronOS.Modules.Schedule/ViewModels/GoalsViewModel.cs` | Goals and releases page state |
| `src/AaronOS.Modules.Schedule/Views/SleepPage.xaml(.cs)` | Edit target and lead times, show tonight's bedtime |
| `src/AaronOS.Modules.Schedule/Views/GoalsPage.xaml(.cs)` | Goals, milestones, releases |
| `src/AaronOS.Modules.Schedule.Tests/SleepPlannerTests.cs` | Bedtime tests |
| `src/AaronOS.Modules.Schedule.Tests/SuggestionEngineTests.cs` | Ranking tests |

---

## Task 1: Sleep settings entity

Settings only. There is no sleep-history entity in this module — see the sleep-scope note in the
header. Do not add a `SleepLog`.

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Data/SleepSettings.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/SleepSettingsConfiguration.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs`

**Interfaces:**
- Produces: `SleepSettings` with `Id`, `TargetHours`, `SleepOnsetMinutes`, `MorningRoutineMinutes`, `WindDownLeadMinutes`, and a `static SleepSettings Default()`.

- [ ] **Step 1: Write the failing test**

Add to the existing `ScheduleSchemaTests` class (it already has `CreateContext()` and `Dispose()` from Plan 1):

> ⚠️ **The test code below uses one `db` for both the write and the read. That is stale — restructure it before running.** EF Core's identity resolution returns the already-tracked entity, so asserting through the context that performed the insert checks the object the test constructed, not what SQLite stored: a broken value converter would pass. Write in one context, dispose it, then verify through a fresh `CreateContext()` against the same `_dbPath`. And where a test deletes to prove a cascade, the deleting context must not load or track the children — otherwise it proves EF's client-side cascade rather than the database foreign key. Use `ExecuteDeleteAsync` or a key-only attached stub there.

```csharp
    [Fact]
    public void SleepSettings_DefaultsMatchTheSpec()
    {
        var settings = SleepSettings.Default();

        Assert.Equal(8.0m, settings.TargetHours);
        Assert.Equal(15, settings.SleepOnsetMinutes);
        Assert.Equal(45, settings.MorningRoutineMinutes);
        Assert.Equal(30, settings.WindDownLeadMinutes);
    }

    [Fact]
    public async Task SleepSettings_RoundTripsItsDecimalPrecision()
    {
        // TargetHours is configured HasPrecision(4,2); a whole-number column type would read 7.5
        // back as 7 or 8 and silently shift every bedtime this module recommends.
        await using (var db = CreateContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Add(new SleepSettings { TargetHours = 7.5m, SleepOnsetMinutes = 20, MorningRoutineMinutes = 50, WindDownLeadMinutes = 25 });
            await db.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var loaded = await verify.Set<SleepSettings>().SingleAsync();
        Assert.Equal(7.5m, loaded.TargetHours);
        Assert.Equal(20, loaded.SleepOnsetMinutes);
        Assert.Equal(50, loaded.MorningRoutineMinutes);
        Assert.Equal(25, loaded.WindDownLeadMinutes);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `SleepSettings`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Data/SleepSettings.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// Single-row settings table, following the UserProfile pattern but kept in this module because
/// nothing else needs it.
///
/// <see cref="TargetHours"/> is the user's own choice. Nothing in this module infers it, and
/// nothing should: working a bedtime backwards from a stated target is arithmetic, whereas
/// determining how much sleep a person needs is not something this app can know.
///
/// This is the only sleep table in the Schedule module. Hours actually slept live in the Medical
/// module (self-reported on MoodEntry, measured on SleepNight) and are deliberately not duplicated
/// or read from here.
/// </summary>
public class SleepSettings
{
    public int Id { get; set; }

    /// <summary>Nightly target in hours. Defaults to 8.0.</summary>
    public decimal TargetHours { get; set; } = 8.0m;

    /// <summary>How long it takes to actually fall asleep after getting into bed.</summary>
    public int SleepOnsetMinutes { get; set; } = 15;

    /// <summary>Time needed between waking and the first commitment.</summary>
    public int MorningRoutineMinutes { get; set; } = 45;

    /// <summary>How far before the recommended bedtime a wind-down reminder fires.</summary>
    public int WindDownLeadMinutes { get; set; } = 30;

    public static SleepSettings Default() => new();
}
```

`src/AaronOS.Modules.Schedule/Data/SleepSettingsConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class SleepSettingsConfiguration : IEntityTypeConfiguration<SleepSettings>
{
    public void Configure(EntityTypeBuilder<SleepSettings> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TargetHours).HasPrecision(4, 2);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed!` with 0 failures and 2 more passing tests than before this task.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Data src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs
git commit -m "Add the SleepSettings entity"
```

---

## Task 2: SleepPlanner

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Sleep/SleepPlanner.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/SleepPlannerTests.cs`

**Interfaces:**
- Consumes: `AgendaDay` and `AgendaEntry` from Plan 1, `SleepSettings`.
- Produces:
  - `static DateTime? SleepPlanner.RecommendedBedtime(DateOnly tonight, AgendaDay tomorrow, SleepSettings settings)`

  Plan 3's notification tick calls `RecommendedBedtime` with exactly this signature.

This service is one method. There is no `SleepSummary` record and no `Summarize` — with debt and
averages out of scope (see the header), a summary type would wrap a single nullable `DateTime`,
which is noise. Callers call `RecommendedBedtime` directly.

- [ ] **Step 1: Write the failing tests**

Create `src/AaronOS.Modules.Schedule.Tests/SleepPlannerTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Sleep;

namespace AaronOS.Modules.Schedule.Tests;

public class SleepPlannerTests
{
    private static readonly DateOnly Tonight = new(2026, 7, 6);
    private static readonly DateOnly Tomorrow = new(2026, 7, 7);

    private static SleepSettings Settings() => SleepSettings.Default(); // 8h, 15m onset, 45m routine

    private static AgendaDay Day(params AgendaEntry[] entries) =>
        new(Tomorrow, entries, []);

    private static AgendaEntry Work(int hour) =>
        new(new TimeSpan(hour, 0, 0), new TimeSpan(17, 0, 0), ScheduleBlockKind.Work, "Core hours", AgendaEntrySource.Block);

    private static AgendaEntry SleepEntry() =>
        new(TimeSpan.Zero, new TimeSpan(7, 0, 0), ScheduleBlockKind.Sleep, "Sleep", AgendaEntrySource.Block);


    [Fact]
    public void Bedtime_WorksBackFromTomorrowsFirstCommitment()
    {
        // First commitment 08:00, minus 45m routine = wake 07:15, minus 8h = asleep 23:15,
        // minus 15m onset = in bed 23:00.
        var bedtime = SleepPlanner.RecommendedBedtime(Tonight, Day(Work(8)), Settings());

        Assert.Equal(new DateTime(2026, 7, 6, 23, 0, 0), bedtime);
    }

    [Fact]
    public void Bedtime_IgnoresSleepEntriesWhenPickingTheFirstCommitment()
    {
        // Tomorrow's agenda opens with the tail of tonight's sleep block; that is not a commitment.
        var bedtime = SleepPlanner.RecommendedBedtime(Tonight, Day(SleepEntry(), Work(9)), Settings());

        // 09:00 − 45m = 08:15, − 8h = 00:15 on the 7th, − 15m = 00:00 on the 7th.
        Assert.Equal(new DateTime(2026, 7, 7, 0, 0, 0), bedtime);
    }

    [Fact]
    public void Bedtime_IsNullWhenTomorrowHasNoCommitments()
    {
        Assert.Null(SleepPlanner.RecommendedBedtime(Tonight, Day(SleepEntry()), Settings()));
        Assert.Null(SleepPlanner.RecommendedBedtime(Tonight, Day(), Settings()));
    }

    [Fact]
    public void Bedtime_HonoursCustomSettings()
    {
        var settings = new SleepSettings { TargetHours = 7.0m, SleepOnsetMinutes = 30, MorningRoutineMinutes = 60 };

        // 08:00 − 60m = 07:00, − 7h = 00:00, − 30m = 23:30 the night before.
        var bedtime = SleepPlanner.RecommendedBedtime(Tonight, Day(Work(8)), settings);

        Assert.Equal(new DateTime(2026, 7, 6, 23, 30, 0), bedtime);
    }

}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `SleepPlanner`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Sleep/SleepPlanner.cs`:

```csharp
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Sleep;

/// <summary>
/// Arithmetic over the user's own numbers: when to go to bed given what tomorrow demands.
/// Deliberately not in the business of deciding what the target should be — that is
/// <see cref="SleepSettings.TargetHours"/>, which the user sets.
///
/// Hours actually slept are not this module's concern; the Medical module records them. So there is
/// nothing here about debt, averages, or history — only the forward-looking recommendation, which
/// needs the agenda and the settings and nothing else.
///
/// Pure: takes dates as parameters and never reads the clock, so every rule is testable at any date.
/// </summary>
public static class SleepPlanner
{
    /// <summary>
    /// Works backward from tomorrow's first non-sleep commitment: minus the morning routine, minus
    /// the nightly target, minus the time it takes to fall asleep. Returns null when tomorrow has
    /// no commitment — there is nothing to work back from, and inventing one would be a guess.
    /// </summary>
    public static DateTime? RecommendedBedtime(DateOnly tonight, AgendaDay tomorrow, SleepSettings settings)
    {
        if (tomorrow.FirstCommitment is not { } first) return null;

        var commitmentAt = tomorrow.Date.ToDateTime(TimeOnly.MinValue) + first.Start;

        return commitmentAt
            .AddMinutes(-settings.MorningRoutineMinutes)
            .AddHours(-(double)settings.TargetHours)
            .AddMinutes(-settings.SleepOnsetMinutes);
    }
}
```

Note the returned `DateTime` can land on either calendar day: a 09:00 start with an 8-hour target
puts bedtime just after midnight, so it belongs to tomorrow's date, not tonight's. The
`Bedtime_IgnoresSleepEntriesWhenPickingTheFirstCommitment` test pins exactly that case. Do not clamp
the result to `tonight`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed!` with 0 failures and 4 more passing tests than before this task.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Sleep src/AaronOS.Modules.Schedule.Tests/SleepPlannerTests.cs
git commit -m "Add SleepPlanner bedtime recommendation"
```

---

## Task 3: Sleep page

**Files:**
- Create: `src/AaronOS.Modules.Schedule/ViewModels/SleepViewModel.cs`
- Create: `src/AaronOS.Modules.Schedule/Views/SleepPage.xaml`
- Create: `src/AaronOS.Modules.Schedule/Views/SleepPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `SleepPlanner.RecommendedBedtime`, `AgendaBuilder.Build`, `SleepSettings`.
- Produces: `SleepViewModel` with the bedtime display, the four settings properties, `LoadCommand` and `SaveSettingsCommand`.

This page edits settings and shows tonight's recommended bedtime. It does NOT log nights and shows
no history — see the sleep-scope note in the header. Sleep history is the Medical module's Sleep and
Mood pages.

- [ ] **Step 1: Write the ViewModel**

No unit test: the arithmetic is covered by the 4 `SleepPlannerTests`. This is a database read plus a call into the planner.

`src/AaronOS.Modules.Schedule/ViewModels/SleepViewModel.cs`:

```csharp
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;
using AaronOS.Modules.Schedule.Sleep;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class SleepViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    [ObservableProperty]
    private string _bedtimeDisplay = "";

    // Settings editor. Every property below is bound to a ui:NumberBox, so each is double? and null
    // means "cleared" — see the Global Constraint. Do not narrow these to double.
    [ObservableProperty]
    private double? _targetHours = 8;

    [ObservableProperty]
    private double? _sleepOnsetMinutes = 15;

    [ObservableProperty]
    private double? _morningRoutineMinutes = 45;

    [ObservableProperty]
    private double? _windDownLeadMinutes = 30;

    [ObservableProperty]
    private string? _validationMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var tomorrowDate = today.AddDays(1);

            await using var db = await dbContextFactory.CreateDbContextAsync();

            var settings = await LoadOrCreateSettingsAsync(db);
            TargetHours = (double)settings.TargetHours;
            SleepOnsetMinutes = settings.SleepOnsetMinutes;
            MorningRoutineMinutes = settings.MorningRoutineMinutes;
            WindDownLeadMinutes = settings.WindDownLeadMinutes;

            var blocks = await db.Set<ScheduleBlock>().Where(b => b.IsActive).ToListAsync();
            var exceptions = await db.Set<ScheduleException>()
                .Where(e => e.Date >= today && e.Date <= tomorrowDate)
                .ToListAsync();
            // External calendars (Plan 4) already shipped, so tomorrow's first commitment must
            // account for a real meeting — that is the whole point of the integration, and an empty
            // list here would silently recommend a bedtime based only on template blocks.
            // Interval-overlap test, NOT a StartsAt range. A multi-day event that began before
            // this window but is still running has to count — otherwise a bedtime is recommended as
            // though tomorrow were free. Plan 4 shipped the same bug in three places and it cost a
            // permanently failing sync; do not "simplify" this back to a StartsAt filter.
            var windowStart = today.ToDateTime(TimeOnly.MinValue);
            var windowEnd = tomorrowDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var externalRows = await db.Set<ExternalEvent>()
                .Where(e => e.StartsAt < windowEnd && e.EndsAt > windowStart)
                .ToListAsync();

            var tomorrow = AgendaBuilder.Build(
                tomorrowDate, tomorrowDate, blocks, exceptions,
                ExternalEventProjector.ToAgendaEntries(externalRows)).Single();

            var bedtime = SleepPlanner.RecommendedBedtime(today, tomorrow, settings);

            // Show the date as well as the time. An early first commitment can push the
            // recommendation past midnight, and "be in bed by 12:15 AM" is ambiguous without it.
            BedtimeDisplay = bedtime is { } at
                ? $"Aim to be in bed by {at:h:mm tt} on {at:ddd d MMM}"
                : "No commitments tomorrow — no bedtime to work back from.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        ValidationMessage = null;

        // `is null or <= 0 or > 16` covers the cleared box too — a relational pattern never
        // matches a null double?, so the null arm has to be spelled out.
        if (TargetHours is null or <= 0 or > 16)
        {
            ValidationMessage = "Target hours must be between 1 and 16.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var settings = await LoadOrCreateSettingsAsync(db);
        settings.TargetHours = (decimal)TargetHours.Value;
        settings.SleepOnsetMinutes = (int)(SleepOnsetMinutes ?? 0);
        settings.MorningRoutineMinutes = (int)(MorningRoutineMinutes ?? 0);
        settings.WindDownLeadMinutes = (int)(WindDownLeadMinutes ?? 0);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private static async Task<SleepSettings> LoadOrCreateSettingsAsync(AaronOsDbContext db)
    {
        var settings = await db.Set<SleepSettings>().FirstOrDefaultAsync();
        if (settings is not null) return settings;

        settings = SleepSettings.Default();
        db.Add(settings);
        await db.SaveChangesAsync();
        return settings;
    }
}
```

Register in `ScheduleModule.RegisterServices`:

```csharp
        services.AddTransient<SleepViewModel>();
```

- [ ] **Step 2: Write the page**

`src/AaronOS.Modules.Schedule/Views/SleepPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Schedule.Views.SleepPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    mc:Ignorable="d">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16">
            <ui:TextBlock Text="Sleep" FontTypography="Subtitle" Margin="0,0,0,12" />

            <ui:Card Margin="0,0,0,12">
                <StackPanel>
                    <ui:TextBlock Text="{Binding BedtimeDisplay}" FontTypography="BodyStrong" />
                </StackPanel>
            </ui:Card>

            <ui:Card Margin="0,0,0,12">
                <StackPanel>
                    <ui:TextBlock Text="Your target" FontTypography="BodyStrong" Margin="0,0,0,4" />
                    <TextBlock TextWrapping="Wrap" Margin="0,0,0,8"
                               Text="These are your numbers. The bedtime above is worked back from tomorrow's first commitment using them — it isn't a recommendation about how much sleep you need." />
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                        <ui:NumberBox PlaceholderText="Target hours" Value="{Binding TargetHours, Mode=TwoWay}" Width="130" Margin="0,0,8,0" />
                        <ui:NumberBox PlaceholderText="Onset min" Value="{Binding SleepOnsetMinutes, Mode=TwoWay}" Width="130" Margin="0,0,8,0" />
                        <ui:NumberBox PlaceholderText="Morning min" Value="{Binding MorningRoutineMinutes, Mode=TwoWay}" Width="130" Margin="0,0,8,0" />
                        <ui:NumberBox PlaceholderText="Wind-down min" Value="{Binding WindDownLeadMinutes, Mode=TwoWay}" Width="130" />
                    </StackPanel>
                    <ui:TextBlock Text="{Binding ValidationMessage}" Foreground="{DynamicResource SystemFillColorCriticalBrush}" Margin="0,0,0,8" />
                    <ui:Button Content="Save target" Appearance="Primary" Command="{Binding SaveSettingsCommand}" HorizontalAlignment="Left" />
                </StackPanel>
            </ui:Card>

            <ui:Card>
                <StackPanel>
                    <ui:TextBlock Text="Hours slept" FontTypography="BodyStrong" Margin="0,0,0,4" />
                    <TextBlock TextWrapping="Wrap"
                               Text="Nights slept are recorded in Medical — the Sleep page imports measured hours from the sleep pad, and the Mood page takes a self-report. This page only plans forward, so it deliberately doesn't ask you to enter the same thing twice." />
                </StackPanel>
            </ui:Card>
        </StackPanel>
    </ScrollViewer>
</Page>
```

`src/AaronOS.Modules.Schedule/Views/SleepPage.xaml.cs`:

```csharp
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class SleepPage : Page
{
    public SleepViewModel ViewModel { get; }

    public SleepPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<SleepViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
```

Add the shell button. In `ScheduleShellPage.xaml`, inside the `StackPanel`:

```xml
            <ui:Button Content="Sleep" Click="Sleep_Click" Margin="0,0,8,0" />
```

and in `ScheduleShellPage.xaml.cs`:

```csharp
    private void Sleep_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new SleepPage());
```

Note the existing `Routines` button was the last in the row and has no right margin. Give it `Margin="0,0,8,0"` now that buttons follow it.

- [ ] **Step 3: Verify in the running app**

Run: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`

With the `Core hours` (Mon–Fri 08:00–17:00) and `Sleep` blocks from Plan 1 in place, confirm:

1. Schedule → Sleep loads. If tomorrow is a weekday, the top card reads "Aim to be in bed by 11:00 PM" — 08:00 minus 45 minutes minus 8 hours minus 15 minutes. If tomorrow is Saturday, it reads "No commitments tomorrow".
2. Log last night: `23:30` to `06:30`. The row appears reading `7 h`, and the debt line reads `1 h behind your 8 h target`.
3. Re-log the same night with `22:00` to `07:00`. No error appears, the row updates to `9 h`, and the debt returns to `0 h` — re-logging is an edit, enforced by the unique index.
4. Set target hours to `7` and save. The debt line updates to reference a 7 h target and the bedtime moves an hour later.
5. Enter `abc` as a bed time and save; "Enter times as HH:mm." appears and nothing is written.

Close the app.

- [ ] **Step 4: Run the tests to confirm nothing regressed**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed!` with 0 failures and the same test count as Step 2 — this task adds no tests.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add Sleep page with logging, target settings, bedtime and debt"
```

---

## Task 4: Goal, GoalMilestone, and Release entities

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Data/Goal.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/GoalConfiguration.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/GoalMilestone.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/GoalMilestoneConfiguration.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/Release.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/ReleaseConfiguration.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs`

**Interfaces:**
- Consumes: `GoalStatus`, `ReleaseCategory` from Plan 1's `ScheduleEnums.cs`.
- Produces: `Goal` (`Id`, `Title`, `Description`, `TargetDate`, `ProgressPercent`, `Status`, `CreatedAt`, `CompletedAt`), `GoalMilestone` (`Id`, `GoalId`, `Title`, `DueDate`, `IsDone`, `SortOrder`), `Release` (`Id`, `Title`, `Category`, `ReleaseDate`, `IsDateEstimated`, `Url`, `Notes`, `IsDismissed`).

- [ ] **Step 1: Write the failing test**

Add to `ScheduleSchemaTests`:

> ⚠️ **The test code below uses one `db` for both the write and the read. That is stale — restructure it before running.** EF Core's identity resolution returns the already-tracked entity, so asserting through the context that performed the insert checks the object the test constructed, not what SQLite stored: a broken value converter would pass. Write in one context, dispose it, then verify through a fresh `CreateContext()` against the same `_dbPath`. And where a test deletes to prove a cascade, the deleting context must not load or track the children — otherwise it proves EF's client-side cascade rather than the database foreign key. Use `ExecuteDeleteAsync` or a key-only attached stub there.

```csharp
    [Fact]
    public void Goal_MapsToScheduleGoal_NotTheTableBodyMeasurementsOwns()
    {
        // Not cosmetic. BodyMeasurements owns `Goal` in the same shared database, and
        // SchemaBootstrapper only creates missing tables — so a default mapping here would silently
        // bind to the wrong columns instead of failing. This test is the guard.
        using var db = CreateContext();

        Assert.Equal("ScheduleGoal", db.Model.FindEntityType(typeof(Goal))!.GetTableName());
    }

    [Fact]
    public async Task Goal_CascadeDeletesItsMilestones()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        var goal = new Goal
        {
            Title = "Ship the Schedule module",
            TargetDate = new DateOnly(2026, 9, 1),
            Status = GoalStatus.Active,
            CreatedAt = new DateTime(2026, 7, 27, 12, 0, 0),
        };
        db.Add(goal);
        await db.SaveChangesAsync();

        db.Add(new GoalMilestone { GoalId = goal.Id, Title = "Phase 1", SortOrder = 0 });
        db.Add(new GoalMilestone { GoalId = goal.Id, Title = "Phase 2", DueDate = new DateOnly(2026, 8, 10), SortOrder = 1 });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Set<GoalMilestone>().CountAsync());

        db.Remove(goal);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.Set<GoalMilestone>().CountAsync());
    }

    [Fact]
    public async Task Release_StoresBothCategories()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.Add(new Release
        {
            Title = "Some Game",
            Category = ReleaseCategory.Media,
            ReleaseDate = new DateOnly(2026, 11, 20),
            IsDateEstimated = true,
        });
        db.Add(new Release
        {
            Title = "GPU restock",
            Category = ReleaseCategory.Product,
            ReleaseDate = new DateOnly(2026, 8, 5),
            Url = "https://example.com/restock",
        });
        await db.SaveChangesAsync();

        var releases = await db.Set<Release>().OrderBy(r => r.ReleaseDate).ToListAsync();
        Assert.Equal(ReleaseCategory.Product, releases[0].Category);
        Assert.True(releases[1].IsDateEstimated);
        Assert.False(releases[0].IsDismissed);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `Goal`, `GoalMilestone`, and `Release`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Data/Goal.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// A generic dated goal. Deliberately unrelated to the BodyMeasurements module's weight and
/// muscle goals: MODULE_GUIDELINES.md forbids reaching across module boundaries, so the two
/// coexist without referencing each other. Body-composition goals belong there, not here.
/// </summary>
public class Goal
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly? TargetDate { get; set; }

    /// <summary>0 to 100.</summary>
    public int ProgressPercent { get; set; }

    public GoalStatus Status { get; set; } = GoalStatus.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

`src/AaronOS.Modules.Schedule/Data/GoalConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        // MUST be ToTable("ScheduleGoal"), not the default "Goal". BodyMeasurements already owns a
        // `Goal` table (Metric, Direction, StartValue, TargetValue, ...) in the same shared SQLite
        // database — verified against the live file. Leaving the default name would not throw:
        // SchemaBootstrapper only creates MISSING tables, so it would find `Goal` already present,
        // skip it, and every query here would then run against BodyMeasurements' columns and fail at
        // runtime. Two modules cannot share a table name; see docs/MODULE_GUIDELINES.md.
        builder.ToTable("ScheduleGoal");

        builder.HasKey(g => g.Id);
        builder.HasIndex(g => g.Status);
        builder.Property(g => g.Title).HasMaxLength(200).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(2000);
        builder.Property(g => g.Status).HasConversion<int>();
    }
}
```

`src/AaronOS.Modules.Schedule/Data/GoalMilestone.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

public class GoalMilestone
{
    public int Id { get; set; }
    public int GoalId { get; set; }
    public string Title { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
}
```

`src/AaronOS.Modules.Schedule/Data/GoalMilestoneConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class GoalMilestoneConfiguration : IEntityTypeConfiguration<GoalMilestone>
{
    public void Configure(EntityTypeBuilder<GoalMilestone> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.GoalId, m.SortOrder });
        builder.Property(m => m.Title).HasMaxLength(200).IsRequired();
        builder.HasOne<Goal>()
            .WithMany()
            .HasForeignKey(m => m.GoalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`src/AaronOS.Modules.Schedule/Data/Release.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// One dated thing worth knowing about — a game or show (<see cref="ReleaseCategory.Media"/>) or a
/// hardware launch or restock (<see cref="ReleaseCategory.Product"/>). One table for both, because
/// they differ only by category and by whether the date implies an action.
/// </summary>
public class Release
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public ReleaseCategory Category { get; set; }
    public DateOnly ReleaseDate { get; set; }

    /// <summary>True for a "Q4 2026" style date entered as a placeholder day.</summary>
    public bool IsDateEstimated { get; set; }

    public string? Url { get; set; }
    public string? Notes { get; set; }

    /// <summary>Set once it's been dealt with, so it stops appearing in suggestions without
    /// losing the record.</summary>
    public bool IsDismissed { get; set; }
}
```

`src/AaronOS.Modules.Schedule/Data/ReleaseConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class ReleaseConfiguration : IEntityTypeConfiguration<Release>
{
    public void Configure(EntityTypeBuilder<Release> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.ReleaseDate);
        builder.Property(r => r.Title).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Category).HasConversion<int>();
        builder.Property(r => r.Url).HasMaxLength(500);
        builder.Property(r => r.Notes).HasMaxLength(1000);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed!` with 0 failures and 3 more passing tests than before this task.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Data src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs
git commit -m "Add Goal, GoalMilestone, and Release entities"
```

---

## Task 5: SuggestionEngine

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Suggestions/Suggestion.cs`
- Create: `src/AaronOS.Modules.Schedule/Suggestions/SuggestionEngine.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/SuggestionEngineTests.cs`

**Interfaces:**
- Consumes: `AgendaDay`, `FreeGap` (Plan 1), `Routine`, `RoutineDueState` (Plan 1), `Release`, `GoalMilestone`, and `DateTime?` bedtime from `SleepPlanner`.
- Produces:
  - `enum SuggestionUrgency { Informational, Due, Overdue }`
  - `enum SuggestionKind { Routine, Release, Milestone, Bedtime }`
  - `record Suggestion(SuggestionKind Kind, string Title, string Reason, SuggestionUrgency Urgency, TimeSpan? SuggestedStart, int? EstimatedMinutes, int? SourceId)`
  - `static IReadOnlyList<Suggestion> SuggestionEngine.Build(SuggestionInput input)`
  - `record SuggestionInput(DateOnly Today, AgendaDay TodayAgenda, IReadOnlyList<Routine> Routines, IReadOnlyList<RoutineDueState> DueStates, IReadOnlyList<Release> Releases, IReadOnlyList<GoalMilestone> Milestones, DateTime? RecommendedBedtime, int LookaheadDays = 7)`

- [ ] **Step 1: Write the failing tests**

Create `src/AaronOS.Modules.Schedule.Tests/SuggestionEngineTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;
using AaronOS.Modules.Schedule.Suggestions;

namespace AaronOS.Modules.Schedule.Tests;

public class SuggestionEngineTests
{
    private static readonly DateOnly Today = new(2026, 7, 6);

    /// <summary>A day with work 08:00-17:00 and sleep, leaving gaps 07:00-08:00 and 17:00-23:00.</summary>
    private static AgendaDay TypicalDay() => new(
        Today,
        [
            new AgendaEntry(TimeSpan.Zero, new TimeSpan(7, 0, 0), ScheduleBlockKind.Sleep, "Sleep", AgendaEntrySource.Block),
            new AgendaEntry(new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), ScheduleBlockKind.Work, "Core hours", AgendaEntrySource.Block),
            new AgendaEntry(new TimeSpan(23, 0, 0), new TimeSpan(24, 0, 0), ScheduleBlockKind.Sleep, "Sleep", AgendaEntrySource.Block),
        ],
        [
            new FreeGap(new TimeSpan(7, 0, 0), new TimeSpan(8, 0, 0)),   // 60 min
            new FreeGap(new TimeSpan(17, 0, 0), new TimeSpan(23, 0, 0)), // 360 min
        ]);

    private static AgendaDay FullyBookedDay() => new(
        Today,
        [new AgendaEntry(TimeSpan.Zero, new TimeSpan(24, 0, 0), ScheduleBlockKind.Work, "On call", AgendaEntrySource.Block)],
        []);

    private static Routine Routine(int id, string name, int? minutes = null, TimeSpan? preferred = null) => new()
    {
        Id = id, Name = name, Category = RoutineCategory.Other, IntervalDays = 2,
        EstimatedMinutes = minutes, PreferredTimeOfDay = preferred,
    };

    private static RoutineDueState Due(int id, int overdue = 0) =>
        new(id, Today.AddDays(-overdue), overdue, null, IsDue: true);

    private static RoutineDueState NotDue(int id) =>
        new(id, Today.AddDays(3), 0, null, IsDue: false);

    private static SuggestionInput Input(
        AgendaDay? day = null,
        IReadOnlyList<Routine>? routines = null,
        IReadOnlyList<RoutineDueState>? states = null,
        IReadOnlyList<Release>? releases = null,
        IReadOnlyList<GoalMilestone>? milestones = null,
        DateTime? bedtime = null) =>
        new(Today, day ?? TypicalDay(), routines ?? [], states ?? [], releases ?? [], milestones ?? [], bedtime);

    [Fact]
    public void OverdueRoutines_OutrankDueOnes_MostOverdueFirst()
    {
        var routines = new[] { Routine(1, "Slightly late"), Routine(2, "Very late"), Routine(3, "Due today") };
        var states = new[] { Due(1, overdue: 1), Due(2, overdue: 5), Due(3) };

        var suggestions = SuggestionEngine.Build(Input(routines: routines, states: states));

        Assert.Equal(["Very late", "Slightly late", "Due today"], suggestions.Select(s => s.Title));
        Assert.Equal(SuggestionUrgency.Overdue, suggestions[0].Urgency);
        Assert.Equal(SuggestionUrgency.Due, suggestions[2].Urgency);
    }

    [Fact]
    public void RoutinesThatAreNotDue_AreExcluded()
    {
        var suggestions = SuggestionEngine.Build(
            Input(routines: [Routine(1, "Later")], states: [NotDue(1)]));

        Assert.Empty(suggestions);
    }

    [Fact]
    public void RoutineFittingAGap_OutranksOneThatDoesNot_AtEqualUrgency()
    {
        // Largest gap is 360 minutes. The 600-minute routine cannot fit anywhere today.
        var routines = new[] { Routine(1, "Too long", minutes: 600), Routine(2, "Fits", minutes: 30) };
        var states = new[] { Due(1), Due(2) };

        var suggestions = SuggestionEngine.Build(Input(routines: routines, states: states));

        Assert.Equal(["Fits", "Too long"], suggestions.Select(s => s.Title));
        Assert.Equal(new TimeSpan(7, 0, 0), suggestions[0].SuggestedStart);
        Assert.Null(suggestions[1].SuggestedStart); // nowhere to put it
    }

    [Fact]
    public void RoutineIsPlacedInTheGapContainingItsPreferredTime()
    {
        var routines = new[] { Routine(1, "Evening walk", minutes: 45, preferred: new TimeSpan(19, 0, 0)) };

        var suggestions = SuggestionEngine.Build(Input(routines: routines, states: [Due(1)]));

        // 19:00 falls inside the 17:00-23:00 gap, so it is placed at its preferred time rather
        // than at the start of the first gap that happens to be large enough.
        Assert.Equal(new TimeSpan(19, 0, 0), Assert.Single(suggestions).SuggestedStart);
    }

    [Fact]
    public void PreferredTimeOutsideEveryGap_FallsBackToTheFirstFittingGap()
    {
        // 12:00 is inside the work block, not a gap.
        var routines = new[] { Routine(1, "Midday errand", minutes: 30, preferred: new TimeSpan(12, 0, 0)) };

        var suggestions = SuggestionEngine.Build(Input(routines: routines, states: [Due(1)]));

        Assert.Equal(new TimeSpan(7, 0, 0), Assert.Single(suggestions).SuggestedStart);
    }

    [Fact]
    public void RoutineWithNoEstimate_IsPlacedInTheFirstGap()
    {
        var suggestions = SuggestionEngine.Build(
            Input(routines: [Routine(1, "Unknown length")], states: [Due(1)]));

        Assert.Equal(new TimeSpan(7, 0, 0), Assert.Single(suggestions).SuggestedStart);
    }

    [Fact]
    public void FullyBookedDay_StillSuggests_ButWithNoTime()
    {
        var suggestions = SuggestionEngine.Build(
            Input(day: FullyBookedDay(), routines: [Routine(1, "Litter box", minutes: 5)], states: [Due(1)]));

        var only = Assert.Single(suggestions);
        Assert.Equal("Litter box", only.Title);
        Assert.Null(only.SuggestedStart);
    }

    [Fact]
    public void ReleasesWithinTheLookahead_AppearAsInformational_NeverAsChores()
    {
        var releases = new[]
        {
            new Release { Id = 1, Title = "Some Game", Category = ReleaseCategory.Media, ReleaseDate = Today.AddDays(3) },
            new Release { Id = 2, Title = "Far off", Category = ReleaseCategory.Media, ReleaseDate = Today.AddDays(30) },
            new Release { Id = 3, Title = "Dismissed", Category = ReleaseCategory.Product, ReleaseDate = Today.AddDays(1), IsDismissed = true },
            new Release { Id = 4, Title = "Yesterday", Category = ReleaseCategory.Product, ReleaseDate = Today.AddDays(-1) },
        };

        var suggestions = SuggestionEngine.Build(Input(releases: releases));

        var only = Assert.Single(suggestions);
        Assert.Equal("Some Game", only.Title);
        Assert.Equal(SuggestionKind.Release, only.Kind);
        Assert.Equal(SuggestionUrgency.Informational, only.Urgency);
        Assert.Null(only.SuggestedStart);
    }

    [Fact]
    public void MilestonesWithinTheLookahead_AppearAsInformational_AndSkipDoneOnes()
    {
        var milestones = new[]
        {
            new GoalMilestone { Id = 1, GoalId = 1, Title = "Phase 1", DueDate = Today.AddDays(2) },
            new GoalMilestone { Id = 2, GoalId = 1, Title = "Already done", DueDate = Today.AddDays(2), IsDone = true },
            new GoalMilestone { Id = 3, GoalId = 1, Title = "No date" },
            new GoalMilestone { Id = 4, GoalId = 1, Title = "Far off", DueDate = Today.AddDays(30) },
        };

        var suggestions = SuggestionEngine.Build(Input(milestones: milestones));

        var only = Assert.Single(suggestions);
        Assert.Equal("Phase 1", only.Title);
        Assert.Equal(SuggestionKind.Milestone, only.Kind);
    }

    [Fact]
    public void Bedtime_IsAlwaysLast()
    {
        var suggestions = SuggestionEngine.Build(Input(
            routines: [Routine(1, "Overdue chore")],
            states: [Due(1, overdue: 9)],
            releases: [new Release { Id = 1, Title = "Launch", Category = ReleaseCategory.Product, ReleaseDate = Today }],
            bedtime: new DateTime(2026, 7, 6, 23, 0, 0)));

        Assert.Equal(SuggestionKind.Bedtime, suggestions[^1].Kind);
        Assert.Equal(new TimeSpan(23, 0, 0), suggestions[^1].SuggestedStart);
        Assert.Equal(3, suggestions.Count);
    }

    [Fact]
    public void NoBedtime_MeansNoBedtimeSuggestion()
    {
        var suggestions = SuggestionEngine.Build(Input(bedtime: null));

        Assert.Empty(suggestions);
    }

    [Fact]
    public void DueStateWithoutAMatchingRoutine_IsSkippedRatherThanThrowing()
    {
        var suggestions = SuggestionEngine.Build(Input(routines: [], states: [Due(42)]));

        Assert.Empty(suggestions);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `SuggestionEngine`, `SuggestionInput`, `Suggestion`, `SuggestionKind`, `SuggestionUrgency`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Suggestions/Suggestion.cs`:

```csharp
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;

namespace AaronOS.Modules.Schedule.Suggestions;

public enum SuggestionUrgency { Informational, Due, Overdue }

public enum SuggestionKind { Routine, Release, Milestone, Bedtime }

/// <param name="SuggestedStart">Where in the day this fits, or null when nothing today fits it.</param>
/// <param name="SourceId">The originating entity's Id, so the UI can act on it (mark a routine
/// done, open a release). Null for the bedtime entry, which has no row behind it.</param>
public sealed record Suggestion(
    SuggestionKind Kind,
    string Title,
    string Reason,
    SuggestionUrgency Urgency,
    TimeSpan? SuggestedStart,
    int? EstimatedMinutes,
    int? SourceId);

/// <summary>Everything the engine needs, gathered by the caller. A record rather than eight
/// parameters so adding an input later doesn't break every call site.</summary>
public sealed record SuggestionInput(
    DateOnly Today,
    AgendaDay TodayAgenda,
    IReadOnlyList<Routine> Routines,
    IReadOnlyList<RoutineDueState> DueStates,
    IReadOnlyList<Release> Releases,
    IReadOnlyList<GoalMilestone> Milestones,
    DateTime? RecommendedBedtime,
    int LookaheadDays = 7);
```

`src/AaronOS.Modules.Schedule/Suggestions/SuggestionEngine.cs`:

```csharp
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;

namespace AaronOS.Modules.Schedule.Suggestions;

/// <summary>
/// The single place anything gets ranked. Pure, so the ordering rules are directly testable
/// (see SuggestionEngineTests) rather than being emergent behaviour of a ViewModel.
///
/// Ranking, in order:
///   1. Overdue routines before due ones, most overdue first.
///   2. At equal urgency, a routine that fits an actual free gap before one that doesn't.
///   3. A routine whose preferred time falls inside a gap is placed there rather than at the
///      first gap that merely fits.
///   4. Releases and milestones inside the lookahead window are informational, never chores.
///   5. Tonight's bedtime is always last.
/// </summary>
public static class SuggestionEngine
{
    public static IReadOnlyList<Suggestion> Build(SuggestionInput input)
    {
        var routineSuggestions = BuildRoutineSuggestions(input);
        var informational = BuildInformationalSuggestions(input);

        var ordered = routineSuggestions
            .OrderByDescending(s => s.Urgency)                     // Overdue > Due
            .ThenByDescending(s => s.OverdueByDays)
            .ThenByDescending(s => s.Suggestion.SuggestedStart.HasValue) // fits today first
            .ThenBy(s => s.Suggestion.SuggestedStart ?? TimeSpan.MaxValue)
            .ThenBy(s => s.Suggestion.Title, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Suggestion)
            .Concat(informational)
            .ToList();

        if (input.RecommendedBedtime is { } bedtime)
        {
            ordered.Add(new Suggestion(
                SuggestionKind.Bedtime,
                $"Be in bed by {bedtime:h:mm tt}",
                "Worked back from tomorrow's first commitment and your target",
                SuggestionUrgency.Informational,
                bedtime.TimeOfDay,
                EstimatedMinutes: null,
                SourceId: null));
        }

        return ordered;
    }

    /// <summary>A suggestion plus the sort keys that don't belong on the public record.</summary>
    private sealed record RankedRoutine(Suggestion Suggestion, SuggestionUrgency Urgency, int OverdueByDays);

    private static List<RankedRoutine> BuildRoutineSuggestions(SuggestionInput input)
    {
        var byId = input.Routines.ToDictionary(r => r.Id);
        var results = new List<RankedRoutine>();

        foreach (var due in input.DueStates)
        {
            if (!due.IsDue) continue;

            // A due state whose routine isn't in the list (deactivated between queries) is
            // skipped rather than throwing — the caller reads two tables, not one transaction.
            if (!byId.TryGetValue(due.RoutineId, out var routine)) continue;

            var start = PlaceInGap(routine, input.TodayAgenda.FreeGaps);
            var urgency = due.IsOverdue ? SuggestionUrgency.Overdue : SuggestionUrgency.Due;

            var reason = due.IsOverdue
                ? $"Overdue by {due.OverdueByDays} day{(due.OverdueByDays == 1 ? "" : "s")}"
                : "Due today";

            results.Add(new RankedRoutine(
                new Suggestion(
                    SuggestionKind.Routine,
                    routine.Name,
                    reason,
                    urgency,
                    start,
                    routine.EstimatedMinutes,
                    routine.Id),
                urgency,
                due.OverdueByDays));
        }

        return results;
    }

    /// <summary>
    /// Prefers the gap containing the routine's preferred time, then the first gap it fits in.
    /// Null means nothing today can hold it — which is information, not a failure: the suggestion
    /// still appears, just without a time.
    /// </summary>
    private static TimeSpan? PlaceInGap(Routine routine, IReadOnlyList<FreeGap> gaps)
    {
        var needed = routine.EstimatedMinutes ?? 0;

        if (routine.PreferredTimeOfDay is { } preferred)
        {
            var containing = gaps.FirstOrDefault(g =>
                preferred >= g.Start && preferred < g.End && g.End - preferred >= TimeSpan.FromMinutes(needed));
            if (containing is not null) return preferred;
        }

        return gaps.FirstOrDefault(g => g.Minutes >= needed)?.Start;
    }

    private static List<Suggestion> BuildInformationalSuggestions(SuggestionInput input)
    {
        var horizon = input.Today.AddDays(input.LookaheadDays);
        var items = new List<(DateOnly When, Suggestion Suggestion)>();

        foreach (var release in input.Releases)
        {
            if (release.IsDismissed) continue;
            if (release.ReleaseDate < input.Today || release.ReleaseDate > horizon) continue;

            items.Add((release.ReleaseDate, new Suggestion(
                SuggestionKind.Release,
                release.Title,
                release.IsDateEstimated
                    ? $"Estimated for {release.ReleaseDate:ddd MMM d}"
                    : $"Out {release.ReleaseDate:ddd MMM d}",
                SuggestionUrgency.Informational,
                SuggestedStart: null,
                EstimatedMinutes: null,
                release.Id)));
        }

        foreach (var milestone in input.Milestones)
        {
            if (milestone.IsDone) continue;
            if (milestone.DueDate is not { } dueDate) continue;
            if (dueDate < input.Today || dueDate > horizon) continue;

            items.Add((dueDate, new Suggestion(
                SuggestionKind.Milestone,
                milestone.Title,
                $"Milestone due {dueDate:ddd MMM d}",
                SuggestionUrgency.Informational,
                SuggestedStart: null,
                EstimatedMinutes: null,
                milestone.Id)));
        }

        return items.OrderBy(i => i.When).Select(i => i.Suggestion).ToList();
    }
}
```

`OrderByDescending(s => s.Urgency)` relies on the enum being declared `Informational, Due, Overdue` so higher urgency has a higher value. That ordering is why the enum is declared in that sequence — do not reorder its members.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed!` with 0 failures and 12 more passing tests than before this task.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Suggestions src/AaronOS.Modules.Schedule.Tests/SuggestionEngineTests.cs
git commit -m "Add SuggestionEngine ranking routines, releases, milestones, and bedtime"
```

---

## Task 6: Goals and Releases page

**Files:**
- Create: `src/AaronOS.Modules.Schedule/ViewModels/GoalsViewModel.cs`
- Create: `src/AaronOS.Modules.Schedule/Views/GoalsPage.xaml`
- Create: `src/AaronOS.Modules.Schedule/Views/GoalsPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `Goal`, `GoalMilestone`, `Release`.
- Produces: `GoalsViewModel` with `ObservableCollection<Goal> Goals`, `ObservableCollection<GoalMilestone> Milestones`, `ObservableCollection<Release> Releases`, `LoadCommand`, `AddGoalCommand`, `DeleteGoalCommand`, `SelectGoalCommand`, `AddMilestoneCommand`, `ToggleMilestoneCommand`, `AddReleaseCommand`, `DismissReleaseCommand`.

- [ ] **Step 1: Write the ViewModel**

`src/AaronOS.Modules.Schedule/ViewModels/GoalsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class GoalsViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<Goal> Goals { get; } = [];
    public ObservableCollection<GoalMilestone> Milestones { get; } = [];
    public ObservableCollection<Release> Releases { get; } = [];

    public IReadOnlyList<ReleaseCategory> ReleaseCategories { get; } = Enum.GetValues<ReleaseCategory>();

    [ObservableProperty]
    private Goal? _selectedGoal;

    [ObservableProperty]
    private string _newGoalTitle = "";

    [ObservableProperty]
    private DateTime? _newGoalTargetDate;

    [ObservableProperty]
    private string _newMilestoneTitle = "";

    [ObservableProperty]
    private DateTime? _newMilestoneDueDate;

    [ObservableProperty]
    private string _newReleaseTitle = "";

    [ObservableProperty]
    private ReleaseCategory _newReleaseCategory = ReleaseCategory.Media;

    [ObservableProperty]
    private DateTime? _newReleaseDate = DateTime.Today;

    [ObservableProperty]
    private bool _newReleaseDateEstimated;

    [ObservableProperty]
    private string? _validationMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var goals = await db.Set<Goal>()
                .Where(g => g.Status == GoalStatus.Active || g.Status == GoalStatus.Paused)
                .ToListAsync();

            Goals.Clear();
            foreach (var goal in goals.OrderBy(g => g.TargetDate ?? DateOnly.MaxValue).ThenBy(g => g.Title))
            {
                Goals.Add(goal);
            }

            SelectedGoal = Goals.FirstOrDefault(g => g.Id == SelectedGoal?.Id) ?? Goals.FirstOrDefault();
            await LoadMilestonesAsync(db);

            var releases = await db.Set<Release>().Where(r => !r.IsDismissed).ToListAsync();
            Releases.Clear();
            foreach (var release in releases.OrderBy(r => r.ReleaseDate)) Releases.Add(release);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadMilestonesAsync(AaronOsDbContext db)
    {
        Milestones.Clear();
        if (SelectedGoal is null) return;

        var goalId = SelectedGoal.Id;
        var milestones = await db.Set<GoalMilestone>().Where(m => m.GoalId == goalId).ToListAsync();
        foreach (var milestone in milestones.OrderBy(m => m.SortOrder)) Milestones.Add(milestone);
    }

    [RelayCommand]
    private async Task SelectGoalAsync(Goal goal)
    {
        SelectedGoal = goal;
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await LoadMilestonesAsync(db);
    }

    [RelayCommand]
    private async Task AddGoalAsync()
    {
        ValidationMessage = null;
        if (string.IsNullOrWhiteSpace(NewGoalTitle))
        {
            ValidationMessage = "Give the goal a title.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new Goal
        {
            Title = NewGoalTitle.Trim(),
            TargetDate = NewGoalTargetDate is { } d ? DateOnly.FromDateTime(d) : null,
            Status = GoalStatus.Active,
            CreatedAt = DateTime.Now,
        });
        await db.SaveChangesAsync();

        NewGoalTitle = "";
        NewGoalTargetDate = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteGoalAsync(Goal goal)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(await db.Set<Goal>().SingleAsync(g => g.Id == goal.Id));
        await db.SaveChangesAsync();

        if (SelectedGoal?.Id == goal.Id) SelectedGoal = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddMilestoneAsync()
    {
        ValidationMessage = null;
        if (SelectedGoal is null)
        {
            ValidationMessage = "Pick a goal first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewMilestoneTitle))
        {
            ValidationMessage = "Give the milestone a title.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new GoalMilestone
        {
            GoalId = SelectedGoal.Id,
            Title = NewMilestoneTitle.Trim(),
            DueDate = NewMilestoneDueDate is { } d ? DateOnly.FromDateTime(d) : null,
            SortOrder = Milestones.Count,
        });
        await db.SaveChangesAsync();

        NewMilestoneTitle = "";
        NewMilestoneDueDate = null;
        await LoadAsync();
    }

    /// <summary>Flips a milestone's done flag and recomputes the goal's progress from the
    /// proportion completed, so progress can't drift out of step with the checklist.</summary>
    [RelayCommand]
    private async Task ToggleMilestoneAsync(GoalMilestone milestone)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var tracked = await db.Set<GoalMilestone>().SingleAsync(m => m.Id == milestone.Id);
        tracked.IsDone = !tracked.IsDone;

        var siblings = await db.Set<GoalMilestone>().Where(m => m.GoalId == tracked.GoalId).ToListAsync();
        var goal = await db.Set<Goal>().SingleAsync(g => g.Id == tracked.GoalId);
        goal.ProgressPercent = siblings.Count == 0
            ? 0
            : (int)Math.Round(100.0 * siblings.Count(m => m.IsDone) / siblings.Count);

        if (goal.ProgressPercent == 100 && goal.Status == GoalStatus.Active)
        {
            goal.Status = GoalStatus.Done;
            goal.CompletedAt = DateTime.Now;
        }
        else if (goal.ProgressPercent < 100 && goal.Status == GoalStatus.Done)
        {
            goal.Status = GoalStatus.Active;
            goal.CompletedAt = null;
        }

        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddReleaseAsync()
    {
        ValidationMessage = null;
        if (string.IsNullOrWhiteSpace(NewReleaseTitle))
        {
            ValidationMessage = "Give the release a title.";
            return;
        }
        if (NewReleaseDate is not { } date)
        {
            ValidationMessage = "Pick a release date.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new Release
        {
            Title = NewReleaseTitle.Trim(),
            Category = NewReleaseCategory,
            ReleaseDate = DateOnly.FromDateTime(date),
            IsDateEstimated = NewReleaseDateEstimated,
        });
        await db.SaveChangesAsync();

        NewReleaseTitle = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DismissReleaseAsync(Release release)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var tracked = await db.Set<Release>().SingleAsync(r => r.Id == release.Id);
        tracked.IsDismissed = true; // dismissed, not deleted — the record survives
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}
```

Register in `ScheduleModule.RegisterServices`:

```csharp
        services.AddTransient<GoalsViewModel>();
```

- [ ] **Step 2: Write the page**

`src/AaronOS.Modules.Schedule/Views/GoalsPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Schedule.Views.GoalsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    mc:Ignorable="d">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16">
            <ui:TextBlock Text="Goals" FontTypography="Subtitle" Margin="0,0,0,12" />

            <ItemsControl ItemsSource="{Binding Goals}" Margin="0,0,0,12">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <ui:Card Margin="0,0,0,8">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <ui:TextBlock Text="{Binding Title}" FontTypography="BodyStrong" />
                                    <TextBlock>
                                        <Run Text="{Binding ProgressPercent, Mode=OneWay}" /><Run Text="% · " />
                                        <Run Text="{Binding TargetDate, StringFormat='target {0:MMM d, yyyy}', TargetNullValue='no target date', Mode=OneWay}" />
                                    </TextBlock>
                                </StackPanel>
                                <ui:Button Grid.Column="1" Content="Milestones" Click="SelectGoal_Click" Margin="0,0,8,0" />
                                <ui:Button Grid.Column="2" Content="Delete" Click="DeleteGoal_Click" />
                            </Grid>
                        </ui:Card>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <ui:Card Margin="0,0,0,12">
                <StackPanel>
                    <ui:TextBlock Text="Add a goal" FontTypography="BodyStrong" Margin="0,0,0,8" />
                    <ui:TextBox PlaceholderText="Title" Text="{Binding NewGoalTitle, Mode=TwoWay}" Margin="0,0,0,8" />
                    <DatePicker SelectedDate="{Binding NewGoalTargetDate, Mode=TwoWay}" Margin="0,0,0,8" />
                    <ui:Button Content="Add goal" Appearance="Primary" Command="{Binding AddGoalCommand}" HorizontalAlignment="Left" />
                </StackPanel>
            </ui:Card>

            <ui:Card Margin="0,0,0,12">
                <StackPanel>
                    <ui:TextBlock FontTypography="BodyStrong" Margin="0,0,0,8"
                                  Text="{Binding SelectedGoal.Title, StringFormat='Milestones — {0}', TargetNullValue='Milestones'}" />
                    <ItemsControl ItemsSource="{Binding Milestones}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" VerticalAlignment="Center">
                                        <Run Text="{Binding Title, Mode=OneWay}" />
                                        <Run Text="{Binding DueDate, StringFormat=' · due {0:MMM d}', TargetNullValue='', Mode=OneWay}" />
                                    </TextBlock>
                                    <ui:Button Grid.Column="1" Content="{Binding IsDone}" Click="ToggleMilestone_Click" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                        <ui:TextBox PlaceholderText="Milestone title" Text="{Binding NewMilestoneTitle, Mode=TwoWay}" Width="240" Margin="0,0,8,0" />
                        <DatePicker SelectedDate="{Binding NewMilestoneDueDate, Mode=TwoWay}" Margin="0,0,8,0" />
                        <ui:Button Content="Add" Command="{Binding AddMilestoneCommand}" />
                    </StackPanel>
                </StackPanel>
            </ui:Card>

            <ui:TextBlock Text="Releases" FontTypography="Subtitle" Margin="0,8,0,12" />

            <ItemsControl ItemsSource="{Binding Releases}" Margin="0,0,0,12">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <ui:Card Margin="0,0,0,8">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <ui:TextBlock Text="{Binding Title}" FontTypography="BodyStrong" />
                                    <TextBlock>
                                        <Run Text="{Binding Category, Mode=OneWay}" />
                                        <Run Text=" · " />
                                        <Run Text="{Binding ReleaseDate, StringFormat='{}{0:ddd MMM d, yyyy}', Mode=OneWay}" />
                                    </TextBlock>
                                </StackPanel>
                                <ui:Button Grid.Column="1" Content="Dismiss" Click="DismissRelease_Click" />
                            </Grid>
                        </ui:Card>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <ui:Card>
                <StackPanel>
                    <ui:TextBlock Text="Add a release" FontTypography="BodyStrong" Margin="0,0,0,8" />
                    <ui:TextBox PlaceholderText="Title" Text="{Binding NewReleaseTitle, Mode=TwoWay}" Margin="0,0,0,8" />
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                        <ComboBox ItemsSource="{Binding ReleaseCategories}" SelectedItem="{Binding NewReleaseCategory, Mode=TwoWay}" Width="140" Margin="0,0,8,0" />
                        <DatePicker SelectedDate="{Binding NewReleaseDate, Mode=TwoWay}" Margin="0,0,8,0" />
                        <CheckBox Content="Date is estimated" IsChecked="{Binding NewReleaseDateEstimated, Mode=TwoWay}" VerticalAlignment="Center" />
                    </StackPanel>
                    <ui:TextBlock Text="{Binding ValidationMessage}" Foreground="{DynamicResource SystemFillColorCriticalBrush}" Margin="0,0,0,8" />
                    <ui:Button Content="Add release" Appearance="Primary" Command="{Binding AddReleaseCommand}" HorizontalAlignment="Left" />
                </StackPanel>
            </ui:Card>
        </StackPanel>
    </ScrollViewer>
</Page>
```

`src/AaronOS.Modules.Schedule/Views/GoalsPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class GoalsPage : Page
{
    public GoalsViewModel ViewModel { get; }

    public GoalsPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<GoalsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void SelectGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Goal goal })
        {
            _ = ViewModel.SelectGoalCommand.ExecuteAsync(goal);
        }
    }

    private void DeleteGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Goal goal })
        {
            _ = ViewModel.DeleteGoalCommand.ExecuteAsync(goal);
        }
    }

    private void ToggleMilestone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GoalMilestone milestone })
        {
            _ = ViewModel.ToggleMilestoneCommand.ExecuteAsync(milestone);
        }
    }

    private void DismissRelease_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Release release })
        {
            _ = ViewModel.DismissReleaseCommand.ExecuteAsync(release);
        }
    }
}
```

Add the shell button. In `ScheduleShellPage.xaml`:

```xml
            <ui:Button Content="Goals" Click="Goals_Click" />
```

and in `ScheduleShellPage.xaml.cs`:

```csharp
    private void Goals_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new GoalsPage());
```

Give the preceding `Sleep` button `Margin="0,0,8,0"` if it does not already have it.

- [ ] **Step 3: Verify in the running app**

Run: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`

Confirm:

1. Schedule → Goals loads with empty lists.
2. Add a goal `Ship the Schedule module` with a target date. It appears reading `0% · target <date>`.
3. Click **Milestones** on it, then add `Phase 1` and `Phase 2`. Both appear under the milestone card.
4. Toggle `Phase 1`. The goal's percentage becomes `50%`.
5. Toggle `Phase 2`. The percentage becomes `100%` and the goal disappears from the list — it moved to `Done`, which the list filters out. Toggle nothing further; this confirms the status transition.
6. Add a release `Some Game`, category `Media`, a date a few days out. It appears in the Releases list.
7. Click **Dismiss** on it; it disappears. Navigate away and back to confirm it stays gone.
8. Add a milestone without selecting a goal first (after deleting all goals): "Pick a goal first." appears.

Close the app.

- [ ] **Step 4: Run the tests to confirm nothing regressed**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed!` with 0 failures and the same test count as Step 2 — this task adds no tests.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add Goals and Releases page with milestone progress rollup"
```

---

## Task 7: Wire suggestions into the Today page

**Files:**
- Modify: `src/AaronOS.Modules.Schedule/ViewModels/TodayViewModel.cs`
- Modify: `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml`
- Modify: `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml.cs`

**Interfaces:**
- Consumes: `SuggestionEngine.Build`, `SuggestionInput`, `Suggestion`, `RoutineScheduler.EvaluateAll`, `SleepPlanner.RecommendedBedtime`.
- Produces: `TodayViewModel.Suggestions` (`ObservableCollection<Suggestion>`) and `CompleteRoutineCommand`. Plan 3's notification tick reuses the same gather-then-build sequence.

- [ ] **Step 1: Extend the ViewModel**

Replace `TodayViewModel`'s `LoadAsync` and add the collection and command. The full file after the change:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;
using AaronOS.Modules.Schedule.Sleep;
using AaronOS.Modules.Schedule.Suggestions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class TodayViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<AgendaEntry> Entries { get; } = [];
    public ObservableCollection<FreeGap> FreeGaps { get; } = [];
    public ObservableCollection<Suggestion> Suggestions { get; } = [];

    [ObservableProperty]
    private string _dateHeading = "";

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var tomorrowDate = today.AddDays(1);
            DateHeading = today.ToString("dddd, MMMM d");

            await using var db = await dbContextFactory.CreateDbContextAsync();

            // Materialise everything before handing it to the pure services: they operate on
            // plain lists, and DateOnly comparisons plus the computed properties on these
            // entities are not translatable to SQL.
            var blocks = await db.Set<ScheduleBlock>().Where(b => b.IsActive).ToListAsync();
            var exceptions = await db.Set<ScheduleException>()
                .Where(e => e.Date >= today.AddDays(-1) && e.Date <= tomorrowDate)
                .ToListAsync();

            // Two days, so tomorrow's first commitment is available for the bedtime figure.
            var agenda = AgendaBuilder.Build(today, tomorrowDate, blocks, exceptions, []);
            var todayAgenda = agenda[0];
            var tomorrowAgenda = agenda[1];

            var routines = await db.Set<Routine>().Where(r => r.IsActive).ToListAsync();
            var completions = await db.Set<RoutineCompletion>().ToListAsync();
            var dueStates = RoutineScheduler.EvaluateAll(routines, completions, today);

            var horizon = today.AddDays(7);
            var releases = await db.Set<Release>()
                .Where(r => !r.IsDismissed && r.ReleaseDate >= today && r.ReleaseDate <= horizon)
                .ToListAsync();
            var milestones = await db.Set<GoalMilestone>()
                .Where(m => !m.IsDone && m.DueDate != null && m.DueDate >= today && m.DueDate <= horizon)
                .ToListAsync();

            var settings = await db.Set<SleepSettings>().FirstOrDefaultAsync() ?? SleepSettings.Default();
            var bedtime = SleepPlanner.RecommendedBedtime(today, tomorrowAgenda, settings);

            Entries.Clear();
            foreach (var entry in todayAgenda.Entries) Entries.Add(entry);

            FreeGaps.Clear();
            foreach (var gap in todayAgenda.FreeGaps) FreeGaps.Add(gap);

            var suggestions = SuggestionEngine.Build(new SuggestionInput(
                today, todayAgenda, routines, dueStates, releases, milestones, bedtime));

            Suggestions.Clear();
            foreach (var suggestion in suggestions) Suggestions.Add(suggestion);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Logs a completion straight from the Today panel, so an overdue chore can be
    /// cleared without navigating to the Routines page.</summary>
    [RelayCommand]
    private async Task CompleteRoutineAsync(Suggestion suggestion)
    {
        if (suggestion.Kind != SuggestionKind.Routine || suggestion.SourceId is not { } routineId) return;

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new RoutineCompletion { RoutineId = routineId, CompletedAt = DateTime.Now });
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}
```

- [ ] **Step 2: Add the suggestions card to the page**

In `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml`, insert this card immediately after the `DateHeading` text block and before the "Schedule" card — suggestions are the point of the page and belong at the top:

```xml
            <ui:Card Margin="0,0,0,12">
                <StackPanel>
                    <ui:TextBlock Text="Suggestions" FontTypography="BodyStrong" Margin="0,0,0,8" />
                    <ItemsControl ItemsSource="{Binding Suggestions}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,3">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <StackPanel Grid.Column="0">
                                        <TextBlock>
                                            <Run Text="{Binding Title, Mode=OneWay}" FontWeight="SemiBold" />
                                            <Run Text="{Binding SuggestedStart, StringFormat=' · {0:hh\\:mm}', TargetNullValue='', Mode=OneWay}" />
                                        </TextBlock>
                                        <TextBlock Text="{Binding Reason}" Opacity="0.75" />
                                    </StackPanel>
                                    <ui:Button Grid.Column="1" Content="Done" Click="CompleteRoutine_Click" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ui:Card>
```

The **Done** button appears on every row including informational ones; `CompleteRoutineAsync` returns immediately for anything that is not a routine, so clicking it on a release or the bedtime row does nothing. That is deliberately simpler than a visibility converter — the alternative is XAML machinery for a harmless no-op.

- [ ] **Step 3: Add the click handler**

In `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml.cs`, add the using and the handler:

```csharp
using AaronOS.Modules.Schedule.Suggestions;
```

```csharp
    private void CompleteRoutine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Suggestion suggestion })
        {
            _ = ViewModel.CompleteRoutineCommand.ExecuteAsync(suggestion);
        }
    }
```

Add `using System.Windows;` if it is not already present.

- [ ] **Step 4: Verify in the running app**

Run: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`

With the blocks, routines, sleep target, and a near-term release from earlier tasks in place, confirm:

1. Today shows a Suggestions card above the schedule.
2. A routine that is due appears with a suggested time inside a real free gap — with work 08:00–17:00 and sleep 23:00–07:00, expect `07:00` or `17:00`, never a time inside the work block.
3. An overdue routine sorts above a merely-due one. To create one: on the Routines page add a routine with interval `1`, then use the Sleep page's date picker as a reminder of the date and simply wait, or temporarily set the interval to `1` and log no completion — a never-completed routine is "due today", and one whose last completion is older than its interval is overdue.
4. The near-term release appears as an informational row with no time.
5. The bedtime row is last, reading "Be in bed by …".
6. Click **Done** on the routine row; the routine disappears from suggestions (no longer due) and its completion shows on the Routines page.
7. Click **Done** on the bedtime row; nothing happens and no error appears.

Close the app.

- [ ] **Step 5: Run the tests and commit**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed!` with 0 failures and the same test count as Step 2 — this task adds no tests.

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Surface ranked suggestions on the Today page"
```

---

## Definition of done for Plan 2

- `dotnet build AaronOS.slnx --nologo` succeeds.
- `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo` reports 0 failing tests, and 55 passing if the plans ran in their original order (the absolute number shifts with ordering and with any fix round — the per-task deltas above are what to check).
- Sleep, Goals, and Today all load against the real database; Today ranks routines, releases, milestones, and bedtime in one list.
- Sleep settings, goals, milestones, and releases persist across an app restart.
- No `SleepLog` entity exists, nothing computes sleep debt, and nothing in this module reads Medical's `MoodEntry` or `SleepNight`.
- No external network call exists anywhere in the module.

## Deferred to later plans

Notifications (Plan 3), external calendars (Plan 4), Gmail extraction (Plan 5). Also: editing an existing goal's title or target date (only add and delete exist), and reordering milestones (`SortOrder` is set on insert and never changed).

**Sleep debt**, deliberately unscheduled. It needs actual hours slept, which the Medical module owns
on `MoodEntry` and `SleepNight`, and `docs/MODULE_GUIDELINES.md` forbids reading another module's
entities. Doing it properly means promoting the nightly-sleep shape into `AaronOS.Core` and pointing
both modules at it — worth a spec of its own, not a task bolted onto this plan.

The weekday-pinned routine editor that Plan 1 left open has since been built on the Routines page,
so `Routine.PreferredDaysOfWeek` now has a writer and a fixed trash night can be entered. Nothing in
this plan needs to add it.

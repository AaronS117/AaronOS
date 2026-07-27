# Schedule Module — Plan 2: Sleep, Goals, Releases, and Suggestions

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add manual sleep logging with a derived bedtime recommendation and 14-day sleep debt, generic goals with milestones, dated release tracking, and a ranked suggestion list surfaced on the Today page.

**Architecture:** Same shape as Plan 1 — two more pure static services (`SleepPlanner`, `SuggestionEngine`) that take materialised lists and return values, plus four entities and two pages. `SuggestionEngine` is the one place that combines agenda gaps, routine due states, sleep, releases, and milestones; nothing else ranks anything.

**Tech Stack:** Unchanged from Plan 1 — .NET 8 `net8.0-windows`, WPF, WPF-UI 4.3.0, EF Core 8 + SQLite, CommunityToolkit.Mvvm, xUnit 2.5.3.

**Spec:** `docs/superpowers/specs/2026-07-27-schedule-module-design.md` — this plan covers phases 3, 4, and 5.

**Prerequisite:** Plan 1 complete — `AaronOS.Modules.Schedule` exists and is registered, `AgendaBuilder.Build` and `RoutineScheduler.EvaluateAll` are in place, and 32 tests pass.

## Global Constraints

Every task's requirements implicitly include this section. These repeat Plan 1's constraints because a task's implementer may not have read Plan 1.

- Target framework `net8.0-windows`; `UseWPF` true; `LangVersion` `13.0`; `Nullable` `enable`; `ImplicitUsings` `enable`.
- **Never use the partial-property `[ObservableProperty]` form.** The generator does not run in this environment. Always write `[ObservableProperty] private bool _x;` and ignore `MVVMTK0045`.
- ViewModels transient, services singleton.
- Pages have a public parameterless constructor, resolve their ViewModel via `AaronOS.Core.AppServices.Provider.GetRequiredService<T>()`, set `DataContext` explicitly, then `InitializeComponent()`, then hook `Loaded`.
- `Frame.Navigate` takes an instance: `ContentFrame.Navigate(new SleepPage())`.
- Never reference another module's entities. `Goal` here is unrelated to `BodyMeasurements` goals and must not read them.
- WPF `Grid`/`StackPanel` have no `Spacing`/`Padding` — use explicit `Margin` on children.
- `ui:NumberBox.Value` is a `double`; cleared reports `double.NaN`. Use `NaN` as the not-entered sentinel, convert at save time, no value converter.
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
- **Sleep advice is arithmetic, not diagnosis.** `SleepPlanner` computes a bedtime from the next day's commitments and a shortfall against a user-set target. It must not infer, recommend, or hard-code a target beyond the 8.0-hour default. No UI copy may imply a clinical recommendation.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/AaronOS.Modules.Schedule/Data/SleepLog.cs` (+`Configuration`) | One night's self-reported sleep |
| `src/AaronOS.Modules.Schedule/Data/SleepSettings.cs` (+`Configuration`) | Single-row target and lead times |
| `src/AaronOS.Modules.Schedule/Data/Goal.cs` (+`Configuration`) | Generic dated goal |
| `src/AaronOS.Modules.Schedule/Data/GoalMilestone.cs` (+`Configuration`) | A goal's checklist item |
| `src/AaronOS.Modules.Schedule/Data/Release.cs` (+`Configuration`) | Media or product release date |
| `src/AaronOS.Modules.Schedule/Sleep/SleepPlanner.cs` | Pure bedtime and debt computation |
| `src/AaronOS.Modules.Schedule/Sleep/SleepSummary.cs` | Result record for the sleep page |
| `src/AaronOS.Modules.Schedule/Suggestions/Suggestion.cs` | Result record, one ranked item |
| `src/AaronOS.Modules.Schedule/Suggestions/SuggestionEngine.cs` | Pure ranking across every input |
| `src/AaronOS.Modules.Schedule/ViewModels/SleepViewModel.cs` | Sleep page state |
| `src/AaronOS.Modules.Schedule/ViewModels/GoalsViewModel.cs` | Goals and releases page state |
| `src/AaronOS.Modules.Schedule/Views/SleepPage.xaml(.cs)` | Log sleep, edit target, show debt |
| `src/AaronOS.Modules.Schedule/Views/GoalsPage.xaml(.cs)` | Goals, milestones, releases |
| `src/AaronOS.Modules.Schedule.Tests/SleepPlannerTests.cs` | Bedtime and debt tests |
| `src/AaronOS.Modules.Schedule.Tests/SuggestionEngineTests.cs` | Ranking tests |

---

## Task 1: Sleep entities

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Data/SleepLog.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/SleepLogConfiguration.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/SleepSettings.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/SleepSettingsConfiguration.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs`

**Interfaces:**
- Produces: `SleepLog` with `Id`, `NightOf`, `BedTime`, `WakeTime`, `Quality`, `Note`, and computed `decimal Hours`. `SleepSettings` with `Id`, `TargetHours`, `SleepOnsetMinutes`, `MorningRoutineMinutes`, `WindDownLeadMinutes`, and a `static SleepSettings Default()`.

- [ ] **Step 1: Write the failing test**

Add to the existing `ScheduleSchemaTests` class (it already has `CreateContext()` and `Dispose()` from Plan 1):

```csharp
    [Fact]
    public async Task SleepLog_EnforcesOneRowPerNight()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.Add(new SleepLog
        {
            NightOf = new DateOnly(2026, 7, 6),
            BedTime = new DateTime(2026, 7, 6, 23, 15, 0),
            WakeTime = new DateTime(2026, 7, 7, 6, 45, 0),
            Quality = 4,
        });
        await db.SaveChangesAsync();

        var loaded = await db.Set<SleepLog>().SingleAsync();
        Assert.Equal(7.5m, loaded.Hours);

        // A second log for the same night must be rejected by the unique index rather than
        // silently producing two rows the debt calculation would double-count.
        db.Add(new SleepLog
        {
            NightOf = new DateOnly(2026, 7, 6),
            BedTime = new DateTime(2026, 7, 6, 22, 0, 0),
            WakeTime = new DateTime(2026, 7, 7, 6, 0, 0),
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void SleepSettings_DefaultsMatchTheSpec()
    {
        var settings = SleepSettings.Default();

        Assert.Equal(8.0m, settings.TargetHours);
        Assert.Equal(15, settings.SleepOnsetMinutes);
        Assert.Equal(45, settings.MorningRoutineMinutes);
        Assert.Equal(30, settings.WindDownLeadMinutes);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `SleepLog` and `SleepSettings`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Data/SleepLog.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// One night of self-reported sleep. <see cref="NightOf"/> is the date the sleep *started*, which
/// removes the perennial ambiguity of which calendar day a 1am bedtime belongs to.
///
/// Shaped so a wearable or phone importer can backfill these rows later without a schema change:
/// nothing here depends on the values having been typed by hand.
/// </summary>
public class SleepLog
{
    public int Id { get; set; }
    public DateOnly NightOf { get; set; }
    public DateTime BedTime { get; set; }
    public DateTime WakeTime { get; set; }

    /// <summary>Optional self-rating, 1 (poor) to 5 (good).</summary>
    public int? Quality { get; set; }

    public string? Note { get; set; }

    /// <summary>Rounded to two places so the debt sum doesn't accumulate float noise.</summary>
    public decimal Hours => Math.Round((decimal)(WakeTime - BedTime).TotalHours, 2);
}
```

`src/AaronOS.Modules.Schedule/Data/SleepLogConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class SleepLogConfiguration : IEntityTypeConfiguration<SleepLog>
{
    public void Configure(EntityTypeBuilder<SleepLog> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.NightOf).IsUnique();
        builder.Property(s => s.Note).HasMaxLength(500);
        builder.Ignore(s => s.Hours);
    }
}
```

`src/AaronOS.Modules.Schedule/Data/SleepSettings.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// Single-row settings table, following the UserProfile pattern but kept in this module because
/// nothing else needs it.
///
/// <see cref="TargetHours"/> is the user's own choice. Nothing in this module infers it, and
/// nothing should: computing a schedule and a shortfall against a stated target is arithmetic,
/// whereas determining how much sleep a person needs is not something this app can know.
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
Expected: `Passed! - Failed: 0, Passed: 34`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Data src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs
git commit -m "Add SleepLog and SleepSettings entities"
```

---

## Task 2: SleepPlanner

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Sleep/SleepSummary.cs`
- Create: `src/AaronOS.Modules.Schedule/Sleep/SleepPlanner.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/SleepPlannerTests.cs`

**Interfaces:**
- Consumes: `AgendaDay` and `AgendaEntry` from Plan 1, `SleepLog`, `SleepSettings`.
- Produces:
  - `record SleepSummary(DateTime? RecommendedBedtime, decimal DebtHours, decimal AverageHours, int NightsLogged)`
  - `static DateTime? SleepPlanner.RecommendedBedtime(DateOnly tonight, AgendaDay tomorrow, SleepSettings settings)`
  - `static decimal SleepPlanner.DebtHours(IReadOnlyList<SleepLog> logs, SleepSettings settings, DateOnly today, int windowNights = 14)`
  - `static SleepSummary SleepPlanner.Summarize(DateOnly today, AgendaDay tomorrow, IReadOnlyList<SleepLog> logs, SleepSettings settings)`

  Plan 3's notification tick calls `RecommendedBedtime` with exactly this signature.

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

    private static SleepLog Night(DateOnly nightOf, double hours) => new()
    {
        NightOf = nightOf,
        BedTime = nightOf.ToDateTime(new TimeOnly(23, 0)),
        WakeTime = nightOf.ToDateTime(new TimeOnly(23, 0)).AddHours(hours),
    };

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

    [Fact]
    public void Debt_SumsShortfallsAcrossTheWindow()
    {
        var today = new DateOnly(2026, 7, 10);
        var logs = new[]
        {
            Night(today.AddDays(-1), 6.0), // 2h short
            Night(today.AddDays(-2), 7.5), // 0.5h short
            Night(today.AddDays(-3), 8.0), // on target
        };

        Assert.Equal(2.5m, SleepPlanner.DebtHours(logs, Settings(), today));
    }

    [Fact]
    public void Debt_DoesNotLetALongNightOffsetAShortOne()
    {
        var today = new DateOnly(2026, 7, 10);
        var logs = new[]
        {
            Night(today.AddDays(-1), 5.0),  // 3h short
            Night(today.AddDays(-2), 11.0), // 3h surplus — must NOT cancel the shortfall
        };

        Assert.Equal(3.0m, SleepPlanner.DebtHours(logs, Settings(), today));
    }

    [Fact]
    public void Debt_ExcludesNightsOutsideTheWindow()
    {
        var today = new DateOnly(2026, 7, 20);
        var logs = new[]
        {
            Night(today.AddDays(-1), 4.0),  // 4h short, inside
            Night(today.AddDays(-14), 4.0), // 4h short, inside (boundary)
            Night(today.AddDays(-15), 0.0), // outside — 8h short, excluded
        };

        Assert.Equal(8.0m, SleepPlanner.DebtHours(logs, Settings(), today));
    }

    [Fact]
    public void Debt_IsZeroWithNoLogs()
    {
        Assert.Equal(0m, SleepPlanner.DebtHours([], Settings(), new DateOnly(2026, 7, 10)));
    }

    [Fact]
    public void Summarize_ReportsAverageAndCountOverTheWindow()
    {
        var today = new DateOnly(2026, 7, 10);
        var logs = new[] { Night(today.AddDays(-1), 6.0), Night(today.AddDays(-2), 8.0) };

        var summary = SleepPlanner.Summarize(today, Day(Work(8)), logs, Settings());

        Assert.Equal(2, summary.NightsLogged);
        Assert.Equal(7.0m, summary.AverageHours);
        Assert.Equal(2.0m, summary.DebtHours);
        Assert.NotNull(summary.RecommendedBedtime);
    }

    [Fact]
    public void Summarize_AverageIsZeroRatherThanDividingByZero()
    {
        var summary = SleepPlanner.Summarize(new DateOnly(2026, 7, 10), Day(), [], Settings());

        Assert.Equal(0, summary.NightsLogged);
        Assert.Equal(0m, summary.AverageHours);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `SleepPlanner` and `SleepSummary`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Sleep/SleepSummary.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Sleep;

/// <param name="RecommendedBedtime">Null when tomorrow has no commitment to work back from.</param>
/// <param name="DebtHours">Total shortfall against the target across the window, never negative.</param>
/// <param name="AverageHours">Mean logged hours across the window; zero when nothing is logged.</param>
public sealed record SleepSummary(
    DateTime? RecommendedBedtime,
    decimal DebtHours,
    decimal AverageHours,
    int NightsLogged);
```

`src/AaronOS.Modules.Schedule/Sleep/SleepPlanner.cs`:

```csharp
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Sleep;

/// <summary>
/// Arithmetic over the user's own numbers: when to go to bed given what tomorrow demands, and how
/// far behind a target they've fallen. Deliberately not in the business of deciding what the target
/// should be — that is <see cref="SleepSettings.TargetHours"/>, which the user sets.
///
/// Pure: takes dates as parameters and never reads the clock, so every rule is testable at any date.
/// </summary>
public static class SleepPlanner
{
    public const int DefaultWindowNights = 14;

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

    /// <summary>
    /// Sum of (target − actual) across the window, floored at zero per night. A long night does not
    /// offset a short one: eleven hours on Saturday does not undo five hours on Friday, and letting
    /// it cancel out would report "no debt" for a week that plainly had some.
    /// </summary>
    public static decimal DebtHours(
        IReadOnlyList<SleepLog> logs,
        SleepSettings settings,
        DateOnly today,
        int windowNights = DefaultWindowNights)
    {
        var earliest = today.AddDays(-windowNights);

        return logs
            .Where(l => l.NightOf >= earliest && l.NightOf < today)
            .Sum(l => Math.Max(0m, settings.TargetHours - l.Hours));
    }

    public static SleepSummary Summarize(
        DateOnly today,
        AgendaDay tomorrow,
        IReadOnlyList<SleepLog> logs,
        SleepSettings settings,
        int windowNights = DefaultWindowNights)
    {
        var earliest = today.AddDays(-windowNights);
        var inWindow = logs.Where(l => l.NightOf >= earliest && l.NightOf < today).ToList();

        var average = inWindow.Count == 0
            ? 0m
            : Math.Round(inWindow.Sum(l => l.Hours) / inWindow.Count, 2);

        return new SleepSummary(
            RecommendedBedtime(today, tomorrow, settings),
            DebtHours(logs, settings, today, windowNights),
            average,
            inWindow.Count);
    }
}
```

The window is `>= today - 14 && < today`: tonight is not yet logged, so including `today` would count an absent row as a full night's shortfall the moment the date rolled over.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 44`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Sleep src/AaronOS.Modules.Schedule.Tests/SleepPlannerTests.cs
git commit -m "Add SleepPlanner bedtime recommendation and sleep debt"
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
- Consumes: `SleepPlanner.Summarize`, `AgendaBuilder.Build`, `SleepLog`, `SleepSettings`.
- Produces: `SleepViewModel` with `ObservableCollection<SleepLog> RecentNights`, summary display properties, `LoadCommand`, `LogNightCommand`, `SaveSettingsCommand`, `DeleteNightCommand`.

- [ ] **Step 1: Write the ViewModel**

No unit test: the arithmetic is covered by 10 `SleepPlannerTests`. This is a database read plus a call into the planner.

`src/AaronOS.Modules.Schedule/ViewModels/SleepViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Sleep;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class SleepViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<SleepLog> RecentNights { get; } = [];

    [ObservableProperty]
    private string _bedtimeDisplay = "";

    [ObservableProperty]
    private string _debtDisplay = "";

    [ObservableProperty]
    private string _averageDisplay = "";

    // Log-a-night editor. Defaults to last night, which is what you're logging when you open this.
    [ObservableProperty]
    private DateTime? _newNightOf = DateTime.Today.AddDays(-1);

    [ObservableProperty]
    private string _newBedTimeText = "23:00";

    [ObservableProperty]
    private string _newWakeTimeText = "07:00";

    [ObservableProperty]
    private double _newQuality = double.NaN;

    // Settings editor.
    [ObservableProperty]
    private double _targetHours = 8;

    [ObservableProperty]
    private double _sleepOnsetMinutes = 15;

    [ObservableProperty]
    private double _morningRoutineMinutes = 45;

    [ObservableProperty]
    private double _windDownLeadMinutes = 30;

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
            var tomorrow = AgendaBuilder.Build(tomorrowDate, tomorrowDate, blocks, exceptions, []).Single();

            var logs = await db.Set<SleepLog>()
                .Where(l => l.NightOf >= today.AddDays(-SleepPlanner.DefaultWindowNights))
                .ToListAsync();

            var summary = SleepPlanner.Summarize(today, tomorrow, logs, settings);

            BedtimeDisplay = summary.RecommendedBedtime is { } bedtime
                ? $"Aim to be in bed by {bedtime:h:mm tt}"
                : "No commitments tomorrow — no bedtime to work back from.";
            DebtDisplay = $"{summary.DebtHours:0.#} h behind your {settings.TargetHours:0.#} h target over the last {SleepPlanner.DefaultWindowNights} nights";
            AverageDisplay = summary.NightsLogged == 0
                ? "Nothing logged yet."
                : $"Averaging {summary.AverageHours:0.#} h across {summary.NightsLogged} logged night(s)";

            RecentNights.Clear();
            foreach (var log in logs.OrderByDescending(l => l.NightOf)) RecentNights.Add(log);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogNightAsync()
    {
        ValidationMessage = null;

        if (NewNightOf is not { } nightOfDate)
        {
            ValidationMessage = "Pick the night.";
            return;
        }
        if (!TimeSpan.TryParse(NewBedTimeText, out var bed) || !TimeSpan.TryParse(NewWakeTimeText, out var wake))
        {
            ValidationMessage = "Enter times as HH:mm.";
            return;
        }

        var nightOf = DateOnly.FromDateTime(nightOfDate);
        var bedTime = nightOf.ToDateTime(TimeOnly.MinValue) + bed;
        var wakeTime = nightOf.ToDateTime(TimeOnly.MinValue) + wake;
        // Waking "before" bedtime means the wake time is on the following morning, which is the
        // normal case for any bedtime after midnight-minus-target.
        if (wakeTime <= bedTime) wakeTime = wakeTime.AddDays(1);

        await using var db = await dbContextFactory.CreateDbContextAsync();

        var existing = await db.Set<SleepLog>().SingleOrDefaultAsync(l => l.NightOf == nightOf);
        if (existing is null)
        {
            db.Add(new SleepLog
            {
                NightOf = nightOf,
                BedTime = bedTime,
                WakeTime = wakeTime,
                Quality = double.IsNaN(NewQuality) ? null : (int)NewQuality,
            });
        }
        else
        {
            // One row per night is a unique index, so re-logging the same night is an edit, not
            // an error the user has to resolve.
            existing.BedTime = bedTime;
            existing.WakeTime = wakeTime;
            existing.Quality = double.IsNaN(NewQuality) ? null : (int)NewQuality;
        }

        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteNightAsync(SleepLog log)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(await db.Set<SleepLog>().SingleAsync(l => l.Id == log.Id));
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        ValidationMessage = null;

        if (double.IsNaN(TargetHours) || TargetHours is <= 0 or > 16)
        {
            ValidationMessage = "Target hours must be between 1 and 16.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var settings = await LoadOrCreateSettingsAsync(db);
        settings.TargetHours = (decimal)TargetHours;
        settings.SleepOnsetMinutes = double.IsNaN(SleepOnsetMinutes) ? 0 : (int)SleepOnsetMinutes;
        settings.MorningRoutineMinutes = double.IsNaN(MorningRoutineMinutes) ? 0 : (int)MorningRoutineMinutes;
        settings.WindDownLeadMinutes = double.IsNaN(WindDownLeadMinutes) ? 0 : (int)WindDownLeadMinutes;
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
                    <ui:TextBlock Text="{Binding BedtimeDisplay}" FontTypography="BodyStrong" Margin="0,0,0,4" />
                    <TextBlock Text="{Binding DebtDisplay}" Margin="0,0,0,2" />
                    <TextBlock Text="{Binding AverageDisplay}" />
                </StackPanel>
            </ui:Card>

            <ui:Card Margin="0,0,0,12">
                <StackPanel>
                    <ui:TextBlock Text="Log a night" FontTypography="BodyStrong" Margin="0,0,0,8" />
                    <DatePicker SelectedDate="{Binding NewNightOf, Mode=TwoWay}" Margin="0,0,0,8" />
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                        <ui:TextBox PlaceholderText="In bed HH:mm" Text="{Binding NewBedTimeText, Mode=TwoWay}" Width="130" Margin="0,0,8,0" />
                        <ui:TextBox PlaceholderText="Awake HH:mm" Text="{Binding NewWakeTimeText, Mode=TwoWay}" Width="130" Margin="0,0,8,0" />
                        <ui:NumberBox PlaceholderText="Quality 1-5" Value="{Binding NewQuality, Mode=TwoWay}" Width="130" />
                    </StackPanel>
                    <ui:TextBlock Text="{Binding ValidationMessage}" Foreground="{DynamicResource SystemFillColorCriticalBrush}" Margin="0,0,0,8" />
                    <ui:Button Content="Save night" Appearance="Primary" Command="{Binding LogNightCommand}" HorizontalAlignment="Left" />
                </StackPanel>
            </ui:Card>

            <ui:Card Margin="0,0,0,12">
                <StackPanel>
                    <ui:TextBlock Text="Recent nights" FontTypography="BodyStrong" Margin="0,0,0,8" />
                    <ItemsControl ItemsSource="{Binding RecentNights}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" VerticalAlignment="Center">
                                        <Run Text="{Binding NightOf, StringFormat='{}{0:ddd MMM d}'}" />
                                        <Run Text=" · " />
                                        <Run Text="{Binding Hours, StringFormat='{}{0:0.#} h'}" />
                                        <Run Text=" · " />
                                        <Run Text="{Binding BedTime, StringFormat='{}{0:h:mm tt}'}" />
                                        <Run Text="→" />
                                        <Run Text="{Binding WakeTime, StringFormat='{}{0:h:mm tt}'}" />
                                    </TextBlock>
                                    <ui:Button Grid.Column="1" Content="Delete" Click="DeleteNight_Click" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ui:Card>

            <ui:Card>
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
                    <ui:Button Content="Save target" Command="{Binding SaveSettingsCommand}" HorizontalAlignment="Left" />
                </StackPanel>
            </ui:Card>
        </StackPanel>
    </ScrollViewer>
</Page>
```

`src/AaronOS.Modules.Schedule/Views/SleepPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.Data;
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

    private void DeleteNight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SleepLog log })
        {
            _ = ViewModel.DeleteNightCommand.ExecuteAsync(log);
        }
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
Expected: `Passed! - Failed: 0, Passed: 44`

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

```csharp
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
Expected: `Passed! - Failed: 0, Passed: 46`

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
Expected: `Passed! - Failed: 0, Passed: 58`

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
                                        <Run Text="{Binding ProgressPercent}" /><Run Text="% · " />
                                        <Run Text="{Binding TargetDate, StringFormat='target {0:MMM d, yyyy}', TargetNullValue='no target date'}" />
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
                                        <Run Text="{Binding Title}" />
                                        <Run Text="{Binding DueDate, StringFormat=' · due {0:MMM d}', TargetNullValue=''}" />
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
                                        <Run Text="{Binding Category}" />
                                        <Run Text=" · " />
                                        <Run Text="{Binding ReleaseDate, StringFormat='{}{0:ddd MMM d, yyyy}'}" />
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
Expected: `Passed! - Failed: 0, Passed: 58`

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
                                            <Run Text="{Binding Title}" FontWeight="SemiBold" />
                                            <Run Text="{Binding SuggestedStart, StringFormat=' · {0:hh\\:mm}', TargetNullValue=''}" />
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
Expected: `Passed! - Failed: 0, Passed: 58`

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Surface ranked suggestions on the Today page"
```

---

## Definition of done for Plan 2

- `dotnet build AaronOS.slnx --nologo` succeeds.
- `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo` reports 58 passing tests, 0 failing.
- Sleep, Goals, and Today all load against the real database; Today ranks routines, releases, milestones, and bedtime in one list.
- Sleep logs, goals, milestones, and releases persist across an app restart.
- No external network call exists anywhere in the module.

## Deferred to later plans

Notifications (Plan 3), external calendars (Plan 4), Gmail extraction (Plan 5). Also: a weekday picker in the routine editor, editing an existing goal's title or target date (only add and delete exist), and reordering milestones (`SortOrder` is set on insert and never changed).

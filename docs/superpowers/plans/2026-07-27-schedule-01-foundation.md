# Schedule Module — Plan 1: Foundation, Agenda, and Routines

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the `AaronOS.Modules.Schedule` module with a recurring weekly schedule template, dated exceptions, a pure agenda expander, and recurring routines with completion tracking — a usable module with zero external dependencies.

**Architecture:** A compiled-in `IAppModule` following `docs/MODULE_GUIDELINES.md`. All recurrence interpretation lives in one pure static class (`AgendaBuilder`) that takes plain values and returns plain values, so it is tested against in-memory lists with no database. ViewModels read the database through `IDbContextFactory<AaronOsDbContext>` and hand materialised lists to the pure services.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, WPF-UI 4.3.0 for Fluent controls, EF Core 8 + SQLite (inherited transitively from `AaronOS.Core`), CommunityToolkit.Mvvm, xUnit 2.5.3.

**Spec:** `docs/superpowers/specs/2026-07-27-schedule-module-design.md` — this plan covers phases 1 and 2.

## Global Constraints

Every task's requirements implicitly include this section. Values are copied verbatim from the spec and from `docs/MODULE_GUIDELINES.md`.

- Target framework `net8.0-windows`; `UseWPF` true; `LangVersion` `13.0`; `Nullable` `enable`; `ImplicitUsings` `enable`.
- **Never use the partial-property `[ObservableProperty]` form.** The generator does not run in this environment. Always write `[ObservableProperty] private bool _x;` and ignore the resulting `MVVMTK0045` warning — this app is never published Native AOT.
- Register ViewModels as **transient**, services as **singleton**.
- Pages must have a public parameterless constructor. Resolve the ViewModel inside it via `AaronOS.Core.AppServices.Provider.GetRequiredService<T>()`, set `DataContext = ViewModel;` explicitly, then `InitializeComponent()`, then hook load work from the `Loaded` event.
- `Frame.Navigate` in WPF takes an **instance**, not a `Type`: `ContentFrame.Navigate(new TodayPage())`.
- Never reference another module's entities or tables. `Goal` in this module is unrelated to `BodyMeasurements` goals and must not read them.
- WPF has no `ColumnSpacing`/`RowSpacing`/`Spacing`/`Padding` on `Grid`/`StackPanel`. Use explicit `Margin` on children.
- `ui:NumberBox.Value` is a `double`; a cleared box reports `double.NaN`, not null. Use `double.NaN` as the not-entered sentinel and convert to a nullable type at save time. Do not add a value converter.
- `DatePicker.SelectedDate` is `DateTime?` in WPF (not `DateTimeOffset`).
- For per-item buttons inside a `DataTemplate`, use a code-behind `Click` handler reading the item off `DataContext`, matching the existing pages.
- Imperial units and local time only. No unit toggle, no time-zone handling.
- Local times are stored as `TimeSpan` (wall clock) or `DateTime` (local). Never `DateTimeOffset`.
- Run tests with `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`. Expect `NU1701` warnings from unrelated projects; they are pre-existing and not a failure.
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

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj` | Project definition and package references |
| `src/AaronOS.Modules.Schedule/ScheduleModule.cs` | `IAppModule` implementation and DI registration |
| `src/AaronOS.Modules.Schedule/Data/ScheduleEnums.cs` | Every enum used by the module, in one file |
| `src/AaronOS.Modules.Schedule/Data/ScheduleBlock.cs` | Recurring template entity |
| `src/AaronOS.Modules.Schedule/Data/ScheduleBlockConfiguration.cs` | EF mapping for `ScheduleBlock` |
| `src/AaronOS.Modules.Schedule/Data/ScheduleException.cs` | Dated override entity |
| `src/AaronOS.Modules.Schedule/Data/ScheduleExceptionConfiguration.cs` | EF mapping for `ScheduleException` |
| `src/AaronOS.Modules.Schedule/Data/Routine.cs` | Recurring chore entity |
| `src/AaronOS.Modules.Schedule/Data/RoutineConfiguration.cs` | EF mapping for `Routine` |
| `src/AaronOS.Modules.Schedule/Data/RoutineCompletion.cs` | One logged completion |
| `src/AaronOS.Modules.Schedule/Data/RoutineCompletionConfiguration.cs` | EF mapping for `RoutineCompletion` |
| `src/AaronOS.Modules.Schedule/Agenda/AgendaTypes.cs` | `AgendaEntry`, `FreeGap`, `AgendaDay`, `ExternalEventEntry` records |
| `src/AaronOS.Modules.Schedule/Agenda/AgendaBuilder.cs` | Pure recurrence expansion, exception application, gap computation |
| `src/AaronOS.Modules.Schedule/Routines/RoutineDueState.cs` | Result record for routine scheduling |
| `src/AaronOS.Modules.Schedule/Routines/RoutineScheduler.cs` | Pure next-due / overdue computation |
| `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml(.cs)` | Nav entry point with internal `Frame` |
| `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml(.cs)` | Today's agenda |
| `src/AaronOS.Modules.Schedule/Views/WeekPage.xaml(.cs)` | Week agenda plus block and exception editing |
| `src/AaronOS.Modules.Schedule/Views/RoutinesPage.xaml(.cs)` | Routine list, due states, completion logging |
| `src/AaronOS.Modules.Schedule/ViewModels/TodayViewModel.cs` | Loads today's agenda |
| `src/AaronOS.Modules.Schedule/ViewModels/WeekViewModel.cs` | Loads the week, saves blocks and exceptions |
| `src/AaronOS.Modules.Schedule/ViewModels/RoutinesViewModel.cs` | Loads routines with due states, logs completions |
| `src/AaronOS.Modules.Schedule.Tests/*` | xUnit tests |

**Why `ExternalEventEntry` is a plain record rather than the `ExternalEvent` entity:** `AgendaBuilder` must not depend on the external-calendar tables, which do not exist until Plan 4. Plan 4 maps its entity rows into this record. That keeps this plan self-contained and the builder's tests free of any calendar concept.

---

## Task 1: Project scaffold and module registration

**Files:**
- Create: `src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj`
- Create: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`
- Create: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml`
- Create: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml.cs`
- Create: `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml`
- Create: `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml.cs`
- Create: `src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj`
- Create: `src/AaronOS.Modules.Schedule.Tests/ScheduleModuleTests.cs`
- Modify: `AaronOS.slnx`
- Modify: `src/AaronOS.App/AaronOS.App.csproj`
- Modify: `src/AaronOS.App/App.xaml.cs`

**Interfaces:**
- Consumes: `AaronOS.Core.IAppModule`, `AaronOS.Core.AppServices`.
- Produces: `ScheduleModule` (`Id => "schedule"`), `ScheduleShellPage`, `TodayPage`. Later tasks add pages to the shell's button row and `RegisterServices`.

- [ ] **Step 1: Write the failing test**

Create `src/AaronOS.Modules.Schedule.Tests/ScheduleModuleTests.cs`:

```csharp
using AaronOS.Modules.Schedule;
using AaronOS.Modules.Schedule.Views;

namespace AaronOS.Modules.Schedule.Tests;

public class ScheduleModuleTests
{
    [Fact]
    public void Exposes_StableContractValues()
    {
        var module = new ScheduleModule();

        Assert.Equal("schedule", module.Id);
        Assert.Equal("Schedule", module.DisplayName);
        Assert.Equal(typeof(ScheduleShellPage), module.HomePageType);
        // The shell does Enum.Parse<SymbolRegular>(IconGlyph) at startup; a bad name is a
        // crash on launch, not a compile error, so pin it here.
        Assert.False(string.IsNullOrWhiteSpace(module.IconGlyph));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: the project does not exist yet, so the command fails with `MSB1009: Project file does not exist`. That is the expected starting failure.

- [ ] **Step 3: Create both projects and the minimal implementation**

`src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj` — mirrors the Finance csproj, with `UseWindowsForms` added now so Plan 3 does not have to touch the project file again:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\AaronOS.Core\AaronOS.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.10" />
    <PackageReference Include="WPF-UI" Version="4.3.0" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <LangVersion>13.0</LangVersion>
    <RootNamespace>AaronOS.Modules.Schedule</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

`src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj` — a copy of the Finance test csproj with the project reference swapped:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\AaronOS.Modules.Schedule\AaronOS.Modules.Schedule.csproj" />
  </ItemGroup>
</Project>
```

`src/AaronOS.Modules.Schedule/ScheduleModule.cs`:

```csharp
using AaronOS.Core;
using AaronOS.Modules.Schedule.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule;

public class ScheduleModule : IAppModule
{
    public string Id => "schedule";
    public string DisplayName => "Schedule";
    public string IconGlyph => "CalendarLtr24";
    public Type HomePageType => typeof(ScheduleShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        // ViewModels are added by later tasks as their pages land.
    }
}
```

`src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Schedule.Views.ScheduleShellPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    mc:Ignorable="d">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="12,8" HorizontalAlignment="Left">
            <ui:Button Content="Today" Click="Today_Click" Margin="0,0,8,0" />
        </StackPanel>

        <Frame x:Name="ContentFrame" Grid.Row="1" NavigationUIVisibility="Hidden" />
    </Grid>
</Page>
```

`src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Schedule.Views;

/// <summary>
/// The module's single nav-pane entry point. Hosts an internal Frame so the shell needs only one
/// NavigationView item, per docs/MODULE_GUIDELINES.md.
/// </summary>
public sealed partial class ScheduleShellPage : Page
{
    public ScheduleShellPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ContentFrame.Navigate(new TodayPage());
    }

    private void Today_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new TodayPage());
}
```

`src/AaronOS.Modules.Schedule/Views/TodayPage.xaml` — a placeholder that Task 8 replaces:

```xml
<Page
    x:Class="AaronOS.Modules.Schedule.Views.TodayPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    mc:Ignorable="d">
    <ui:TextBlock Margin="16" Text="Today" FontTypography="Subtitle" />
</Page>
```

`src/AaronOS.Modules.Schedule/Views/TodayPage.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class TodayPage : Page
{
    public TodayPage() => InitializeComponent();
}
```

Add two `<Project Path=... />` lines inside the `/src/` folder element of `AaronOS.slnx`:

```xml
    <Project Path="src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj" />
    <Project Path="src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj" />
```

Add a project reference to `src/AaronOS.App/AaronOS.App.csproj` alongside the existing module references:

```xml
    <ProjectReference Include="..\AaronOS.Modules.Schedule\AaronOS.Modules.Schedule.csproj" />
```

In `src/AaronOS.App/App.xaml.cs`, add `using AaronOS.Modules.Schedule;` and extend the module array (this is the one line the guidelines refer to):

```csharp
IAppModule[] modules = [new BodyMeasurementsModule(), new FinanceModule(), new NutritionModule(), new MedicalModule(), new ScheduleModule()];
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 1`

Then confirm the whole solution still builds: `dotnet build AaronOS.slnx --nologo`
Expected: `Build succeeded` (NU1701 warnings from Finance are pre-existing).

- [ ] **Step 5: Verify the icon name is real, in the app**

`IconGlyph` is parsed with `Enum.Parse<SymbolRegular>` at startup, so a wrong name crashes on launch rather than failing to compile. Run the app: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`

Expected: the window opens, a **Schedule** item appears in the left navigation, and clicking it shows the "Today" placeholder. If startup throws an `ArgumentException` from `Enum.Parse`, pick a different `SymbolRegular` member (`Calendar24` and `CalendarDay24` are alternatives) and re-run. Close the app.

- [ ] **Step 6: Commit**

```bash
git add src/AaronOS.Modules.Schedule src/AaronOS.Modules.Schedule.Tests AaronOS.slnx src/AaronOS.App
git commit -m "Scaffold AaronOS.Modules.Schedule module"
```

---

## Task 2: Enums and the ScheduleBlock entity

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Data/ScheduleEnums.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/ScheduleBlock.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/ScheduleBlockConfiguration.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs`

**Interfaces:**
- Produces: `ScheduleBlockKind`, `DayOfWeekFlags`, `RoutineCategory`, `GoalStatus`, `ReleaseCategory`, `InboxItemKind`, `InboxItemStatus`, `CalendarProvider`, `AgendaEntrySource` enums — later plans rely on these exact member names. `ScheduleBlock` with `Id`, `Kind`, `Label`, `DaysOfWeek`, `StartTime`, `EndTime`, `EffectiveFrom`, `EffectiveTo`, `IsActive`. `DayOfWeekFlags.From(DayOfWeek)` helper.

- [ ] **Step 1: Write the failing test**

Create `src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs`. This uses a real temp-file SQLite database, matching `AccountTotalsTests` in the Finance test project — the only way to catch a mapping that EF cannot translate.

> ⚠️ **The test code below uses one `db` for both the write and the read. That is stale — restructure it before running.** EF Core's identity resolution returns the already-tracked entity, so asserting through the context that performed the insert checks the object the test constructed, not what SQLite stored: a broken value converter would pass. Write in one context, dispose it, then verify through a fresh `CreateContext()` against the same `_dbPath`. And where a test deletes to prove a cascade, the deleting context must not load or track the children — otherwise it proves EF's client-side cascade rather than the database foreign key. Use `ExecuteDeleteAsync` or a key-only attached stub there.

```csharp
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
        await using var db = CreateContext();
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

        var loaded = await db.Set<ScheduleBlock>().SingleAsync();
        Assert.Equal(ScheduleBlockKind.Work, loaded.Kind);
        Assert.Equal(DayOfWeekFlags.Monday | DayOfWeekFlags.Friday, loaded.DaysOfWeek);
        Assert.Equal(new TimeSpan(8, 0, 0), loaded.StartTime);
        Assert.Null(loaded.EffectiveTo);
    }

    [Fact]
    public void DayOfWeekFlags_MapsEveryDayOfWeek()
    {
        Assert.Equal(DayOfWeekFlags.Sunday, DayOfWeekFlagsExtensions.From(DayOfWeek.Sunday));
        Assert.Equal(DayOfWeekFlags.Wednesday, DayOfWeekFlagsExtensions.From(DayOfWeek.Wednesday));
        Assert.Equal(DayOfWeekFlags.Saturday, DayOfWeekFlagsExtensions.From(DayOfWeek.Saturday));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: compile errors — `ScheduleBlock`, `ScheduleBlockKind`, `DayOfWeekFlags`, and `DayOfWeekFlagsExtensions` do not exist (`CS0246`).

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Data/ScheduleEnums.cs` — every enum the module uses, declared once. Later plans reference these exact names.

```csharp
namespace AaronOS.Modules.Schedule.Data;

public enum ScheduleBlockKind { Work, Sleep, Personal }

/// <summary>A set of weekdays stored as a single int column. Flag values match
/// (1 &lt;&lt; (int)DayOfWeek) so <see cref="DayOfWeekFlagsExtensions.From"/> is a shift.</summary>
[Flags]
public enum DayOfWeekFlags
{
    None = 0,
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    EveryDay = Weekdays | Weekend,
}

public static class DayOfWeekFlagsExtensions
{
    public static DayOfWeekFlags From(DayOfWeek day) => (DayOfWeekFlags)(1 << (int)day);

    public static bool Includes(this DayOfWeekFlags flags, DayOfWeek day) => (flags & From(day)) != 0;
}

public enum RoutineCategory { Gym, Cleaning, LitterBox, Trash, Other }

public enum GoalStatus { Active, Paused, Done, Abandoned }

public enum ReleaseCategory { Media, Product }

public enum InboxItemKind { Appointment, Delivery, Release, Deadline, Other }

public enum InboxItemStatus { Pending, Accepted, Dismissed }

public enum CalendarProvider { OutlookIcs, GoogleCalendar }

/// <summary>Where an agenda entry came from, so the UI can style it and the suggestion engine
/// can tell a template block from a real meeting.</summary>
public enum AgendaEntrySource { Block, Exception, External }
```

`src/AaronOS.Modules.Schedule/Data/ScheduleBlock.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// One recurring entry in the weekly template — "work, Mon-Fri, 8 to 5". Reality is layered on
/// top with <see cref="ScheduleException"/> rather than by editing these rows.
/// </summary>
public class ScheduleBlock
{
    public int Id { get; set; }
    public ScheduleBlockKind Kind { get; set; }
    public string Label { get; set; } = "";
    public DayOfWeekFlags DaysOfWeek { get; set; }

    /// <summary>Local wall-clock time of day.</summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>Local wall-clock time of day. When less than <see cref="StartTime"/> the block
    /// wraps past midnight into the following day — which is how a sleep block is expressed.</summary>
    public TimeSpan EndTime { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Null means open-ended.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public bool WrapsMidnight => EndTime < StartTime;
}
```

`src/AaronOS.Modules.Schedule/Data/ScheduleBlockConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class ScheduleBlockConfiguration : IEntityTypeConfiguration<ScheduleBlock>
{
    public void Configure(EntityTypeBuilder<ScheduleBlock> builder)
    {
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => b.IsActive);
        builder.Property(b => b.Label).HasMaxLength(120).IsRequired();
        builder.Property(b => b.Kind).HasConversion<int>();
        builder.Property(b => b.DaysOfWeek).HasConversion<int>();
        builder.Ignore(b => b.WrapsMidnight);
    }
}
```

`builder.Ignore` on the computed property rather than `[NotMapped]` keeps the entity file free of a mapping attribute; either works, and the existing Finance entities use `[NotMapped]` — match whichever the reviewer prefers, but be consistent within this module.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Data src/AaronOS.Modules.Schedule.Tests
git commit -m "Add Schedule module enums and ScheduleBlock entity"
```

---

## Task 3: The ScheduleException entity

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Data/ScheduleException.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/ScheduleExceptionConfiguration.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs`

**Interfaces:**
- Consumes: `ScheduleBlockKind` from Task 2.
- Produces: `ScheduleException` with `Id`, `Date`, `ScheduleBlockId`, `IsCancelled`, `Kind`, `Label`, `StartTime`, `EndTime`, `Note`.

- [ ] **Step 1: Write the failing test**

Add to `ScheduleSchemaTests`:

> ⚠️ **The test code below uses one `db` for both the write and the read. That is stale — restructure it before running.** EF Core's identity resolution returns the already-tracked entity, so asserting through the context that performed the insert checks the object the test constructed, not what SQLite stored: a broken value converter would pass. Write in one context, dispose it, then verify through a fresh `CreateContext()` against the same `_dbPath`. And where a test deletes to prove a cascade, the deleting context must not load or track the children — otherwise it proves EF's client-side cascade rather than the database foreign key. Use `ExecuteDeleteAsync` or a key-only attached stub there.

```csharp
    [Fact]
    public async Task ScheduleException_RoundTripsBothShapes()
    {
        await using var db = CreateContext();
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

        var loaded = await db.Set<ScheduleException>().OrderBy(e => e.Date).ToListAsync();
        Assert.True(loaded[0].IsCancelled);
        Assert.Equal(block.Id, loaded[0].ScheduleBlockId);
        Assert.Null(loaded[1].ScheduleBlockId);
        Assert.Equal("Deploy window", loaded[1].Label);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246: The type or namespace name 'ScheduleException' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Data/ScheduleException.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// A dated override on top of the recurring template. Two shapes, distinguished by whether
/// <see cref="ScheduleBlockId"/> is set:
/// a modification of a template block (cancel it for PTO, or replace its times for a short day),
/// or a standalone one-off entry that no template block produced.
/// </summary>
public class ScheduleException
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>Null for a standalone one-off entry.</summary>
    public int? ScheduleBlockId { get; set; }

    /// <summary>True means the referenced block does not occur on <see cref="Date"/>.</summary>
    public bool IsCancelled { get; set; }

    /// <summary>Required for a standalone entry; ignored when modifying a block.</summary>
    public ScheduleBlockKind? Kind { get; set; }

    public string? Label { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string? Note { get; set; }

    public bool IsStandalone => ScheduleBlockId is null;
}
```

`src/AaronOS.Modules.Schedule/Data/ScheduleExceptionConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class ScheduleExceptionConfiguration : IEntityTypeConfiguration<ScheduleException>
{
    public void Configure(EntityTypeBuilder<ScheduleException> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Date);
        builder.Property(e => e.Label).HasMaxLength(120);
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.Kind).HasConversion<int?>();
        builder.Ignore(e => e.IsStandalone);
    }
}
```

There is deliberately no navigation property or foreign-key constraint to `ScheduleBlock`: deleting a block should not cascade-delete the historical record that something was cancelled that day, and the agenda builder tolerates an exception whose block no longer exists by ignoring it.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Data src/AaronOS.Modules.Schedule.Tests
git commit -m "Add ScheduleException entity for dated schedule overrides"
```

---

## Task 4: Agenda types and block expansion

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Agenda/AgendaTypes.cs`
- Create: `src/AaronOS.Modules.Schedule/Agenda/AgendaBuilder.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/AgendaBuilderTests.cs`

**Interfaces:**
- Consumes: `ScheduleBlock`, `ScheduleException`, `ScheduleBlockKind`, `AgendaEntrySource`, `DayOfWeekFlagsExtensions.Includes`.
- Produces:
  - `record AgendaEntry(TimeSpan Start, TimeSpan End, ScheduleBlockKind Kind, string Label, AgendaEntrySource Source)`
  - `record FreeGap(TimeSpan Start, TimeSpan End)` with `int Minutes`
  - `record AgendaDay(DateOnly Date, IReadOnlyList<AgendaEntry> Entries, IReadOnlyList<FreeGap> FreeGaps)` with `AgendaEntry? FirstCommitment`
  - `record ExternalEventEntry(DateOnly Date, TimeSpan Start, TimeSpan End, string Title, bool IsBusy)`
  - `static IReadOnlyList<AgendaDay> AgendaBuilder.Build(DateOnly from, DateOnly to, IReadOnlyList<ScheduleBlock> blocks, IReadOnlyList<ScheduleException> exceptions, IReadOnlyList<ExternalEventEntry> externalEvents)`

  Every later plan consumes `AgendaDay` and `AgendaBuilder.Build` with exactly this signature.

- [ ] **Step 1: Write the failing test**

Create `src/AaronOS.Modules.Schedule.Tests/AgendaBuilderTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Tests;

public class AgendaBuilderTests
{
    private static ScheduleBlock Work(DayOfWeekFlags days) => new()
    {
        Id = 1,
        Kind = ScheduleBlockKind.Work,
        Label = "Core hours",
        DaysOfWeek = days,
        StartTime = new TimeSpan(8, 0, 0),
        EndTime = new TimeSpan(17, 0, 0),
        EffectiveFrom = new DateOnly(2026, 1, 1),
        IsActive = true,
    };

    // Mon 2026-07-06 .. Sun 2026-07-12
    private static readonly DateOnly Monday = new(2026, 7, 6);
    private static readonly DateOnly Sunday = new(2026, 7, 12);

    [Fact]
    public void ExpandsBlock_OnlyOnItsWeekdays()
    {
        var days = AgendaBuilder.Build(Monday, Sunday, [Work(DayOfWeekFlags.Weekdays)], [], []);

        Assert.Equal(7, days.Count);
        Assert.All(days.Take(5), d => Assert.Single(d.Entries));
        Assert.Empty(days[5].Entries); // Saturday
        Assert.Empty(days[6].Entries); // Sunday
        Assert.Equal(new TimeSpan(8, 0, 0), days[0].Entries[0].Start);
        Assert.Equal(AgendaEntrySource.Block, days[0].Entries[0].Source);
    }

    [Fact]
    public void SkipsBlocks_OutsideTheirEffectiveWindow()
    {
        var starting = Work(DayOfWeekFlags.EveryDay);
        starting.EffectiveFrom = new DateOnly(2026, 7, 8);
        starting.EffectiveTo = new DateOnly(2026, 7, 9);

        var days = AgendaBuilder.Build(Monday, Sunday, [starting], [], []);

        Assert.Empty(days[0].Entries);                 // Mon 6th, before EffectiveFrom
        Assert.Single(days[2].Entries);                // Wed 8th
        Assert.Single(days[3].Entries);                // Thu 9th
        Assert.Empty(days[4].Entries);                 // Fri 10th, after EffectiveTo
    }

    [Fact]
    public void SkipsInactiveBlocks()
    {
        var inactive = Work(DayOfWeekFlags.EveryDay);
        inactive.IsActive = false;

        var days = AgendaBuilder.Build(Monday, Monday, [inactive], [], []);

        Assert.Empty(days[0].Entries);
    }

    [Fact]
    public void OrdersEntriesByStartTime()
    {
        var evening = Work(DayOfWeekFlags.EveryDay);
        evening.Id = 2;
        evening.Label = "Evening";
        evening.StartTime = new TimeSpan(19, 0, 0);
        evening.EndTime = new TimeSpan(21, 0, 0);

        var days = AgendaBuilder.Build(Monday, Monday, [evening, Work(DayOfWeekFlags.EveryDay)], [], []);

        Assert.Equal(["Core hours", "Evening"], days[0].Entries.Select(e => e.Label));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `AgendaBuilder`, `AgendaEntrySource` resolves (Task 2) but `AgendaBuilder` and the agenda records do not exist.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Agenda/AgendaTypes.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Agenda;

/// <summary>One committed span on a single day. Times are wall-clock offsets from that day's
/// midnight, always within [00:00, 24:00] — a block that wraps midnight is split by
/// <see cref="AgendaBuilder"/> so consumers never have to reason about wrapping.</summary>
public sealed record AgendaEntry(
    TimeSpan Start,
    TimeSpan End,
    ScheduleBlockKind Kind,
    string Label,
    AgendaEntrySource Source);

/// <summary>An uncommitted span. Sleep counts as committed, so gaps are naturally waking hours.</summary>
public sealed record FreeGap(TimeSpan Start, TimeSpan End)
{
    public int Minutes => (int)(End - Start).TotalMinutes;
}

public sealed record AgendaDay(
    DateOnly Date,
    IReadOnlyList<AgendaEntry> Entries,
    IReadOnlyList<FreeGap> FreeGaps)
{
    /// <summary>The first entry that isn't sleep — what a bedtime recommendation works back from.
    /// Null when the day has no waking commitments.</summary>
    public AgendaEntry? FirstCommitment =>
        Entries.FirstOrDefault(e => e.Kind != ScheduleBlockKind.Sleep);
}

/// <summary>
/// A cached external calendar event, flattened to a single day. Deliberately a plain record rather
/// than the ExternalEvent entity so the agenda logic carries no dependency on the external-calendar
/// tables — those arrive in a later phase and map their rows into this shape.
/// </summary>
public sealed record ExternalEventEntry(
    DateOnly Date,
    TimeSpan Start,
    TimeSpan End,
    string Title,
    bool IsBusy);
```

`src/AaronOS.Modules.Schedule/Agenda/AgendaBuilder.cs` — the block-expansion half. Exceptions, wrapping, and gaps are added by Tasks 5 and 6; write the file with the full public shape now and leave those pieces to those tasks.

```csharp
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Agenda;

/// <summary>
/// The single place recurrence is interpreted. Pure by design — it takes materialised lists and
/// returns values, with no DbContext or clock dependency, so every rule below is testable against
/// in-memory data (see AgendaBuilderTests).
/// </summary>
public static class AgendaBuilder
{
    public static IReadOnlyList<AgendaDay> Build(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<ScheduleBlock> blocks,
        IReadOnlyList<ScheduleException> exceptions,
        IReadOnlyList<ExternalEventEntry> externalEvents)
    {
        var days = new List<AgendaDay>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var entries = new List<AgendaEntry>();

            foreach (var block in blocks)
            {
                if (!IsActiveOn(block, date)) continue;
                entries.Add(new AgendaEntry(
                    block.StartTime, block.EndTime, block.Kind, block.Label, AgendaEntrySource.Block));
            }

            entries.Sort(CompareByStart);
            days.Add(new AgendaDay(date, entries, []));
        }

        return days;
    }

    private static bool IsActiveOn(ScheduleBlock block, DateOnly date)
    {
        if (!block.IsActive) return false;
        if (date < block.EffectiveFrom) return false;
        if (block.EffectiveTo is { } end && date > end) return false;
        return block.DaysOfWeek.Includes(date.DayOfWeek);
    }

    private static int CompareByStart(AgendaEntry a, AgendaEntry b)
    {
        var byStart = a.Start.CompareTo(b.Start);
        return byStart != 0 ? byStart : a.End.CompareTo(b.End);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 8`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Agenda src/AaronOS.Modules.Schedule.Tests/AgendaBuilderTests.cs
git commit -m "Add AgendaBuilder block expansion over a date range"
```

---

## Task 5: Applying exceptions and external events

**Files:**
- Modify: `src/AaronOS.Modules.Schedule/Agenda/AgendaBuilder.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/AgendaBuilderTests.cs`

**Interfaces:**
- Consumes: everything from Task 4.
- Produces: no new public names — `Build` gains behaviour for the `exceptions` and `externalEvents` parameters it already declares.

- [ ] **Step 1: Write the failing tests**

Add to `AgendaBuilderTests`:

```csharp
    [Fact]
    public void CancellationException_RemovesTheBlockForThatDayOnly()
    {
        ScheduleException pto = new() { Id = 1, Date = Monday, ScheduleBlockId = 1, IsCancelled = true, Note = "PTO" };

        var days = AgendaBuilder.Build(Monday, Sunday, [Work(DayOfWeekFlags.Weekdays)], [pto], []);

        Assert.Empty(days[0].Entries);   // Monday cancelled
        Assert.Single(days[1].Entries);  // Tuesday unaffected
    }

    [Fact]
    public void TimeOverrideException_ReplacesTheBlocksTimes()
    {
        ScheduleException shortDay = new()
        {
            Id = 1,
            Date = Monday,
            ScheduleBlockId = 1,
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(12, 0, 0),
        };

        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.Weekdays)], [shortDay], []);

        var entry = Assert.Single(days[0].Entries);
        Assert.Equal(new TimeSpan(12, 0, 0), entry.End);
        Assert.Equal(AgendaEntrySource.Exception, entry.Source);
        Assert.Equal("Core hours", entry.Label); // label carries over from the block
    }

    [Fact]
    public void StandaloneException_AddsAnEntryWithNoParentBlock()
    {
        ScheduleException oneOff = new()
        {
            Id = 1,
            Date = Monday,
            Kind = ScheduleBlockKind.Work,
            Label = "Deploy window",
            StartTime = new TimeSpan(20, 0, 0),
            EndTime = new TimeSpan(23, 0, 0),
        };

        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.Weekdays)], [oneOff], []);

        Assert.Equal(["Core hours", "Deploy window"], days[0].Entries.Select(e => e.Label));
        Assert.Equal(AgendaEntrySource.Exception, days[0].Entries[1].Source);
    }

    [Fact]
    public void OrphanedException_IsIgnored()
    {
        // Block 99 was deleted; the exception row survives. It must not throw or invent an entry.
        ScheduleException orphan = new() { Id = 1, Date = Monday, ScheduleBlockId = 99, IsCancelled = true };

        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.Weekdays)], [orphan], []);

        Assert.Single(days[0].Entries);
    }

    [Fact]
    public void ExternalEvents_MergeInStartOrder_AndFreeEventsAreExcluded()
    {
        ExternalEventEntry standup = new(Monday, new TimeSpan(9, 30, 0), new TimeSpan(10, 0, 0), "Standup", IsBusy: true);
        ExternalEventEntry earlyCall = new(Monday, new TimeSpan(7, 0, 0), new TimeSpan(7, 30, 0), "Call", IsBusy: true);
        ExternalEventEntry fyi = new(Monday, new TimeSpan(11, 0, 0), new TimeSpan(12, 0, 0), "FYI: launch", IsBusy: false);
        ExternalEventEntry otherDay = new(Monday.AddDays(1), new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), "Tomorrow", IsBusy: true);

        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.Weekdays)], [], [standup, earlyCall, fyi, otherDay]);

        Assert.Equal(["Call", "Core hours", "Standup"], days[0].Entries.Select(e => e.Label));
        Assert.Equal(AgendaEntrySource.External, days[0].Entries[0].Source);
        Assert.Equal(ScheduleBlockKind.Personal, days[0].Entries[0].Kind);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: 5 failures. `CancellationException_RemovesTheBlockForThatDayOnly` fails with `Assert.Empty() Failure: Collection was not empty`; the external-event test fails with a one-item collection instead of three.

- [ ] **Step 3: Write the implementation**

Replace the body of the per-day loop in `AgendaBuilder.Build` and add the helpers:

```csharp
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayExceptions = exceptions.Where(e => e.Date == date).ToList();
            var entries = new List<AgendaEntry>();

            foreach (var block in blocks)
            {
                if (!IsActiveOn(block, date)) continue;

                var over = dayExceptions.FirstOrDefault(e => e.ScheduleBlockId == block.Id);
                if (over is null)
                {
                    entries.Add(new AgendaEntry(
                        block.StartTime, block.EndTime, block.Kind, block.Label, AgendaEntrySource.Block));
                    continue;
                }

                if (over.IsCancelled) continue;

                entries.Add(new AgendaEntry(
                    over.StartTime ?? block.StartTime,
                    over.EndTime ?? block.EndTime,
                    over.Kind ?? block.Kind,
                    over.Label ?? block.Label,
                    AgendaEntrySource.Exception));
            }

            foreach (var standalone in dayExceptions.Where(e => e.IsStandalone && !e.IsCancelled))
            {
                // A standalone entry without times is meaningless; skip rather than guess.
                if (standalone.StartTime is not { } start || standalone.EndTime is not { } end) continue;

                entries.Add(new AgendaEntry(
                    start,
                    end,
                    standalone.Kind ?? ScheduleBlockKind.Personal,
                    standalone.Label ?? "(untitled)",
                    AgendaEntrySource.Exception));
            }

            foreach (var external in externalEvents.Where(e => e.Date == date && e.IsBusy))
            {
                entries.Add(new AgendaEntry(
                    external.Start, external.End, ScheduleBlockKind.Personal, external.Title, AgendaEntrySource.External));
            }

            entries.Sort(CompareByStart);
            days.Add(new AgendaDay(date, entries, []));
        }
```

Two decisions worth naming. An exception referencing a block that is not in `blocks` (deleted, inactive, or outside its effective window) never matches, so it is silently ignored — that is the orphan case, and it is why the lookup is a `FirstOrDefault` over the day's exceptions rather than an indexed join. And an external event with `IsBusy == false` is excluded entirely: a free/FYI event should not consume a free gap or push the recommended bedtime around.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 13`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Agenda src/AaronOS.Modules.Schedule.Tests/AgendaBuilderTests.cs
git commit -m "Apply schedule exceptions and external events in AgendaBuilder"
```

---

## Task 6: Midnight wrapping and free gaps

**Files:**
- Modify: `src/AaronOS.Modules.Schedule/Agenda/AgendaBuilder.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/AgendaBuilderTests.cs`

**Interfaces:**
- Produces: populated `AgendaDay.FreeGaps`, and entries split at midnight so no `AgendaEntry` has `End < Start`.

- [ ] **Step 1: Write the failing tests**

Add to `AgendaBuilderTests`:

```csharp
    private static ScheduleBlock Sleep() => new()
    {
        Id = 10,
        Kind = ScheduleBlockKind.Sleep,
        Label = "Sleep",
        DaysOfWeek = DayOfWeekFlags.EveryDay,
        StartTime = new TimeSpan(23, 0, 0),
        EndTime = new TimeSpan(7, 0, 0),   // wraps midnight
        EffectiveFrom = new DateOnly(2026, 1, 1),
        IsActive = true,
    };

    [Fact]
    public void WrappingBlock_IsSplitAcrossTheDayBoundary()
    {
        var days = AgendaBuilder.Build(Monday, Monday.AddDays(1), [Sleep()], [], []);

        // Monday carries the tail of Sunday night plus Monday night's opening segment.
        Assert.Equal(
            [(TimeSpan.Zero, new TimeSpan(7, 0, 0)), (new TimeSpan(23, 0, 0), new TimeSpan(24, 0, 0))],
            days[0].Entries.Select(e => (e.Start, e.End)));
        // No entry may wrap: every End is after its Start.
        Assert.All(days[0].Entries, e => Assert.True(e.End > e.Start));
    }

    [Fact]
    public void FreeGaps_AreTheSpansBetweenCommittedEntries()
    {
        var days = AgendaBuilder.Build(Monday, Monday, [Sleep(), Work(DayOfWeekFlags.Weekdays)], [], []);

        // Sleep 00:00-07:00 and 23:00-24:00, work 08:00-17:00.
        Assert.Equal(
            [(new TimeSpan(7, 0, 0), new TimeSpan(8, 0, 0)), (new TimeSpan(17, 0, 0), new TimeSpan(23, 0, 0))],
            days[0].FreeGaps.Select(g => (g.Start, g.End)));
        Assert.Equal(60, days[0].FreeGaps[0].Minutes);
        Assert.Equal(360, days[0].FreeGaps[1].Minutes);
    }

    [Fact]
    public void FreeGaps_MergeOverlappingEntriesBeforeMeasuring()
    {
        var overlapping = Work(DayOfWeekFlags.EveryDay);
        overlapping.Id = 2;
        overlapping.Label = "Overlapping";
        overlapping.StartTime = new TimeSpan(16, 0, 0);
        overlapping.EndTime = new TimeSpan(18, 0, 0);

        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.EveryDay), overlapping], [], []);

        // 08:00-17:00 and 16:00-18:00 union to 08:00-18:00, so exactly two gaps, not three.
        Assert.Equal(
            [(TimeSpan.Zero, new TimeSpan(8, 0, 0)), (new TimeSpan(18, 0, 0), new TimeSpan(24, 0, 0))],
            days[0].FreeGaps.Select(g => (g.Start, g.End)));
    }

    [Fact]
    public void EmptyDay_IsOneFullDayGap()
    {
        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.Weekend)], [], []);

        var gap = Assert.Single(days[0].FreeGaps);
        Assert.Equal(TimeSpan.Zero, gap.Start);
        Assert.Equal(new TimeSpan(24, 0, 0), gap.End);
    }

    [Fact]
    public void FullyBookedDay_HasNoGaps()
    {
        var allDay = Work(DayOfWeekFlags.EveryDay);
        allDay.StartTime = TimeSpan.Zero;
        allDay.EndTime = new TimeSpan(24, 0, 0);

        var days = AgendaBuilder.Build(Monday, Monday, [allDay], [], []);

        Assert.Empty(days[0].FreeGaps);
    }

    [Fact]
    public void FreeGaps_EnclosedEntryDoesNotOpenASpuriousGap()
    {
        var outer = Work(DayOfWeekFlags.EveryDay);
        outer.EndTime = new TimeSpan(19, 0, 0);            // 08:00-19:00

        var enclosed = Work(DayOfWeekFlags.EveryDay);
        enclosed.Id = 2;
        enclosed.Label = "Enclosed";
        enclosed.StartTime = new TimeSpan(16, 0, 0);
        enclosed.EndTime = new TimeSpan(18, 0, 0);         // wholly inside 08:00-19:00

        var days = AgendaBuilder.Build(Monday, Monday, [outer, enclosed], [], []);

        // This is the case the `entry.End > cursor` guard exists for, and the only test that
        // executes its false branch: the enclosed entry must not move the union frontier, so the
        // afternoon gap starts at 19:00. An unconditional `cursor = entry.End` yields 18:00 here
        // and passes every other test in this file.
        Assert.Equal(
            [(TimeSpan.Zero, new TimeSpan(8, 0, 0)), (new TimeSpan(19, 0, 0), new TimeSpan(24, 0, 0))],
            days[0].FreeGaps.Select(g => (g.Start, g.End)));
    }

    [Fact]
    public void ZeroDurationSpan_IsSkippedRatherThanTreatedAsAWrap()
    {
        ScheduleException zeroLength = new()
        {
            Id = 1, Date = Monday, Kind = ScheduleBlockKind.Personal, Label = "Mis-entered",
            StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(9, 0, 0),
        };

        var days = AgendaBuilder.Build(Monday, Monday.AddDays(1), [], [zeroLength], []);

        // A zero-length span is not a wrap: no entry on its own day, and no phantom tail carried
        // onto the next. Without the guard in AddSpan this produced a 09:00-24:00 entry plus a
        // 00:00-09:00 entry the following day.
        Assert.Empty(days[0].Entries);
        Assert.Empty(days[1].Entries);
        Assert.All(days, d => Assert.All(d.Entries, e => Assert.True(e.End > e.Start)));
    }

    [Fact]
    public void FirstCommitment_SkipsTheCarriedSleepTail()
    {
        var days = AgendaBuilder.Build(Monday, Monday, [Sleep(), Work(DayOfWeekFlags.Weekdays)], [], []);

        // The first two assertions are what make this test depend on *this* task: without the
        // midnight split, Entries[0] would be Work at 08:00 rather than the previous night's sleep
        // tail opening the day at 00:00. Asserting FirstCommitment alone re-tests Task 4's filter
        // and passes whether the split works, is subtly wrong, or is absent entirely.
        Assert.Equal(ScheduleBlockKind.Sleep, days[0].Entries[0].Kind);
        Assert.Equal(TimeSpan.Zero, days[0].Entries[0].Start);
        Assert.Equal("Core hours", days[0].FirstCommitment!.Label);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: several failures — the wrapping test reporting one entry `(23:00, 07:00)` instead of two split entries, the gap tests failing on an empty `FreeGaps`, and the zero-duration test failing because `AddSpan` does not exist yet.

**Do not treat a specific failure count as the gate.** Some of these tests pass trivially against the pre-task code: `FullyBookedDay_HasNoGaps` passes while `FreeGaps` is hardcoded empty, and `FirstCommitment_SkipsTheCarriedSleepTail`'s third assertion holds without any split. What matters is that each test fails *for the reason its name describes* once it is exercising real logic, and that all of them pass afterwards.

- [ ] **Step 3: Write the implementation**

Two changes to `AgendaBuilder`. First, emit wrapping entries as two segments. Replace each of the three `entries.Add(new AgendaEntry(...))` calls with a call to a new `AddSpan` helper, and add the helpers below.

```csharp
    private static readonly TimeSpan DayEnd = new(24, 0, 0);

    /// <summary>
    /// Adds a span to <paramref name="today"/>, and when it wraps past midnight also records the
    /// portion that lands on the following day so callers never see End &lt; Start. The tail is
    /// stashed in <paramref name="carry"/> keyed by the date it belongs to, because that date may
    /// not have been built yet.
    /// </summary>
    private static void AddSpan(
        List<AgendaEntry> today,
        Dictionary<DateOnly, List<AgendaEntry>> carry,
        DateOnly date,
        TimeSpan start,
        TimeSpan end,
        ScheduleBlockKind kind,
        string label,
        AgendaEntrySource source)
    {
        // A zero-duration commitment is meaningless, and it is NOT a wrap — ScheduleBlock.WrapsMidnight
        // uses strict `<`, so treating equal times as wrapping would fabricate a near-full-day entry
        // plus a phantom tail on the next day. Guard before the wrap branch, and do not relax the
        // condition below to `>=`: that would emit a zero-width entry and break the no-End<=Start
        // invariant this method exists to maintain.
        if (end == start) return;

        if (end > start)
        {
            today.Add(new AgendaEntry(start, end, kind, label, source));
            return;
        }

        today.Add(new AgendaEntry(start, DayEnd, kind, label, source));

        var next = date.AddDays(1);
        if (!carry.TryGetValue(next, out var list))
        {
            carry[next] = list = [];
        }
        list.Add(new AgendaEntry(TimeSpan.Zero, end, kind, label, source));
    }
```

The loop must therefore seed each day from `carry` before expanding blocks, and it must start one day early so the range's first day picks up the previous night's tail:

```csharp
        var carry = new Dictionary<DateOnly, List<AgendaEntry>>();

        // Start a day early so a wrapping block from the night before contributes to `from`,
        // then drop that warm-up day from the result.
        for (var date = from.AddDays(-1); date <= to; date = date.AddDays(1))
        {
            var entries = carry.TryGetValue(date, out var carried) ? carried : [];
            carry.Remove(date);

            // ... block expansion, exception application, and external merging as in Task 5,
            //     but calling AddSpan(entries, carry, date, start, end, kind, label, source)
            //     instead of entries.Add(new AgendaEntry(...)).

            entries.Sort(CompareByStart);

            if (date >= from)
            {
                days.Add(new AgendaDay(date, entries, ComputeFreeGaps(entries)));
            }
        }
```

Second, compute the gaps by unioning overlapping entries and taking the complement across the day:

```csharp
    /// <summary>
    /// The complement of the union of committed entries across [00:00, 24:00]. Sleep entries count
    /// as committed, which is what makes the gaps waking hours without needing a separate
    /// "available window" concept.
    /// </summary>
    private static List<FreeGap> ComputeFreeGaps(List<AgendaEntry> sortedEntries)
    {
        var gaps = new List<FreeGap>();
        var cursor = TimeSpan.Zero;

        foreach (var entry in sortedEntries)
        {
            if (entry.Start > cursor)
            {
                gaps.Add(new FreeGap(cursor, entry.Start));
            }
            if (entry.End > cursor)
            {
                cursor = entry.End;
            }
        }

        if (cursor < DayEnd)
        {
            gaps.Add(new FreeGap(cursor, DayEnd));
        }

        return gaps;
    }
```

Advancing `cursor` only when `entry.End > cursor` is what makes a fully-enclosed entry (`16:00-18:00` inside `08:00-19:00`) fail to open a spurious gap — that is the union, done in one pass over the already-sorted list.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 21`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Agenda src/AaronOS.Modules.Schedule.Tests/AgendaBuilderTests.cs
git commit -m "Split midnight-wrapping blocks and compute free gaps"
```

---

## Task 7: Routine and RoutineCompletion entities

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Data/Routine.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/RoutineConfiguration.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/RoutineCompletion.cs`
- Create: `src/AaronOS.Modules.Schedule/Data/RoutineCompletionConfiguration.cs`
- Modify: `src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs`

**Interfaces:**
- Consumes: `RoutineCategory`, `DayOfWeekFlags`.
- Produces: `Routine` with `Id`, `Name`, `Category`, `IntervalDays`, `PreferredDaysOfWeek`, `PreferredTimeOfDay`, `EstimatedMinutes`, `IsActive`. `RoutineCompletion` with `Id`, `RoutineId`, `CompletedAt`, `Note`.

- [ ] **Step 1: Write the failing test**

Add to `ScheduleSchemaTests`:

> ⚠️ **The test code below uses one `db` for both the write and the read. That is stale — restructure it before running.** EF Core's identity resolution returns the already-tracked entity, so asserting through the context that performed the insert checks the object the test constructed, not what SQLite stored: a broken value converter would pass. Write in one context, dispose it, then verify through a fresh `CreateContext()` against the same `_dbPath`. And where a test deletes to prove a cascade, the deleting context must not load or track the children — otherwise it proves EF's client-side cascade rather than the database foreign key. Use `ExecuteDeleteAsync` or a key-only attached stub there.

```csharp
    [Fact]
    public async Task Routine_CascadeDeletesItsCompletions()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        var litter = new Routine
        {
            Name = "Scoop litter box",
            Category = RoutineCategory.LitterBox,
            IntervalDays = 2,
            EstimatedMinutes = 5,
        };
        db.Add(litter);
        await db.SaveChangesAsync();

        db.Add(new RoutineCompletion { RoutineId = litter.Id, CompletedAt = new DateTime(2026, 7, 6, 21, 0, 0) });
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Set<RoutineCompletion>().CountAsync());

        db.Remove(litter);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.Set<RoutineCompletion>().CountAsync());
    }

    [Fact]
    public async Task Routine_StoresAWeekdayPinnedShape()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.Add(new Routine
        {
            Name = "Take out trash",
            Category = RoutineCategory.Trash,
            PreferredDaysOfWeek = DayOfWeekFlags.Tuesday,
            PreferredTimeOfDay = new TimeSpan(20, 0, 0),
        });
        await db.SaveChangesAsync();

        var loaded = await db.Set<Routine>().SingleAsync();
        Assert.Null(loaded.IntervalDays);
        Assert.Equal(DayOfWeekFlags.Tuesday, loaded.PreferredDaysOfWeek);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `Routine` and `RoutineCompletion`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Data/Routine.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// A recurring chore. Exactly one of <see cref="IntervalDays"/> and
/// <see cref="PreferredDaysOfWeek"/> drives its due date: the litter box is an interval
/// ("every 2 days"), trash night is a weekday ("Tuesdays"). Both set, or neither, is invalid.
/// </summary>
public class Routine
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public RoutineCategory Category { get; set; }

    /// <summary>Days between completions. Null when the routine is weekday-pinned instead.</summary>
    public int? IntervalDays { get; set; }

    /// <summary>Fixed weekdays. Null when the routine is interval-driven instead.</summary>
    public DayOfWeekFlags? PreferredDaysOfWeek { get; set; }

    /// <summary>A ranking hint for the suggestion engine, not a hard slot.</summary>
    public TimeSpan? PreferredTimeOfDay { get; set; }

    /// <summary>Used to check whether the routine actually fits a free gap.</summary>
    public int? EstimatedMinutes { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsIntervalDriven => IntervalDays is > 0;
}
```

`src/AaronOS.Modules.Schedule/Data/RoutineConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class RoutineConfiguration : IEntityTypeConfiguration<Routine>
{
    public void Configure(EntityTypeBuilder<Routine> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.IsActive);
        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Category).HasConversion<int>();
        builder.Property(r => r.PreferredDaysOfWeek).HasConversion<int?>();
        builder.Ignore(r => r.IsIntervalDriven);
    }
}
```

`src/AaronOS.Modules.Schedule/Data/RoutineCompletion.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// One logged completion. Next-due is always derived from these rows rather than stored on
/// <see cref="Routine"/>: a stored "next due" column would need rewriting on every completion and
/// would drift silently if a completion were later edited or deleted.
/// </summary>
public class RoutineCompletion
{
    public int Id { get; set; }
    public int RoutineId { get; set; }
    public DateTime CompletedAt { get; set; }
    public string? Note { get; set; }
}
```

`src/AaronOS.Modules.Schedule/Data/RoutineCompletionConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Schedule.Data;

public class RoutineCompletionConfiguration : IEntityTypeConfiguration<RoutineCompletion>
{
    public void Configure(EntityTypeBuilder<RoutineCompletion> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.RoutineId, c.CompletedAt });
        builder.Property(c => c.Note).HasMaxLength(500);
        builder.HasOne<Routine>()
            .WithMany()
            .HasForeignKey(c => c.RoutineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 23`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Data src/AaronOS.Modules.Schedule.Tests/ScheduleSchemaTests.cs
git commit -m "Add Routine and RoutineCompletion entities"
```

---

## Task 8: RoutineScheduler

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Routines/RoutineDueState.cs`
- Create: `src/AaronOS.Modules.Schedule/Routines/RoutineScheduler.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/RoutineSchedulerTests.cs`

**Interfaces:**
- Consumes: `Routine`, `RoutineCompletion`, `DayOfWeekFlagsExtensions.Includes`.
- Produces:
  - `record RoutineDueState(int RoutineId, DateOnly NextDue, int OverdueByDays, DateTime? LastCompletedAt)` with `bool IsDue` and `bool IsOverdue`
  - `static RoutineDueState RoutineScheduler.Evaluate(Routine routine, IReadOnlyList<RoutineCompletion> completions, DateOnly today)`
  - `static IReadOnlyList<RoutineDueState> RoutineScheduler.EvaluateAll(IReadOnlyList<Routine> routines, IReadOnlyList<RoutineCompletion> completions, DateOnly today)`

  Plan 2's `SuggestionEngine` consumes `RoutineDueState` with exactly these members.

- [ ] **Step 1: Write the failing tests**

Create `src/AaronOS.Modules.Schedule.Tests/RoutineSchedulerTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;

namespace AaronOS.Modules.Schedule.Tests;

public class RoutineSchedulerTests
{
    private static readonly DateOnly Today = new(2026, 7, 10); // a Friday

    private static Routine Interval(int days) => new()
    {
        Id = 1, Name = "Scoop litter box", Category = RoutineCategory.LitterBox, IntervalDays = days,
    };

    private static Routine OnDays(DayOfWeekFlags days) => new()
    {
        Id = 2, Name = "Take out trash", Category = RoutineCategory.Trash, PreferredDaysOfWeek = days,
    };

    private static RoutineCompletion Done(int routineId, DateOnly date) =>
        new() { RoutineId = routineId, CompletedAt = date.ToDateTime(new TimeOnly(21, 0)) };

    [Fact]
    public void NeverCompletedInterval_IsDueToday()
    {
        var state = RoutineScheduler.Evaluate(Interval(2), [], Today);

        Assert.Equal(Today, state.NextDue);
        Assert.Equal(0, state.OverdueByDays);
        Assert.True(state.IsDue);
        Assert.False(state.IsOverdue);
        Assert.Null(state.LastCompletedAt);
    }

    [Fact]
    public void CompletedToday_IsNextDueAfterTheInterval()
    {
        var state = RoutineScheduler.Evaluate(Interval(2), [Done(1, Today)], Today);

        Assert.Equal(Today.AddDays(2), state.NextDue);
        Assert.False(state.IsDue);
        Assert.Equal(0, state.OverdueByDays);
    }

    [Fact]
    public void OverdueInterval_ReportsDaysPastDue()
    {
        // Completed 5 days ago on a 2-day interval: due 3 days ago.
        var state = RoutineScheduler.Evaluate(Interval(2), [Done(1, Today.AddDays(-5))], Today);

        Assert.Equal(Today.AddDays(-3), state.NextDue);
        Assert.Equal(3, state.OverdueByDays);
        Assert.True(state.IsOverdue);
    }

    [Fact]
    public void UsesTheMostRecentCompletion_NotTheFirst()
    {
        var completions = new[] { Done(1, Today.AddDays(-9)), Done(1, Today.AddDays(-1)), Done(1, Today.AddDays(-5)) };

        var state = RoutineScheduler.Evaluate(Interval(2), completions, Today);

        Assert.Equal(Today.AddDays(1), state.NextDue);
        Assert.Equal(Today.AddDays(-1).ToDateTime(new TimeOnly(21, 0)), state.LastCompletedAt);
    }

    [Fact]
    public void WeekdayPinned_IsDueOnItsWeekday()
    {
        // Today is Friday; the routine is pinned to Friday and hasn't been done today.
        var state = RoutineScheduler.Evaluate(OnDays(DayOfWeekFlags.Friday), [], Today);

        Assert.Equal(Today, state.NextDue);
        Assert.True(state.IsDue);
    }

    [Fact]
    public void WeekdayPinned_SkipsAWeekdayAlreadyCompleted()
    {
        var state = RoutineScheduler.Evaluate(OnDays(DayOfWeekFlags.Friday), [Done(2, Today)], Today);

        Assert.Equal(Today.AddDays(7), state.NextDue);
        Assert.False(state.IsDue);
    }

    [Fact]
    public void WeekdayPinned_MissedDay_IsOverdue()
    {
        // Pinned to Tuesday, last done two Tuesdays ago, today is Friday: Tuesday the 7th was missed.
        var state = RoutineScheduler.Evaluate(OnDays(DayOfWeekFlags.Tuesday), [Done(2, new DateOnly(2026, 6, 30))], Today);

        Assert.Equal(new DateOnly(2026, 7, 7), state.NextDue);
        Assert.Equal(3, state.OverdueByDays);
    }

    [Fact]
    public void EvaluateAll_SkipsInactiveRoutines_AndPartitionsCompletionsByRoutine()
    {
        var litter = Interval(2);
        var trash = OnDays(DayOfWeekFlags.Friday);
        var retired = Interval(1);
        retired.Id = 3;
        retired.IsActive = false;

        var states = RoutineScheduler.EvaluateAll([litter, trash, retired], [Done(1, Today)], Today);

        Assert.Equal([1, 2], states.Select(s => s.RoutineId));
        Assert.Equal(Today.AddDays(2), states[0].NextDue); // used only routine 1's completion
        Assert.Equal(Today, states[1].NextDue);
    }

    [Fact]
    public void MisconfiguredRoutine_ThrowsRatherThanGuessing()
    {
        var broken = new Routine { Id = 9, Name = "Neither", Category = RoutineCategory.Other };

        Assert.Throws<InvalidOperationException>(() => RoutineScheduler.Evaluate(broken, [], Today));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `RoutineScheduler` and `RoutineDueState`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Routines/RoutineDueState.cs`:

`IsDue` means "next due is today or earlier", but a record has no clock and must not reach for `DateTime.Today` — that would make it untestable at an arbitrary date, which is the whole point of passing `today` into `Evaluate`. So `IsDue` is a stored constructor parameter, not a computed property:

```csharp
namespace AaronOS.Modules.Schedule.Routines;

/// <param name="OverdueByDays">Zero when the routine is due today or later; otherwise how many
/// days past <see cref="NextDue"/> it is.</param>
/// <param name="IsDue">True when <see cref="NextDue"/> is on or before the date passed to
/// <see cref="RoutineScheduler.Evaluate"/>. Stored rather than computed because a record has no
/// clock of its own and must not reach for DateTime.Today.</param>
public sealed record RoutineDueState(
    int RoutineId,
    DateOnly NextDue,
    int OverdueByDays,
    DateTime? LastCompletedAt,
    bool IsDue)
{
    public bool IsOverdue => OverdueByDays > 0;
}
```

`src/AaronOS.Modules.Schedule/Routines/RoutineScheduler.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Routines;

/// <summary>
/// Pure next-due computation. Takes today as a parameter rather than reading the clock so the
/// rules are testable at any date (see RoutineSchedulerTests).
/// </summary>
public static class RoutineScheduler
{
    public static IReadOnlyList<RoutineDueState> EvaluateAll(
        IReadOnlyList<Routine> routines,
        IReadOnlyList<RoutineCompletion> completions,
        DateOnly today)
    {
        var byRoutine = completions.GroupBy(c => c.RoutineId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoutineCompletion>)g.ToList());

        return routines
            .Where(r => r.IsActive)
            .Select(r => Evaluate(
                r,
                byRoutine.TryGetValue(r.Id, out var mine) ? mine : [],
                today))
            .ToList();
    }

    public static RoutineDueState Evaluate(
        Routine routine,
        IReadOnlyList<RoutineCompletion> completions,
        DateOnly today)
    {
        var last = completions.Count == 0 ? null : (DateTime?)completions.Max(c => c.CompletedAt);

        var nextDue = routine switch
        {
            { IntervalDays: > 0 } => NextIntervalDue(routine.IntervalDays!.Value, last, today),
            { PreferredDaysOfWeek: { } days } when days != DayOfWeekFlags.None => NextWeekdayDue(days, last, today),
            _ => throw new InvalidOperationException(
                $"Routine {routine.Id} ('{routine.Name}') has neither IntervalDays nor PreferredDaysOfWeek set."),
        };

        var overdue = nextDue < today ? today.DayNumber - nextDue.DayNumber : 0;
        return new RoutineDueState(routine.Id, nextDue, overdue, last, IsDue: nextDue <= today);
    }

    private static DateOnly NextIntervalDue(int intervalDays, DateTime? last, DateOnly today)
        => last is null
            ? today                                                   // never done: do it now
            : DateOnly.FromDateTime(last.Value).AddDays(intervalDays);

    /// <summary>
    /// The earliest matching weekday that hasn't been completed. With no completion, that is the
    /// next matching weekday on or after today. With one, it is the next matching weekday strictly
    /// after the completion — which is how a missed Tuesday shows up as overdue rather than being
    /// silently rolled forward to next Tuesday.
    /// </summary>
    private static DateOnly NextWeekdayDue(DayOfWeekFlags days, DateTime? last, DateOnly today)
    {
        var searchFrom = last is null ? today : DateOnly.FromDateTime(last.Value).AddDays(1);

        for (var date = searchFrom; date < searchFrom.AddDays(8); date = date.AddDays(1))
        {
            if (days.Includes(date.DayOfWeek)) return date;
        }

        // Unreachable for any non-None flag set: eight consecutive days cover every weekday.
        throw new InvalidOperationException($"No weekday in {days} matched within 8 days of {searchFrom}.");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 32`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Routines src/AaronOS.Modules.Schedule.Tests/RoutineSchedulerTests.cs
git commit -m "Add RoutineScheduler next-due and overdue computation"
```

---

## Task 9: Today page

**Files:**
- Create: `src/AaronOS.Modules.Schedule/ViewModels/TodayViewModel.cs`
- Modify: `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml`
- Modify: `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `AgendaBuilder.Build`, `AgendaDay`, `IDbContextFactory<AaronOsDbContext>`.
- Produces: `TodayViewModel` with `ObservableCollection<AgendaEntry> Entries`, `ObservableCollection<FreeGap> FreeGaps`, `string DateHeading`, and `LoadCommand`. Plan 2 adds a suggestions collection to this same ViewModel.

- [ ] **Step 1: Write the ViewModel**

There is no unit test for this task: the ViewModel is a thin database read handing materialised lists to `AgendaBuilder`, which is already covered by 19 tests. Verification is manual, in the running app (Step 3). Do not add an EF-backed test that merely re-asserts `AgendaBuilder` behaviour through a database.

`src/AaronOS.Modules.Schedule/ViewModels/TodayViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class TodayViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<AgendaEntry> Entries { get; } = [];
    public ObservableCollection<FreeGap> FreeGaps { get; } = [];

    // ponytail: field-backed [ObservableProperty] — the partial-property generator doesn't run
    // in this environment. See docs/MODULE_GUIDELINES.md.
    [ObservableProperty]
    private string _dateHeading = "";

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            DateHeading = today.ToString("dddd, MMMM d");

            await using var db = await dbContextFactory.CreateDbContextAsync();

            // Materialise before handing to AgendaBuilder: it works on plain lists, and DateOnly
            // comparisons plus the computed properties on these entities are not translatable.
            var blocks = await db.Set<ScheduleBlock>().Where(b => b.IsActive).ToListAsync();
            var exceptions = await db.Set<ScheduleException>()
                .Where(e => e.Date == today || e.Date == today.AddDays(-1))
                .ToListAsync();

            var day = AgendaBuilder.Build(today, today, blocks, exceptions, []).Single();

            Entries.Clear();
            foreach (var entry in day.Entries) Entries.Add(entry);

            FreeGaps.Clear();
            foreach (var gap in day.FreeGaps) FreeGaps.Add(gap);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
```

The exception query pulls yesterday as well as today, because `AgendaBuilder` starts a day early to pick up a wrapping block from the night before and would otherwise apply that night's cancellation incorrectly.

Add the CommunityToolkit using and register the ViewModel. In `ScheduleModule.RegisterServices`:

```csharp
        services.AddTransient<TodayViewModel>();
```

with `using AaronOS.Modules.Schedule.ViewModels;` at the top of `ScheduleModule.cs`.

- [ ] **Step 2: Write the page**

Replace `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Schedule.Views.TodayPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    mc:Ignorable="d">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16">
            <ui:TextBlock Text="{Binding DateHeading}" FontTypography="Subtitle" Margin="0,0,0,12" />

            <ui:Card Margin="0,0,0,12">
                <StackPanel>
                    <ui:TextBlock Text="Schedule" FontTypography="BodyStrong" Margin="0,0,0,8" />
                    <ItemsControl ItemsSource="{Binding Entries}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="140" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="{Binding Start, StringFormat='{}{0:hh\\:mm}'}" />
                                    <TextBlock Grid.Column="1" Text="{Binding Label}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ui:Card>

            <ui:Card>
                <StackPanel>
                    <ui:TextBlock Text="Free time" FontTypography="BodyStrong" Margin="0,0,0,8" />
                    <ItemsControl ItemsSource="{Binding FreeGaps}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Margin="0,2">
                                    <Run Text="{Binding Start, StringFormat='{}{0:hh\\:mm}', Mode=OneWay}" />
                                    <Run Text="–" />
                                    <Run Text="{Binding End, StringFormat='{}{0:hh\\:mm}', Mode=OneWay}" />
                                    <Run Text=" (" /><Run Text="{Binding Minutes, Mode=OneWay}" /><Run Text=" min)" />
                                </TextBlock>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ui:Card>
        </StackPanel>
    </ScrollViewer>
</Page>
```

Replace `src/AaronOS.Modules.Schedule/Views/TodayPage.xaml.cs`:

```csharp
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class TodayPage : Page
{
    public TodayViewModel ViewModel { get; }

    public TodayPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<TodayViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
```

- [ ] **Step 3: Verify in the running app**

The database has no schedule blocks yet, so this checks the plumbing, not the content.

Run: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`

Expected: Schedule → Today shows today's date as the heading, an empty "Schedule" card, and one "Free time" row reading `00:00 – 24:00 (1440 min)` — the empty-day gap from Task 6, proving `AgendaBuilder` ran end to end against the real database. Close the app.

If the window closes or an error dialog appears mentioning `no such table: ScheduleBlocks`, `SchemaBootstrapper` did not create the new tables; confirm `ScheduleModule` is in the module array in `App.xaml.cs`.

- [ ] **Step 4: Run the tests to confirm nothing regressed**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 32`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add Today page showing the day's agenda and free gaps"
```

---

## Task 10: Week page with block and exception editing

**Files:**
- Create: `src/AaronOS.Modules.Schedule/ViewModels/WeekViewModel.cs`
- Create: `src/AaronOS.Modules.Schedule/Views/WeekPage.xaml`
- Create: `src/AaronOS.Modules.Schedule/Views/WeekPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `AgendaBuilder.Build`, `ScheduleBlock`, `ScheduleException`.
- Produces: `WeekViewModel` with `ObservableCollection<AgendaDay> Days`, `ObservableCollection<ScheduleBlock> Blocks`, `LoadCommand`, `SaveBlockCommand`, `DeleteBlockCommand`, `AddExceptionCommand`, `PreviousWeekCommand`, `NextWeekCommand`, and editor properties for a new block.

- [ ] **Step 1: Write the ViewModel**

Again no unit test: the logic under test is `AgendaBuilder`, already covered. This ViewModel reads, writes, and delegates.

`src/AaronOS.Modules.Schedule/ViewModels/WeekViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class WeekViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<AgendaDay> Days { get; } = [];
    public ObservableCollection<ScheduleBlock> Blocks { get; } = [];

    public IReadOnlyList<ScheduleBlockKind> Kinds { get; } = Enum.GetValues<ScheduleBlockKind>();

    [ObservableProperty]
    private DateOnly _weekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Now));

    [ObservableProperty]
    private string _weekHeading = "";

    // New-block editor fields.
    [ObservableProperty]
    private string _newLabel = "";

    [ObservableProperty]
    private ScheduleBlockKind _newKind = ScheduleBlockKind.Work;

    [ObservableProperty]
    private bool _newMonday = true;

    [ObservableProperty]
    private bool _newTuesday = true;

    [ObservableProperty]
    private bool _newWednesday = true;

    [ObservableProperty]
    private bool _newThursday = true;

    [ObservableProperty]
    private bool _newFriday = true;

    [ObservableProperty]
    private bool _newSaturday;

    [ObservableProperty]
    private bool _newSunday;

    /// <summary>Entered as "HH:mm" text rather than through a NumberBox pair — a time of day is one
    /// value, and ui:NumberBox's double/NaN handling makes two-box entry worse, not better.</summary>
    [ObservableProperty]
    private string _newStartText = "08:00";

    [ObservableProperty]
    private string _newEndText = "17:00";

    [ObservableProperty]
    private string? _validationMessage;

    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7)); // Monday-first

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var end = WeekStart.AddDays(6);
            WeekHeading = $"{WeekStart:MMM d} – {end:MMM d, yyyy}";

            await using var db = await dbContextFactory.CreateDbContextAsync();
            var blocks = await db.Set<ScheduleBlock>().ToListAsync();
            var exceptions = await db.Set<ScheduleException>()
                .Where(e => e.Date >= WeekStart.AddDays(-1) && e.Date <= end)
                .ToListAsync();

            Blocks.Clear();
            foreach (var block in blocks.OrderBy(b => b.StartTime)) Blocks.Add(block);

            Days.Clear();
            foreach (var day in AgendaBuilder.Build(WeekStart, end, blocks, exceptions, [])) Days.Add(day);
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
    private async Task SaveBlockAsync()
    {
        ValidationMessage = null;

        if (string.IsNullOrWhiteSpace(NewLabel))
        {
            ValidationMessage = "Give the block a label.";
            return;
        }
        if (!TimeSpan.TryParse(NewStartText, out var start) || !TimeSpan.TryParse(NewEndText, out var end))
        {
            ValidationMessage = "Enter times as HH:mm.";
            return;
        }
        if (start == end)
        {
            ValidationMessage = "Start and end can't be the same time.";
            return;
        }

        var days = SelectedDays();
        if (days == DayOfWeekFlags.None)
        {
            ValidationMessage = "Pick at least one day.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new ScheduleBlock
        {
            Kind = NewKind,
            Label = NewLabel.Trim(),
            DaysOfWeek = days,
            StartTime = start,
            EndTime = end,
            EffectiveFrom = WeekStart,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        NewLabel = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteBlockAsync(ScheduleBlock block)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(await db.Set<ScheduleBlock>().SingleAsync(b => b.Id == block.Id));
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    /// <summary>Cancels every block on a date — the PTO case, which is what an exception is
    /// overwhelmingly used for. Finer-grained editing can come later if it's actually wanted.</summary>
    [RelayCommand]
    private async Task AddExceptionAsync(DateOnly date)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var existing = await db.Set<ScheduleException>().Where(e => e.Date == date).ToListAsync();
        db.RemoveRange(existing);

        foreach (var block in Blocks.Where(b => b.Kind != ScheduleBlockKind.Sleep))
        {
            db.Add(new ScheduleException
            {
                Date = date,
                ScheduleBlockId = block.Id,
                IsCancelled = true,
                Note = "Day off",
            });
        }

        await db.SaveChangesAsync();
        await LoadAsync();
    }

    private DayOfWeekFlags SelectedDays()
    {
        var days = DayOfWeekFlags.None;
        if (NewMonday) days |= DayOfWeekFlags.Monday;
        if (NewTuesday) days |= DayOfWeekFlags.Tuesday;
        if (NewWednesday) days |= DayOfWeekFlags.Wednesday;
        if (NewThursday) days |= DayOfWeekFlags.Thursday;
        if (NewFriday) days |= DayOfWeekFlags.Friday;
        if (NewSaturday) days |= DayOfWeekFlags.Saturday;
        if (NewSunday) days |= DayOfWeekFlags.Sunday;
        return days;
    }
}
```

Register it in `ScheduleModule.RegisterServices`:

```csharp
        services.AddTransient<WeekViewModel>();
```

- [ ] **Step 2: Write the page**

`src/AaronOS.Modules.Schedule/Views/WeekPage.xaml` — a week column list, a block list with delete, and a new-block editor. Note `ui:TextBox` for `PlaceholderText`, explicit `Margin` everywhere (WPF has no `Spacing`), and per-item buttons handled in code-behind.

```xml
<Page
    x:Class="AaronOS.Modules.Schedule.Views.WeekPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    mc:Ignorable="d">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                <ui:Button Content="◀" Command="{Binding PreviousWeekCommand}" Margin="0,0,8,0" />
                <ui:TextBlock Text="{Binding WeekHeading}" FontTypography="Subtitle" VerticalAlignment="Center" />
                <ui:Button Content="▶" Command="{Binding NextWeekCommand}" Margin="8,0,0,0" />
            </StackPanel>

            <ItemsControl ItemsSource="{Binding Days}" Margin="0,0,0,16">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <ui:Card Margin="0,0,0,8">
                            <StackPanel>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <ui:TextBlock Grid.Column="0" FontTypography="BodyStrong"
                                                  Text="{Binding Date, StringFormat='{}{0:ddd MMM d}'}" />
                                    <ui:Button Grid.Column="1" Content="Day off" Click="DayOff_Click" />
                                </Grid>
                                <ItemsControl ItemsSource="{Binding Entries}" Margin="0,6,0,0">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <TextBlock Margin="0,1">
                                                <Run Text="{Binding Start, StringFormat='{}{0:hh\\:mm}', Mode=OneWay}" />
                                                <Run Text="–" />
                                                <Run Text="{Binding End, StringFormat='{}{0:hh\\:mm}', Mode=OneWay}" />
                                                <Run Text="  " />
                                                <Run Text="{Binding Label, Mode=OneWay}" />
                                            </TextBlock>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </ui:Card>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <ui:Card Margin="0,0,0,12">
                <StackPanel>
                    <ui:TextBlock Text="Recurring blocks" FontTypography="BodyStrong" Margin="0,0,0,8" />
                    <ItemsControl ItemsSource="{Binding Blocks}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" VerticalAlignment="Center">
                                        <Run Text="{Binding Label, Mode=OneWay}" />
                                        <Run Text=" · " />
                                        <Run Text="{Binding DaysOfWeek, Mode=OneWay}" />
                                        <Run Text=" · " />
                                        <Run Text="{Binding StartTime, StringFormat='{}{0:hh\\:mm}', Mode=OneWay}" />
                                        <Run Text="–" />
                                        <Run Text="{Binding EndTime, StringFormat='{}{0:hh\\:mm}', Mode=OneWay}" />
                                    </TextBlock>
                                    <ui:Button Grid.Column="1" Content="Delete" Click="DeleteBlock_Click" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ui:Card>

            <ui:Card>
                <StackPanel>
                    <ui:TextBlock Text="Add a block" FontTypography="BodyStrong" Margin="0,0,0,8" />
                    <ui:TextBox PlaceholderText="Label (e.g. Core hours)" Text="{Binding NewLabel, Mode=TwoWay}" Margin="0,0,0,8" />
                    <ComboBox ItemsSource="{Binding Kinds}" SelectedItem="{Binding NewKind, Mode=TwoWay}" Margin="0,0,0,8" />
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                        <CheckBox Content="Mon" IsChecked="{Binding NewMonday, Mode=TwoWay}" Margin="0,0,8,0" />
                        <CheckBox Content="Tue" IsChecked="{Binding NewTuesday, Mode=TwoWay}" Margin="0,0,8,0" />
                        <CheckBox Content="Wed" IsChecked="{Binding NewWednesday, Mode=TwoWay}" Margin="0,0,8,0" />
                        <CheckBox Content="Thu" IsChecked="{Binding NewThursday, Mode=TwoWay}" Margin="0,0,8,0" />
                        <CheckBox Content="Fri" IsChecked="{Binding NewFriday, Mode=TwoWay}" Margin="0,0,8,0" />
                        <CheckBox Content="Sat" IsChecked="{Binding NewSaturday, Mode=TwoWay}" Margin="0,0,8,0" />
                        <CheckBox Content="Sun" IsChecked="{Binding NewSunday, Mode=TwoWay}" />
                    </StackPanel>
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                        <ui:TextBox PlaceholderText="Start HH:mm" Text="{Binding NewStartText, Mode=TwoWay}" Width="120" Margin="0,0,8,0" />
                        <ui:TextBox PlaceholderText="End HH:mm" Text="{Binding NewEndText, Mode=TwoWay}" Width="120" />
                    </StackPanel>
                    <ui:TextBlock Text="{Binding ValidationMessage}" Foreground="{DynamicResource SystemFillColorCriticalBrush}" Margin="0,0,0,8" />
                    <ui:Button Content="Add block" Appearance="Primary" Command="{Binding SaveBlockCommand}" HorizontalAlignment="Left" />
                </StackPanel>
            </ui:Card>
        </StackPanel>
    </ScrollViewer>
</Page>
```

`src/AaronOS.Modules.Schedule/Views/WeekPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class WeekPage : Page
{
    public WeekViewModel ViewModel { get; }

    public WeekPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<WeekViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScheduleBlock block })
        {
            _ = ViewModel.DeleteBlockCommand.ExecuteAsync(block);
        }
    }

    private void DayOff_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AgendaDay day })
        {
            _ = ViewModel.AddExceptionCommand.ExecuteAsync(day.Date);
        }
    }
}
```

Add a Week button to the shell. In `ScheduleShellPage.xaml`, inside the `StackPanel`:

```xml
            <ui:Button Content="Week" Click="Week_Click" Margin="0,0,8,0" />
```

and in `ScheduleShellPage.xaml.cs`:

```csharp
    private void Week_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new WeekPage());
```

- [ ] **Step 3: Verify in the running app**

Run: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`

Walk through this exact sequence and confirm each result:

1. Schedule → Week shows the current Monday-to-Sunday range in the heading.
2. Add a block labelled `Core hours`, kind `Work`, Mon–Fri checked, `08:00` to `17:00`. It appears under "Recurring blocks" and on all five weekday cards, not on Saturday or Sunday.
3. Add a block labelled `Sleep`, kind `Sleep`, all seven days, `23:00` to `07:00`. Each day now shows two sleep rows — `00:00–24:00`'s tail at the top and the `23:00` segment at the bottom — which is the midnight split from Task 6 rendering correctly.
4. Click **Day off** on Wednesday. The work row disappears from Wednesday only; sleep stays.
5. Go to Today. If today is a weekday, the work and sleep rows appear and "Free time" shows `07:00 – 08:00` and `17:00 – 23:00`.
6. Click ◀ then ▶ and confirm the heading moves a week each way and the blocks still render.
7. Leave the label blank and click **Add block**; the message "Give the block a label." appears and nothing is saved. Enter `99:99` as a start time and confirm "Enter times as HH:mm."

Close the app.

- [ ] **Step 4: Run the tests to confirm nothing regressed**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 32`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add Week page with recurring block editing and day-off exceptions"
```

---

## Task 11: Routines page

**Files:**
- Create: `src/AaronOS.Modules.Schedule/ViewModels/RoutinesViewModel.cs`
- Create: `src/AaronOS.Modules.Schedule/ViewModels/RoutineRow.cs`
- Create: `src/AaronOS.Modules.Schedule/Views/RoutinesPage.xaml`
- Create: `src/AaronOS.Modules.Schedule/Views/RoutinesPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml`
- Modify: `src/AaronOS.Modules.Schedule/Views/ScheduleShellPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `Routine`, `RoutineCompletion`, `RoutineScheduler.EvaluateAll`, `RoutineDueState`.
- Produces: `RoutineRow` (a `Routine` paired with its `RoutineDueState` for binding) and `RoutinesViewModel` with `ObservableCollection<RoutineRow> Rows`, `LoadCommand`, `CompleteCommand`, `SaveRoutineCommand`, `DeleteRoutineCommand`.

- [ ] **Step 1: Write the row type and ViewModel**

`src/AaronOS.Modules.Schedule/ViewModels/RoutineRow.cs`:

```csharp
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;

namespace AaronOS.Modules.Schedule.ViewModels;

/// <summary>A routine paired with its computed due state, so the page binds to one object per row
/// instead of correlating two collections in XAML.</summary>
public sealed record RoutineRow(Routine Routine, RoutineDueState Due)
{
    public string Name => Routine.Name;

    public string Cadence => Routine.IntervalDays is { } days
        ? $"every {days} day{(days == 1 ? "" : "s")}"
        : $"{Routine.PreferredDaysOfWeek}";

    public string DueDisplay => Due switch
    {
        { IsOverdue: true } => $"overdue by {Due.OverdueByDays} day{(Due.OverdueByDays == 1 ? "" : "s")}",
        { IsDue: true } => "due today",
        _ => $"next {Due.NextDue:ddd MMM d}",
    };

    public string LastDoneDisplay => Due.LastCompletedAt is { } last
        ? $"last done {last:ddd MMM d}"
        : "never done";
}
```

`src/AaronOS.Modules.Schedule/ViewModels/RoutinesViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class RoutinesViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<RoutineRow> Rows { get; } = [];

    public IReadOnlyList<RoutineCategory> Categories { get; } = Enum.GetValues<RoutineCategory>();

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private RoutineCategory _newCategory = RoutineCategory.Other;

    /// <summary>ui:NumberBox binds a double and reports NaN when cleared, so NaN is the
    /// not-entered sentinel — converted to int? at save time, per MODULE_GUIDELINES.md.</summary>
    [ObservableProperty]
    private double _newIntervalDays = 2;

    [ObservableProperty]
    private double _newEstimatedMinutes = double.NaN;

    [ObservableProperty]
    private string? _validationMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);

            await using var db = await dbContextFactory.CreateDbContextAsync();
            var routines = await db.Set<Routine>().ToListAsync();
            var completions = await db.Set<RoutineCompletion>().ToListAsync();

            var states = RoutineScheduler.EvaluateAll(routines, completions, today)
                .ToDictionary(s => s.RoutineId);

            Rows.Clear();
            foreach (var routine in routines.Where(r => r.IsActive))
            {
                if (!states.TryGetValue(routine.Id, out var due)) continue;
                Rows.Add(new RoutineRow(routine, due));
            }

            // Most pressing first: overdue by the most days, then due today, then upcoming.
            var ordered = Rows.OrderByDescending(r => r.Due.OverdueByDays)
                .ThenBy(r => r.Due.NextDue)
                .ToList();
            Rows.Clear();
            foreach (var row in ordered) Rows.Add(row);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CompleteAsync(RoutineRow row)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new RoutineCompletion { RoutineId = row.Routine.Id, CompletedAt = DateTime.Now });
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SaveRoutineAsync()
    {
        ValidationMessage = null;

        if (string.IsNullOrWhiteSpace(NewName))
        {
            ValidationMessage = "Give the routine a name.";
            return;
        }

        var interval = double.IsNaN(NewIntervalDays) ? 0 : (int)NewIntervalDays;
        if (interval <= 0)
        {
            ValidationMessage = "Interval must be at least 1 day.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new Routine
        {
            Name = NewName.Trim(),
            Category = NewCategory,
            IntervalDays = interval,
            EstimatedMinutes = double.IsNaN(NewEstimatedMinutes) ? null : (int)NewEstimatedMinutes,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        NewName = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteRoutineAsync(RoutineRow row)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(await db.Set<Routine>().SingleAsync(r => r.Id == row.Routine.Id));
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}
```

This editor creates interval routines only. Weekday-pinned routines (trash night) are supported by the entity and by `RoutineScheduler`, and adding a weekday picker here is a small follow-up — noted rather than silently dropped.

Register in `ScheduleModule.RegisterServices`:

```csharp
        services.AddTransient<RoutinesViewModel>();
```

- [ ] **Step 2: Write the page**

`src/AaronOS.Modules.Schedule/Views/RoutinesPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Schedule.Views.RoutinesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    mc:Ignorable="d">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16">
            <ui:TextBlock Text="Routines" FontTypography="Subtitle" Margin="0,0,0,12" />

            <ItemsControl ItemsSource="{Binding Rows}" Margin="0,0,0,16">
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
                                    <ui:TextBlock Text="{Binding Name}" FontTypography="BodyStrong" />
                                    <TextBlock>
                                        <Run Text="{Binding Cadence, Mode=OneWay}" />
                                        <Run Text=" · " />
                                        <Run Text="{Binding DueDisplay, Mode=OneWay}" />
                                        <Run Text=" · " />
                                        <Run Text="{Binding LastDoneDisplay, Mode=OneWay}" />
                                    </TextBlock>
                                </StackPanel>
                                <ui:Button Grid.Column="1" Content="Done" Appearance="Primary"
                                           Click="Complete_Click" Margin="0,0,8,0" />
                                <ui:Button Grid.Column="2" Content="Delete" Click="Delete_Click" />
                            </Grid>
                        </ui:Card>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <ui:Card>
                <StackPanel>
                    <ui:TextBlock Text="Add a routine" FontTypography="BodyStrong" Margin="0,0,0,8" />
                    <ui:TextBox PlaceholderText="Name (e.g. Scoop litter box)" Text="{Binding NewName, Mode=TwoWay}" Margin="0,0,0,8" />
                    <ComboBox ItemsSource="{Binding Categories}" SelectedItem="{Binding NewCategory, Mode=TwoWay}" Margin="0,0,0,8" />
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                        <ui:NumberBox PlaceholderText="Every N days" Value="{Binding NewIntervalDays, Mode=TwoWay}" Width="140" Margin="0,0,8,0" />
                        <ui:NumberBox PlaceholderText="Minutes" Value="{Binding NewEstimatedMinutes, Mode=TwoWay}" Width="140" />
                    </StackPanel>
                    <ui:TextBlock Text="{Binding ValidationMessage}" Foreground="{DynamicResource SystemFillColorCriticalBrush}" Margin="0,0,0,8" />
                    <ui:Button Content="Add routine" Appearance="Primary" Command="{Binding SaveRoutineCommand}" HorizontalAlignment="Left" />
                </StackPanel>
            </ui:Card>
        </StackPanel>
    </ScrollViewer>
</Page>
```

`src/AaronOS.Modules.Schedule/Views/RoutinesPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using AaronOS.Core;
using AaronOS.Modules.Schedule.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Schedule.Views;

public sealed partial class RoutinesPage : Page
{
    public RoutinesViewModel ViewModel { get; }

    public RoutinesPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<RoutinesViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RoutineRow row })
        {
            _ = ViewModel.CompleteCommand.ExecuteAsync(row);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RoutineRow row })
        {
            _ = ViewModel.DeleteRoutineCommand.ExecuteAsync(row);
        }
    }
}
```

Add the shell button. In `ScheduleShellPage.xaml`:

```xml
            <ui:Button Content="Routines" Click="Routines_Click" />
```

and in `ScheduleShellPage.xaml.cs`:

```csharp
    private void Routines_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new RoutinesPage());
```

- [ ] **Step 3: Verify in the running app**

Run: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`

Confirm this sequence:

1. Schedule → Routines shows an empty list and the add form.
2. Add `Scoop litter box`, category `LitterBox`, every `2` days, `5` minutes. The row appears reading "every 2 days · due today · never done".
3. Click **Done**. The row changes to "next" a date two days out and "last done" today.
4. Add `Vacuum`, every `7` days. It appears above or below by due date — the list is sorted most-pressing-first, so the litter box (not due) sits below the new "due today" vacuum.
5. Clear the interval box and click **Add routine**; "Interval must be at least 1 day." appears and nothing is saved.
6. Click **Delete** on `Vacuum`; it disappears and does not come back after navigating away and returning.

Close the app.

- [ ] **Step 4: Run the tests to confirm nothing regressed**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 32`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add Routines page with completion logging and due-state display"
```

---

## Definition of done for Plan 1

- `dotnet build AaronOS.slnx --nologo` succeeds.
- `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo` reports 32 passing tests, 0 failing.
- The app launches, the Schedule nav item appears, and Today, Week, and Routines all load against the real database.
- Recurring blocks, day-off exceptions, routines, and completions all persist across an app restart.
- No external network call exists anywhere in the module.

## Deferred to later plans

Recorded so nothing is lost: sleep logging and `SleepPlanner`, goals and releases, `SuggestionEngine`, notifications, external calendars, and Gmail extraction. Also a weekday picker in the routine editor (the entity and `RoutineScheduler` already support weekday-pinned routines; only the add-form is interval-only), and finer-grained exception editing than the whole-day "Day off" button.

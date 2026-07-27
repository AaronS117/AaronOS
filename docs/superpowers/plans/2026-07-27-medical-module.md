# Medical Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `AaronOS.Modules.Medical` — manual medical-history tracking across nine record types plus a C-CDA importer for MyChart exports — per `docs/superpowers/specs/2026-07-27-medical-module-design.md`.

**Architecture:** A compiled-in `IAppModule` like the other three modules: own entities auto-discovered by the shared `AaronOsDbContext`, own ViewModels/Pages, one project reference from `AaronOS.App`, one line appended to the module array. Import logic is pure functions over XML text and in-memory snapshots, so it is unit-testable without EF or file I/O.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF + `Wpf.Ui.Controls` (WPF-UI 4.3.0), `LiveChartsCore.SkiaSharpView.WPF` 2.0.5, EF Core + SQLite via the shared context, `CommunityToolkit.Mvvm`, xunit 2.5.3.

## Global Constraints

- `TargetFramework` `net8.0-windows`, `UseWPF` true, `LangVersion` 13.0, `Nullable` enable. Test project targets `net8.0-windows` with no `UseWPF`.
- Field-backed `[ObservableProperty] private T _x;` only — the partial-property generator does not work in this environment.
- **`ui:NumberBox.Value` binds to `double?`, and `null` is "not entered". Never `double.NaN`** — see the corrected note in `docs/MODULE_GUIDELINES.md`. A non-nullable double seeded with NaN renders a stray glyph and suppresses `PlaceholderText`.
- **No page may nest a `ScrollViewer` or `ListView`** — WPF-UI's `NavigationView` already hosts page content in its own `DynamicScrollViewer`. An inner scroller is allowed only with an explicit `Height` (bounded). See the note in `FinanceDashboardPage.xaml`.
- Use the design system in `App.xaml`: `PageTitleText`, `EyebrowText` (text typed in UPPERCASE), `HeroMetricText`, `NumericText`, `BodyText`, `CaptionText`, `SurfaceCard`, `RowDivider`, brushes `ReactorGlow`, `MoneyIn`, `ExpirySoon`, `ExpiryPast`, converter `BoolToVis`. No `ui:Card` — use `Border Style="{DynamicResource SurfaceCard}"`.
- Compute display strings on entities as getter-only properties (EF ignores them), following `FinanceTransaction.DateDisplay` — prefer that over `IValueConverter`.
- Every entity needs an `IEntityTypeConfiguration<T>` under `Data/`. Never edit `AaronOsDbContext`.
- No EF migrations. `SchemaBootstrapper` (already in Core) creates tables for newly registered modules against an existing database — nothing manual is required.
- Never reference another module's entities. Body weight/height stay `BodyMeasurements`' business.
- `App.xaml.cs` module array and `AaronOS.slnx` get **appended to**, never reordered — Body Measurements stays `modules[0]`.
- Reference implementations to mirror for structure: `src/AaronOS.Modules.Nutrition/Views/InventoryPage.xaml` (ledger + add-form page), `src/AaronOS.Modules.Finance/Views/FinanceDashboardPage.xaml` (hero + sections dashboard), `src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml` (module shell).

---

## File Structure

```
src/AaronOS.Modules.Medical/
  AaronOS.Modules.Medical.csproj
  MedicalModule.cs
  Data/
    RecordSource.cs  ConditionStatus.cs  AllergySeverity.cs
    MedicalCondition.cs  + Configuration
    Medication.cs        + Configuration
    Allergy.cs           + Configuration
    Immunization.cs      + Configuration
    MedicalProcedure.cs  + Configuration
    MedicalVisit.cs      + Configuration
    LabResult.cs         + Configuration
    Provider.cs          + Configuration
    MedicalDocument.cs   + Configuration
  Import/
    Hl7Time.cs          — HL7 TS parsing
    CcdaModels.cs       — parsed-record records + CcdaDocument
    CcdaParser.cs       — XML -> CcdaDocument
    ImportPlanner.cs    — CcdaDocument + ExistingKeys -> ImportPlan
  ViewModels/
    MedicalOverviewViewModel.cs  HistoryViewModel.cs  MedicationsViewModel.cs
    VisitsViewModel.cs  LabsViewModel.cs  ImportViewModel.cs
  Views/
    MedicalShellPage.xaml(.cs)  MedicalOverviewPage.xaml(.cs)  HistoryPage.xaml(.cs)
    MedicationsPage.xaml(.cs)   VisitsPage.xaml(.cs)  LabsPage.xaml(.cs)  ImportPage.xaml(.cs)
src/AaronOS.Modules.Medical.Tests/
  AaronOS.Modules.Medical.Tests.csproj
  Hl7TimeTests.cs  CcdaParserTests.cs  ImportPlannerTests.cs  EntityLogicTests.cs
  Fixtures/CcdaFixtures.cs   — C-CDA fragments authored from the R2.1 spec
```

---

### Task 1: Scaffold the module (walking skeleton)

**Files:**
- Create: `src/AaronOS.Modules.Medical/AaronOS.Modules.Medical.csproj`
- Create: `src/AaronOS.Modules.Medical/MedicalModule.cs`
- Create: `src/AaronOS.Modules.Medical/Views/MedicalShellPage.xaml` + `.xaml.cs`
- Modify: `AaronOS.slnx`, `src/AaronOS.App/AaronOS.App.csproj`, `src/AaronOS.App/App.xaml.cs`

**Interfaces:**
- Produces: `MedicalModule : IAppModule`, `Id => "medical"`, `HomePageType => typeof(MedicalShellPage)`. Later tasks add registrations and replace the shell's placeholder content — do not rename the class or `Id`.

- [ ] **Step 1: csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\AaronOS.Core\AaronOS.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.0.5" />
    <PackageReference Include="WPF-UI" Version="4.3.0" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <LangVersion>13.0</LangVersion>
    <RootNamespace>AaronOS.Modules.Medical</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: `MedicalModule.cs`**

```csharp
using AaronOS.Core;
using AaronOS.Modules.Medical.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Medical;

public class MedicalModule : IAppModule
{
    public string Id => "medical";
    public string DisplayName => "Medical";
    public string IconGlyph => "HeartPulse24"; // confirm the exact SymbolRegular member in Step 5
    public Type HomePageType => typeof(MedicalShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        // Later tasks add ViewModel registrations here.
    }
}
```

- [ ] **Step 3: placeholder shell page**

`MedicalShellPage.xaml` — a `<Page>` (namespaces as in `NutritionShellPage.xaml`) whose only content is
`<TextBlock Text="Medical module coming soon" Style="{DynamicResource PageTitleText}" Margin="28,20" />`.
`MedicalShellPage.xaml.cs` — `public sealed partial class MedicalShellPage : Page` calling only `InitializeComponent()`. Task 9 replaces both.

- [ ] **Step 4: wire into solution and app**

`AaronOS.slnx`: add `<Project Path="src/AaronOS.Modules.Medical/AaronOS.Modules.Medical.csproj" />` inside the `/src/` folder.
`AaronOS.App.csproj`: add `<ProjectReference Include="..\AaronOS.Modules.Medical\AaronOS.Modules.Medical.csproj" />`.
`App.xaml.cs`: add `using AaronOS.Modules.Medical;` and **append** to the array — do not reorder:

```csharp
IAppModule[] modules = [new BodyMeasurementsModule(), new FinanceModule(), new NutritionModule(), new MedicalModule()];
```

- [ ] **Step 5: build, run, confirm the icon glyph parses**

Run: `dotnet build AaronOS.slnx` → expect 0 errors.
Run the app. `MainWindow` does `Enum.Parse<SymbolRegular>(module.IconGlyph)`, so an invalid glyph throws at startup naming the bad value. If `HeartPulse24` is not a real member, pick one that is (candidates: `Heart24`, `HeartPulse24`, `Pill24`, `Stethoscope24`) and update `IconGlyph`. Confirm a "Medical" nav item appears last and shows the placeholder.

- [ ] **Step 6: Commit**

```bash
git add AaronOS.slnx src/AaronOS.App/AaronOS.App.csproj src/AaronOS.App/App.xaml.cs src/AaronOS.Modules.Medical
git commit -m "Scaffold AaronOS.Modules.Medical module"
```

---

### Task 2: Entities, enums and EF configuration

**Files:** Create all files under `src/AaronOS.Modules.Medical/Data/` listed in File Structure.

**Interfaces:**
- Produces the nine entities, three enums, and their computed display members, referenced verbatim by every later task.

No automated test for the EF configs themselves (this repo does not test them); Task 3 tests the computed logic. Verification here is `SchemaBootstrapper` creating the tables at startup.

- [ ] **Step 1: enums**

```csharp
// RecordSource.cs
namespace AaronOS.Modules.Medical.Data;
/// <summary>Where a row came from, so imported rows stay distinguishable from hand-entered ones.</summary>
public enum RecordSource { Manual, Imported }

// ConditionStatus.cs
namespace AaronOS.Modules.Medical.Data;
public enum ConditionStatus { Active, Chronic, Resolved }

// AllergySeverity.cs
namespace AaronOS.Modules.Medical.Data;
public enum AllergySeverity { Unknown, Mild, Moderate, Severe }
```

- [ ] **Step 2: `MedicalCondition`**

```csharp
namespace AaronOS.Modules.Medical.Data;

public class MedicalCondition
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Code { get; set; }
    public DateOnly? OnsetDate { get; set; }
    public DateOnly? ResolvedDate { get; set; }
    public ConditionStatus Status { get; set; } = ConditionStatus.Active;
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    // Getter-only: EF ignores these, so no [NotMapped] is needed.
    public bool IsActive => Status != ConditionStatus.Resolved;
    public bool IsImported => Source == RecordSource.Imported;
    public string OnsetDisplay => OnsetDate?.ToString("MMM yyyy") ?? "—";
    public string StatusDisplay => Status.ToString();
}
```

`MedicalConditionConfiguration` — `HasKey(c => c.Id)`, `Property(c => c.Name).IsRequired()`, `HasIndex(c => c.ExternalId)` (non-unique: the same id may legitimately appear for different record types).

- [ ] **Step 3: `Medication`**

```csharp
namespace AaronOS.Modules.Medical.Data;

public class Medication
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Dose { get; set; }
    public string? Frequency { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? ProviderId { get; set; }
    public Provider? Provider { get; set; }
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    /// <summary>Active until an end date has passed. No end date means still being taken.</summary>
    public bool IsActive => EndDate is null || EndDate.Value >= DateOnly.FromDateTime(DateTime.Now);
    public bool IsImported => Source == RecordSource.Imported;
    public string DoseDisplay => string.Join(" · ", new[] { Dose, Frequency }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string StartedDisplay => StartDate?.ToString("MMM yyyy") ?? "—";
}
```

`MedicationConfiguration` — key, `Name` required, `HasOne(m => m.Provider).WithMany().HasForeignKey(m => m.ProviderId)`, index on `ExternalId`.

- [ ] **Step 4: `Allergy`, `Immunization`**

```csharp
public class Allergy
{
    public int Id { get; set; }
    public required string Substance { get; set; }
    public string? Reaction { get; set; }
    public AllergySeverity Severity { get; set; } = AllergySeverity.Unknown;
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    public bool IsSevere => Severity == AllergySeverity.Severe;
    public bool IsImported => Source == RecordSource.Imported;
    public string SeverityDisplay => Severity == AllergySeverity.Unknown ? "—" : Severity.ToString();
    public string ReactionDisplay => string.IsNullOrWhiteSpace(Reaction) ? "—" : Reaction;
}

public class Immunization
{
    public int Id { get; set; }
    public required string Vaccine { get; set; }
    public DateOnly? DateGiven { get; set; }
    public int? DoseNumber { get; set; }
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    public bool IsImported => Source == RecordSource.Imported;
    public string DateDisplay => DateGiven?.ToString("MMM d, yyyy") ?? "—";
    public string DoseDisplay => DoseNumber is { } n ? $"Dose {n}" : "—";
}
```

Configurations: key, required (`Substance`/`Vaccine`), index on `ExternalId`.

- [ ] **Step 5: `MedicalProcedure`, `MedicalVisit`**

```csharp
public class MedicalProcedure
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateOnly? Date { get; set; }
    public int? ProviderId { get; set; }
    public Provider? Provider { get; set; }
    public string? Facility { get; set; }
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    public bool IsImported => Source == RecordSource.Imported;
    public string DateDisplay => Date?.ToString("MMM d, yyyy") ?? "—";
}

public class MedicalVisit
{
    public int Id { get; set; }
    public DateOnly? Date { get; set; }
    public string? VisitType { get; set; }
    public int? ProviderId { get; set; }
    public Provider? Provider { get; set; }
    public string? Facility { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    public bool IsImported => Source == RecordSource.Imported;
    public string DateDisplay => Date?.ToString("MMM d, yyyy") ?? "—";
    public string TypeDisplay => string.IsNullOrWhiteSpace(VisitType) ? "Visit" : VisitType;
    public string WhereDisplay => string.IsNullOrWhiteSpace(Facility) ? "—" : Facility;
}
```

Configurations: key, provider FK as in Step 3, index on `ExternalId`.

- [ ] **Step 6: `LabResult`**

```csharp
namespace AaronOS.Modules.Medical.Data;

public class LabResult
{
    public int Id { get; set; }
    public required string TestName { get; set; }

    /// <summary>Numeric result when there is one. Null for textual results like "Negative".</summary>
    public decimal? Value { get; set; }

    /// <summary>Textual result, kept alongside Value because real results include "Negative" and
    /// "&lt;0.01" — forcing those into a decimal loses them.</summary>
    public string? ValueText { get; set; }

    public string? Unit { get; set; }
    public decimal? ReferenceLow { get; set; }
    public decimal? ReferenceHigh { get; set; }
    public DateOnly? TakenOn { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    public bool IsImported => Source == RecordSource.Imported;
    public bool HasReferenceRange => ReferenceLow is not null || ReferenceHigh is not null;

    /// <summary>True only when a numeric value falls outside a known bound. A one-sided range is
    /// still usable; no value or no range is never "out of range".</summary>
    public bool IsOutOfRange => Value is { } v
        && ((ReferenceLow is { } lo && v < lo) || (ReferenceHigh is { } hi && v > hi));

    public string ValueDisplay => Value is { } v
        ? (string.IsNullOrWhiteSpace(Unit) ? v.ToString("0.##") : $"{v:0.##} {Unit}")
        : (string.IsNullOrWhiteSpace(ValueText) ? "—" : ValueText!);

    public string RangeDisplay => (ReferenceLow, ReferenceHigh) switch
    {
        ({ } lo, { } hi) => $"{lo:0.##}–{hi:0.##}",
        ({ } lo, null) => $"≥ {lo:0.##}",
        (null, { } hi) => $"≤ {hi:0.##}",
        _ => "—"
    };

    public string TakenDisplay => TakenOn?.ToString("MMM d, yyyy") ?? "—";
}
```

`LabResultConfiguration` — key, `TestName` required, precision `(12,4)` on `Value`/`ReferenceLow`/`ReferenceHigh`, index on `TestName` (the Labs page groups by it) and on `ExternalId`.

- [ ] **Step 7: `Provider`, `MedicalDocument`**

```csharp
public class Provider
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Specialty { get; set; }
    public string? Phone { get; set; }
    public string? Facility { get; set; }
    public string? Notes { get; set; }

    public string SpecialtyDisplay => string.IsNullOrWhiteSpace(Specialty) ? "—" : Specialty;
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "—" : Phone;
}

public class MedicalDocument
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string FilePath { get; set; }
    public DateOnly AddedOn { get; set; }
    public int? VisitId { get; set; }
    public MedicalVisit? Visit { get; set; }
    public string? Notes { get; set; }

    /// <summary>Only the path is stored, never the bytes — so a moved or deleted file must be shown
    /// as missing rather than silently failing to open.</summary>
    public bool FileExists => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);
    public string StatusDisplay => FileExists ? "OK" : "File missing";
    public string AddedDisplay => AddedOn.ToString("MMM d, yyyy");
}
```

`MedicalDocument.cs` needs `using System.IO;`. Configuration: key, `Title`/`FilePath` required, `HasOne(d => d.Visit).WithMany().HasForeignKey(d => d.VisitId)`.

- [ ] **Step 8: build and confirm schema creation**

Run: `dotnet build AaronOS.slnx` → 0 errors.
Run the app once. `SchemaBootstrapper` adds the nine new tables to the existing database. Confirm no startup exception, then verify with:
`python -c "import sqlite3,os;c=sqlite3.connect(os.path.expandvars(r'%LOCALAPPDATA%\AaronOS\aaronos.db')).cursor();print(sorted(r[0] for r in c.execute(\"SELECT name FROM sqlite_master WHERE type='table'\")))"`
Expect `Allergy`, `Immunization`, `LabResult`, `MedicalCondition`, `MedicalDocument`, `MedicalProcedure`, `MedicalVisit`, `Medication`, `Provider` present alongside the existing tables, and existing row counts unchanged.

- [ ] **Step 9: Commit** — `git add src/AaronOS.Modules.Medical/Data && git commit -m "Add Medical module entities"`

---

### Task 3: Entity logic tests (TDD)

**Files:**
- Create: `src/AaronOS.Modules.Medical.Tests/AaronOS.Modules.Medical.Tests.csproj`
- Test: `src/AaronOS.Modules.Medical.Tests/EntityLogicTests.cs`
- Modify: `AaronOS.slnx`

**Interfaces:** Consumes Task 2's entities. Produces the test project every later task adds to.

- [ ] **Step 1: test csproj** — copy `src/AaronOS.Modules.Nutrition.Tests/AaronOS.Modules.Nutrition.Tests.csproj` verbatim, changing only the `ProjectReference` to `..\AaronOS.Modules.Medical\AaronOS.Modules.Medical.csproj`. Add the project to `AaronOS.slnx`.

- [ ] **Step 2: write the failing tests**

```csharp
using AaronOS.Modules.Medical.Data;

namespace AaronOS.Modules.Medical.Tests;

public class EntityLogicTests
{
    private static LabResult Lab(decimal? value, decimal? low, decimal? high, string? text = null) =>
        new() { TestName = "T", Value = value, ReferenceLow = low, ReferenceHigh = high, ValueText = text };

    [Fact] public void LabIsOutOfRange_WhenAboveHigh() => Assert.True(Lab(200, 100, 150).IsOutOfRange);
    [Fact] public void LabIsOutOfRange_WhenBelowLow() => Assert.True(Lab(50, 100, 150).IsOutOfRange);
    [Fact] public void LabInRange_WhenBetween() => Assert.False(Lab(120, 100, 150).IsOutOfRange);
    [Fact] public void LabInRange_OnBoundaries() { Assert.False(Lab(100, 100, 150).IsOutOfRange); Assert.False(Lab(150, 100, 150).IsOutOfRange); }
    [Fact] public void LabOneSidedRange_LowOnly() { Assert.True(Lab(90, 100, null).IsOutOfRange); Assert.False(Lab(110, 100, null).IsOutOfRange); }
    [Fact] public void LabOneSidedRange_HighOnly() { Assert.True(Lab(160, null, 150).IsOutOfRange); Assert.False(Lab(140, null, 150).IsOutOfRange); }
    [Fact] public void LabNeverOutOfRange_WithNoRange() => Assert.False(Lab(999, null, null).IsOutOfRange);
    [Fact] public void LabNeverOutOfRange_WithNoNumericValue() => Assert.False(Lab(null, 1, 2, "Negative").IsOutOfRange);

    [Fact]
    public void LabValueDisplay_PrefersNumericThenTextThenDash()
    {
        Assert.Equal("14.2 g/dL", new LabResult { TestName = "T", Value = 14.2m, Unit = "g/dL" }.ValueDisplay);
        Assert.Equal("Negative", new LabResult { TestName = "T", ValueText = "Negative" }.ValueDisplay);
        Assert.Equal("—", new LabResult { TestName = "T" }.ValueDisplay);
    }

    [Fact]
    public void LabRangeDisplay_CoversBothOneSidedAndNone()
    {
        Assert.Equal("100–150", Lab(1, 100, 150).RangeDisplay);
        Assert.Equal("≥ 100", Lab(1, 100, null).RangeDisplay);
        Assert.Equal("≤ 150", Lab(1, null, 150).RangeDisplay);
        Assert.Equal("—", Lab(1, null, null).RangeDisplay);
    }

    [Fact]
    public void MedicationIsActive_UntilItsEndDatePasses()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        Assert.True(new Medication { Name = "M" }.IsActive);                              // no end date
        Assert.True(new Medication { Name = "M", EndDate = today }.IsActive);              // ends today
        Assert.True(new Medication { Name = "M", EndDate = today.AddDays(30) }.IsActive);
        Assert.False(new Medication { Name = "M", EndDate = today.AddDays(-1) }.IsActive);
    }

    [Fact]
    public void ConditionIsActive_UnlessResolved()
    {
        Assert.True(new MedicalCondition { Name = "C", Status = ConditionStatus.Active }.IsActive);
        Assert.True(new MedicalCondition { Name = "C", Status = ConditionStatus.Chronic }.IsActive);
        Assert.False(new MedicalCondition { Name = "C", Status = ConditionStatus.Resolved }.IsActive);
    }

    [Fact]
    public void DocumentReportsMissingFile()
    {
        var doc = new MedicalDocument { Title = "T", FilePath = @"C:\does\not\exist\nope.pdf", AddedOn = default };
        Assert.False(doc.FileExists);
        Assert.Equal("File missing", doc.StatusDisplay);
    }
}
```

- [ ] **Step 3: run — expect FAIL to compile** if any member is missing; otherwise expect PASS.
Run: `dotnet test src/AaronOS.Modules.Medical.Tests`

- [ ] **Step 4:** fix any entity member the tests reveal as missing or wrong, then re-run until PASS.

- [ ] **Step 5: Commit** — `git add AaronOS.slnx src/AaronOS.Modules.Medical.Tests && git commit -m "Add Medical entity logic tests"`

---

### Task 4: HL7 timestamp parsing (TDD)

**Files:**
- Create: `src/AaronOS.Modules.Medical/Import/Hl7Time.cs`
- Test: `src/AaronOS.Modules.Medical.Tests/Hl7TimeTests.cs`

**Interfaces:**
- Produces `Hl7Time.ParseDate(string? value) -> DateOnly?`. Consumed by `CcdaParser` throughout.

- [ ] **Step 1: write the failing test**

```csharp
using AaronOS.Modules.Medical.Import;

namespace AaronOS.Modules.Medical.Tests;

public class Hl7TimeTests
{
    [Fact] public void ParsesDateOnly() => Assert.Equal(new DateOnly(2026, 1, 15), Hl7Time.ParseDate("20260115"));
    [Fact] public void ParsesFullTimestamp() => Assert.Equal(new DateOnly(2026, 1, 15), Hl7Time.ParseDate("20260115143000"));
    [Fact] public void ParsesTimestampWithPositiveOffset() => Assert.Equal(new DateOnly(2026, 1, 15), Hl7Time.ParseDate("20260115143000+0500"));
    [Fact] public void ParsesTimestampWithNegativeOffset() => Assert.Equal(new DateOnly(2026, 1, 15), Hl7Time.ParseDate("20260115143000-0800"));
    [Fact] public void ParsesYearMonthOnly() => Assert.Equal(new DateOnly(2026, 1, 1), Hl7Time.ParseDate("202601"));
    [Fact] public void ParsesYearOnly() => Assert.Equal(new DateOnly(2026, 1, 1), Hl7Time.ParseDate("2026"));
    [Fact] public void ReturnsNullForNullEmptyOrGarbage()
    {
        Assert.Null(Hl7Time.ParseDate(null));
        Assert.Null(Hl7Time.ParseDate(""));
        Assert.Null(Hl7Time.ParseDate("   "));
        Assert.Null(Hl7Time.ParseDate("not-a-date"));
        Assert.Null(Hl7Time.ParseDate("99999999"));
    }
}
```

- [ ] **Step 2: run to verify it fails** (`Hl7Time` does not exist).

- [ ] **Step 3: implement**

```csharp
using System.Globalization;

namespace AaronOS.Modules.Medical.Import;

/// <summary>
/// Parses HL7 v3 TS values as they appear in C-CDA: YYYY, YYYYMM, YYYYMMDD, YYYYMMDDHHMMSS, any of
/// them optionally followed by a ±ZZZZ zone offset. Only the date is kept — every consumer here
/// stores DateOnly, and a clinical record's time of day is not information this app uses.
/// Anything unparseable returns null rather than throwing: a single malformed date in a long
/// document must not abort an import.
/// </summary>
public static class Hl7Time
{
    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = value.Trim();

        // Drop a zone offset if present; the date portion precedes it.
        var offset = digits.IndexOfAny(['+', '-']);
        if (offset > 0)
        {
            digits = digits[..offset];
        }

        // Keep leading digits only (some producers append fractional seconds).
        var end = 0;
        while (end < digits.Length && char.IsAsciiDigit(digits[end]))
        {
            end++;
        }
        digits = digits[..end];

        string[] formats = ["yyyyMMddHHmmss", "yyyyMMddHHmm", "yyyyMMddHH", "yyyyMMdd", "yyyyMM", "yyyy"];
        foreach (var format in formats)
        {
            if (digits.Length >= format.Length
                && DateTime.TryParseExact(
                    digits[..format.Length], format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                return DateOnly.FromDateTime(parsed);
            }
        }

        return null;
    }
}
```

- [ ] **Step 4: run to verify PASS** (7 tests).
- [ ] **Step 5: Commit** — `git commit -m "Add HL7 timestamp parsing"`

---

### Task 5: C-CDA parse models

**Files:** Create `src/AaronOS.Modules.Medical/Import/CcdaModels.cs`

**Interfaces:**
- Produces the parsed-record records and `CcdaDocument`, consumed by `CcdaParser`, `ImportPlanner`, and `ImportViewModel`. These are parse-time shapes deliberately separate from the EF entities, so a parsed row can be shown for review before anything is written.

- [ ] **Step 1: implement**

```csharp
namespace AaronOS.Modules.Medical.Import;

/// <summary>One parsed record, in the shape the review table and the planner both need. Kept
/// separate from the EF entities so nothing touches the database until the user confirms.</summary>
public record ParsedCondition(string Name, string? Code, DateOnly? Onset, DateOnly? Resolved, bool IsResolved, string? ExternalId);
public record ParsedMedication(string Name, string? Dose, string? Frequency, DateOnly? Start, DateOnly? End, string? ExternalId);
public record ParsedAllergy(string Substance, string? Reaction, string? Severity, string? ExternalId);
public record ParsedImmunization(string Vaccine, DateOnly? Given, string? ExternalId);
public record ParsedProcedure(string Name, DateOnly? Date, string? Facility, string? ExternalId);
public record ParsedVisit(DateOnly? Date, string? VisitType, string? Facility, string? Reason, string? ExternalId);
public record ParsedLab(string TestName, decimal? Value, string? ValueText, string? Unit, decimal? Low, decimal? High, DateOnly? TakenOn, string? ExternalId);

/// <summary>Everything a document yielded, plus an honest account of what it did not.</summary>
public record CcdaDocument
{
    public List<ParsedCondition> Conditions { get; init; } = [];
    public List<ParsedMedication> Medications { get; init; } = [];
    public List<ParsedAllergy> Allergies { get; init; } = [];
    public List<ParsedImmunization> Immunizations { get; init; } = [];
    public List<ParsedProcedure> Procedures { get; init; } = [];
    public List<ParsedVisit> Visits { get; init; } = [];
    public List<ParsedLab> Labs { get; init; } = [];

    /// <summary>Entries present in the document that could not be read, per section.</summary>
    public Dictionary<string, int> SkippedBySection { get; init; } = [];

    /// <summary>Human-readable notes about anything unusual, surfaced on the review screen.</summary>
    public List<string> Warnings { get; init; } = [];

    public int TotalParsed => Conditions.Count + Medications.Count + Allergies.Count
        + Immunizations.Count + Procedures.Count + Visits.Count + Labs.Count;

    public int TotalSkipped => SkippedBySection.Values.Sum();
}
```

- [ ] **Step 2: build** → 0 errors. **Commit** — `git commit -m "Add C-CDA parse models"`

---

### Task 6: C-CDA parser (TDD)

**Files:**
- Create: `src/AaronOS.Modules.Medical/Import/CcdaParser.cs`
- Test: `src/AaronOS.Modules.Medical.Tests/Fixtures/CcdaFixtures.cs`
- Test: `src/AaronOS.Modules.Medical.Tests/CcdaParserTests.cs`

**Interfaces:**
- Consumes `Hl7Time` (Task 4) and the models (Task 5).
- Produces `CcdaParser.Parse(string xml) -> CcdaDocument`. Consumed by `ImportViewModel`.

This is the task where the real work is. Write the fixtures first — they are what makes the parser developable without the user's export, and what will catch regressions when it is later refined against that export.

- [ ] **Step 1: write the fixtures**

`Fixtures/CcdaFixtures.cs` — a static class exposing const strings. Each wraps sections in the standard envelope. Author these from the C-CDA R2.1 spec:

```csharp
namespace AaronOS.Modules.Medical.Tests.Fixtures;

public static class CcdaFixtures
{
    /// <summary>Wraps section XML in the minimal ClinicalDocument envelope a parser must walk.</summary>
    public static string Document(params string[] sections) => $"""
        <ClinicalDocument xmlns="urn:hl7-org:v3" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <recordTarget><patientRole><patient><name><given>Test</given><family>Patient</family></name></patient></patientRole></recordTarget>
          <component><structuredBody>
            {string.Join("\n", sections.Select(s => $"<component>{s}</component>"))}
          </structuredBody></component>
        </ClinicalDocument>
        """;

    public const string ProblemsSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.5.1"/>
          <code code="11450-4" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Problems</title>
          <text><table><tbody><tr><td ID="prob1">Essential hypertension</td></tr></tbody></table></text>
          <entry>
            <act classCode="ACT" moodCode="EVN">
              <id root="1.2.3" extension="cond-1"/>
              <entryRelationship typeCode="SUBJ">
                <observation classCode="OBS" moodCode="EVN">
                  <code code="55607006" codeSystem="2.16.840.1.113883.6.96"/>
                  <effectiveTime><low value="20200115"/></effectiveTime>
                  <value xsi:type="CD" code="59621000" displayName="Essential hypertension"/>
                </observation>
              </entryRelationship>
            </act>
          </entry>
          <entry>
            <act classCode="ACT" moodCode="EVN">
              <id root="1.2.3" extension="cond-2"/>
              <entryRelationship typeCode="SUBJ">
                <observation classCode="OBS" moodCode="EVN">
                  <effectiveTime><low value="20180301"/><high value="20190601"/></effectiveTime>
                  <value xsi:type="CD" code="195967001"/>
                  <text><reference value="#prob1"/></text>
                </observation>
              </entryRelationship>
            </act>
          </entry>
        </section>
        """;

    // MedicationsSection, AllergiesSection, ImmunizationsSection, ResultsSection,
    // ProceduresSection, EncountersSection, VitalSignsSection follow the same shape —
    // see each Step below for the exact element nesting each one must contain.
}
```

`MedicationsSection` (templateId `2.16.840.1.113883.10.20.22.2.1.1`) contains one `<entry><substanceAdministration>` with `<id extension="med-1"/>`, an `<effectiveTime xsi:type="IVL_TS"><low value="20240101"/><high value="20250101"/></effectiveTime>`, a **second** `<effectiveTime xsi:type="PIVL_TS"><period value="1" unit="d"/></effectiveTime>`, `<doseQuantity value="10" unit="mg"/>`, and `<consumable><manufacturedProduct><manufacturedMaterial><code displayName="Lisinopril 10 MG"/></manufacturedMaterial></manufacturedProduct></consumable>`.

`AllergiesSection` (`2.16.840.1.113883.10.20.22.2.6.1`) contains `<entry><act><entryRelationship><observation>` with `<id extension="alg-1"/>`, a `<participant><participantRole><playingEntity><code displayName="Penicillin G"/></playingEntity></participantRole></participant>`, a nested reaction `<entryRelationship><observation><value xsi:type="CD" displayName="Hives"/></observation></entryRelationship>`, and a severity `<entryRelationship><observation><code code="SEV"/><value xsi:type="CD" displayName="Moderate"/></observation></entryRelationship>`.

`ImmunizationsSection` (`2.16.840.1.113883.10.20.22.2.2.1`) contains `<entry><substanceAdministration moodCode="EVN"><id extension="imm-1"/><effectiveTime value="20210310"/>` and a consumable naming `Influenza vaccine`.

`ResultsSection` (`2.16.840.1.113883.10.20.22.2.3.1`) contains `<entry><organizer><component><observation>` with `<id extension="lab-1"/>`, `<code code="718-7" displayName="Hemoglobin"/>`, `<effectiveTime value="20260101"/>`, `<value xsi:type="PQ" value="14.2" unit="g/dL"/>`, and `<referenceRange><observationRange><value xsi:type="IVL_PQ"><low value="13.5"/><high value="17.5"/></value></observationRange></referenceRange>`. Add a second observation with `<value xsi:type="ST">Negative</value>` and no reference range.

`ProceduresSection` (`2.16.840.1.113883.10.20.22.2.7.1`) contains `<entry><procedure><id extension="proc-1"/><code displayName="Appendectomy"/><effectiveTime value="20180412"/></procedure></entry>`.

`EncountersSection` (`2.16.840.1.113883.10.20.22.2.22.1`) contains `<entry><encounter><id extension="enc-1"/><code displayName="Office visit"/><effectiveTime><low value="20260115"/></effectiveTime><participant typeCode="LOC"><participantRole><playingEntity><name>Springfield Clinic</name></playingEntity></participantRole></participant></encounter></entry>`.

`VitalSignsSection` (`2.16.840.1.113883.10.20.22.2.4.1`) contains three observations: systolic `<code code="8480-6" displayName="Systolic blood pressure"/>` value 128 mm[Hg]; heart rate `<code code="8867-4" displayName="Heart rate"/>` value 72 /min; and **body weight** `<code code="29463-7" displayName="Body weight"/>` value 240 [lb_av] — present specifically so a test can prove it is excluded.

Also add `MalformedProblemsSection` — the Problems section shape but with one entry whose observation has no `value` and no resolvable narrative text, so it cannot yield a name.

- [ ] **Step 2: write the failing tests**

```csharp
using AaronOS.Modules.Medical.Import;
using AaronOS.Modules.Medical.Tests.Fixtures;

namespace AaronOS.Modules.Medical.Tests;

public class CcdaParserTests
{
    [Fact]
    public void ParsesConditions_NameFromValueDisplayName()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ProblemsSection));

        var hypertension = doc.Conditions.Single(c => c.ExternalId == "cond-1");
        Assert.Equal("Essential hypertension", hypertension.Name);
        Assert.Equal(new DateOnly(2020, 1, 15), hypertension.Onset);
        Assert.False(hypertension.IsResolved);
    }

    [Fact]
    public void ParsesConditions_NameFromNarrativeReference_AndMarksResolvedWhenHighDatePresent()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ProblemsSection));

        var resolved = doc.Conditions.Single(c => c.ExternalId == "cond-2");
        Assert.Equal("Essential hypertension", resolved.Name); // resolved via <reference value="#prob1"/>
        Assert.Equal(new DateOnly(2019, 6, 1), resolved.Resolved);
        Assert.True(resolved.IsResolved);
    }

    [Fact]
    public void ParsesMedications_TakingDatesFromTheIntervalNotTheFrequency()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.MedicationsSection));

        var med = Assert.Single(doc.Medications);
        Assert.Equal("Lisinopril 10 MG", med.Name);
        Assert.Equal("10 mg", med.Dose);
        Assert.Equal(new DateOnly(2024, 1, 1), med.Start);
        Assert.Equal(new DateOnly(2025, 1, 1), med.End);
    }

    [Fact]
    public void ParsesAllergies_SubstanceReactionAndSeverity()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.AllergiesSection));

        var allergy = Assert.Single(doc.Allergies);
        Assert.Equal("Penicillin G", allergy.Substance);
        Assert.Equal("Hives", allergy.Reaction);
        Assert.Equal("Moderate", allergy.Severity);
    }

    [Fact]
    public void ParsesImmunizations()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ImmunizationsSection));

        var shot = Assert.Single(doc.Immunizations);
        Assert.Equal("Influenza vaccine", shot.Vaccine);
        Assert.Equal(new DateOnly(2021, 3, 10), shot.Given);
    }

    [Fact]
    public void ParsesLabs_NumericWithRange_AndTextualWithout()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ResultsSection));

        var hb = doc.Labs.Single(l => l.TestName == "Hemoglobin");
        Assert.Equal(14.2m, hb.Value);
        Assert.Equal("g/dL", hb.Unit);
        Assert.Equal(13.5m, hb.Low);
        Assert.Equal(17.5m, hb.High);
        Assert.Equal(new DateOnly(2026, 1, 1), hb.TakenOn);

        var textual = doc.Labs.Single(l => l.ValueText == "Negative");
        Assert.Null(textual.Value);
    }

    [Fact]
    public void ParsesProceduresAndVisits()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(
            CcdaFixtures.ProceduresSection, CcdaFixtures.EncountersSection));

        Assert.Equal("Appendectomy", Assert.Single(doc.Procedures).Name);
        var visit = Assert.Single(doc.Visits);
        Assert.Equal("Office visit", visit.VisitType);
        Assert.Equal("Springfield Clinic", visit.Facility);
        Assert.Equal(new DateOnly(2026, 1, 15), visit.Date);
    }

    [Fact]
    public void VitalSigns_ImportClinicalObservationsButExcludeBodyWeight()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.VitalSignsSection));

        Assert.Contains(doc.Labs, l => l.TestName.Contains("Systolic", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(doc.Labs, l => l.TestName.Contains("Heart rate", StringComparison.OrdinalIgnoreCase));
        // Body weight belongs to BodyMeasurements — two sources of truth would be worse than one.
        Assert.DoesNotContain(doc.Labs, l => l.TestName.Contains("weight", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CountsSkippedEntries_RatherThanThrowingOrDroppingSilently()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.MalformedProblemsSection));

        Assert.Empty(doc.Conditions);
        Assert.True(doc.TotalSkipped >= 1);
        Assert.NotEmpty(doc.Warnings);
    }

    [Fact]
    public void AbsentSectionIsNotAnError()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(CcdaFixtures.ProblemsSection));

        Assert.Empty(doc.Labs);
        Assert.Empty(doc.Medications);
    }

    [Fact]
    public void ThrowsFormatException_OnXmlThatIsNotACcdaAtAll()
    {
        Assert.Throws<FormatException>(() => CcdaParser.Parse("<html><body>nope</body></html>"));
    }

    [Fact]
    public void ThrowsFormatException_OnMalformedXml()
    {
        Assert.Throws<FormatException>(() => CcdaParser.Parse("<not closed"));
    }
}
```

- [ ] **Step 3: run to verify these fail.**

- [ ] **Step 4: implement `CcdaParser`**

```csharp
using System.Globalization;
using System.Xml.Linq;

namespace AaronOS.Modules.Medical.Import;

/// <summary>
/// Reads a C-CDA R2.1 document into plain parsed records. Pure: no file I/O, no database, no
/// network, so it is fully unit-testable against fixture documents.
///
/// Written defensively on purpose. Real exports vary in how completely they populate the standard —
/// values live in coded elements or in the narrative table, template IDs are sometimes versioned or
/// absent, dates come in several precisions, and nullFlavor stands in for missing data. So each
/// entry is parsed in a try/catch: one bad entry increments a skip count and adds a warning rather
/// than aborting an import of hundreds of good ones.
/// </summary>
public static class CcdaParser
{
    private static readonly XNamespace V = "urn:hl7-org:v3";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    // Section template IDs (C-CDA R2.1), with the LOINC section code as a fallback because some
    // producers omit or version the templateId.
    private const string ProblemsTemplate = "2.16.840.1.113883.10.20.22.2.5.1";
    private const string MedicationsTemplate = "2.16.840.1.113883.10.20.22.2.1.1";
    private const string AllergiesTemplate = "2.16.840.1.113883.10.20.22.2.6.1";
    private const string ImmunizationsTemplate = "2.16.840.1.113883.10.20.22.2.2.1";
    private const string ResultsTemplate = "2.16.840.1.113883.10.20.22.2.3.1";
    private const string ProceduresTemplate = "2.16.840.1.113883.10.20.22.2.7.1";
    private const string EncountersTemplate = "2.16.840.1.113883.10.20.22.2.22.1";
    private const string VitalSignsTemplate = "2.16.840.1.113883.10.20.22.2.4.1";

    /// <summary>LOINC codes for body weight and height. Excluded deliberately: BodyMeasurements owns
    /// those numbers, modules may not write to each other's tables, and two sources of truth for the
    /// same measurement is worse than one.</summary>
    private static readonly HashSet<string> ExcludedVitalCodes =
        ["29463-7", "3141-9", "8350-1", "8302-2", "3137-7"];

    public static CcdaDocument Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new FormatException("That file is not valid XML.", ex);
        }

        if (doc.Root is null || doc.Root.Name != V + "ClinicalDocument")
        {
            throw new FormatException(
                "That file is not a C-CDA document. In MyChart, use Sharing → Download My Record.");
        }

        var result = new CcdaDocument();

        ParseSection(doc, ProblemsTemplate, "11450-4", "Problems", result, ParseProblem);
        ParseSection(doc, MedicationsTemplate, "10160-0", "Medications", result, ParseMedication);
        ParseSection(doc, AllergiesTemplate, "48765-2", "Allergies", result, ParseAllergy);
        ParseSection(doc, ImmunizationsTemplate, "11369-6", "Immunizations", result, ParseImmunization);
        ParseSection(doc, ResultsTemplate, "30954-2", "Results", result, ParseResultEntry);
        ParseSection(doc, ProceduresTemplate, "47519-4", "Procedures", result, ParseProcedure);
        ParseSection(doc, EncountersTemplate, "46240-8", "Encounters", result, ParseEncounter);
        ParseSection(doc, VitalSignsTemplate, "8716-3", "Vital signs", result, ParseResultEntry);

        return result;
    }

    private static void ParseSection(
        XDocument doc, string templateRoot, string loincCode, string label,
        CcdaDocument result, Action<XElement, XElement, CcdaDocument> parseEntry)
    {
        var section = FindSection(doc, templateRoot, loincCode);
        if (section is null)
        {
            return; // An absent section is normal, not an error.
        }

        var narrative = BuildNarrativeIndex(section);

        foreach (var entry in section.Elements(V + "entry"))
        {
            try
            {
                parseEntry(entry, narrative, result);
            }
            catch (Exception ex)
            {
                Skip(result, label, ex.Message);
            }
        }
    }

    private static XElement? FindSection(XDocument doc, string templateRoot, string loincCode)
    {
        foreach (var section in doc.Descendants(V + "section"))
        {
            if (section.Elements(V + "templateId").Any(t => Attr(t, "root") == templateRoot))
            {
                return section;
            }
        }

        // Fallback: match the section's LOINC code when the templateId is missing or versioned.
        return doc.Descendants(V + "section")
            .FirstOrDefault(s => Attr(s.Element(V + "code"), "code") == loincCode);
    }

    /// <summary>
    /// Index of narrative ids to their text. C-CDA frequently carries the human-readable value only
    /// in the section's narrative table, with the entry pointing at it via
    /// &lt;text&gt;&lt;reference value="#id"/&gt;&lt;/text&gt; — so without this, names come back empty.
    /// Returned as an XElement so it can be passed alongside each entry; keys are looked up by ID.
    /// </summary>
    private static XElement BuildNarrativeIndex(XElement section)
    {
        var index = new XElement("narrative");
        var text = section.Element(V + "text");
        if (text is null)
        {
            return index;
        }

        foreach (var element in text.Descendants())
        {
            var id = Attr(element, "ID");
            if (!string.IsNullOrEmpty(id))
            {
                index.Add(new XElement("item", new XAttribute("id", id), Flatten(element)));
            }
        }

        return index;
    }

    private static string Flatten(XElement element) =>
        string.Join(" ", element.DescendantNodes().OfType<XText>().Select(t => t.Value.Trim()))
            .Trim();

    private static string? Narrative(XElement index, XElement? owner)
    {
        var reference = owner?.Element(V + "text")?.Element(V + "reference");
        var value = Attr(reference, "value")?.TrimStart('#');
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var hit = index.Elements("item").FirstOrDefault(i => (string?)i.Attribute("id") == value);
        return string.IsNullOrWhiteSpace(hit?.Value) ? null : hit!.Value;
    }

    // ---- per-section entry parsers -------------------------------------------------------------

    private static void ParseProblem(XElement entry, XElement narrative, CcdaDocument result)
    {
        var act = entry.Element(V + "act");
        var observation = act?.Descendants(V + "observation").FirstOrDefault()
            ?? entry.Descendants(V + "observation").FirstOrDefault()
            ?? throw new FormatException("problem entry has no observation");

        var name = DisplayName(observation.Element(V + "value"))
            ?? Narrative(narrative, observation)
            ?? throw new FormatException("problem has no readable name");

        var effective = observation.Element(V + "effectiveTime");
        var onset = Hl7Time.ParseDate(Attr(effective?.Element(V + "low"), "value"));
        var resolved = Hl7Time.ParseDate(Attr(effective?.Element(V + "high"), "value"));

        result.Conditions.Add(new ParsedCondition(
            name,
            Attr(observation.Element(V + "value"), "code"),
            onset,
            resolved,
            resolved is not null,
            ExternalId(act) ?? ExternalId(observation)));
    }

    private static void ParseMedication(XElement entry, XElement narrative, CcdaDocument result)
    {
        var admin = entry.Descendants(V + "substanceAdministration").FirstOrDefault()
            ?? throw new FormatException("medication entry has no substanceAdministration");

        var material = admin.Descendants(V + "manufacturedMaterial").FirstOrDefault();
        var name = DisplayName(material?.Element(V + "code"))
            ?? Narrative(narrative, admin)
            ?? throw new FormatException("medication has no readable name");

        // Two effectiveTime elements are normal: IVL_TS carries the date range, PIVL_TS the
        // frequency. Picking the wrong one yields nonsense dates, so select on the low/high shape.
        var interval = admin.Elements(V + "effectiveTime")
            .FirstOrDefault(e => e.Element(V + "low") is not null || e.Element(V + "high") is not null);
        var period = admin.Elements(V + "effectiveTime")
            .FirstOrDefault(e => e.Element(V + "period") is not null);

        var dose = admin.Element(V + "doseQuantity");
        var doseText = Attr(dose, "value") is { Length: > 0 } dv
            ? $"{dv} {Attr(dose, "unit")}".Trim()
            : null;

        var periodValue = Attr(period?.Element(V + "period"), "value");
        var periodUnit = Attr(period?.Element(V + "period"), "unit");
        var frequency = periodValue is { Length: > 0 }
            ? $"every {periodValue} {periodUnit}".Trim()
            : null;

        result.Medications.Add(new ParsedMedication(
            name,
            doseText,
            frequency,
            Hl7Time.ParseDate(Attr(interval?.Element(V + "low"), "value")),
            Hl7Time.ParseDate(Attr(interval?.Element(V + "high"), "value")),
            ExternalId(admin) ?? ExternalId(entry.Element(V + "act"))));
    }

    private static void ParseAllergy(XElement entry, XElement narrative, CcdaDocument result)
    {
        var observation = entry.Descendants(V + "observation").FirstOrDefault()
            ?? throw new FormatException("allergy entry has no observation");

        var substance = DisplayName(observation
                .Descendants(V + "playingEntity").FirstOrDefault()?.Element(V + "code"))
            ?? Narrative(narrative, observation)
            ?? throw new FormatException("allergy has no readable substance");

        // Reaction and severity are nested observations; severity is the one coded SEV.
        var nested = observation.Descendants(V + "observation").ToList();
        var severity = nested
            .Where(o => Attr(o.Element(V + "code"), "code") == "SEV")
            .Select(o => DisplayName(o.Element(V + "value")))
            .FirstOrDefault(s => s is not null);
        var reaction = nested
            .Where(o => Attr(o.Element(V + "code"), "code") != "SEV")
            .Select(o => DisplayName(o.Element(V + "value")))
            .FirstOrDefault(s => s is not null);

        result.Allergies.Add(new ParsedAllergy(substance, reaction, severity, ExternalId(observation)));
    }

    private static void ParseImmunization(XElement entry, XElement narrative, CcdaDocument result)
    {
        var admin = entry.Descendants(V + "substanceAdministration").FirstOrDefault()
            ?? throw new FormatException("immunization entry has no substanceAdministration");

        var material = admin.Descendants(V + "manufacturedMaterial").FirstOrDefault();
        var vaccine = DisplayName(material?.Element(V + "code"))
            ?? Narrative(narrative, admin)
            ?? throw new FormatException("immunization has no readable vaccine");

        var effective = admin.Element(V + "effectiveTime");
        var given = Hl7Time.ParseDate(Attr(effective, "value"))
            ?? Hl7Time.ParseDate(Attr(effective?.Element(V + "low"), "value"));

        result.Immunizations.Add(new ParsedImmunization(vaccine, given, ExternalId(admin)));
    }

    /// <summary>Handles both the Results and Vital Signs sections — identical organizer/observation
    /// shape, and both land in LabResult.</summary>
    private static void ParseResultEntry(XElement entry, XElement narrative, CcdaDocument result)
    {
        var observations = entry.Descendants(V + "observation").ToList();
        if (observations.Count == 0)
        {
            throw new FormatException("result entry has no observations");
        }

        foreach (var observation in observations)
        {
            var code = observation.Element(V + "code");
            if (ExcludedVitalCodes.Contains(Attr(code, "code") ?? ""))
            {
                continue; // body weight/height — see ExcludedVitalCodes
            }

            var name = DisplayName(code) ?? Narrative(narrative, observation);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var value = observation.Element(V + "value");
            var numeric = ParseDecimal(Attr(value, "value"));
            var text = numeric is null
                ? (Attr(value, "displayName") ?? value?.Value.Trim())
                : null;

            var range = observation.Descendants(V + "observationRange")
                .Select(r => r.Element(V + "value"))
                .FirstOrDefault(v => v is not null);

            result.Labs.Add(new ParsedLab(
                name!,
                numeric,
                string.IsNullOrWhiteSpace(text) ? null : text,
                Attr(value, "unit"),
                ParseDecimal(Attr(range?.Element(V + "low"), "value")),
                ParseDecimal(Attr(range?.Element(V + "high"), "value")),
                Hl7Time.ParseDate(Attr(observation.Element(V + "effectiveTime"), "value"))
                    ?? Hl7Time.ParseDate(Attr(observation.Element(V + "effectiveTime")?.Element(V + "low"), "value")),
                ExternalId(observation)));
        }
    }

    private static void ParseProcedure(XElement entry, XElement narrative, CcdaDocument result)
    {
        // The Procedures section legitimately uses procedure, observation or act.
        var element = entry.Element(V + "procedure")
            ?? entry.Element(V + "observation")
            ?? entry.Element(V + "act")
            ?? throw new FormatException("procedure entry is empty");

        var name = DisplayName(element.Element(V + "code"))
            ?? Narrative(narrative, element)
            ?? throw new FormatException("procedure has no readable name");

        var effective = element.Element(V + "effectiveTime");
        var date = Hl7Time.ParseDate(Attr(effective, "value"))
            ?? Hl7Time.ParseDate(Attr(effective?.Element(V + "low"), "value"));

        result.Procedures.Add(new ParsedProcedure(name, date, FacilityName(element), ExternalId(element)));
    }

    private static void ParseEncounter(XElement entry, XElement narrative, CcdaDocument result)
    {
        var encounter = entry.Element(V + "encounter")
            ?? entry.Descendants(V + "encounter").FirstOrDefault()
            ?? throw new FormatException("encounter entry has no encounter");

        var effective = encounter.Element(V + "effectiveTime");
        var date = Hl7Time.ParseDate(Attr(effective, "value"))
            ?? Hl7Time.ParseDate(Attr(effective?.Element(V + "low"), "value"));

        result.Visits.Add(new ParsedVisit(
            date,
            DisplayName(encounter.Element(V + "code")) ?? Narrative(narrative, encounter),
            FacilityName(encounter),
            null,
            ExternalId(encounter)));
    }

    // ---- small shared helpers ------------------------------------------------------------------

    private static string? Attr(XElement? element, string name) =>
        element?.Attribute(name)?.Value is { Length: > 0 } v ? v : null;

    /// <summary>A coded element's human-readable text, respecting nullFlavor.</summary>
    private static string? DisplayName(XElement? coded)
    {
        if (coded is null || Attr(coded, "nullFlavor") is not null)
        {
            return null;
        }

        return Attr(coded, "displayName")
            ?? (coded.Elements(V + "originalText").FirstOrDefault()?.Value.Trim() is { Length: > 0 } t ? t : null);
    }

    private static string? ExternalId(XElement? owner)
    {
        var id = owner?.Element(V + "id");
        if (id is null)
        {
            return null;
        }

        return Attr(id, "extension") ?? Attr(id, "root");
    }

    private static string? FacilityName(XElement owner) =>
        owner.Descendants(V + "playingEntity").Select(p => p.Element(V + "name")?.Value.Trim())
            .Concat(owner.Descendants(V + "participantRole").Select(p => p.Element(V + "addr")?.Value.Trim()))
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static void Skip(CcdaDocument result, string section, string reason)
    {
        result.SkippedBySection[section] = result.SkippedBySection.GetValueOrDefault(section) + 1;
        var warning = $"{section}: {reason}";
        if (!result.Warnings.Contains(warning))
        {
            result.Warnings.Add(warning);
        }
    }
}
```

- [ ] **Step 5: run the tests; iterate on parser and fixtures until all PASS.**
Run: `dotnet test src/AaronOS.Modules.Medical.Tests --filter CcdaParserTests`
Expect all 12 to pass. Where a test fails, prefer fixing the parser — the fixtures encode the spec.

- [ ] **Step 6: Commit** — `git commit -m "Add C-CDA parser with fixture-driven tests"`

---

### Task 7: Import planner (TDD)

**Files:**
- Create: `src/AaronOS.Modules.Medical/Import/ImportPlanner.cs`
- Test: `src/AaronOS.Modules.Medical.Tests/ImportPlannerTests.cs`

**Interfaces:**
- Produces `ImportStatus` enum (`New`, `AlreadyImported`), `ImportRow(string Section, string Description, string? ExternalId, ImportStatus Status)`, `ImportPlan` (rows + per-section counts + `NewCount`), `ExistingKeys` (per-type `HashSet<string>` of `ExternalId` **and** natural keys), and `ImportPlanner.BuildPlan(CcdaDocument, ExistingKeys) -> ImportPlan`. Consumed by `ImportViewModel`.

- [ ] **Step 1: write the failing tests** covering: a record with an unseen `ExternalId` is `New`; the same `ExternalId` already present is `AlreadyImported`; a record with **no** `ExternalId` whose natural key is already present is `AlreadyImported`; two identical records inside one document collapse to one `New` (a document can repeat a row across sections); `NewCount` counts only `New`.

- [ ] **Step 2: run to verify failure.**

- [ ] **Step 3: implement**

```csharp
namespace AaronOS.Modules.Medical.Import;

public enum ImportStatus { New, AlreadyImported }

public record ImportRow(string Section, string Description, string? Key, ImportStatus Status);

public record ExistingKeys
{
    public HashSet<string> Conditions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Medications { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Allergies { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Immunizations { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Procedures { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Visits { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Labs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public record ImportPlan(List<ImportRow> Rows)
{
    public int NewCount => Rows.Count(r => r.Status == ImportStatus.New);
    public int AlreadyImportedCount => Rows.Count(r => r.Status == ImportStatus.AlreadyImported);
    public IEnumerable<IGrouping<string, ImportRow>> BySection => Rows.GroupBy(r => r.Section);
}

/// <summary>
/// Classifies parsed records against what the database already holds, so an import can be reviewed
/// before it is committed and re-importing the same document is a no-op.
///
/// Matching prefers the document's own id. When a producer omits ids, a natural key derived from the
/// record's identifying fields is used instead — without that fallback, every re-import would
/// duplicate everything.
/// </summary>
public static class ImportPlanner
{
    public static ImportPlan BuildPlan(CcdaDocument parsed, ExistingKeys existing)
    {
        var rows = new List<ImportRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string section, string description, string? externalId, string naturalKey, HashSet<string> known)
        {
            var key = $"{section}|{externalId ?? naturalKey}";
            if (!seen.Add(key))
            {
                return; // same record twice in one document
            }

            var status = known.Contains(externalId ?? naturalKey)
                ? ImportStatus.AlreadyImported
                : ImportStatus.New;
            rows.Add(new ImportRow(section, description, externalId ?? naturalKey, status));
        }

        foreach (var c in parsed.Conditions)
            Add("Conditions", c.Name, c.ExternalId, $"{c.Name}|{c.Onset}", existing.Conditions);
        foreach (var m in parsed.Medications)
            Add("Medications", m.Name, m.ExternalId, $"{m.Name}|{m.Start}", existing.Medications);
        foreach (var a in parsed.Allergies)
            Add("Allergies", a.Substance, a.ExternalId, a.Substance, existing.Allergies);
        foreach (var i in parsed.Immunizations)
            Add("Immunizations", i.Vaccine, i.ExternalId, $"{i.Vaccine}|{i.Given}", existing.Immunizations);
        foreach (var p in parsed.Procedures)
            Add("Procedures", p.Name, p.ExternalId, $"{p.Name}|{p.Date}", existing.Procedures);
        foreach (var v in parsed.Visits)
            Add("Visits", $"{v.VisitType ?? "Visit"} · {v.Facility ?? "—"}", v.ExternalId, $"{v.Date}|{v.Facility}", existing.Visits);
        foreach (var l in parsed.Labs)
            Add("Labs", $"{l.TestName} {l.Value?.ToString() ?? l.ValueText}", l.ExternalId, $"{l.TestName}|{l.TakenOn}|{l.Value}", existing.Labs);

        return new ImportPlan(rows);
    }
}
```

- [ ] **Step 4: run to PASS. Commit** — `git commit -m "Add import planner"`

---

### Task 8: Overview page

**Files:** Create `ViewModels/MedicalOverviewViewModel.cs`, `Views/MedicalOverviewPage.xaml(.cs)`; modify `MedicalModule.cs` to register the ViewModel.

**Interfaces:** `MedicalOverviewViewModel` exposes `ObservableCollection<Allergy> Allergies`, `ObservableCollection<Medication> ActiveMedications`, `ObservableCollection<LabResult> FlaggedLabs`, `ActiveConditionCount`, `MedicationCount`, `AllergyCount`, `LastVisitDisplay`, `HasAllergies`, `HasFlaggedLabs`, `HasActiveMedications`, `LoadCommand`.

Structure — mirror `FinanceDashboardPage.xaml`:
- Title row: "Medical" (`PageTitleText`).
- **Allergies banner first.** A `SurfaceCard` with `BorderBrush="{DynamicResource ExpiryPast}"` when any allergy `IsSevere`, eyebrow `ALLERGIES`, each substance with reaction and severity; severity coloured `ExpiryPast` for Severe, `ExpirySoon` for Moderate. When there are none, a quiet "No allergies recorded" caption — absence must be explicit, since a blank space here could be misread as "no known allergies" when it means "never entered".
- Hero row card: three columns using `HeroMetricText` at `FontSize="26"` — `ACTIVE CONDITIONS`, `MEDICATIONS`, `LABS FLAGGED` (the last coloured `ExpirySoon` when non-zero), with a `CaptionText` line beneath showing `LastVisitDisplay`.
- `CURRENT MEDICATIONS` ledger: name, `DoseDisplay`, `StartedDisplay`.
- `LABS OUT OF RANGE` ledger: `TestName`, `ValueDisplay` (coloured `ExpirySoon`), `RangeDisplay`, `TakenDisplay`. Only rendered when `HasFlaggedLabs`.

`LoadAsync` queries each set with `AsNoTracking()`, computes counts, and sets the `Has*` flags. `FlaggedLabs` filters `IsOutOfRange` **in memory** after materialising — it is a computed property EF cannot translate to SQL.

- [ ] Steps: implement ViewModel → implement page → register → `dotnet build` → run app, click Medical, confirm the page renders and empty states read correctly → commit.

---

### Task 9: History, Medications, Visits and Labs pages

**Files:** `ViewModels/HistoryViewModel.cs`, `MedicationsViewModel.cs`, `VisitsViewModel.cs`, `LabsViewModel.cs`; matching `Views/*.xaml(.cs)`; register all four in `MedicalModule.cs`.

Each page follows the same proven shape as `InventoryPage.xaml`: root `<StackPanel MaxWidth="780" HorizontalAlignment="Left" Margin="28,20,28,32">`, no page `ScrollViewer`, a `SurfaceCard` add-form per record type with `EyebrowText` field labels, and a ledger with an `EyebrowText` column-header grid, a 1px divider `Border`, an `ItemsControl`, a `DataTrigger`-driven empty state, and a flat `Remove` button per row wired through a code-behind `Click` handler reading `DataContext` (the pattern in `ClothingSizesPage.xaml.cs`). Ledger first columns take `MinWidth` so they do not collapse when empty. Every ledger shows an `IMPORTED` marker (`EyebrowText`, `FontSize="10"`, `ReactorGlowDim`) on rows where `IsImported`.

- **HistoryPage** — three ledgers: `CONDITIONS` (name, `StatusDisplay`, `OnsetDisplay`), `PROCEDURES` (name, `DateDisplay`, facility), `IMMUNIZATIONS` (vaccine, `DateDisplay`, `DoseDisplay`). Status bound through a `ComboBox` over `Enum.GetValues<ConditionStatus>()`.
- **MedicationsPage** — `MEDICATIONS` ledger (name, `DoseDisplay`, `StartedDisplay`, active/past via a `DataTrigger` on `IsActive` dimming past rows) and `ALLERGIES` ledger (substance, `ReactionDisplay`, `SeverityDisplay` coloured by severity). Medication add-form includes a `Provider` `ComboBox`.
- **VisitsPage** — `VISITS` ledger (`DateDisplay`, `TypeDisplay`, provider name, `WhereDisplay`, reason), `PROVIDERS` ledger (name, `SpecialtyDisplay`, `PhoneDisplay`, facility), `DOCUMENTS` ledger (title, `AddedDisplay`, linked visit, `StatusDisplay` coloured `ExpiryPast` when `FileExists` is false, and an `Open` button). Document add uses `Microsoft.Win32.OpenFileDialog` in code-behind, passing the chosen path to the ViewModel — file dialogs are a view concern.
- **LabsPage** — an `ADD RESULT` form; a `ComboBox` of distinct `TestName`s driving a `LiveChartsCore` `CartesianChart` (`Height="260"`) of that test's values over time, mirroring `DashboardPage.xaml`'s `WeightSeries`/`WeightAxes` binding; and a full `RESULTS` ledger (`TestName`, `ValueDisplay`, `RangeDisplay`, `TakenDisplay`, out-of-range rows coloured `ExpirySoon`).

- [ ] Steps per page: ViewModel → page → register → build → run and verify → commit. Four commits, one per page.

---

### Task 10: Import page

**Files:** `ViewModels/ImportViewModel.cs`, `Views/ImportPage.xaml(.cs)`; register in `MedicalModule.cs`.

**Interfaces:** `ImportViewModel` exposes `FilePath`, `HasParsed`, `HasError`, `ErrorMessage`, `ObservableCollection<ImportRow> Rows`, `ObservableCollection<string> Warnings`, `SummaryText`, `NewCount`, `SkippedCount`, `CanCommit`, `ParseCommand`, `CommitCommand`, and `void SetFile(string path)`.

- [ ] **Step 1: ViewModel**
  - `SetFile` stores the path and clears prior results.
  - `ParseAsync`: read the file, `CcdaParser.Parse`, load `ExistingKeys` by querying each entity's non-null `ExternalId` set **plus** the natural keys (so the fallback matching works), `ImportPlanner.BuildPlan`, populate `Rows`/`Warnings`/counts. Catch `FormatException` → `ErrorMessage`, `HasError = true`, nothing written. Catch `IOException` likewise.
  - `CommitAsync`: for each parsed record whose planned status is `New`, insert the corresponding entity with `Source = RecordSource.Imported` and its `ExternalId`; save once; then re-run `ParseAsync` so the review table now shows everything as `AlreadyImported` — which both proves idempotency to the user and leaves the screen honest.
  - Map `ParsedAllergy.Severity` text onto `AllergySeverity` with a case-insensitive `switch` defaulting to `Unknown`; map `ParsedCondition.IsResolved` onto `ConditionStatus.Resolved` else `Active`.

- [ ] **Step 2: page** — mirror the other pages' shell. Sections: title "Import"; an explanatory `CaptionText` naming where the file comes from ("In MyChart: Sharing → Download My Record"); a `Choose file…` button plus the selected path; `Parse` primary button; an error card (`Background="#2A1416" BorderBrush="#7A2E33"`, as `FinanceDashboardPage.xaml` does) shown on `HasError`; a summary card with `HeroMetricText` `NEW RECORDS` and captions for already-imported and skipped counts; a `WARNINGS` card listing `Warnings` in `ExpirySoon`; the review ledger (`SECTION`, `RECORD`, `STATUS` — `New` in `MoneyIn`, `AlreadyImported` dimmed); and a `Import N records` primary button bound to `CommitCommand`, enabled only when `CanCommit`.
  - File choosing lives in code-behind via `OpenFileDialog` with `Filter = "C-CDA / XML records (*.xml;*.ccda;*.zip)|*.xml;*.ccda;*.zip|All files (*.*)|*.*"`, calling `ViewModel.SetFile(dialog.FileName)`.

- [ ] **Step 3:** build, then verify with a **fixture file**: write one of the test fixtures to a temp `.xml`, choose it, parse, confirm counts and the review table; commit it; parse again and confirm every row now reads `AlreadyImported` and `NewCount` is 0. This proves idempotency end to end against a real database.

- [ ] **Step 4: Commit** — `git commit -m "Add C-CDA import page"`

---

### Task 11: Real shell and final registration

**Files:** Replace `Views/MedicalShellPage.xaml(.cs)` placeholder content; finalise `MedicalModule.cs`.

- [ ] **Step 1:** shell XAML — copy `NutritionShellPage.xaml` exactly, with six buttons: `Overview`, `History`, `Medications`, `Visits`, `Labs`, `Import`.
- [ ] **Step 2:** code-behind — `Loaded += (_, _) => ContentFrame.Navigate(new MedicalOverviewPage());` plus one `Click` handler per button navigating the internal `Frame`, mirroring `NutritionShellPage.xaml.cs`.
- [ ] **Step 3:** confirm `MedicalModule.RegisterServices` registers all six ViewModels as transient.
- [ ] **Step 4:** build; run; walk all six pages; confirm the nav bar and default landing page.
- [ ] **Step 5: Commit** — `git commit -m "Wire up the Medical module shell"`

---

### Task 12: Full verification

- [ ] **Step 1:** `dotnet build AaronOS.slnx` → 0 errors.
- [ ] **Step 2:** `dotnet test AaronOS.slnx` → every suite passes (Finance 10, Nutrition 23, Medical's new tests).
- [ ] **Step 3:** run the app and confirm all four modules load, Body Measurements is still the landing page, and the nine Medical tables exist in the real database with existing data untouched.
- [ ] **Step 4:** end-to-end pass: add a condition, a medication with a provider, an allergy, two lab results for one test (confirm the trend chart draws), and a document pointing at a real file (confirm `OK`) and a fake path (confirm `File missing`).
- [ ] **Step 5:** no commit — verification only. Fix failures in whichever task owns the code.

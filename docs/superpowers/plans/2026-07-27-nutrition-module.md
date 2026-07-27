# Nutrition Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `AaronOS.Modules.Nutrition`, a third AaronOS module tracking food preferences (like/dislike, preferred form, tags), nutrition facts, recipes, and a pantry/fridge inventory with expiration tracking, per `docs/superpowers/specs/2026-07-27-nutrition-module-design.md`.

**Architecture:** A compiled-in `IAppModule` exactly like `AaronOS.Modules.Finance`/`AaronOS.Modules.BodyMeasurements` — its own EF Core entities auto-discovered by the shared `AaronOsDbContext`, its own ViewModels/Pages, one project reference from `AaronOS.App`, one line appended to the module array. Preference/nutrition/stock logic lives in small pure-function services, unit-tested; USDA lookups and the DPAPI-protected API key store mirror Finance's Plaid client/credential-store pattern.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF + `Wpf.Ui.Controls` (WPF-UI 4.3.0), EF Core + SQLite (via the shared `AaronOsDbContext`), `CommunityToolkit.Mvvm`, `System.Security.Cryptography.ProtectedData` 10.0.10, xunit 2.5.3 for tests.

## Global Constraints

- `TargetFramework` = `net8.0-windows`, `UseWPF` = `true`, `LangVersion` = `13.0`, `Nullable` = `enable` on the main module project (per `docs/MODULE_GUIDELINES.md`).
- The test project targets `net8.0-windows` too (matching `AaronOS.Modules.Finance.Tests`'s actual csproj, not a plain `net8.0`) but does **not** set `UseWPF`.
- Use field-backed `[ObservableProperty] private T _x;` — the partial-property generator syntax doesn't work in this environment (see `AaronOS.Core.ViewModelBase`).
- `ui:NumberBox.Value` is `double`; the established convention is a `double` field defaulting to `double.NaN` for "not entered," converted to `decimal?` at save time — no value converters for this case.
- Every module-owned entity needs an `IEntityTypeConfiguration<T>` under `Data/`; never edit `AaronOsDbContext` directly — it discovers configs via `ApplyConfigurationsFromAssembly` over every registered module's assembly.
- No EF migrations — `EnsureCreatedAsync()` only. Don't seed via `HasData`; use a runtime "seed if empty" check instead (see Task 6).
- Imperial/US units only; cost and nutrition are both normalized per 100g (see design spec's "Cost convention").
- Never reference `AaronOS.Modules.Finance` from this module (or vice versa) — duplicate the small DPAPI helper rather than share it.
- `App.xaml.cs`'s module array and `AaronOS.slnx`/`AaronOS.App.csproj`'s project references get **appended to**, never reordered — Body Measurements must stay `modules[0]` so it remains the app's landing page.
- Every code file's `namespace`/`using` lines must match the exact namespaces defined by earlier tasks — cross-check before commit.

---

### Task 1: Scaffold the module project (walking skeleton)

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/AaronOS.Modules.Nutrition.csproj`
- Create: `src/AaronOS.Modules.Nutrition/NutritionModule.cs`
- Create: `src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml`
- Create: `src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml.cs`
- Modify: `AaronOS.slnx`
- Modify: `src/AaronOS.App/AaronOS.App.csproj`
- Modify: `src/AaronOS.App/App.xaml.cs`

**Interfaces:**
- Produces: `AaronOS.Modules.Nutrition.NutritionModule` implementing `AaronOS.Core.IAppModule`, `Id => "nutrition"`, `HomePageType => typeof(NutritionShellPage)`. Every later task adds to `RegisterServices` and eventually replaces `NutritionShellPage`'s placeholder content — don't rename the class or the `Id`.

No automated test for this task — it's app wiring with no pure logic yet. Verification is "the app builds and the new nav item shows a page" per the existing module precedent (Finance's own design/implementation had no test for its module-registration step either).

- [ ] **Step 1: Create the module's csproj**

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
    <LangVersion>13.0</LangVersion>
    <RootNamespace>AaronOS.Modules.Nutrition</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

No `<EmbeddedResource>` entries yet — Tasks 4 and 6 each add one when they create the JSON file it points at. MSBuild's resource-embedding step opens the file during build, so declaring the reference before the file exists would break this task's own build-verification step.

- [ ] **Step 2: Create the module contract implementation**

```csharp
using AaronOS.Core;
using AaronOS.Modules.Nutrition.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Nutrition;

public class NutritionModule : IAppModule
{
    public string Id => "nutrition";
    public string DisplayName => "Nutrition";
    public string IconGlyph => "Food24"; // confirm exact Wpf.Ui.Controls.SymbolRegular member when the app first builds
    public Type HomePageType => typeof(NutritionShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        // Later tasks add each ViewModel/service registration here as they're built.
    }
}
```

- [ ] **Step 3: Create a placeholder shell page**

`src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Nutrition.Views.NutritionShellPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    mc:Ignorable="d">

    <TextBlock Text="Nutrition module coming soon" FontSize="24" Margin="24" />
</Page>
```

`src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

public sealed partial class NutritionShellPage : Page
{
    public NutritionShellPage()
    {
        InitializeComponent();
    }
}
```

(Task 11 replaces both files' contents with the real button-row-plus-Frame shell — same file paths, same class name.)

- [ ] **Step 4: Wire the module into the solution and app**

In `AaronOS.slnx`, add a line inside the existing `<Folder Name="/src/">` (alongside the Finance entries already there):

```xml
    <Project Path="src/AaronOS.Modules.Nutrition/AaronOS.Modules.Nutrition.csproj" />
```

In `src/AaronOS.App/AaronOS.App.csproj`, add inside the existing `<ItemGroup>` with the other `<ProjectReference>` lines:

```xml
    <ProjectReference Include="..\AaronOS.Modules.Nutrition\AaronOS.Modules.Nutrition.csproj" />
```

In `src/AaronOS.App/App.xaml.cs`, add the using and append to the module array (do not reorder the existing entries):

```csharp
using AaronOS.Modules.Nutrition;
```

```csharp
IAppModule[] modules = [new BodyMeasurementsModule(), new FinanceModule(), new NutritionModule()];
```

- [ ] **Step 5: Build and manually verify**

Run: `dotnet build AaronOS.slnx`
Expected: builds with no errors (the two `EmbeddedResource` lines from Step 1 should not error even with no matching files yet — if they do, comment them out per Step 1's note).

Manually run the app (`dotnet run --project src/AaronOS.App/AaronOS.App.csproj`) and confirm a "Nutrition" nav item appears (after Body Measurements and Finance) and clicking it shows "Nutrition module coming soon." Close the app.

- [ ] **Step 6: Commit**

```bash
git add AaronOS.slnx src/AaronOS.App/AaronOS.App.csproj src/AaronOS.App/App.xaml.cs src/AaronOS.Modules.Nutrition
git commit -m "$(cat <<'EOF'
Scaffold AaronOS.Modules.Nutrition module

Adds the module project, contract implementation, and a placeholder
shell page, wired into the solution and app shell — the walking
skeleton later tasks build the real feature set onto.
EOF
)"
```

---

### Task 2: Core data entities and EF configuration

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/Data/Rating.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/StorageLocation.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/Ingredient.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/IngredientConfiguration.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/Tag.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/TagConfiguration.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/Recipe.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/RecipeConfiguration.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/RecipeIngredient.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/RecipeIngredientConfiguration.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/InventoryItem.cs`
- Create: `src/AaronOS.Modules.Nutrition/Data/InventoryItemConfiguration.cs`

**Interfaces:**
- Consumes: nothing from Task 1 beyond the project existing.
- Produces: the six entity types below (`Ingredient`, `Tag`, `Recipe`, `RecipeIngredient`, `InventoryItem`, plus enums `Rating`/`StorageLocation`) with exactly these property names/types — every later task's calculators, seeders, and ViewModels reference these names verbatim.

No automated test — this codebase doesn't unit-test EF entities/configs directly (Finance's design didn't either; only its pure logic services got tests). Verification is `EnsureCreatedAsync()` succeeding at app startup.

- [ ] **Step 1: Create the two enums**

`src/AaronOS.Modules.Nutrition/Data/Rating.cs`:

```csharp
namespace AaronOS.Modules.Nutrition.Data;

public enum Rating { Dislike, Neutral, Like }
```

`src/AaronOS.Modules.Nutrition/Data/StorageLocation.cs`:

```csharp
namespace AaronOS.Modules.Nutrition.Data;

public enum StorageLocation { Fridge, Freezer, Pantry }
```

- [ ] **Step 2: Create `Ingredient` and `Tag` with their configs**

`src/AaronOS.Modules.Nutrition/Data/Ingredient.cs`:

```csharp
namespace AaronOS.Modules.Nutrition.Data;

public class Ingredient
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public Rating? Rating { get; set; }
    public string? PreferredForm { get; set; }
    public decimal? CaloriesPer100g { get; set; }
    public decimal? ProteinPer100g { get; set; }
    public decimal? FatPer100g { get; set; }
    public decimal? CarbsPer100g { get; set; }
    public decimal? FiberPer100g { get; set; }
    public decimal? SodiumMgPer100g { get; set; }
    public decimal? CostPer100g { get; set; }
    public int? FdcId { get; set; }
    public List<Tag> Tags { get; set; } = [];
}
```

`src/AaronOS.Modules.Nutrition/Data/Tag.cs`:

```csharp
namespace AaronOS.Modules.Nutrition.Data;

public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<Ingredient> Ingredients { get; set; } = [];
}
```

`src/AaronOS.Modules.Nutrition/Data/IngredientConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Nutrition.Data;

public class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Name).IsRequired();
        builder.HasIndex(i => i.Name).IsUnique();
        builder.Property(i => i.CaloriesPer100g).HasPrecision(8, 2);
        builder.Property(i => i.ProteinPer100g).HasPrecision(8, 2);
        builder.Property(i => i.FatPer100g).HasPrecision(8, 2);
        builder.Property(i => i.CarbsPer100g).HasPrecision(8, 2);
        builder.Property(i => i.FiberPer100g).HasPrecision(8, 2);
        builder.Property(i => i.SodiumMgPer100g).HasPrecision(8, 2);
        builder.Property(i => i.CostPer100g).HasPrecision(8, 2);

        builder.HasMany(i => i.Tags)
            .WithMany(t => t.Ingredients)
            .UsingEntity(j => j.ToTable("IngredientTags"));
    }
}
```

`src/AaronOS.Modules.Nutrition/Data/TagConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Nutrition.Data;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired();
        builder.HasIndex(t => t.Name).IsUnique();
    }
}
```

- [ ] **Step 3: Create `Recipe` and `RecipeIngredient` with their configs**

`src/AaronOS.Modules.Nutrition/Data/Recipe.cs`:

```csharp
namespace AaronOS.Modules.Nutrition.Data;

public class Recipe
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Instructions { get; set; }
    public int Servings { get; set; } = 1;
    public List<RecipeIngredient> Ingredients { get; set; } = [];
}
```

`src/AaronOS.Modules.Nutrition/Data/RecipeConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Nutrition.Data;

public class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public void Configure(EntityTypeBuilder<Recipe> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired();
    }
}
```

`src/AaronOS.Modules.Nutrition/Data/RecipeIngredient.cs`:

```csharp
namespace AaronOS.Modules.Nutrition.Data;

public class RecipeIngredient
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public Recipe? Recipe { get; set; }
    public int IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public decimal QuantityGrams { get; set; }
    public string? DisplayAmount { get; set; }
    public string? FormUsed { get; set; }
}
```

`src/AaronOS.Modules.Nutrition/Data/RecipeIngredientConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Nutrition.Data;

public class RecipeIngredientConfiguration : IEntityTypeConfiguration<RecipeIngredient>
{
    public void Configure(EntityTypeBuilder<RecipeIngredient> builder)
    {
        builder.HasKey(ri => ri.Id);
        builder.Property(ri => ri.QuantityGrams).HasPrecision(8, 2);

        builder.HasOne(ri => ri.Recipe)
            .WithMany(r => r.Ingredients)
            .HasForeignKey(ri => ri.RecipeId);

        builder.HasOne(ri => ri.Ingredient)
            .WithMany()
            .HasForeignKey(ri => ri.IngredientId);
    }
}
```

- [ ] **Step 4: Create `InventoryItem` with its config**

`src/AaronOS.Modules.Nutrition/Data/InventoryItem.cs`:

```csharp
namespace AaronOS.Modules.Nutrition.Data;

public class InventoryItem
{
    public int Id { get; set; }
    public int IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public StorageLocation StorageLocation { get; set; }
    public DateOnly DateAcquired { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? QuantityLabel { get; set; }
    public string? Notes { get; set; }
}
```

`src/AaronOS.Modules.Nutrition/Data/InventoryItemConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Nutrition.Data;

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.HasKey(i => i.Id);

        builder.HasOne(i => i.Ingredient)
            .WithMany()
            .HasForeignKey(i => i.IngredientId);
    }
}
```

- [ ] **Step 5: Build and manually verify schema creation**

Run: `dotnet build AaronOS.slnx`
Expected: builds with no errors.

Delete the local dev database if one already exists (`%LocalAppData%\AaronOS\aaronos.db`) so `EnsureCreatedAsync()` recreates the schema fresh, then run the app once and confirm it starts without an exception (the six new tables get created silently). Close the app.

- [ ] **Step 6: Commit**

```bash
git add src/AaronOS.Modules.Nutrition/Data
git commit -m "$(cat <<'EOF'
Add Nutrition module's core entities

Ingredient, Tag (implicit many-to-many), Recipe, RecipeIngredient, and
InventoryItem, each with an IEntityTypeConfiguration<T> auto-discovered
by AaronOsDbContext — no shared tables with any other module.
EOF
)"
```

---

### Task 3: Recipe calculation services

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/Calculations/RecipeNutritionCalculator.cs`
- Create: `src/AaronOS.Modules.Nutrition/Calculations/RecipeCompatibilityChecker.cs`
- Create: `src/AaronOS.Modules.Nutrition/Calculations/RecipeStockChecker.cs`
- Create: `src/AaronOS.Modules.Nutrition.Tests/AaronOS.Modules.Nutrition.Tests.csproj`
- Test: `src/AaronOS.Modules.Nutrition.Tests/RecipeNutritionCalculatorTests.cs`
- Test: `src/AaronOS.Modules.Nutrition.Tests/RecipeCompatibilityCheckerTests.cs`
- Test: `src/AaronOS.Modules.Nutrition.Tests/RecipeStockCheckerTests.cs`
- Modify: `AaronOS.slnx`

**Interfaces:**
- Consumes: `Ingredient`, `Tag`, `Recipe`, `RecipeIngredient`, `InventoryItem`, `Rating`, `StorageLocation` from Task 2 (`AaronOS.Modules.Nutrition.Data`), with exactly the property names defined there.
- Produces:
  - `RecipeNutritionTotals(decimal Calories, decimal Protein, decimal Fat, decimal Carbs, decimal Fiber, decimal SodiumMg, decimal Cost)` — a record.
  - `RecipeNutritionCalculator.CalculateTotals(IEnumerable<RecipeIngredient>)` and `.CalculatePerServing(IEnumerable<RecipeIngredient>, int servings)`, both returning `RecipeNutritionTotals`.
  - `CompatibilityLevel` enum (`Clear`, `Caution`, `Blocked`) and `CompatibilityConcern(CompatibilityLevel Level, string Message)` record.
  - `RecipeCompatibilityChecker.CheckRecipe(IEnumerable<RecipeIngredient>)` returning `List<CompatibilityConcern>`.
  - `IngredientStockStatus(int IngredientId, string IngredientName, bool InStock, bool ExpiringSoon)` record and `RecipeStockResult(bool HasEverything, List<IngredientStockStatus> Ingredients)` record, the latter exposing `MissingIngredientNames` (`List<string>`) and `HasExpiringSoonIngredient` (`bool`) as computed properties.
  - `RecipeStockChecker.CheckStock(IEnumerable<RecipeIngredient>, IEnumerable<InventoryItem>, DateOnly today, int expiringSoonWithinDays = 3)` returning `RecipeStockResult`. Later tasks (ViewModels) call these three static classes directly — don't rename any of the above.

This is the first task with real TDD — build the test project first, then work test-first through each calculator.

- [ ] **Step 1: Create the test project**

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
    <ProjectReference Include="..\AaronOS.Modules.Nutrition\AaronOS.Modules.Nutrition.csproj" />
  </ItemGroup>

</Project>
```

Add it to `AaronOS.slnx` alongside the other project entries:

```xml
    <Project Path="src/AaronOS.Modules.Nutrition.Tests/AaronOS.Modules.Nutrition.Tests.csproj" />
```

- [ ] **Step 2: Write the failing test for `RecipeNutritionCalculator`**

`src/AaronOS.Modules.Nutrition.Tests/RecipeNutritionCalculatorTests.cs`:

```csharp
using AaronOS.Modules.Nutrition.Calculations;
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Tests;

public class RecipeNutritionCalculatorTests
{
    private static RecipeIngredient Line(decimal quantityGrams, decimal caloriesPer100g, decimal proteinPer100g, decimal costPer100g) => new()
    {
        QuantityGrams = quantityGrams,
        Ingredient = new Ingredient
        {
            Name = "Test Ingredient",
            CaloriesPer100g = caloriesPer100g,
            ProteinPer100g = proteinPer100g,
            CostPer100g = costPer100g
        }
    };

    [Fact]
    public void CalculateTotals_SumsAcrossIngredients_ScaledByQuantity()
    {
        var lines = new List<RecipeIngredient>
        {
            Line(quantityGrams: 200, caloriesPer100g: 150, proteinPer100g: 20, costPer100g: 1.00m),
            Line(quantityGrams: 50, caloriesPer100g: 400, proteinPer100g: 5, costPer100g: 2.00m),
        };

        var totals = RecipeNutritionCalculator.CalculateTotals(lines);

        Assert.Equal(500m, totals.Calories); // 200/100*150 + 50/100*400 = 300 + 200
        Assert.Equal(42.5m, totals.Protein);  // 200/100*20 + 50/100*5 = 40 + 2.5
        Assert.Equal(3.00m, totals.Cost);     // 200/100*1 + 50/100*2 = 2 + 1
    }

    [Fact]
    public void CalculatePerServing_DividesTotalsByServings()
    {
        var lines = new List<RecipeIngredient> { Line(quantityGrams: 400, caloriesPer100g: 100, proteinPer100g: 10, costPer100g: 1.00m) };

        var perServing = RecipeNutritionCalculator.CalculatePerServing(lines, servings: 4);

        Assert.Equal(100m, perServing.Calories); // 400 total / 4 servings
        Assert.Equal(10m, perServing.Protein);
    }

    [Fact]
    public void CalculatePerServing_Throws_WhenServingsIsZeroOrNegative()
    {
        var lines = new List<RecipeIngredient> { Line(100, 100, 10, 1.00m) };

        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeNutritionCalculator.CalculatePerServing(lines, servings: 0));
    }

    [Fact]
    public void CalculateTotals_TreatsMissingNutritionFieldsAsZero()
    {
        var lines = new List<RecipeIngredient>
        {
            new() { QuantityGrams = 100, Ingredient = new Ingredient { Name = "Unrated" } }
        };

        var totals = RecipeNutritionCalculator.CalculateTotals(lines);

        Assert.Equal(0m, totals.Calories);
        Assert.Equal(0m, totals.Cost);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter RecipeNutritionCalculatorTests`
Expected: FAIL to compile (`RecipeNutritionCalculator`/`Calculations` namespace doesn't exist yet).

- [ ] **Step 4: Implement `RecipeNutritionCalculator`**

`src/AaronOS.Modules.Nutrition/Calculations/RecipeNutritionCalculator.cs`:

```csharp
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Calculations;

public record RecipeNutritionTotals(
    decimal Calories, decimal Protein, decimal Fat, decimal Carbs, decimal Fiber, decimal SodiumMg, decimal Cost);

public static class RecipeNutritionCalculator
{
    public static RecipeNutritionTotals CalculateTotals(IEnumerable<RecipeIngredient> ingredients)
    {
        decimal calories = 0, protein = 0, fat = 0, carbs = 0, fiber = 0, sodium = 0, cost = 0;

        foreach (var ri in ingredients)
        {
            var ingredient = ri.Ingredient
                ?? throw new InvalidOperationException($"RecipeIngredient {ri.Id} has no loaded Ingredient.");
            var factor = ri.QuantityGrams / 100m;

            calories += factor * (ingredient.CaloriesPer100g ?? 0);
            protein += factor * (ingredient.ProteinPer100g ?? 0);
            fat += factor * (ingredient.FatPer100g ?? 0);
            carbs += factor * (ingredient.CarbsPer100g ?? 0);
            fiber += factor * (ingredient.FiberPer100g ?? 0);
            sodium += factor * (ingredient.SodiumMgPer100g ?? 0);
            cost += factor * (ingredient.CostPer100g ?? 0);
        }

        return new RecipeNutritionTotals(calories, protein, fat, carbs, fiber, sodium, cost);
    }

    public static RecipeNutritionTotals CalculatePerServing(IEnumerable<RecipeIngredient> ingredients, int servings)
    {
        if (servings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(servings), "Servings must be at least 1.");
        }

        var totals = CalculateTotals(ingredients);
        return new RecipeNutritionTotals(
            totals.Calories / servings,
            totals.Protein / servings,
            totals.Fat / servings,
            totals.Carbs / servings,
            totals.Fiber / servings,
            totals.SodiumMg / servings,
            totals.Cost / servings);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter RecipeNutritionCalculatorTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Write the failing test for `RecipeCompatibilityChecker`**

`src/AaronOS.Modules.Nutrition.Tests/RecipeCompatibilityCheckerTests.cs`:

```csharp
using AaronOS.Modules.Nutrition.Calculations;
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Tests;

public class RecipeCompatibilityCheckerTests
{
    private static RecipeIngredient Line(Ingredient ingredient, string? formUsed = null) =>
        new() { Ingredient = ingredient, FormUsed = formUsed };

    [Fact]
    public void FlagsDislikedIngredient_AsBlocked()
    {
        var truffle = new Ingredient { Name = "Truffle", Rating = Rating.Dislike };
        var lines = new List<RecipeIngredient> { Line(truffle) };

        var concerns = RecipeCompatibilityChecker.CheckRecipe(lines);

        Assert.Single(concerns);
        Assert.Equal(CompatibilityLevel.Blocked, concerns[0].Level);
        Assert.Contains("Truffle", concerns[0].Message);
    }

    [Fact]
    public void FlagsUnratedIngredient_SharingTagWithDislikedIngredient_AsCaution()
    {
        var fungiTag = new Tag { Name = "fungi" };
        var mushroom = new Ingredient { Name = "Mushroom", Rating = Rating.Dislike, Tags = [fungiTag] };
        var truffle = new Ingredient { Name = "Truffle", Rating = null, Tags = [fungiTag] };
        var lines = new List<RecipeIngredient> { Line(mushroom), Line(truffle) };

        var concerns = RecipeCompatibilityChecker.CheckRecipe(lines);

        Assert.Contains(concerns, c => c.Level == CompatibilityLevel.Caution && c.Message.Contains("Truffle"));
    }

    [Fact]
    public void DoesNotFlag_UnratedIngredientWithNoSharedTags()
    {
        var mushroom = new Ingredient { Name = "Mushroom", Rating = Rating.Dislike, Tags = [new Tag { Name = "fungi" }] };
        var carrot = new Ingredient { Name = "Carrot", Rating = null, Tags = [new Tag { Name = "root-vegetable" }] };
        var lines = new List<RecipeIngredient> { Line(mushroom), Line(carrot) };

        var concerns = RecipeCompatibilityChecker.CheckRecipe(lines);

        Assert.DoesNotContain(concerns, c => c.Message.Contains("Carrot"));
    }

    [Fact]
    public void FlagsFormMismatch_AsCaution()
    {
        var chicken = new Ingredient { Name = "Chicken", PreferredForm = "fresh" };
        var lines = new List<RecipeIngredient> { Line(chicken, formUsed: "canned") };

        var concerns = RecipeCompatibilityChecker.CheckRecipe(lines);

        Assert.Contains(concerns, c => c.Level == CompatibilityLevel.Caution && c.Message.Contains("fresh") && c.Message.Contains("canned"));
    }

    [Fact]
    public void DoesNotFlagFormMismatch_WhenFormsMatch()
    {
        var chicken = new Ingredient { Name = "Chicken", PreferredForm = "fresh" };
        var lines = new List<RecipeIngredient> { Line(chicken, formUsed: "fresh") };

        var concerns = RecipeCompatibilityChecker.CheckRecipe(lines);

        Assert.Empty(concerns);
    }
}
```

- [ ] **Step 7: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter RecipeCompatibilityCheckerTests`
Expected: FAIL to compile.

- [ ] **Step 8: Implement `RecipeCompatibilityChecker`**

`src/AaronOS.Modules.Nutrition/Calculations/RecipeCompatibilityChecker.cs`:

```csharp
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Calculations;

public enum CompatibilityLevel { Clear, Caution, Blocked }

public record CompatibilityConcern(CompatibilityLevel Level, string Message);

/// <summary>
/// Flags a recipe's ingredients against preferences: a hard flag for anything rated Dislike, a
/// soft flag for unrated ingredients sharing a tag with a Dislike-rated ingredient (never
/// auto-assumes a rating — just a hint), and a soft note when a recipe's FormUsed differs from
/// the ingredient's PreferredForm.
/// </summary>
public static class RecipeCompatibilityChecker
{
    public static List<CompatibilityConcern> CheckRecipe(IEnumerable<RecipeIngredient> recipeIngredients)
    {
        var items = recipeIngredients.ToList();
        var dislikedTags = items
            .Select(ri => ri.Ingredient)
            .Where(i => i is not null && i.Rating == Rating.Dislike)
            .SelectMany(i => i!.Tags)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var concerns = new List<CompatibilityConcern>();

        foreach (var ri in items)
        {
            var ingredient = ri.Ingredient
                ?? throw new InvalidOperationException($"RecipeIngredient {ri.Id} has no loaded Ingredient.");

            if (ingredient.Rating == Rating.Dislike)
            {
                concerns.Add(new CompatibilityConcern(
                    CompatibilityLevel.Blocked, $"Contains disliked ingredient: {ingredient.Name}."));
                continue;
            }

            if (ingredient.Rating is null)
            {
                var sharedTag = ingredient.Tags.FirstOrDefault(t => dislikedTags.Contains(t.Name));
                if (sharedTag is not null)
                {
                    concerns.Add(new CompatibilityConcern(
                        CompatibilityLevel.Caution,
                        $"Possible dislike (tagged {sharedTag.Name}): {ingredient.Name}."));
                }
            }

            if (!string.IsNullOrWhiteSpace(ri.FormUsed)
                && !string.IsNullOrWhiteSpace(ingredient.PreferredForm)
                && !string.Equals(ri.FormUsed, ingredient.PreferredForm, StringComparison.OrdinalIgnoreCase))
            {
                concerns.Add(new CompatibilityConcern(
                    CompatibilityLevel.Caution,
                    $"You prefer {ingredient.Name} {ingredient.PreferredForm}; this recipe uses {ri.FormUsed}."));
            }
        }

        return concerns;
    }
}
```

- [ ] **Step 9: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter RecipeCompatibilityCheckerTests`
Expected: PASS (5 tests).

- [ ] **Step 10: Write the failing test for `RecipeStockChecker`**

`src/AaronOS.Modules.Nutrition.Tests/RecipeStockCheckerTests.cs`:

```csharp
using AaronOS.Modules.Nutrition.Calculations;
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Tests;

public class RecipeStockCheckerTests
{
    private static readonly DateOnly Today = new(2026, 7, 27);

    private static RecipeIngredient Line(int ingredientId, string name) =>
        new() { IngredientId = ingredientId, Ingredient = new Ingredient { Name = name } };

    [Fact]
    public void ReportsHasEverything_WhenAllIngredientsHaveInventory()
    {
        var lines = new List<RecipeIngredient> { Line(1, "Chicken"), Line(2, "Rice") };
        var inventory = new List<InventoryItem>
        {
            new() { IngredientId = 1, DateAcquired = Today },
            new() { IngredientId = 2, DateAcquired = Today },
        };

        var result = RecipeStockChecker.CheckStock(lines, inventory, Today);

        Assert.True(result.HasEverything);
        Assert.Empty(result.MissingIngredientNames);
    }

    [Fact]
    public void ReportsMissingIngredients_ByName()
    {
        var lines = new List<RecipeIngredient> { Line(1, "Chicken"), Line(2, "Rice") };
        var inventory = new List<InventoryItem> { new() { IngredientId = 1, DateAcquired = Today } };

        var result = RecipeStockChecker.CheckStock(lines, inventory, Today);

        Assert.False(result.HasEverything);
        Assert.Equal(["Rice"], result.MissingIngredientNames);
    }

    [Fact]
    public void FlagsExpiringSoon_WithinThreshold()
    {
        var lines = new List<RecipeIngredient> { Line(1, "Chicken") };
        var inventory = new List<InventoryItem>
        {
            new() { IngredientId = 1, DateAcquired = Today, ExpiresOn = Today.AddDays(2) }
        };

        var result = RecipeStockChecker.CheckStock(lines, inventory, Today, expiringSoonWithinDays: 3);

        Assert.True(result.HasExpiringSoonIngredient);
    }

    [Fact]
    public void DoesNotFlagExpiringSoon_WhenBeyondThreshold()
    {
        var lines = new List<RecipeIngredient> { Line(1, "Chicken") };
        var inventory = new List<InventoryItem>
        {
            new() { IngredientId = 1, DateAcquired = Today, ExpiresOn = Today.AddDays(10) }
        };

        var result = RecipeStockChecker.CheckStock(lines, inventory, Today, expiringSoonWithinDays: 3);

        Assert.False(result.HasExpiringSoonIngredient);
    }

    [Fact]
    public void DoesNotFlagExpiringSoon_ForAlreadyExpiredItems()
    {
        var lines = new List<RecipeIngredient> { Line(1, "Chicken") };
        var inventory = new List<InventoryItem>
        {
            new() { IngredientId = 1, DateAcquired = Today, ExpiresOn = Today.AddDays(-1) }
        };

        var result = RecipeStockChecker.CheckStock(lines, inventory, Today, expiringSoonWithinDays: 3);

        Assert.False(result.HasExpiringSoonIngredient);
    }
}
```

- [ ] **Step 11: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter RecipeStockCheckerTests`
Expected: FAIL to compile.

- [ ] **Step 12: Implement `RecipeStockChecker`**

`src/AaronOS.Modules.Nutrition/Calculations/RecipeStockChecker.cs`:

```csharp
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Calculations;

public record IngredientStockStatus(int IngredientId, string IngredientName, bool InStock, bool ExpiringSoon);

public record RecipeStockResult(bool HasEverything, List<IngredientStockStatus> Ingredients)
{
    public List<string> MissingIngredientNames =>
        Ingredients.Where(i => !i.InStock).Select(i => i.IngredientName).ToList();

    public bool HasExpiringSoonIngredient => Ingredients.Any(i => i.InStock && i.ExpiringSoon);
}

/// <summary>
/// Reports per-ingredient in-stock/missing for a recipe against the current inventory, and
/// whether any in-stock ingredient is within expiringSoonWithinDays of its ExpiresOn (already-
/// expired items don't count as "expiring soon" — they're a separate, worse state the Inventory
/// page flags directly).
/// </summary>
public static class RecipeStockChecker
{
    public static RecipeStockResult CheckStock(
        IEnumerable<RecipeIngredient> recipeIngredients,
        IEnumerable<InventoryItem> inventory,
        DateOnly today,
        int expiringSoonWithinDays = 3)
    {
        var inventoryByIngredient = inventory
            .GroupBy(i => i.IngredientId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var statuses = new List<IngredientStockStatus>();

        foreach (var ri in recipeIngredients)
        {
            var ingredient = ri.Ingredient
                ?? throw new InvalidOperationException($"RecipeIngredient {ri.Id} has no loaded Ingredient.");

            var hasStock = inventoryByIngredient.TryGetValue(ri.IngredientId, out var items);
            var expiringSoon = hasStock && items!.Any(i =>
            {
                if (i.ExpiresOn is not { } expires)
                {
                    return false;
                }

                var daysLeft = expires.DayNumber - today.DayNumber;
                return daysLeft >= 0 && daysLeft <= expiringSoonWithinDays;
            });

            statuses.Add(new IngredientStockStatus(ri.IngredientId, ingredient.Name, hasStock, expiringSoon));
        }

        return new RecipeStockResult(statuses.All(s => s.InStock), statuses);
    }
}
```

- [ ] **Step 13: Run all Task 3 tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests`
Expected: PASS (14 tests total).

- [ ] **Step 14: Commit**

```bash
git add AaronOS.slnx src/AaronOS.Modules.Nutrition/Calculations src/AaronOS.Modules.Nutrition.Tests
git commit -m "$(cat <<'EOF'
Add recipe calculation services with tests

RecipeNutritionCalculator (per-100g rollup), RecipeCompatibilityChecker
(dislike/tag-based/form-mismatch flags), and RecipeStockChecker
(in-stock/expiring-soon against inventory) — all pure functions,
unit-tested, no DB dependency.
EOF
)"
```

---

### Task 4: Shelf-life reference data and estimator

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/Resources/ShelfLifeReference.json`
- Create: `src/AaronOS.Modules.Nutrition/ShelfLife/ShelfLifeReferenceEntry.cs`
- Create: `src/AaronOS.Modules.Nutrition/ShelfLife/ShelfLifeEstimator.cs`
- Test: `src/AaronOS.Modules.Nutrition.Tests/ShelfLifeEstimatorTests.cs`

**Interfaces:**
- Consumes: `StorageLocation` enum from Task 2.
- Produces: `ShelfLifeReferenceEntry(string Keyword, int FridgeDays, int FreezerDays, int PantryDays)` record; `ShelfLifeEstimator` with a public constructor `ShelfLifeEstimator(string referenceJson)` (used directly by tests), a static factory `ShelfLifeEstimator.LoadFromEmbeddedResource()` (used by DI registration in Task 11), `FindMatch(string ingredientName)` returning `ShelfLifeReferenceEntry?`, and `EstimateExpiration(string ingredientName, StorageLocation storageLocation, DateOnly dateAcquired)` returning `DateOnly?`. Task 10's `InventoryViewModel` calls `EstimateExpiration` — don't rename it.

- [ ] **Step 1: Create the reference JSON resource**

`src/AaronOS.Modules.Nutrition/Resources/ShelfLifeReference.json`. Entries are ordered from most specific keyword to least specific, since matching is a case-insensitive substring `Contains` check against the ingredient name and the **first** match in this list wins — e.g. "rice, cooked" must appear before the bare "rice" fallback, or a cooked-rice ingredient would match the raw-rice entry instead. This is a hand-curated subset of FDA FoodKeeper-style guidance, not exhaustive — ingredients with no match just get no suggested date.

```json
[
  { "Keyword": "chicken breast", "FridgeDays": 2, "FreezerDays": 270, "PantryDays": 0 },
  { "Keyword": "chicken thigh", "FridgeDays": 2, "FreezerDays": 270, "PantryDays": 0 },
  { "Keyword": "chicken wing", "FridgeDays": 2, "FreezerDays": 270, "PantryDays": 0 },
  { "Keyword": "chicken drumstick", "FridgeDays": 2, "FreezerDays": 270, "PantryDays": 0 },
  { "Keyword": "chicken", "FridgeDays": 2, "FreezerDays": 270, "PantryDays": 0 },
  { "Keyword": "ground beef", "FridgeDays": 2, "FreezerDays": 120, "PantryDays": 0 },
  { "Keyword": "ground turkey", "FridgeDays": 2, "FreezerDays": 120, "PantryDays": 0 },
  { "Keyword": "steak", "FridgeDays": 4, "FreezerDays": 180, "PantryDays": 0 },
  { "Keyword": "pork chop", "FridgeDays": 4, "FreezerDays": 180, "PantryDays": 0 },
  { "Keyword": "pork tenderloin", "FridgeDays": 4, "FreezerDays": 180, "PantryDays": 0 },
  { "Keyword": "bacon", "FridgeDays": 7, "FreezerDays": 30, "PantryDays": 0 },
  { "Keyword": "sausage", "FridgeDays": 2, "FreezerDays": 60, "PantryDays": 0 },
  { "Keyword": "turkey breast", "FridgeDays": 2, "FreezerDays": 120, "PantryDays": 0 },
  { "Keyword": "deli", "FridgeDays": 5, "FreezerDays": 60, "PantryDays": 0 },
  { "Keyword": "ham", "FridgeDays": 5, "FreezerDays": 60, "PantryDays": 0 },
  { "Keyword": "hot dog", "FridgeDays": 7, "FreezerDays": 60, "PantryDays": 0 },
  { "Keyword": "lamb", "FridgeDays": 4, "FreezerDays": 180, "PantryDays": 0 },
  { "Keyword": "salmon", "FridgeDays": 2, "FreezerDays": 90, "PantryDays": 0 },
  { "Keyword": "tuna", "FridgeDays": 2, "FreezerDays": 90, "PantryDays": 0 },
  { "Keyword": "tilapia", "FridgeDays": 2, "FreezerDays": 90, "PantryDays": 0 },
  { "Keyword": "cod", "FridgeDays": 2, "FreezerDays": 90, "PantryDays": 0 },
  { "Keyword": "shrimp", "FridgeDays": 2, "FreezerDays": 180, "PantryDays": 0 },
  { "Keyword": "fish", "FridgeDays": 2, "FreezerDays": 90, "PantryDays": 0 },
  { "Keyword": "egg white", "FridgeDays": 4, "FreezerDays": 365, "PantryDays": 0 },
  { "Keyword": "egg", "FridgeDays": 35, "FreezerDays": 365, "PantryDays": 0 },
  { "Keyword": "tofu", "FridgeDays": 5, "FreezerDays": 150, "PantryDays": 0 },
  { "Keyword": "tempeh", "FridgeDays": 10, "FreezerDays": 150, "PantryDays": 0 },
  { "Keyword": "milk", "FridgeDays": 7, "FreezerDays": 90, "PantryDays": 0 },
  { "Keyword": "yogurt", "FridgeDays": 14, "FreezerDays": 60, "PantryDays": 0 },
  { "Keyword": "cottage cheese", "FridgeDays": 7, "FreezerDays": 0, "PantryDays": 0 },
  { "Keyword": "cream cheese", "FridgeDays": 14, "FreezerDays": 60, "PantryDays": 0 },
  { "Keyword": "cheddar", "FridgeDays": 28, "FreezerDays": 180, "PantryDays": 0 },
  { "Keyword": "mozzarella", "FridgeDays": 21, "FreezerDays": 180, "PantryDays": 0 },
  { "Keyword": "parmesan", "FridgeDays": 30, "FreezerDays": 180, "PantryDays": 0 },
  { "Keyword": "cheese", "FridgeDays": 21, "FreezerDays": 180, "PantryDays": 0 },
  { "Keyword": "butter", "FridgeDays": 30, "FreezerDays": 270, "PantryDays": 0 },
  { "Keyword": "sour cream", "FridgeDays": 21, "FreezerDays": 0, "PantryDays": 0 },
  { "Keyword": "heavy cream", "FridgeDays": 10, "FreezerDays": 60, "PantryDays": 0 },
  { "Keyword": "lettuce", "FridgeDays": 7, "FreezerDays": 0, "PantryDays": 0 },
  { "Keyword": "spinach", "FridgeDays": 5, "FreezerDays": 300, "PantryDays": 0 },
  { "Keyword": "kale", "FridgeDays": 5, "FreezerDays": 300, "PantryDays": 0 },
  { "Keyword": "broccoli", "FridgeDays": 5, "FreezerDays": 300, "PantryDays": 0 },
  { "Keyword": "cauliflower", "FridgeDays": 5, "FreezerDays": 300, "PantryDays": 0 },
  { "Keyword": "carrot", "FridgeDays": 21, "FreezerDays": 300, "PantryDays": 0 },
  { "Keyword": "celery", "FridgeDays": 14, "FreezerDays": 0, "PantryDays": 0 },
  { "Keyword": "cucumber", "FridgeDays": 7, "FreezerDays": 0, "PantryDays": 0 },
  { "Keyword": "bell pepper", "FridgeDays": 7, "FreezerDays": 240, "PantryDays": 0 },
  { "Keyword": "tomato", "FridgeDays": 5, "FreezerDays": 60, "PantryDays": 3 },
  { "Keyword": "mushroom", "FridgeDays": 7, "FreezerDays": 300, "PantryDays": 0 },
  { "Keyword": "zucchini", "FridgeDays": 7, "FreezerDays": 300, "PantryDays": 0 },
  { "Keyword": "onion", "FridgeDays": 30, "FreezerDays": 240, "PantryDays": 30 },
  { "Keyword": "garlic", "FridgeDays": 30, "FreezerDays": 240, "PantryDays": 90 },
  { "Keyword": "potato", "FridgeDays": 21, "FreezerDays": 0, "PantryDays": 60 },
  { "Keyword": "sweet potato", "FridgeDays": 21, "FreezerDays": 0, "PantryDays": 30 },
  { "Keyword": "avocado", "FridgeDays": 5, "FreezerDays": 240, "PantryDays": 5 },
  { "Keyword": "berr", "FridgeDays": 5, "FreezerDays": 240, "PantryDays": 0 },
  { "Keyword": "grape", "FridgeDays": 10, "FreezerDays": 240, "PantryDays": 0 },
  { "Keyword": "apple", "FridgeDays": 28, "FreezerDays": 240, "PantryDays": 14 },
  { "Keyword": "banana", "FridgeDays": 5, "FreezerDays": 60, "PantryDays": 5 },
  { "Keyword": "orange", "FridgeDays": 21, "FreezerDays": 90, "PantryDays": 7 },
  { "Keyword": "lemon", "FridgeDays": 21, "FreezerDays": 90, "PantryDays": 7 },
  { "Keyword": "peach", "FridgeDays": 5, "FreezerDays": 240, "PantryDays": 3 },
  { "Keyword": "pear", "FridgeDays": 5, "FreezerDays": 240, "PantryDays": 3 },
  { "Keyword": "mango", "FridgeDays": 5, "FreezerDays": 240, "PantryDays": 3 },
  { "Keyword": "melon", "FridgeDays": 5, "FreezerDays": 0, "PantryDays": 3 },
  { "Keyword": "leftovers", "FridgeDays": 4, "FreezerDays": 90, "PantryDays": 0 },
  { "Keyword": "soup", "FridgeDays": 4, "FreezerDays": 90, "PantryDays": 0 },
  { "Keyword": "rice, cooked", "FridgeDays": 6, "FreezerDays": 180, "PantryDays": 0 },
  { "Keyword": "pasta, cooked", "FridgeDays": 5, "FreezerDays": 90, "PantryDays": 0 },
  { "Keyword": "rice", "FridgeDays": 0, "FreezerDays": 0, "PantryDays": 730 },
  { "Keyword": "pasta", "FridgeDays": 0, "FreezerDays": 0, "PantryDays": 730 },
  { "Keyword": "quinoa", "FridgeDays": 0, "FreezerDays": 0, "PantryDays": 730 },
  { "Keyword": "oats", "FridgeDays": 0, "FreezerDays": 0, "PantryDays": 365 },
  { "Keyword": "bread", "FridgeDays": 14, "FreezerDays": 90, "PantryDays": 5 },
  { "Keyword": "tortilla", "FridgeDays": 14, "FreezerDays": 90, "PantryDays": 5 },
  { "Keyword": "flour", "FridgeDays": 0, "FreezerDays": 0, "PantryDays": 365 },
  { "Keyword": "canned", "FridgeDays": 0, "FreezerDays": 0, "PantryDays": 730 }
]
```

- [ ] **Step 2: Create the reference-entry DTO**

`src/AaronOS.Modules.Nutrition/ShelfLife/ShelfLifeReferenceEntry.cs`:

```csharp
namespace AaronOS.Modules.Nutrition.ShelfLife;

public record ShelfLifeReferenceEntry(string Keyword, int FridgeDays, int FreezerDays, int PantryDays);
```

- [ ] **Step 3: Write the failing test for `ShelfLifeEstimator`**

`src/AaronOS.Modules.Nutrition.Tests/ShelfLifeEstimatorTests.cs`:

```csharp
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.ShelfLife;

namespace AaronOS.Modules.Nutrition.Tests;

public class ShelfLifeEstimatorTests
{
    private const string SampleJson = """
        [
          { "Keyword": "chicken breast", "FridgeDays": 2, "FreezerDays": 270, "PantryDays": 0 },
          { "Keyword": "chicken", "FridgeDays": 2, "FreezerDays": 270, "PantryDays": 0 },
          { "Keyword": "rice, cooked", "FridgeDays": 6, "FreezerDays": 180, "PantryDays": 0 },
          { "Keyword": "rice", "FridgeDays": 0, "FreezerDays": 0, "PantryDays": 730 }
        ]
        """;

    [Fact]
    public void FindMatch_ReturnsFirstMatchingKeyword_CaseInsensitive()
    {
        var estimator = new ShelfLifeEstimator(SampleJson);

        var match = estimator.FindMatch("Boneless Chicken Breast, raw");

        Assert.NotNull(match);
        Assert.Equal("chicken breast", match!.Keyword);
    }

    [Fact]
    public void FindMatch_PrefersMoreSpecificEarlierEntry_OverGenericLaterOne()
    {
        var estimator = new ShelfLifeEstimator(SampleJson);

        var match = estimator.FindMatch("White Rice, cooked");

        Assert.Equal("rice, cooked", match!.Keyword);
    }

    [Fact]
    public void FindMatch_ReturnsNull_WhenNothingMatches()
    {
        var estimator = new ShelfLifeEstimator(SampleJson);

        Assert.Null(estimator.FindMatch("Dragon Fruit"));
    }

    [Fact]
    public void EstimateExpiration_AddsCorrectDaysForStorageLocation()
    {
        var estimator = new ShelfLifeEstimator(SampleJson);
        var acquired = new DateOnly(2026, 7, 1);

        var fridgeEstimate = estimator.EstimateExpiration("Chicken Breast", StorageLocation.Fridge, acquired);
        var freezerEstimate = estimator.EstimateExpiration("Chicken Breast", StorageLocation.Freezer, acquired);

        Assert.Equal(new DateOnly(2026, 7, 3), fridgeEstimate);
        Assert.Equal(acquired.AddDays(270), freezerEstimate);
    }

    [Fact]
    public void EstimateExpiration_ReturnsNull_WhenNoKeywordMatches()
    {
        var estimator = new ShelfLifeEstimator(SampleJson);

        var estimate = estimator.EstimateExpiration("Dragon Fruit", StorageLocation.Fridge, new DateOnly(2026, 7, 1));

        Assert.Null(estimate);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter ShelfLifeEstimatorTests`
Expected: FAIL to compile.

- [ ] **Step 5: Implement `ShelfLifeEstimator`**

`src/AaronOS.Modules.Nutrition/ShelfLife/ShelfLifeEstimator.cs`:

```csharp
using System.Text.Json;
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.ShelfLife;

/// <summary>
/// Estimates an expiration date from a hand-curated FDA FoodKeeper-style reference dataset,
/// matched by case-insensitive keyword containment against the ingredient name (first match in
/// list order wins — see Resources/ShelfLifeReference.json's ordering note). Takes the JSON text
/// directly via the constructor rather than loading the embedded resource itself, so the matching
/// logic is testable without touching the assembly's resource stream.
/// </summary>
public class ShelfLifeEstimator
{
    private readonly List<ShelfLifeReferenceEntry> _entries;

    public ShelfLifeEstimator(string referenceJson)
    {
        _entries = JsonSerializer.Deserialize<List<ShelfLifeReferenceEntry>>(referenceJson) ?? [];
    }

    public static ShelfLifeEstimator LoadFromEmbeddedResource()
    {
        var assembly = typeof(ShelfLifeEstimator).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "AaronOS.Modules.Nutrition.Resources.ShelfLifeReference.json")
            ?? throw new InvalidOperationException("ShelfLifeReference.json embedded resource not found.");
        using var reader = new StreamReader(stream);
        return new ShelfLifeEstimator(reader.ReadToEnd());
    }

    public ShelfLifeReferenceEntry? FindMatch(string ingredientName) =>
        _entries.FirstOrDefault(e => ingredientName.Contains(e.Keyword, StringComparison.OrdinalIgnoreCase));

    public DateOnly? EstimateExpiration(string ingredientName, StorageLocation storageLocation, DateOnly dateAcquired)
    {
        var match = FindMatch(ingredientName);
        if (match is null)
        {
            return null;
        }

        var days = storageLocation switch
        {
            StorageLocation.Fridge => match.FridgeDays,
            StorageLocation.Freezer => match.FreezerDays,
            StorageLocation.Pantry => match.PantryDays,
            _ => throw new ArgumentOutOfRangeException(nameof(storageLocation))
        };

        return dateAcquired.AddDays(days);
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter ShelfLifeEstimatorTests`
Expected: PASS (5 tests).

- [ ] **Step 7: Register the JSON file as an embedded resource and build**

In `src/AaronOS.Modules.Nutrition/AaronOS.Modules.Nutrition.csproj`, add a new `<ItemGroup>` (or add to an existing one):

```xml
  <ItemGroup>
    <EmbeddedResource Include="Resources\ShelfLifeReference.json" />
  </ItemGroup>
```

Run: `dotnet build AaronOS.slnx`
Expected: builds with no errors — confirms the embedded resource resolves against the real file `ShelfLifeEstimator.LoadFromEmbeddedResource()` will read at runtime.

- [ ] **Step 8: Commit**

```bash
git add src/AaronOS.Modules.Nutrition/Resources/ShelfLifeReference.json src/AaronOS.Modules.Nutrition/ShelfLife src/AaronOS.Modules.Nutrition.Tests/ShelfLifeEstimatorTests.cs src/AaronOS.Modules.Nutrition/AaronOS.Modules.Nutrition.csproj
git commit -m "$(cat <<'EOF'
Add shelf-life reference data and ShelfLifeEstimator

A hand-curated FDA FoodKeeper-style keyword table, embedded as a JSON
resource rather than a seeded DB table (this app has no EF migrations,
so a new build is easier to ship than a schema-evolving seed update).
EOF
)"
```

---

### Task 5: USDA API key credential storage

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/Usda/ApiKeyProtector.cs`
- Create: `src/AaronOS.Modules.Nutrition/Usda/UsdaCredentialStore.cs`
- Test: `src/AaronOS.Modules.Nutrition.Tests/ApiKeyProtectorTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `ApiKeyProtector.Protect(string apiKey) -> byte[]` / `.Unprotect(byte[] encrypted) -> string`; `UsdaCredentialStore` with `bool HasApiKey`, `string? Load()`, `void Save(string apiKey)`. Task 7's `UsdaApiClient` and Task 8's `IngredientsViewModel`/its Page consume `UsdaCredentialStore`.

This mirrors `AaronOS.Modules.Finance.Plaid.AccessTokenProtector`/`PlaidCredentialStore` exactly — duplicated rather than shared, since modules can't reference each other's internals (see Global Constraints).

- [ ] **Step 1: Write the failing round-trip test**

`src/AaronOS.Modules.Nutrition.Tests/ApiKeyProtectorTests.cs`:

```csharp
using AaronOS.Modules.Nutrition.Usda;

namespace AaronOS.Modules.Nutrition.Tests;

public class ApiKeyProtectorTests
{
    [Fact]
    public void RoundTrips_ThroughDpapi()
    {
        const string apiKey = "DEMO_KEY-abc123";

        var encrypted = ApiKeyProtector.Protect(apiKey);
        var decrypted = ApiKeyProtector.Unprotect(encrypted);

        Assert.Equal(apiKey, decrypted);
        Assert.NotEqual(apiKey, System.Text.Encoding.UTF8.GetString(encrypted));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter ApiKeyProtectorTests`
Expected: FAIL to compile (`AaronOS.Modules.Nutrition.Usda` namespace doesn't exist yet).

- [ ] **Step 3: Implement `ApiKeyProtector`**

`src/AaronOS.Modules.Nutrition/Usda/ApiKeyProtector.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace AaronOS.Modules.Nutrition.Usda;

/// <summary>DPAPI (current-user scope) protection for the USDA FoodData Central API key, mirroring
/// AaronOS.Modules.Finance.Plaid.AccessTokenProtector — duplicated rather than shared, since
/// modules can't reference each other's internals and one small DPAPI helper isn't worth
/// promoting to Core for two callers yet.</summary>
public static class ApiKeyProtector
{
    public static byte[] Protect(string apiKey) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), optionalEntropy: null, DataProtectionScope.CurrentUser);

    public static string Unprotect(byte[] encrypted) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser));
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter ApiKeyProtectorTests`
Expected: PASS.

- [ ] **Step 5: Implement `UsdaCredentialStore` (no test — file I/O wrapper, mirrors `PlaidCredentialStore` which also has no test)**

`src/AaronOS.Modules.Nutrition/Usda/UsdaCredentialStore.cs`:

```csharp
using System.Security.Cryptography;

namespace AaronOS.Modules.Nutrition.Usda;

/// <summary>
/// Reads/writes the USDA FoodData Central API key as a DPAPI-protected (current-user scope) file
/// under %LocalAppData%\AaronOS\, mirroring AaronOS.Modules.Finance.Plaid.PlaidCredentialStore.
/// The plaintext key never touches the SQLite database, never gets logged, and never lives in
/// source control.
/// </summary>
public class UsdaCredentialStore
{
    private readonly string _filePath;

    public UsdaCredentialStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AaronOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "usda-credentials.dat");
    }

    public bool HasApiKey => File.Exists(_filePath);

    public string? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var encrypted = File.ReadAllBytes(_filePath);
        return ApiKeyProtector.Unprotect(encrypted);
    }

    public void Save(string apiKey)
    {
        File.WriteAllBytes(_filePath, ApiKeyProtector.Protect(apiKey));
    }
}
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build AaronOS.slnx`
Expected: builds with no errors.

- [ ] **Step 7: Commit**

```bash
git add src/AaronOS.Modules.Nutrition/Usda src/AaronOS.Modules.Nutrition.Tests/ApiKeyProtectorTests.cs
git commit -m "$(cat <<'EOF'
Add DPAPI-protected USDA API key storage

ApiKeyProtector + UsdaCredentialStore mirror Finance's Plaid
credential-store pattern for the one secret this module needs.
EOF
)"
```

---

### Task 6: Preloaded ingredient seed data

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/Resources/IngredientSeed.json`
- Create: `src/AaronOS.Modules.Nutrition/Seeding/IngredientSeedEntry.cs`
- Create: `src/AaronOS.Modules.Nutrition/Seeding/IngredientSeeder.cs`
- Test: `src/AaronOS.Modules.Nutrition.Tests/IngredientSeederTests.cs`

**Interfaces:**
- Consumes: `Ingredient` from Task 2.
- Produces: `IngredientSeeder.ParseSeedFile(string json) -> List<Ingredient>` (pure, tested); `IngredientSeeder.SeedIfEmptyAsync(AaronOsDbContext db) -> Task` (DB-touching, not unit-tested — same precedent as Finance's DB-writing sync code). Task 11's `NutritionDashboardViewModel.LoadAsync` calls `SeedIfEmptyAsync` — don't rename it.

- [ ] **Step 1: Create the seed data resource**

`src/AaronOS.Modules.Nutrition/Resources/IngredientSeed.json` — roughly 135 common grocery ingredients with approximate per-100g macros (a reasonable starting point per the design spec, editable afterward; no `Rating`, `PreferredForm`, `Tags`, or `CostPer100g` pre-set, since those are what you cover yourself):

```json
[
  { "Name": "Chicken breast, raw", "CaloriesPer100g": 120, "ProteinPer100g": 22.5, "FatPer100g": 2.6, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 45 },
  { "Name": "Chicken thigh, raw", "CaloriesPer100g": 179, "ProteinPer100g": 17.6, "FatPer100g": 12.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 71 },
  { "Name": "Chicken wing, raw", "CaloriesPer100g": 203, "ProteinPer100g": 18.4, "FatPer100g": 13.6, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 65 },
  { "Name": "Chicken drumstick, raw", "CaloriesPer100g": 172, "ProteinPer100g": 18.3, "FatPer100g": 10.6, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 79 },
  { "Name": "Ground beef 80/20, raw", "CaloriesPer100g": 254, "ProteinPer100g": 17.2, "FatPer100g": 20.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 66 },
  { "Name": "Ground beef 90/10, raw", "CaloriesPer100g": 176, "ProteinPer100g": 20.0, "FatPer100g": 10.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 66 },
  { "Name": "Beef sirloin steak, raw", "CaloriesPer100g": 158, "ProteinPer100g": 22.0, "FatPer100g": 7.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 56 },
  { "Name": "Pork chop, raw", "CaloriesPer100g": 231, "ProteinPer100g": 19.0, "FatPer100g": 17.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 56 },
  { "Name": "Pork tenderloin, raw", "CaloriesPer100g": 143, "ProteinPer100g": 21.0, "FatPer100g": 5.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 51 },
  { "Name": "Bacon, raw", "CaloriesPer100g": 541, "ProteinPer100g": 37.0, "FatPer100g": 42.0, "CarbsPer100g": 1.4, "FiberPer100g": 0, "SodiumMgPer100g": 1717 },
  { "Name": "Turkey breast, raw", "CaloriesPer100g": 135, "ProteinPer100g": 24.0, "FatPer100g": 3.6, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 63 },
  { "Name": "Ground turkey, raw", "CaloriesPer100g": 149, "ProteinPer100g": 20.0, "FatPer100g": 8.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 75 },
  { "Name": "Deli turkey slices", "CaloriesPer100g": 104, "ProteinPer100g": 17.0, "FatPer100g": 1.7, "CarbsPer100g": 3.5, "FiberPer100g": 0, "SodiumMgPer100g": 1050 },
  { "Name": "Ham, deli", "CaloriesPer100g": 145, "ProteinPer100g": 21.0, "FatPer100g": 5.0, "CarbsPer100g": 1.5, "FiberPer100g": 0, "SodiumMgPer100g": 1200 },
  { "Name": "Lamb, ground raw", "CaloriesPer100g": 282, "ProteinPer100g": 16.0, "FatPer100g": 23.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 69 },
  { "Name": "Sausage, pork", "CaloriesPer100g": 325, "ProteinPer100g": 12.6, "FatPer100g": 30.0, "CarbsPer100g": 1.4, "FiberPer100g": 0, "SodiumMgPer100g": 900 },
  { "Name": "Salmon, raw", "CaloriesPer100g": 208, "ProteinPer100g": 20.0, "FatPer100g": 13.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 59 },
  { "Name": "Tuna, raw", "CaloriesPer100g": 144, "ProteinPer100g": 23.0, "FatPer100g": 4.9, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 39 },
  { "Name": "Tilapia, raw", "CaloriesPer100g": 96, "ProteinPer100g": 20.0, "FatPer100g": 1.7, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 52 },
  { "Name": "Cod, raw", "CaloriesPer100g": 82, "ProteinPer100g": 18.0, "FatPer100g": 0.7, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 54 },
  { "Name": "Shrimp, raw", "CaloriesPer100g": 99, "ProteinPer100g": 24.0, "FatPer100g": 0.3, "CarbsPer100g": 0.2, "FiberPer100g": 0, "SodiumMgPer100g": 111 },
  { "Name": "Egg, whole, raw", "CaloriesPer100g": 143, "ProteinPer100g": 12.6, "FatPer100g": 9.5, "CarbsPer100g": 0.7, "FiberPer100g": 0, "SodiumMgPer100g": 142 },
  { "Name": "Egg white, raw", "CaloriesPer100g": 52, "ProteinPer100g": 10.9, "FatPer100g": 0.2, "CarbsPer100g": 0.7, "FiberPer100g": 0, "SodiumMgPer100g": 166 },
  { "Name": "Tofu, firm", "CaloriesPer100g": 144, "ProteinPer100g": 15.8, "FatPer100g": 8.7, "CarbsPer100g": 3.5, "FiberPer100g": 2.3, "SodiumMgPer100g": 12 },
  { "Name": "Tempeh", "CaloriesPer100g": 195, "ProteinPer100g": 20.3, "FatPer100g": 11.4, "CarbsPer100g": 7.6, "FiberPer100g": 9.0, "SodiumMgPer100g": 9 },
  { "Name": "Milk, whole", "CaloriesPer100g": 61, "ProteinPer100g": 3.2, "FatPer100g": 3.3, "CarbsPer100g": 4.8, "FiberPer100g": 0, "SodiumMgPer100g": 43 },
  { "Name": "Milk, 2%", "CaloriesPer100g": 50, "ProteinPer100g": 3.3, "FatPer100g": 2.0, "CarbsPer100g": 4.9, "FiberPer100g": 0, "SodiumMgPer100g": 44 },
  { "Name": "Milk, skim", "CaloriesPer100g": 34, "ProteinPer100g": 3.4, "FatPer100g": 0.2, "CarbsPer100g": 5.0, "FiberPer100g": 0, "SodiumMgPer100g": 42 },
  { "Name": "Almond milk, unsweetened", "CaloriesPer100g": 15, "ProteinPer100g": 0.6, "FatPer100g": 1.2, "CarbsPer100g": 0.6, "FiberPer100g": 0.3, "SodiumMgPer100g": 63 },
  { "Name": "Half and half", "CaloriesPer100g": 131, "ProteinPer100g": 3.0, "FatPer100g": 11.5, "CarbsPer100g": 4.3, "FiberPer100g": 0, "SodiumMgPer100g": 39 },
  { "Name": "Heavy cream", "CaloriesPer100g": 340, "ProteinPer100g": 2.1, "FatPer100g": 36.0, "CarbsPer100g": 2.8, "FiberPer100g": 0, "SodiumMgPer100g": 27 },
  { "Name": "Sour cream", "CaloriesPer100g": 198, "ProteinPer100g": 2.4, "FatPer100g": 19.4, "CarbsPer100g": 4.6, "FiberPer100g": 0, "SodiumMgPer100g": 61 },
  { "Name": "Yogurt, plain whole milk", "CaloriesPer100g": 61, "ProteinPer100g": 3.5, "FatPer100g": 3.3, "CarbsPer100g": 4.7, "FiberPer100g": 0, "SodiumMgPer100g": 46 },
  { "Name": "Greek yogurt, plain nonfat", "CaloriesPer100g": 59, "ProteinPer100g": 10.2, "FatPer100g": 0.4, "CarbsPer100g": 3.6, "FiberPer100g": 0, "SodiumMgPer100g": 36 },
  { "Name": "Cottage cheese, low fat", "CaloriesPer100g": 72, "ProteinPer100g": 12.4, "FatPer100g": 1.0, "CarbsPer100g": 3.4, "FiberPer100g": 0, "SodiumMgPer100g": 364 },
  { "Name": "Cheddar cheese", "CaloriesPer100g": 403, "ProteinPer100g": 25.0, "FatPer100g": 33.0, "CarbsPer100g": 1.3, "FiberPer100g": 0, "SodiumMgPer100g": 621 },
  { "Name": "Mozzarella cheese, part-skim", "CaloriesPer100g": 254, "ProteinPer100g": 24.0, "FatPer100g": 16.0, "CarbsPer100g": 2.8, "FiberPer100g": 0, "SodiumMgPer100g": 536 },
  { "Name": "Parmesan cheese", "CaloriesPer100g": 431, "ProteinPer100g": 38.0, "FatPer100g": 29.0, "CarbsPer100g": 4.1, "FiberPer100g": 0, "SodiumMgPer100g": 1529 },
  { "Name": "Cream cheese", "CaloriesPer100g": 342, "ProteinPer100g": 6.0, "FatPer100g": 34.0, "CarbsPer100g": 4.1, "FiberPer100g": 0, "SodiumMgPer100g": 321 },
  { "Name": "Butter", "CaloriesPer100g": 717, "ProteinPer100g": 0.9, "FatPer100g": 81.0, "CarbsPer100g": 0.1, "FiberPer100g": 0, "SodiumMgPer100g": 11 },
  { "Name": "Broccoli, raw", "CaloriesPer100g": 34, "ProteinPer100g": 2.8, "FatPer100g": 0.4, "CarbsPer100g": 6.6, "FiberPer100g": 2.6, "SodiumMgPer100g": 33 },
  { "Name": "Spinach, raw", "CaloriesPer100g": 23, "ProteinPer100g": 2.9, "FatPer100g": 0.4, "CarbsPer100g": 3.6, "FiberPer100g": 2.2, "SodiumMgPer100g": 79 },
  { "Name": "Kale, raw", "CaloriesPer100g": 49, "ProteinPer100g": 4.3, "FatPer100g": 0.9, "CarbsPer100g": 8.8, "FiberPer100g": 3.6, "SodiumMgPer100g": 38 },
  { "Name": "Carrot, raw", "CaloriesPer100g": 41, "ProteinPer100g": 0.9, "FatPer100g": 0.2, "CarbsPer100g": 9.6, "FiberPer100g": 2.8, "SodiumMgPer100g": 69 },
  { "Name": "Tomato, raw", "CaloriesPer100g": 18, "ProteinPer100g": 0.9, "FatPer100g": 0.2, "CarbsPer100g": 3.9, "FiberPer100g": 1.2, "SodiumMgPer100g": 5 },
  { "Name": "Cucumber, raw", "CaloriesPer100g": 15, "ProteinPer100g": 0.7, "FatPer100g": 0.1, "CarbsPer100g": 3.6, "FiberPer100g": 0.5, "SodiumMgPer100g": 2 },
  { "Name": "Bell pepper, red raw", "CaloriesPer100g": 31, "ProteinPer100g": 1.0, "FatPer100g": 0.3, "CarbsPer100g": 6.0, "FiberPer100g": 2.1, "SodiumMgPer100g": 4 },
  { "Name": "Onion, raw", "CaloriesPer100g": 40, "ProteinPer100g": 1.1, "FatPer100g": 0.1, "CarbsPer100g": 9.3, "FiberPer100g": 1.7, "SodiumMgPer100g": 4 },
  { "Name": "Garlic, raw", "CaloriesPer100g": 149, "ProteinPer100g": 6.4, "FatPer100g": 0.5, "CarbsPer100g": 33.0, "FiberPer100g": 2.1, "SodiumMgPer100g": 17 },
  { "Name": "Potato, raw", "CaloriesPer100g": 77, "ProteinPer100g": 2.0, "FatPer100g": 0.1, "CarbsPer100g": 17.0, "FiberPer100g": 2.2, "SodiumMgPer100g": 6 },
  { "Name": "Sweet potato, raw", "CaloriesPer100g": 86, "ProteinPer100g": 1.6, "FatPer100g": 0.1, "CarbsPer100g": 20.0, "FiberPer100g": 3.0, "SodiumMgPer100g": 55 },
  { "Name": "Mushroom, white raw", "CaloriesPer100g": 22, "ProteinPer100g": 3.1, "FatPer100g": 0.3, "CarbsPer100g": 3.3, "FiberPer100g": 1.0, "SodiumMgPer100g": 5 },
  { "Name": "Zucchini, raw", "CaloriesPer100g": 17, "ProteinPer100g": 1.2, "FatPer100g": 0.3, "CarbsPer100g": 3.1, "FiberPer100g": 1.0, "SodiumMgPer100g": 8 },
  { "Name": "Cauliflower, raw", "CaloriesPer100g": 25, "ProteinPer100g": 1.9, "FatPer100g": 0.3, "CarbsPer100g": 5.0, "FiberPer100g": 2.0, "SodiumMgPer100g": 30 },
  { "Name": "Lettuce, romaine", "CaloriesPer100g": 17, "ProteinPer100g": 1.2, "FatPer100g": 0.3, "CarbsPer100g": 3.3, "FiberPer100g": 2.1, "SodiumMgPer100g": 8 },
  { "Name": "Celery, raw", "CaloriesPer100g": 16, "ProteinPer100g": 0.7, "FatPer100g": 0.2, "CarbsPer100g": 3.0, "FiberPer100g": 1.6, "SodiumMgPer100g": 80 },
  { "Name": "Green beans, raw", "CaloriesPer100g": 31, "ProteinPer100g": 1.8, "FatPer100g": 0.2, "CarbsPer100g": 7.0, "FiberPer100g": 3.4, "SodiumMgPer100g": 6 },
  { "Name": "Corn, sweet, raw", "CaloriesPer100g": 86, "ProteinPer100g": 3.3, "FatPer100g": 1.2, "CarbsPer100g": 19.0, "FiberPer100g": 2.0, "SodiumMgPer100g": 15 },
  { "Name": "Peas, green raw", "CaloriesPer100g": 81, "ProteinPer100g": 5.4, "FatPer100g": 0.4, "CarbsPer100g": 14.5, "FiberPer100g": 5.7, "SodiumMgPer100g": 5 },
  { "Name": "Asparagus, raw", "CaloriesPer100g": 20, "ProteinPer100g": 2.2, "FatPer100g": 0.1, "CarbsPer100g": 3.9, "FiberPer100g": 2.1, "SodiumMgPer100g": 2 },
  { "Name": "Brussels sprouts, raw", "CaloriesPer100g": 43, "ProteinPer100g": 3.4, "FatPer100g": 0.3, "CarbsPer100g": 9.0, "FiberPer100g": 3.8, "SodiumMgPer100g": 25 },
  { "Name": "Cabbage, raw", "CaloriesPer100g": 25, "ProteinPer100g": 1.3, "FatPer100g": 0.1, "CarbsPer100g": 5.8, "FiberPer100g": 2.5, "SodiumMgPer100g": 18 },
  { "Name": "Beets, raw", "CaloriesPer100g": 43, "ProteinPer100g": 1.6, "FatPer100g": 0.2, "CarbsPer100g": 9.6, "FiberPer100g": 2.8, "SodiumMgPer100g": 78 },
  { "Name": "Radish, raw", "CaloriesPer100g": 16, "ProteinPer100g": 0.7, "FatPer100g": 0.1, "CarbsPer100g": 3.4, "FiberPer100g": 1.6, "SodiumMgPer100g": 39 },
  { "Name": "Avocado", "CaloriesPer100g": 160, "ProteinPer100g": 2.0, "FatPer100g": 14.7, "CarbsPer100g": 8.5, "FiberPer100g": 6.7, "SodiumMgPer100g": 7 },
  { "Name": "Butternut squash, raw", "CaloriesPer100g": 45, "ProteinPer100g": 1.0, "FatPer100g": 0.1, "CarbsPer100g": 12.0, "FiberPer100g": 2.0, "SodiumMgPer100g": 4 },
  { "Name": "Eggplant, raw", "CaloriesPer100g": 25, "ProteinPer100g": 1.0, "FatPer100g": 0.2, "CarbsPer100g": 6.0, "FiberPer100g": 3.0, "SodiumMgPer100g": 2 },
  { "Name": "Bok choy, raw", "CaloriesPer100g": 13, "ProteinPer100g": 1.5, "FatPer100g": 0.2, "CarbsPer100g": 2.2, "FiberPer100g": 1.0, "SodiumMgPer100g": 65 },
  { "Name": "Leek, raw", "CaloriesPer100g": 61, "ProteinPer100g": 1.5, "FatPer100g": 0.3, "CarbsPer100g": 14.0, "FiberPer100g": 1.8, "SodiumMgPer100g": 20 },
  { "Name": "Scallion, raw", "CaloriesPer100g": 32, "ProteinPer100g": 1.8, "FatPer100g": 0.2, "CarbsPer100g": 7.3, "FiberPer100g": 2.6, "SodiumMgPer100g": 16 },
  { "Name": "Shallot, raw", "CaloriesPer100g": 72, "ProteinPer100g": 2.5, "FatPer100g": 0.1, "CarbsPer100g": 16.8, "FiberPer100g": 3.2, "SodiumMgPer100g": 12 },
  { "Name": "Ginger, raw", "CaloriesPer100g": 80, "ProteinPer100g": 1.8, "FatPer100g": 0.8, "CarbsPer100g": 17.8, "FiberPer100g": 2.0, "SodiumMgPer100g": 13 },
  { "Name": "Jalapeno, raw", "CaloriesPer100g": 29, "ProteinPer100g": 0.9, "FatPer100g": 0.4, "CarbsPer100g": 6.5, "FiberPer100g": 2.8, "SodiumMgPer100g": 3 },
  { "Name": "Pumpkin, raw", "CaloriesPer100g": 26, "ProteinPer100g": 1.0, "FatPer100g": 0.1, "CarbsPer100g": 6.5, "FiberPer100g": 0.5, "SodiumMgPer100g": 1 },
  { "Name": "Apple, raw", "CaloriesPer100g": 52, "ProteinPer100g": 0.3, "FatPer100g": 0.2, "CarbsPer100g": 13.8, "FiberPer100g": 2.4, "SodiumMgPer100g": 1 },
  { "Name": "Banana, raw", "CaloriesPer100g": 89, "ProteinPer100g": 1.1, "FatPer100g": 0.3, "CarbsPer100g": 22.8, "FiberPer100g": 2.6, "SodiumMgPer100g": 1 },
  { "Name": "Orange, raw", "CaloriesPer100g": 47, "ProteinPer100g": 0.9, "FatPer100g": 0.1, "CarbsPer100g": 11.8, "FiberPer100g": 2.4, "SodiumMgPer100g": 0 },
  { "Name": "Strawberry, raw", "CaloriesPer100g": 32, "ProteinPer100g": 0.7, "FatPer100g": 0.3, "CarbsPer100g": 7.7, "FiberPer100g": 2.0, "SodiumMgPer100g": 1 },
  { "Name": "Blueberry, raw", "CaloriesPer100g": 57, "ProteinPer100g": 0.7, "FatPer100g": 0.3, "CarbsPer100g": 14.5, "FiberPer100g": 2.4, "SodiumMgPer100g": 1 },
  { "Name": "Raspberry, raw", "CaloriesPer100g": 52, "ProteinPer100g": 1.2, "FatPer100g": 0.7, "CarbsPer100g": 11.9, "FiberPer100g": 6.5, "SodiumMgPer100g": 1 },
  { "Name": "Grapes, raw", "CaloriesPer100g": 69, "ProteinPer100g": 0.7, "FatPer100g": 0.2, "CarbsPer100g": 18.0, "FiberPer100g": 0.9, "SodiumMgPer100g": 2 },
  { "Name": "Lemon, raw", "CaloriesPer100g": 29, "ProteinPer100g": 1.1, "FatPer100g": 0.3, "CarbsPer100g": 9.3, "FiberPer100g": 2.8, "SodiumMgPer100g": 2 },
  { "Name": "Lime, raw", "CaloriesPer100g": 30, "ProteinPer100g": 0.7, "FatPer100g": 0.2, "CarbsPer100g": 10.5, "FiberPer100g": 2.8, "SodiumMgPer100g": 2 },
  { "Name": "Pineapple, raw", "CaloriesPer100g": 50, "ProteinPer100g": 0.5, "FatPer100g": 0.1, "CarbsPer100g": 13.1, "FiberPer100g": 1.4, "SodiumMgPer100g": 1 },
  { "Name": "Mango, raw", "CaloriesPer100g": 60, "ProteinPer100g": 0.8, "FatPer100g": 0.4, "CarbsPer100g": 15.0, "FiberPer100g": 1.6, "SodiumMgPer100g": 1 },
  { "Name": "Watermelon, raw", "CaloriesPer100g": 30, "ProteinPer100g": 0.6, "FatPer100g": 0.2, "CarbsPer100g": 7.6, "FiberPer100g": 0.4, "SodiumMgPer100g": 1 },
  { "Name": "Peach, raw", "CaloriesPer100g": 39, "ProteinPer100g": 0.9, "FatPer100g": 0.3, "CarbsPer100g": 9.5, "FiberPer100g": 1.5, "SodiumMgPer100g": 0 },
  { "Name": "Pear, raw", "CaloriesPer100g": 57, "ProteinPer100g": 0.4, "FatPer100g": 0.1, "CarbsPer100g": 15.2, "FiberPer100g": 3.1, "SodiumMgPer100g": 1 },
  { "Name": "Cherry, raw", "CaloriesPer100g": 63, "ProteinPer100g": 1.1, "FatPer100g": 0.2, "CarbsPer100g": 16.0, "FiberPer100g": 2.1, "SodiumMgPer100g": 0 },
  { "Name": "White rice, raw", "CaloriesPer100g": 365, "ProteinPer100g": 7.1, "FatPer100g": 0.7, "CarbsPer100g": 80.0, "FiberPer100g": 1.3, "SodiumMgPer100g": 5 },
  { "Name": "Brown rice, raw", "CaloriesPer100g": 370, "ProteinPer100g": 7.9, "FatPer100g": 2.9, "CarbsPer100g": 77.0, "FiberPer100g": 3.5, "SodiumMgPer100g": 5 },
  { "Name": "Quinoa, raw", "CaloriesPer100g": 368, "ProteinPer100g": 14.1, "FatPer100g": 6.1, "CarbsPer100g": 64.2, "FiberPer100g": 7.0, "SodiumMgPer100g": 5 },
  { "Name": "Oats, rolled raw", "CaloriesPer100g": 389, "ProteinPer100g": 16.9, "FatPer100g": 6.9, "CarbsPer100g": 66.3, "FiberPer100g": 10.6, "SodiumMgPer100g": 2 },
  { "Name": "Pasta, dry", "CaloriesPer100g": 371, "ProteinPer100g": 13.0, "FatPer100g": 1.5, "CarbsPer100g": 74.7, "FiberPer100g": 3.2, "SodiumMgPer100g": 6 },
  { "Name": "White bread", "CaloriesPer100g": 265, "ProteinPer100g": 9.0, "FatPer100g": 3.2, "CarbsPer100g": 49.0, "FiberPer100g": 2.7, "SodiumMgPer100g": 491 },
  { "Name": "Whole wheat bread", "CaloriesPer100g": 247, "ProteinPer100g": 13.0, "FatPer100g": 3.4, "CarbsPer100g": 41.0, "FiberPer100g": 6.0, "SodiumMgPer100g": 400 },
  { "Name": "Bagel, plain", "CaloriesPer100g": 250, "ProteinPer100g": 10.0, "FatPer100g": 1.5, "CarbsPer100g": 49.0, "FiberPer100g": 2.1, "SodiumMgPer100g": 460 },
  { "Name": "Tortilla, flour", "CaloriesPer100g": 312, "ProteinPer100g": 8.2, "FatPer100g": 7.5, "CarbsPer100g": 52.0, "FiberPer100g": 3.0, "SodiumMgPer100g": 553 },
  { "Name": "Couscous, dry", "CaloriesPer100g": 376, "ProteinPer100g": 12.8, "FatPer100g": 0.6, "CarbsPer100g": 77.4, "FiberPer100g": 5.0, "SodiumMgPer100g": 10 },
  { "Name": "Barley, pearled raw", "CaloriesPer100g": 354, "ProteinPer100g": 9.9, "FatPer100g": 1.2, "CarbsPer100g": 77.7, "FiberPer100g": 15.6, "SodiumMgPer100g": 9 },
  { "Name": "Cornmeal", "CaloriesPer100g": 361, "ProteinPer100g": 8.1, "FatPer100g": 3.9, "CarbsPer100g": 76.9, "FiberPer100g": 7.3, "SodiumMgPer100g": 5 },
  { "Name": "Rice noodles, dry", "CaloriesPer100g": 364, "ProteinPer100g": 3.0, "FatPer100g": 0.6, "CarbsPer100g": 82.5, "FiberPer100g": 1.6, "SodiumMgPer100g": 6 },
  { "Name": "Cereal, plain flakes", "CaloriesPer100g": 357, "ProteinPer100g": 7.5, "FatPer100g": 1.0, "CarbsPer100g": 84.0, "FiberPer100g": 3.0, "SodiumMgPer100g": 316 },
  { "Name": "Crackers, saltine", "CaloriesPer100g": 421, "ProteinPer100g": 9.0, "FatPer100g": 10.0, "CarbsPer100g": 74.0, "FiberPer100g": 3.0, "SodiumMgPer100g": 1100 },
  { "Name": "Black beans, cooked", "CaloriesPer100g": 132, "ProteinPer100g": 8.9, "FatPer100g": 0.5, "CarbsPer100g": 23.7, "FiberPer100g": 8.7, "SodiumMgPer100g": 1 },
  { "Name": "Kidney beans, cooked", "CaloriesPer100g": 127, "ProteinPer100g": 8.7, "FatPer100g": 0.5, "CarbsPer100g": 22.8, "FiberPer100g": 6.4, "SodiumMgPer100g": 2 },
  { "Name": "Pinto beans, cooked", "CaloriesPer100g": 143, "ProteinPer100g": 9.0, "FatPer100g": 0.7, "CarbsPer100g": 26.2, "FiberPer100g": 9.0, "SodiumMgPer100g": 1 },
  { "Name": "Chickpeas, cooked", "CaloriesPer100g": 164, "ProteinPer100g": 8.9, "FatPer100g": 2.6, "CarbsPer100g": 27.4, "FiberPer100g": 7.6, "SodiumMgPer100g": 7 },
  { "Name": "Lentils, cooked", "CaloriesPer100g": 116, "ProteinPer100g": 9.0, "FatPer100g": 0.4, "CarbsPer100g": 20.1, "FiberPer100g": 7.9, "SodiumMgPer100g": 2 },
  { "Name": "Edamame, cooked", "CaloriesPer100g": 122, "ProteinPer100g": 11.9, "FatPer100g": 5.2, "CarbsPer100g": 8.9, "FiberPer100g": 5.2, "SodiumMgPer100g": 6 },
  { "Name": "Peanut butter", "CaloriesPer100g": 588, "ProteinPer100g": 25.0, "FatPer100g": 50.0, "CarbsPer100g": 20.0, "FiberPer100g": 6.0, "SodiumMgPer100g": 17 },
  { "Name": "Almonds, raw", "CaloriesPer100g": 579, "ProteinPer100g": 21.2, "FatPer100g": 49.9, "CarbsPer100g": 21.6, "FiberPer100g": 12.5, "SodiumMgPer100g": 1 },
  { "Name": "Walnuts, raw", "CaloriesPer100g": 654, "ProteinPer100g": 15.2, "FatPer100g": 65.2, "CarbsPer100g": 13.7, "FiberPer100g": 6.7, "SodiumMgPer100g": 2 },
  { "Name": "Cashews, raw", "CaloriesPer100g": 553, "ProteinPer100g": 18.2, "FatPer100g": 43.9, "CarbsPer100g": 30.2, "FiberPer100g": 3.3, "SodiumMgPer100g": 12 },
  { "Name": "Peanuts, raw", "CaloriesPer100g": 567, "ProteinPer100g": 25.8, "FatPer100g": 49.2, "CarbsPer100g": 16.1, "FiberPer100g": 8.5, "SodiumMgPer100g": 18 },
  { "Name": "Chia seeds", "CaloriesPer100g": 486, "ProteinPer100g": 16.5, "FatPer100g": 30.7, "CarbsPer100g": 42.1, "FiberPer100g": 34.4, "SodiumMgPer100g": 16 },
  { "Name": "Flax seeds", "CaloriesPer100g": 534, "ProteinPer100g": 18.3, "FatPer100g": 42.2, "CarbsPer100g": 28.9, "FiberPer100g": 27.3, "SodiumMgPer100g": 30 },
  { "Name": "Sunflower seeds", "CaloriesPer100g": 584, "ProteinPer100g": 20.8, "FatPer100g": 51.5, "CarbsPer100g": 20.0, "FiberPer100g": 8.6, "SodiumMgPer100g": 9 },
  { "Name": "Pumpkin seeds", "CaloriesPer100g": 559, "ProteinPer100g": 30.2, "FatPer100g": 49.0, "CarbsPer100g": 10.7, "FiberPer100g": 6.0, "SodiumMgPer100g": 7 },
  { "Name": "Olive oil", "CaloriesPer100g": 884, "ProteinPer100g": 0, "FatPer100g": 100.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 2 },
  { "Name": "Vegetable oil", "CaloriesPer100g": 884, "ProteinPer100g": 0, "FatPer100g": 100.0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 0 },
  { "Name": "Coconut milk, canned", "CaloriesPer100g": 230, "ProteinPer100g": 2.3, "FatPer100g": 23.8, "CarbsPer100g": 5.5, "FiberPer100g": 2.2, "SodiumMgPer100g": 15 },
  { "Name": "Soy sauce", "CaloriesPer100g": 53, "ProteinPer100g": 8.0, "FatPer100g": 0.1, "CarbsPer100g": 4.9, "FiberPer100g": 0.8, "SodiumMgPer100g": 5493 },
  { "Name": "Ketchup", "CaloriesPer100g": 101, "ProteinPer100g": 1.3, "FatPer100g": 0.3, "CarbsPer100g": 25.8, "FiberPer100g": 0.4, "SodiumMgPer100g": 907 },
  { "Name": "Mayonnaise", "CaloriesPer100g": 680, "ProteinPer100g": 1.0, "FatPer100g": 75.0, "CarbsPer100g": 0.6, "FiberPer100g": 0, "SodiumMgPer100g": 635 },
  { "Name": "Mustard, yellow", "CaloriesPer100g": 66, "ProteinPer100g": 4.4, "FatPer100g": 3.3, "CarbsPer100g": 5.8, "FiberPer100g": 3.3, "SodiumMgPer100g": 1120 },
  { "Name": "Honey", "CaloriesPer100g": 304, "ProteinPer100g": 0.3, "FatPer100g": 0, "CarbsPer100g": 82.4, "FiberPer100g": 0.2, "SodiumMgPer100g": 4 },
  { "Name": "Maple syrup", "CaloriesPer100g": 260, "ProteinPer100g": 0, "FatPer100g": 0.1, "CarbsPer100g": 67.0, "FiberPer100g": 0, "SodiumMgPer100g": 12 },
  { "Name": "Sugar, white granulated", "CaloriesPer100g": 387, "ProteinPer100g": 0, "FatPer100g": 0, "CarbsPer100g": 100.0, "FiberPer100g": 0, "SodiumMgPer100g": 1 },
  { "Name": "Salt", "CaloriesPer100g": 0, "ProteinPer100g": 0, "FatPer100g": 0, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 38758 },
  { "Name": "Vinegar, white distilled", "CaloriesPer100g": 18, "ProteinPer100g": 0, "FatPer100g": 0, "CarbsPer100g": 0.04, "FiberPer100g": 0, "SodiumMgPer100g": 2 },
  { "Name": "Tomato sauce, canned", "CaloriesPer100g": 24, "ProteinPer100g": 1.2, "FatPer100g": 0.2, "CarbsPer100g": 5.3, "FiberPer100g": 1.3, "SodiumMgPer100g": 400 },
  { "Name": "Chicken broth", "CaloriesPer100g": 4, "ProteinPer100g": 0.6, "FatPer100g": 0.1, "CarbsPer100g": 0.3, "FiberPer100g": 0, "SodiumMgPer100g": 400 },
  { "Name": "Chocolate chips, semisweet", "CaloriesPer100g": 479, "ProteinPer100g": 4.2, "FatPer100g": 29.8, "CarbsPer100g": 63.5, "FiberPer100g": 5.9, "SodiumMgPer100g": 6 }
]
```

- [ ] **Step 2: Create the seed-entry DTO**

`src/AaronOS.Modules.Nutrition/Seeding/IngredientSeedEntry.cs`:

```csharp
namespace AaronOS.Modules.Nutrition.Seeding;

public record IngredientSeedEntry(
    string Name,
    decimal? CaloriesPer100g,
    decimal? ProteinPer100g,
    decimal? FatPer100g,
    decimal? CarbsPer100g,
    decimal? FiberPer100g,
    decimal? SodiumMgPer100g);
```

- [ ] **Step 3: Write the failing test for the pure parse function**

`src/AaronOS.Modules.Nutrition.Tests/IngredientSeederTests.cs`:

```csharp
using AaronOS.Modules.Nutrition.Seeding;

namespace AaronOS.Modules.Nutrition.Tests;

public class IngredientSeederTests
{
    private const string SampleJson = """
        [
          { "Name": "Chicken breast, raw", "CaloriesPer100g": 120, "ProteinPer100g": 22.5, "FatPer100g": 2.6, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 45 },
          { "Name": "Apple, raw", "CaloriesPer100g": 52, "ProteinPer100g": 0.3, "FatPer100g": 0.2, "CarbsPer100g": 13.8, "FiberPer100g": 2.4, "SodiumMgPer100g": 1 }
        ]
        """;

    [Fact]
    public void ParseSeedFile_MapsEveryEntryToAnIngredient()
    {
        var ingredients = IngredientSeeder.ParseSeedFile(SampleJson);

        Assert.Equal(2, ingredients.Count);
        Assert.Equal("Chicken breast, raw", ingredients[0].Name);
        Assert.Equal(120m, ingredients[0].CaloriesPer100g);
        Assert.Equal(22.5m, ingredients[0].ProteinPer100g);
    }

    [Fact]
    public void ParseSeedFile_LeavesRatingTagsAndCostUnset()
    {
        var ingredients = IngredientSeeder.ParseSeedFile(SampleJson);

        Assert.All(ingredients, i =>
        {
            Assert.Null(i.Rating);
            Assert.Empty(i.Tags);
            Assert.Null(i.CostPer100g);
        });
    }

    [Fact]
    public void ParseSeedFile_ReturnsEmptyList_ForEmptyJsonArray()
    {
        var ingredients = IngredientSeeder.ParseSeedFile("[]");

        Assert.Empty(ingredients);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter IngredientSeederTests`
Expected: FAIL to compile.

- [ ] **Step 5: Implement `IngredientSeeder`**

`src/AaronOS.Modules.Nutrition/Seeding/IngredientSeeder.cs`:

```csharp
using System.Text.Json;
using AaronOS.Core.Data;
using AaronOS.Modules.Nutrition.Data;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Nutrition.Seeding;

public static class IngredientSeeder
{
    /// <summary>Pure parse step, kept separate from the DB write so it's testable without EF.</summary>
    public static List<Ingredient> ParseSeedFile(string json)
    {
        var entries = JsonSerializer.Deserialize<List<IngredientSeedEntry>>(json) ?? [];
        return entries.Select(e => new Ingredient
        {
            Name = e.Name,
            CaloriesPer100g = e.CaloriesPer100g,
            ProteinPer100g = e.ProteinPer100g,
            FatPer100g = e.FatPer100g,
            CarbsPer100g = e.CarbsPer100g,
            FiberPer100g = e.FiberPer100g,
            SodiumMgPer100g = e.SodiumMgPer100g
        }).ToList();
    }

    /// <summary>No-op if any Ingredient rows already exist — safe to call on every dashboard load.</summary>
    public static async Task SeedIfEmptyAsync(AaronOsDbContext db)
    {
        if (await db.Set<Ingredient>().AnyAsync())
        {
            return;
        }

        var assembly = typeof(IngredientSeeder).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "AaronOS.Modules.Nutrition.Resources.IngredientSeed.json");
        if (stream is null)
        {
            return;
        }

        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var ingredients = ParseSeedFile(json);

        db.Set<Ingredient>().AddRange(ingredients);
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test src/AaronOS.Modules.Nutrition.Tests --filter IngredientSeederTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Register the JSON file as an embedded resource and build**

In `src/AaronOS.Modules.Nutrition/AaronOS.Modules.Nutrition.csproj`, add to the `<ItemGroup>` created in Task 4, Step 7 (or create it if Task 4 hasn't run yet):

```xml
  <ItemGroup>
    <EmbeddedResource Include="Resources\ShelfLifeReference.json" />
    <EmbeddedResource Include="Resources\IngredientSeed.json" />
  </ItemGroup>
```

Run: `dotnet build AaronOS.slnx`
Expected: builds with no errors — confirms the embedded resource resolves against the real file `IngredientSeeder.SeedIfEmptyAsync` will read at runtime.

This step's actual seeding behavior gets exercised end-to-end in Task 11 (the Dashboard is what calls `SeedIfEmptyAsync`) — nothing to manually verify yet beyond the build.

- [ ] **Step 8: Commit**

```bash
git add src/AaronOS.Modules.Nutrition/Resources/IngredientSeed.json src/AaronOS.Modules.Nutrition/Seeding src/AaronOS.Modules.Nutrition.Tests/IngredientSeederTests.cs src/AaronOS.Modules.Nutrition/AaronOS.Modules.Nutrition.csproj
git commit -m "$(cat <<'EOF'
Add preloaded ingredient catalog and seeder

~135 common grocery ingredients with per-100g macros, embedded as a
JSON resource and inserted once via a seed-if-empty check — no ratings,
tags, or cost, since that's the part the user covers themselves.
EOF
)"
```

---

### Task 7: USDA FoodData Central API client

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/Usda/UsdaDtos.cs`
- Create: `src/AaronOS.Modules.Nutrition/Usda/UsdaApiClient.cs`

**Interfaces:**
- Consumes: `UsdaCredentialStore` from Task 5.
- Produces: `UsdaSearchResult(int FdcId, string Description, string? DataType)`; `UsdaIngredientFacts(string Name, int FdcId, decimal? CaloriesPer100g, decimal? ProteinPer100g, decimal? FatPer100g, decimal? CarbsPer100g, decimal? FiberPer100g, decimal? SodiumMgPer100g)`; `UsdaApiClient(UsdaCredentialStore credentialStore)` with `Task<List<UsdaSearchResult>> SearchAsync(string query)` and `Task<UsdaIngredientFacts> GetFactsAsync(int fdcId)`. Task 8's `IngredientsViewModel` calls both — don't rename them.

No automated test for this task — it's a thin network client with no branching logic worth a unit test, matching `AaronOS.Modules.Finance.Plaid.PlaidApiClient`'s precedent (also untested). Verification is a build pass here; the actual HTTP calls get manually exercised once Task 8's UI exists.

- [ ] **Step 1: Create the DTOs**

`src/AaronOS.Modules.Nutrition/Usda/UsdaDtos.cs`:

```csharp
using System.Text.Json.Serialization;

namespace AaronOS.Modules.Nutrition.Usda;

public record UsdaSearchResult(
    [property: JsonPropertyName("fdcId")] int FdcId,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("dataType")] string? DataType);

public record UsdaSearchResponse(
    [property: JsonPropertyName("foods")] List<UsdaSearchResult> Foods);

public record UsdaNutrientInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("unitName")] string UnitName);

public record UsdaFoodNutrient(
    [property: JsonPropertyName("nutrient")] UsdaNutrientInfo Nutrient,
    [property: JsonPropertyName("amount")] decimal? Amount);

public record UsdaFoodDetail(
    [property: JsonPropertyName("fdcId")] int FdcId,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("foodNutrients")] List<UsdaFoodNutrient> FoodNutrients);
```

- [ ] **Step 2: Implement `UsdaApiClient`**

`src/AaronOS.Modules.Nutrition/Usda/UsdaApiClient.cs`:

```csharp
using System.Net.Http;
using System.Net.Http.Json;

namespace AaronOS.Modules.Nutrition.Usda;

public record UsdaIngredientFacts(
    string Name, int FdcId, decimal? CaloriesPer100g, decimal? ProteinPer100g,
    decimal? FatPer100g, decimal? CarbsPer100g, decimal? FiberPer100g, decimal? SodiumMgPer100g);

/// <summary>
/// Thin client for the two USDA FoodData Central endpoints this app needs — search and food
/// detail. Not a general-purpose FDC SDK. Owns a single static HttpClient, matching
/// AaronOS.Modules.Finance.Plaid.PlaidApiClient's pattern (this app doesn't register
/// IHttpClientFactory anywhere).
/// </summary>
public class UsdaApiClient(UsdaCredentialStore credentialStore)
{
    private static readonly HttpClient Http = new();
    private const string BaseUrl = "https://api.nal.usda.gov/fdc/v1";

    private string RequireApiKey()
    {
        var apiKey = credentialStore.Load();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("No USDA FoodData Central API key configured.");
        }

        return apiKey;
    }

    public async Task<List<UsdaSearchResult>> SearchAsync(string query)
    {
        var apiKey = RequireApiKey();
        var url = $"{BaseUrl}/foods/search?query={Uri.EscapeDataString(query)}&api_key={apiKey}&pageSize=25";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<UsdaSearchResponse>();
        return body?.Foods ?? [];
    }

    public async Task<UsdaIngredientFacts> GetFactsAsync(int fdcId)
    {
        var apiKey = RequireApiKey();
        var url = $"{BaseUrl}/food/{fdcId}?api_key={apiKey}";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var detail = (await response.Content.ReadFromJsonAsync<UsdaFoodDetail>())!;

        decimal? Find(string nameContains) => detail.FoodNutrients
            .FirstOrDefault(n => n.Nutrient.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            ?.Amount;

        return new UsdaIngredientFacts(
            detail.Description,
            detail.FdcId,
            Find("Energy"),
            Find("Protein"),
            Find("Total lipid"),
            Find("Carbohydrate"),
            Find("Fiber"),
            Find("Sodium"));
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build AaronOS.slnx`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add src/AaronOS.Modules.Nutrition/Usda/UsdaDtos.cs src/AaronOS.Modules.Nutrition/Usda/UsdaApiClient.cs
git commit -m "$(cat <<'EOF'
Add USDA FoodData Central API client

Search and food-detail endpoints only, mirroring PlaidApiClient's thin
single-HttpClient pattern. No automated test — no branching logic
worth one, matching PlaidApiClient's own precedent.
EOF
)"
```

---

### Task 8: Ingredients page

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/ViewModels/IngredientsViewModel.cs`
- Create: `src/AaronOS.Modules.Nutrition/Views/IngredientsPage.xaml`
- Create: `src/AaronOS.Modules.Nutrition/Views/IngredientsPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Nutrition/NutritionModule.cs`

**Interfaces:**
- Consumes: `Ingredient`, `Tag` (Task 2); `UsdaApiClient`, `UsdaSearchResult`, `UsdaIngredientFacts` (Task 7).
- Produces: `IngredientsViewModel` with `ObservableCollection<Ingredient> VisibleIngredients`, `ObservableCollection<UsdaSearchResult> SearchResults`, `Ingredient? SelectedIngredient`, `LoadCommand`, `SearchUsdaCommand`, `AddFromUsdaCommand(UsdaSearchResult)`, `AddManuallyCommand`, `SaveSelectedCommand`. Task 10's `InventoryViewModel` doesn't depend on this ViewModel directly, but both read the same `Ingredient` table — no interface coupling beyond the shared entity.

No automated test — this codebase doesn't unit-test ViewModels or XAML (only pure calculators/services get tests, per Finance's precedent). Verification is manual: run the app, browse the preloaded catalog, add/edit an ingredient.

- [ ] **Step 1: Implement `IngredientsViewModel`**

`src/AaronOS.Modules.Nutrition/ViewModels/IngredientsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.Usda;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Nutrition.ViewModels;

public partial class IngredientsViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    UsdaApiClient usdaApiClient) : ViewModelBase
{
    private List<Ingredient> _allIngredients = [];

    public ObservableCollection<Ingredient> VisibleIngredients { get; } = [];
    public IReadOnlyList<Rating?> RatingOptions { get; } = [null, Rating.Like, Rating.Neutral, Rating.Dislike];
    public ObservableCollection<UsdaSearchResult> SearchResults { get; } = [];

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private Ingredient? _selectedIngredient;
    [ObservableProperty] private string _tagsText = "";
    [ObservableProperty] private Rating? _editRating;
    [ObservableProperty] private string _editPreferredForm = "";
    [ObservableProperty] private double _editCalories = double.NaN;
    [ObservableProperty] private double _editProtein = double.NaN;
    [ObservableProperty] private double _editFat = double.NaN;
    [ObservableProperty] private double _editCarbs = double.NaN;
    [ObservableProperty] private double _editFiber = double.NaN;
    [ObservableProperty] private double _editSodium = double.NaN;
    [ObservableProperty] private double _editCost = double.NaN;
    [ObservableProperty] private string _newIngredientName = "";
    [ObservableProperty] private string _usdaSearchQuery = "";
    [ObservableProperty] private string _statusMessage = "";

    private static double ToDouble(decimal? value) => value is null ? double.NaN : (double)value.Value;
    private static decimal? ToDecimal(double value) => double.IsNaN(value) ? null : (decimal)value;

    partial void OnFilterTextChanged(string value) => RefreshVisible();

    partial void OnSelectedIngredientChanged(Ingredient? value)
    {
        TagsText = value is null ? "" : string.Join(", ", value.Tags.Select(t => t.Name));
        EditRating = value?.Rating;
        EditPreferredForm = value?.PreferredForm ?? "";
        EditCalories = ToDouble(value?.CaloriesPer100g);
        EditProtein = ToDouble(value?.ProteinPer100g);
        EditFat = ToDouble(value?.FatPer100g);
        EditCarbs = ToDouble(value?.CarbsPer100g);
        EditFiber = ToDouble(value?.FiberPer100g);
        EditSodium = ToDouble(value?.SodiumMgPer100g);
        EditCost = ToDouble(value?.CostPer100g);
    }

    private void RefreshVisible()
    {
        var matches = string.IsNullOrWhiteSpace(FilterText)
            ? _allIngredients
            : _allIngredients.Where(i => i.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

        VisibleIngredients.Clear();
        foreach (var ingredient in matches.OrderBy(i => i.Name))
        {
            VisibleIngredients.Add(ingredient);
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            _allIngredients = await db.Set<Ingredient>().Include(i => i.Tags).ToListAsync();
            RefreshVisible();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SearchUsdaAsync()
    {
        if (string.IsNullOrWhiteSpace(UsdaSearchQuery))
        {
            return;
        }

        IsBusy = true;
        try
        {
            SearchResults.Clear();
            var results = await usdaApiClient.SearchAsync(UsdaSearchQuery);
            foreach (var result in results)
            {
                SearchResults.Add(result);
            }
            StatusMessage = results.Count == 0 ? "No USDA matches found." : "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"USDA search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddFromUsdaAsync(UsdaSearchResult result)
    {
        IsBusy = true;
        try
        {
            var facts = await usdaApiClient.GetFactsAsync(result.FdcId);
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var ingredient = new Ingredient
            {
                Name = facts.Name,
                FdcId = facts.FdcId,
                CaloriesPer100g = facts.CaloriesPer100g,
                ProteinPer100g = facts.ProteinPer100g,
                FatPer100g = facts.FatPer100g,
                CarbsPer100g = facts.CarbsPer100g,
                FiberPer100g = facts.FiberPer100g,
                SodiumMgPer100g = facts.SodiumMgPer100g
            };
            db.Add(ingredient);
            await db.SaveChangesAsync();

            _allIngredients.Add(ingredient);
            RefreshVisible();
            SelectedIngredient = ingredient;
            SearchResults.Clear();
            StatusMessage = $"Added {ingredient.Name} from USDA.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't add ingredient: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddManuallyAsync()
    {
        if (string.IsNullOrWhiteSpace(NewIngredientName))
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var ingredient = new Ingredient { Name = NewIngredientName };
        db.Add(ingredient);
        await db.SaveChangesAsync();

        _allIngredients.Add(ingredient);
        RefreshVisible();
        SelectedIngredient = ingredient;
        NewIngredientName = "";
    }

    [RelayCommand]
    private async Task SaveSelectedAsync()
    {
        if (SelectedIngredient is null)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();

        var tagNames = TagsText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existingTags = await db.Set<Tag>().Where(t => tagNames.Contains(t.Name)).ToListAsync();
        var newTags = tagNames
            .Where(name => existingTags.All(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Select(name => new Tag { Name = name })
            .ToList();

        var tracked = await db.Set<Ingredient>()
            .Include(i => i.Tags)
            .FirstAsync(i => i.Id == SelectedIngredient.Id);

        tracked.Rating = EditRating;
        tracked.PreferredForm = string.IsNullOrWhiteSpace(EditPreferredForm) ? null : EditPreferredForm;
        tracked.CaloriesPer100g = ToDecimal(EditCalories);
        tracked.ProteinPer100g = ToDecimal(EditProtein);
        tracked.FatPer100g = ToDecimal(EditFat);
        tracked.CarbsPer100g = ToDecimal(EditCarbs);
        tracked.FiberPer100g = ToDecimal(EditFiber);
        tracked.SodiumMgPer100g = ToDecimal(EditSodium);
        tracked.CostPer100g = ToDecimal(EditCost);
        tracked.Tags.Clear();
        foreach (var tag in existingTags.Concat(newTags))
        {
            tracked.Tags.Add(tag);
        }

        await db.SaveChangesAsync();

        var index = _allIngredients.FindIndex(i => i.Id == tracked.Id);
        if (index >= 0)
        {
            _allIngredients[index] = tracked;
        }
        RefreshVisible();
        SelectedIngredient = tracked;
        StatusMessage = $"Saved {tracked.Name}.";
    }
}
```

- [ ] **Step 2: Create the Ingredients page XAML**

`src/AaronOS.Modules.Nutrition/Views/IngredientsPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Nutrition.Views.IngredientsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    xmlns:usda="clr-namespace:AaronOS.Modules.Nutrition.Usda"
    mc:Ignorable="d">

    <Grid Margin="24">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="280" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <StackPanel Grid.Column="0" Margin="0,0,24,0">
            <TextBlock Text="Ingredients" FontSize="28" FontWeight="SemiBold" Margin="0,0,0,12" />

            <ui:TextBox Text="{Binding FilterText, Mode=TwoWay}" PlaceholderText="Filter by name" Margin="0,0,0,12" />
            <ListBox ItemsSource="{Binding VisibleIngredients}" DisplayMemberPath="Name"
                     SelectedItem="{Binding SelectedIngredient, Mode=TwoWay}" Height="360" Margin="0,0,0,16" />

            <TextBlock Text="Add manually" FontSize="16" FontWeight="SemiBold" Margin="0,0,0,8" />
            <ui:TextBox Text="{Binding NewIngredientName, Mode=TwoWay}" PlaceholderText="New ingredient name" Margin="0,0,0,8" />
            <ui:Button Content="Add" Command="{Binding AddManuallyCommand}" Margin="0,0,0,16" />

            <TextBlock Text="Search USDA FoodData Central" FontSize="16" FontWeight="SemiBold" Margin="0,0,0,8" />
            <ui:TextBox Text="{Binding UsdaSearchQuery, Mode=TwoWay}" PlaceholderText="e.g. chicken breast raw" Margin="0,0,0,8" />
            <ui:Button Content="Search" Command="{Binding SearchUsdaCommand}" Margin="0,0,0,8" />
            <ListBox ItemsSource="{Binding SearchResults}" Height="160">
                <ListBox.ItemTemplate>
                    <DataTemplate DataType="{x:Type usda:UsdaSearchResult}">
                        <Button Content="{Binding Description}" Click="UsdaResult_Click"
                                HorizontalContentAlignment="Left" Background="Transparent" BorderThickness="0" />
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </StackPanel>

        <ScrollViewer Grid.Column="1">
            <StackPanel MaxWidth="480" HorizontalAlignment="Left">
                <TextBlock Text="{Binding SelectedIngredient.Name}" FontSize="22" FontWeight="SemiBold" Margin="0,0,0,16" />

                <TextBlock Text="Rating" Margin="0,0,0,4" />
                <ComboBox ItemsSource="{Binding RatingOptions}" SelectedItem="{Binding EditRating, Mode=TwoWay}" Margin="0,0,0,12" />

                <TextBlock Text="Preferred form" Margin="0,0,0,4" />
                <ui:TextBox Text="{Binding EditPreferredForm, Mode=TwoWay}" PlaceholderText="e.g. fresh, canned, diced" Margin="0,0,0,12" />

                <TextBlock Text="Tags (comma separated)" Margin="0,0,0,4" />
                <ui:TextBox Text="{Binding TagsText, Mode=TwoWay}" PlaceholderText="e.g. fungi, dairy" Margin="0,0,0,16" />

                <TextBlock Text="Nutrition (per 100g)" FontSize="16" FontWeight="SemiBold" Margin="0,0,0,8" />
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition />
                        <RowDefinition />
                        <RowDefinition />
                    </Grid.RowDefinitions>

                    <StackPanel Grid.Row="0" Grid.Column="0" Margin="0,0,12,12">
                        <TextBlock Text="Calories" Margin="0,0,0,4" />
                        <ui:NumberBox Value="{Binding EditCalories, Mode=TwoWay}" />
                    </StackPanel>
                    <StackPanel Grid.Row="0" Grid.Column="1" Margin="0,0,0,12">
                        <TextBlock Text="Protein (g)" Margin="0,0,0,4" />
                        <ui:NumberBox Value="{Binding EditProtein, Mode=TwoWay}" />
                    </StackPanel>

                    <StackPanel Grid.Row="1" Grid.Column="0" Margin="0,0,12,12">
                        <TextBlock Text="Fat (g)" Margin="0,0,0,4" />
                        <ui:NumberBox Value="{Binding EditFat, Mode=TwoWay}" />
                    </StackPanel>
                    <StackPanel Grid.Row="1" Grid.Column="1" Margin="0,0,0,12">
                        <TextBlock Text="Carbs (g)" Margin="0,0,0,4" />
                        <ui:NumberBox Value="{Binding EditCarbs, Mode=TwoWay}" />
                    </StackPanel>

                    <StackPanel Grid.Row="2" Grid.Column="0" Margin="0,0,12,12">
                        <TextBlock Text="Fiber (g)" Margin="0,0,0,4" />
                        <ui:NumberBox Value="{Binding EditFiber, Mode=TwoWay}" />
                    </StackPanel>
                    <StackPanel Grid.Row="2" Grid.Column="1" Margin="0,0,0,12">
                        <TextBlock Text="Sodium (mg)" Margin="0,0,0,4" />
                        <ui:NumberBox Value="{Binding EditSodium, Mode=TwoWay}" />
                    </StackPanel>
                </Grid>

                <TextBlock Text="Cost per 100g ($)" Margin="0,0,0,4" />
                <ui:NumberBox Value="{Binding EditCost, Mode=TwoWay}" Margin="0,0,0,16" />

                <ui:Button Content="Save Ingredient" Command="{Binding SaveSelectedCommand}" Appearance="Primary" />
                <TextBlock Text="{Binding StatusMessage}" Opacity="0.7" Margin="0,8,0,0" />
            </StackPanel>
        </ScrollViewer>
    </Grid>
</Page>
```

- [ ] **Step 3: Create the Ingredients page code-behind**

`src/AaronOS.Modules.Nutrition/Views/IngredientsPage.xaml.cs`:

```csharp
using AaronOS.Core;
using AaronOS.Modules.Nutrition.Usda;
using AaronOS.Modules.Nutrition.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

public sealed partial class IngredientsPage : Page
{
    public IngredientsViewModel ViewModel { get; }

    public IngredientsPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<IngredientsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void UsdaResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UsdaSearchResult result })
        {
            _ = ViewModel.AddFromUsdaCommand.ExecuteAsync(result);
        }
    }
}
```

- [ ] **Step 4: Register the ViewModel**

In `src/AaronOS.Modules.Nutrition/NutritionModule.cs`, replace the `RegisterServices` body's comment with real registrations (later tasks add more lines here):

```csharp
using AaronOS.Core;
using AaronOS.Modules.Nutrition.Usda;
using AaronOS.Modules.Nutrition.ViewModels;
using AaronOS.Modules.Nutrition.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Nutrition;

public class NutritionModule : IAppModule
{
    public string Id => "nutrition";
    public string DisplayName => "Nutrition";
    public string IconGlyph => "Food24"; // confirm exact Wpf.Ui.Controls.SymbolRegular member when the app first builds
    public Type HomePageType => typeof(NutritionShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<UsdaCredentialStore>();
        services.AddSingleton<UsdaApiClient>();
        services.AddTransient<IngredientsViewModel>();
    }
}
```

- [ ] **Step 5: Build and manually verify**

Run: `dotnet build AaronOS.slnx`
Expected: builds with no errors.

Manually run the app. The Nutrition nav item still shows the Task 1 placeholder (`NutritionShellPage` hasn't been rewired to navigate anywhere yet — that's Task 11) — for now, temporarily edit `NutritionShellPage.xaml.cs`'s constructor to `InitializeComponent(); Loaded += (_, _) => Content = new IngredientsPage();` just to exercise this page manually, confirm the preloaded ~135 ingredients list, filtering, selecting one, editing rating/tags/nutrition/cost, and saving all work, then **revert that temporary edit** before committing (Task 11 does this rewiring for real).

- [ ] **Step 6: Commit**

```bash
git add src/AaronOS.Modules.Nutrition/ViewModels/IngredientsViewModel.cs src/AaronOS.Modules.Nutrition/Views/IngredientsPage.xaml src/AaronOS.Modules.Nutrition/Views/IngredientsPage.xaml.cs src/AaronOS.Modules.Nutrition/NutritionModule.cs
git commit -m "$(cat <<'EOF'
Add Ingredients page

Browse/filter the catalog, add ingredients via USDA search or manual
entry, edit rating/preferred form/tags/nutrition/cost per ingredient.
EOF
)"
```

---

### Task 9: Recipe edit page

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/ViewModels/RecipeEditViewModel.cs`
- Create: `src/AaronOS.Modules.Nutrition/Views/RecipeEditPage.xaml`
- Create: `src/AaronOS.Modules.Nutrition/Views/RecipeEditPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Nutrition/NutritionModule.cs`

**Interfaces:**
- Consumes: `Recipe`, `RecipeIngredient`, `Ingredient` (Task 2); `RecipeNutritionCalculator`, `RecipeNutritionTotals`, `RecipeCompatibilityChecker`, `CompatibilityConcern` (Task 3).
- Produces: `RecipeEditViewModel` with `void SetRecipeId(int? recipeId)`, `ObservableCollection<RecipeIngredient> Lines`, `ObservableCollection<Ingredient> AvailableIngredients`, `List<CompatibilityConcern> Concerns`, `RecipeNutritionTotals? PerServingTotals`, `LoadCommand`, `AddLineCommand`, `RemoveLineCommand(RecipeIngredient)`, `SaveCommand`. `RecipeEditPage(int? recipeId = null)` — Task 11's Dashboard constructs this directly (`new RecipeEditPage()` for a new recipe, `new RecipeEditPage(recipe.Id)` to edit one), so keep that constructor signature exactly.

No automated test — same rationale as Task 8 (no ViewModel/XAML test culture in this codebase). Verification is manual: build a recipe, confirm live nutrition/cost rollup and compatibility notes update.

- [ ] **Step 1: Implement `RecipeEditViewModel`**

`src/AaronOS.Modules.Nutrition/ViewModels/RecipeEditViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Nutrition.Calculations;
using AaronOS.Modules.Nutrition.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Nutrition.ViewModels;

public partial class RecipeEditViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    private int? _recipeId;

    public ObservableCollection<RecipeIngredient> Lines { get; } = [];
    public ObservableCollection<Ingredient> AvailableIngredients { get; } = [];
    public ObservableCollection<CompatibilityConcern> Concerns { get; } = [];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _instructions = "";
    [ObservableProperty] private double _servings = 1;
    [ObservableProperty] private Ingredient? _newLineIngredient;
    [ObservableProperty] private double _newLineQuantityGrams = double.NaN;
    [ObservableProperty] private string _newLineDisplayAmount = "";
    [ObservableProperty] private string _newLineFormUsed = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private RecipeNutritionTotals? _perServingTotals;

    public void SetRecipeId(int? recipeId) => _recipeId = recipeId;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var ingredients = await db.Set<Ingredient>().OrderBy(i => i.Name).ToListAsync();
            AvailableIngredients.Clear();
            foreach (var ingredient in ingredients)
            {
                AvailableIngredients.Add(ingredient);
            }

            Lines.Clear();
            if (_recipeId is { } id)
            {
                var recipe = await db.Set<Recipe>()
                    .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient).ThenInclude(i => i!.Tags)
                    .FirstAsync(r => r.Id == id);

                Name = recipe.Name;
                Instructions = recipe.Instructions ?? "";
                Servings = recipe.Servings;
                foreach (var line in recipe.Ingredients)
                {
                    Lines.Add(line);
                }
            }

            Recalculate();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Recalculate()
    {
        PerServingTotals = Lines.Count == 0
            ? null
            : RecipeNutritionCalculator.CalculatePerServing(Lines, Math.Max((int)Servings, 1));

        Concerns.Clear();
        foreach (var concern in RecipeCompatibilityChecker.CheckRecipe(Lines))
        {
            Concerns.Add(concern);
        }
    }

    [RelayCommand]
    private void AddLine()
    {
        if (NewLineIngredient is null || double.IsNaN(NewLineQuantityGrams) || NewLineQuantityGrams <= 0)
        {
            return;
        }

        Lines.Add(new RecipeIngredient
        {
            Ingredient = NewLineIngredient,
            IngredientId = NewLineIngredient.Id,
            QuantityGrams = (decimal)NewLineQuantityGrams,
            DisplayAmount = string.IsNullOrWhiteSpace(NewLineDisplayAmount) ? null : NewLineDisplayAmount,
            FormUsed = string.IsNullOrWhiteSpace(NewLineFormUsed) ? null : NewLineFormUsed
        });

        NewLineIngredient = null;
        NewLineQuantityGrams = double.NaN;
        NewLineDisplayAmount = "";
        NewLineFormUsed = "";
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(RecipeIngredient line)
    {
        Lines.Remove(line);
        Recalculate();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || Lines.Count == 0)
        {
            StatusMessage = "Give the recipe a name and at least one ingredient.";
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            Recipe recipe;
            if (_recipeId is { } id)
            {
                recipe = await db.Set<Recipe>().Include(r => r.Ingredients).FirstAsync(r => r.Id == id);
                db.Set<RecipeIngredient>().RemoveRange(recipe.Ingredients);
                recipe.Ingredients.Clear();
            }
            else
            {
                recipe = new Recipe { Name = Name };
                db.Add(recipe);
            }

            recipe.Name = Name;
            recipe.Instructions = string.IsNullOrWhiteSpace(Instructions) ? null : Instructions;
            recipe.Servings = Math.Max((int)Servings, 1);

            foreach (var line in Lines)
            {
                recipe.Ingredients.Add(new RecipeIngredient
                {
                    IngredientId = line.IngredientId,
                    QuantityGrams = line.QuantityGrams,
                    DisplayAmount = line.DisplayAmount,
                    FormUsed = line.FormUsed
                });
            }

            await db.SaveChangesAsync();
            _recipeId = recipe.Id;
            StatusMessage = "Recipe saved.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
```

- [ ] **Step 2: Create the Recipe edit page XAML**

`src/AaronOS.Modules.Nutrition/Views/RecipeEditPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Nutrition.Views.RecipeEditPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    xmlns:data="clr-namespace:AaronOS.Modules.Nutrition.Data"
    xmlns:calc="clr-namespace:AaronOS.Modules.Nutrition.Calculations"
    mc:Ignorable="d">

    <ScrollViewer Padding="24">
        <StackPanel MaxWidth="760" HorizontalAlignment="Left">
            <TextBlock Text="Recipe" FontSize="28" FontWeight="SemiBold" Margin="0,0,0,16" />

            <TextBlock Text="Name" Margin="0,0,0,4" />
            <ui:TextBox Text="{Binding Name, Mode=TwoWay}" Margin="0,0,0,12" />

            <TextBlock Text="Servings" Margin="0,0,0,4" />
            <ui:NumberBox Value="{Binding Servings, Mode=TwoWay}" Margin="0,0,0,12" />

            <TextBlock Text="Instructions" Margin="0,0,0,4" />
            <TextBox Text="{Binding Instructions, Mode=TwoWay}" AcceptsReturn="True" Height="80" Margin="0,0,0,16" />

            <TextBlock Text="Ingredients" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,8" />
            <ItemsControl ItemsSource="{Binding Lines}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type data:RecipeIngredient}">
                        <Grid Margin="0,4">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="100" />
                                <ColumnDefinition Width="140" />
                                <ColumnDefinition Width="100" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="{Binding Ingredient.Name}" VerticalAlignment="Center" />
                            <TextBlock Grid.Column="1" Text="{Binding QuantityGrams, StringFormat='{}{0}g'}" VerticalAlignment="Center" />
                            <TextBlock Grid.Column="2" Text="{Binding DisplayAmount}" Opacity="0.7" VerticalAlignment="Center" />
                            <TextBlock Grid.Column="3" Text="{Binding FormUsed}" Opacity="0.7" VerticalAlignment="Center" />
                            <Button Grid.Column="4" Content="Remove" Click="RemoveLineButton_Click" />
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <ui:Card Margin="0,12,0,16">
                <StackPanel Orientation="Horizontal">
                    <ComboBox Width="220" ItemsSource="{Binding AvailableIngredients}" DisplayMemberPath="Name"
                              SelectedItem="{Binding NewLineIngredient, Mode=TwoWay}" Margin="0,0,8,0" />
                    <ui:NumberBox Width="100" PlaceholderText="Grams" Value="{Binding NewLineQuantityGrams, Mode=TwoWay}" Margin="0,0,8,0" />
                    <ui:TextBox Width="140" PlaceholderText="e.g. 2 cups diced" Text="{Binding NewLineDisplayAmount, Mode=TwoWay}" Margin="0,0,8,0" />
                    <ui:TextBox Width="100" PlaceholderText="Form used" Text="{Binding NewLineFormUsed, Mode=TwoWay}" Margin="0,0,8,0" />
                    <ui:Button Content="Add Ingredient" Command="{Binding AddLineCommand}" />
                </StackPanel>
            </ui:Card>

            <TextBlock Text="Per serving" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,8" />
            <TextBlock Text="{Binding PerServingTotals.Calories, StringFormat='Calories: {0:0}'}" />
            <TextBlock Text="{Binding PerServingTotals.Protein, StringFormat='Protein: {0:0.0}g'}" />
            <TextBlock Text="{Binding PerServingTotals.Fat, StringFormat='Fat: {0:0.0}g'}" />
            <TextBlock Text="{Binding PerServingTotals.Carbs, StringFormat='Carbs: {0:0.0}g'}" />
            <TextBlock Text="{Binding PerServingTotals.Cost, StringFormat='Cost: {0:C}'}" Margin="0,0,0,16" />

            <TextBlock Text="Preference notes" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,8" />
            <ItemsControl ItemsSource="{Binding Concerns}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type calc:CompatibilityConcern}">
                        <TextBlock Text="{Binding Message}" Margin="0,2" />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <ui:Button Content="Save Recipe" Command="{Binding SaveCommand}" Appearance="Primary" Margin="0,16,0,0" />
            <TextBlock Text="{Binding StatusMessage}" Opacity="0.7" Margin="0,8,0,0" />
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 3: Create the Recipe edit page code-behind**

`src/AaronOS.Modules.Nutrition/Views/RecipeEditPage.xaml.cs`:

```csharp
using AaronOS.Core;
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

public sealed partial class RecipeEditPage : Page
{
    public RecipeEditViewModel ViewModel { get; }

    public RecipeEditPage() : this(null)
    {
    }

    public RecipeEditPage(int? recipeId)
    {
        ViewModel = AppServices.Provider.GetRequiredService<RecipeEditViewModel>();
        ViewModel.SetRecipeId(recipeId);
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void RemoveLineButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecipeIngredient line })
        {
            ViewModel.RemoveLineCommand.Execute(line);
        }
    }
}
```

- [ ] **Step 4: Register the ViewModel**

In `src/AaronOS.Modules.Nutrition/NutritionModule.cs`, add one line to `RegisterServices`:

```csharp
services.AddTransient<RecipeEditViewModel>();
```

- [ ] **Step 5: Build and manually verify**

Run: `dotnet build AaronOS.slnx`
Expected: builds with no errors.

Manually run the app, temporarily point `NutritionShellPage`'s `Loaded` handler at `new RecipeEditPage()` (same throwaway technique as Task 8, revert before committing), and confirm: adding ingredient lines updates the per-serving totals and cost live; adding a `Dislike`-rated ingredient (rate one via the Ingredients page first) shows a "Contains disliked ingredient" note; saving and re-opening the same recipe id round-trips its lines correctly.

- [ ] **Step 6: Commit**

```bash
git add src/AaronOS.Modules.Nutrition/ViewModels/RecipeEditViewModel.cs src/AaronOS.Modules.Nutrition/Views/RecipeEditPage.xaml src/AaronOS.Modules.Nutrition/Views/RecipeEditPage.xaml.cs src/AaronOS.Modules.Nutrition/NutritionModule.cs
git commit -m "$(cat <<'EOF'
Add Recipe edit page

Build/edit a recipe's ingredient lines with live per-serving nutrition,
cost, and preference-compatibility notes as you go.
EOF
)"
```

---

### Task 10: Inventory page

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/Views/ExpirationBrushConverter.cs`
- Create: `src/AaronOS.Modules.Nutrition/ViewModels/InventoryViewModel.cs`
- Create: `src/AaronOS.Modules.Nutrition/Views/InventoryPage.xaml`
- Create: `src/AaronOS.Modules.Nutrition/Views/InventoryPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Nutrition/NutritionModule.cs`

**Interfaces:**
- Consumes: `InventoryItem`, `Ingredient`, `StorageLocation` (Task 2); `ShelfLifeEstimator` (Task 4).
- Produces: `InventoryViewModel` with `ObservableCollection<InventoryItem> Items`, `ObservableCollection<Ingredient> AvailableIngredients`, `LoadCommand`, `AddCommand`, `DeleteCommand(InventoryItem)`. Task 11's Dashboard reads `InventoryItem` rows directly via its own DbContext query (not through this ViewModel) — no direct dependency between them.

No automated test — same rationale as Tasks 8/9. Verification is manual: add an item, confirm the shelf-life suggestion appears and the list sorts/color-codes by expiration.

- [ ] **Step 1: Implement the expiration color-coding converter**

`src/AaronOS.Modules.Nutrition/Views/ExpirationBrushConverter.cs`:

```csharp
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AaronOS.Modules.Nutrition.Views;

/// <summary>Small, purpose-specific IValueConverter for expiration color-coding — date-vs-today
/// comparison isn't representable by the NumberBox NaN-sentinel pattern used elsewhere in this
/// codebase, so a converter is the right tool here rather than a workaround.</summary>
public class ExpirationBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateOnly expiresOn)
        {
            return Brushes.Transparent;
        }

        var daysLeft = expiresOn.DayNumber - DateOnly.FromDateTime(DateTime.Now).DayNumber;
        return daysLeft switch
        {
            < 0 => Brushes.IndianRed,
            <= 3 => Brushes.Orange,
            _ => Brushes.Transparent
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 2: Implement `InventoryViewModel`**

`src/AaronOS.Modules.Nutrition/ViewModels/InventoryViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.ShelfLife;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Nutrition.ViewModels;

public partial class InventoryViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    ShelfLifeEstimator shelfLifeEstimator) : ViewModelBase
{
    public ObservableCollection<InventoryItem> Items { get; } = [];
    public ObservableCollection<Ingredient> AvailableIngredients { get; } = [];
    public IReadOnlyList<StorageLocation> StorageLocations { get; } = Enum.GetValues<StorageLocation>();

    [ObservableProperty] private Ingredient? _newIngredient;
    [ObservableProperty] private StorageLocation _newStorageLocation = StorageLocation.Fridge;
    [ObservableProperty] private DateTime? _newDateAcquired = DateTime.Now;
    [ObservableProperty] private DateTime? _newExpiresOn;
    [ObservableProperty] private string _newQuantityLabel = "";
    [ObservableProperty] private string _statusMessage = "";

    partial void OnNewIngredientChanged(Ingredient? value) => RefreshSuggestedExpiration();
    partial void OnNewStorageLocationChanged(StorageLocation value) => RefreshSuggestedExpiration();
    partial void OnNewDateAcquiredChanged(DateTime? value) => RefreshSuggestedExpiration();

    private void RefreshSuggestedExpiration()
    {
        if (NewIngredient is null || NewDateAcquired is null)
        {
            return;
        }

        var estimate = shelfLifeEstimator.EstimateExpiration(
            NewIngredient.Name, NewStorageLocation, DateOnly.FromDateTime(NewDateAcquired.Value));
        if (estimate is { } date)
        {
            NewExpiresOn = date.ToDateTime(TimeOnly.MinValue);
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var ingredients = await db.Set<Ingredient>().OrderBy(i => i.Name).ToListAsync();
            AvailableIngredients.Clear();
            foreach (var ingredient in ingredients)
            {
                AvailableIngredients.Add(ingredient);
            }

            var items = await db.Set<InventoryItem>()
                .Include(i => i.Ingredient)
                .OrderBy(i => i.ExpiresOn)
                .ToListAsync();

            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (NewIngredient is null || NewDateAcquired is null)
        {
            StatusMessage = "Pick an ingredient and a date acquired.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var item = new InventoryItem
        {
            IngredientId = NewIngredient.Id,
            StorageLocation = NewStorageLocation,
            DateAcquired = DateOnly.FromDateTime(NewDateAcquired.Value),
            ExpiresOn = NewExpiresOn is { } exp ? DateOnly.FromDateTime(exp) : null,
            QuantityLabel = string.IsNullOrWhiteSpace(NewQuantityLabel) ? null : NewQuantityLabel
        };
        db.Add(item);
        await db.SaveChangesAsync();

        item.Ingredient = NewIngredient;
        Items.Add(item);

        NewIngredient = null;
        NewExpiresOn = null;
        NewQuantityLabel = "";
        StatusMessage = "Added to inventory.";
    }

    [RelayCommand]
    private async Task DeleteAsync(InventoryItem item)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(item);
        await db.SaveChangesAsync();
        Items.Remove(item);
    }
}
```

- [ ] **Step 3: Create the Inventory page XAML**

`src/AaronOS.Modules.Nutrition/Views/InventoryPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Nutrition.Views.InventoryPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    xmlns:data="clr-namespace:AaronOS.Modules.Nutrition.Data"
    xmlns:views="clr-namespace:AaronOS.Modules.Nutrition.Views"
    mc:Ignorable="d">

    <Page.Resources>
        <views:ExpirationBrushConverter x:Key="ExpirationBrushConverter" />
    </Page.Resources>

    <Grid Margin="24">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="320" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <StackPanel Grid.Column="0" Margin="0,0,32,0">
            <TextBlock Text="Add to Inventory" FontSize="28" FontWeight="SemiBold" Margin="0,0,0,12" />

            <TextBlock Text="Ingredient" Margin="0,0,0,4" />
            <ComboBox ItemsSource="{Binding AvailableIngredients}" DisplayMemberPath="Name"
                      SelectedItem="{Binding NewIngredient, Mode=TwoWay}"
                      IsEditable="True" IsTextSearchEnabled="True" Margin="0,0,0,12" />

            <TextBlock Text="Storage location" Margin="0,0,0,4" />
            <ComboBox ItemsSource="{Binding StorageLocations}" SelectedItem="{Binding NewStorageLocation, Mode=TwoWay}" Margin="0,0,0,12" />

            <TextBlock Text="Date acquired" Margin="0,0,0,4" />
            <DatePicker SelectedDate="{Binding NewDateAcquired, Mode=TwoWay}" Margin="0,0,0,12" />

            <TextBlock Text="Expires on (suggested, editable)" Margin="0,0,0,4" />
            <DatePicker SelectedDate="{Binding NewExpiresOn, Mode=TwoWay}" Margin="0,0,0,12" />

            <TextBlock Text="Quantity (optional)" Margin="0,0,0,4" />
            <ui:TextBox Text="{Binding NewQuantityLabel, Mode=TwoWay}" PlaceholderText="e.g. 1 lb" Margin="0,0,0,12" />

            <ui:Button Content="Add" Command="{Binding AddCommand}" Appearance="Primary" />
            <TextBlock Text="{Binding StatusMessage}" Opacity="0.7" Margin="0,8,0,0" />
        </StackPanel>

        <ListView Grid.Column="1" ItemsSource="{Binding Items}">
            <ListView.ItemTemplate>
                <DataTemplate DataType="{x:Type data:InventoryItem}">
                    <Grid Margin="0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="200" />
                            <ColumnDefinition Width="100" />
                            <ColumnDefinition Width="100" />
                            <ColumnDefinition Width="100" />
                            <ColumnDefinition Width="120" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{Binding Ingredient.Name}" FontWeight="SemiBold" />
                        <TextBlock Grid.Column="1" Text="{Binding StorageLocation}" Opacity="0.7" />
                        <TextBlock Grid.Column="2" Text="{Binding DateAcquired}" Opacity="0.7" />
                        <TextBlock Grid.Column="3" Text="{Binding ExpiresOn, TargetNullValue='—'}"
                                   Foreground="{Binding ExpiresOn, Converter={StaticResource ExpirationBrushConverter}}" />
                        <TextBlock Grid.Column="4" Text="{Binding QuantityLabel}" Opacity="0.7" />
                        <Button Grid.Column="5" Content="Delete" Click="DeleteButton_Click" />
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</Page>
```

- [ ] **Step 4: Create the Inventory page code-behind**

`src/AaronOS.Modules.Nutrition/Views/InventoryPage.xaml.cs`:

```csharp
using AaronOS.Core;
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

public sealed partial class InventoryPage : Page
{
    public InventoryViewModel ViewModel { get; }

    public InventoryPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<InventoryViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InventoryItem item })
        {
            _ = ViewModel.DeleteCommand.ExecuteAsync(item);
        }
    }
}
```

- [ ] **Step 5: Register the ViewModel and the estimator singleton**

In `src/AaronOS.Modules.Nutrition/NutritionModule.cs`, add:

```csharp
using AaronOS.Modules.Nutrition.ShelfLife;
```

```csharp
services.AddSingleton(_ => ShelfLifeEstimator.LoadFromEmbeddedResource());
services.AddTransient<InventoryViewModel>();
```

- [ ] **Step 6: Build and manually verify**

Run: `dotnet build AaronOS.slnx`
Expected: builds with no errors.

Manually run the app (same throwaway `NutritionShellPage` redirect technique as Tasks 8/9, pointing at `new InventoryPage()`, reverted before committing) and confirm: picking an ingredient like "Chicken breast, raw" with today's date and Fridge storage suggests an expiration ~2 days out (per `ShelfLifeReference.json`); adding it lists it; setting the date acquired further back than the shelf-life window shows red, within 3 days shows amber via `ExpirationBrushConverter`.

- [ ] **Step 7: Commit**

```bash
git add src/AaronOS.Modules.Nutrition/Views/ExpirationBrushConverter.cs src/AaronOS.Modules.Nutrition/ViewModels/InventoryViewModel.cs src/AaronOS.Modules.Nutrition/Views/InventoryPage.xaml src/AaronOS.Modules.Nutrition/Views/InventoryPage.xaml.cs src/AaronOS.Modules.Nutrition/NutritionModule.cs
git commit -m "$(cat <<'EOF'
Add Inventory page

Add pantry/fridge/freezer items with a shelf-life-suggested (always
editable) expiration date; list sorted and color-coded by how soon
each item expires.
EOF
)"
```

---

### Task 11: Dashboard page, real shell, final module registration

**Files:**
- Create: `src/AaronOS.Modules.Nutrition/ViewModels/NutritionDashboardViewModel.cs`
- Create: `src/AaronOS.Modules.Nutrition/Views/NutritionDashboardPage.xaml`
- Create: `src/AaronOS.Modules.Nutrition/Views/NutritionDashboardPage.xaml.cs`
- Modify: `src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml` (replace placeholder content)
- Modify: `src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml.cs` (replace placeholder content)
- Modify: `src/AaronOS.Modules.Nutrition/NutritionModule.cs`

**Interfaces:**
- Consumes: `Recipe`, `RecipeIngredient`, `InventoryItem` (Task 2); `RecipeNutritionCalculator`, `RecipeNutritionTotals`, `RecipeCompatibilityChecker`, `CompatibilityConcern`, `CompatibilityLevel`, `RecipeStockChecker`, `RecipeStockResult` (Task 3); `IngredientSeeder.SeedIfEmptyAsync` (Task 6); `RecipeEditPage(int? recipeId = null)` (Task 9).
- Produces: `RecipeCard(Recipe Recipe, RecipeNutritionTotals PerServing, List<CompatibilityConcern> Concerns, RecipeStockResult Stock)` record with computed `HasDislikedIngredient`/`StockSummary`; `NutritionDashboardViewModel` with `ObservableCollection<RecipeCard> VisibleRecipes`, `ExcludeDisliked`, `MaxCaloriesPerServing`, `SortByUseItUp`, `LoadCommand`. This is the last task — nothing downstream consumes these.

No automated test — same rationale as Tasks 8–10. Verification is manual, and is this task's real end-to-end milestone: the whole module working together.

- [ ] **Step 1: Implement `NutritionDashboardViewModel`**

`src/AaronOS.Modules.Nutrition/ViewModels/NutritionDashboardViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Nutrition.Calculations;
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.Seeding;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Nutrition.ViewModels;

public record RecipeCard(
    Recipe Recipe,
    RecipeNutritionTotals PerServing,
    List<CompatibilityConcern> Concerns,
    RecipeStockResult Stock)
{
    public bool HasDislikedIngredient => Concerns.Any(c => c.Level == CompatibilityLevel.Blocked);

    public string StockSummary => Stock.HasEverything
        ? "Have everything"
        : $"Missing: {string.Join(", ", Stock.MissingIngredientNames)}";
}

public partial class NutritionDashboardViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    private List<RecipeCard> _allCards = [];

    public ObservableCollection<RecipeCard> VisibleRecipes { get; } = [];

    [ObservableProperty] private bool _excludeDisliked = true;
    [ObservableProperty] private double _maxCaloriesPerServing = double.NaN;
    [ObservableProperty] private bool _sortByUseItUp;
    [ObservableProperty] private string _statusMessage = "";

    partial void OnExcludeDislikedChanged(bool value) => ApplyFilters();
    partial void OnMaxCaloriesPerServingChanged(double value) => ApplyFilters();
    partial void OnSortByUseItUpChanged(bool value) => ApplyFilters();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            await IngredientSeeder.SeedIfEmptyAsync(db);

            var recipes = await db.Set<Recipe>()
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient).ThenInclude(i => i!.Tags)
                .ToListAsync();
            var inventory = await db.Set<InventoryItem>().ToListAsync();
            var today = DateOnly.FromDateTime(DateTime.Now);

            _allCards = recipes.Select(recipe => new RecipeCard(
                recipe,
                RecipeNutritionCalculator.CalculatePerServing(recipe.Ingredients, Math.Max(recipe.Servings, 1)),
                RecipeCompatibilityChecker.CheckRecipe(recipe.Ingredients),
                RecipeStockChecker.CheckStock(recipe.Ingredients, inventory, today)
            )).ToList();

            ApplyFilters();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilters()
    {
        IEnumerable<RecipeCard> query = _allCards;

        if (ExcludeDisliked)
        {
            query = query.Where(c => !c.HasDislikedIngredient);
        }

        if (!double.IsNaN(MaxCaloriesPerServing))
        {
            query = query.Where(c => c.PerServing.Calories <= (decimal)MaxCaloriesPerServing);
        }

        query = SortByUseItUp
            ? query.OrderByDescending(c => c.Stock.HasExpiringSoonIngredient).ThenBy(c => c.Recipe.Name)
            : query.OrderBy(c => c.Recipe.Name);

        VisibleRecipes.Clear();
        foreach (var card in query)
        {
            VisibleRecipes.Add(card);
        }
    }
}
```

- [ ] **Step 2: Create the Dashboard page XAML**

`src/AaronOS.Modules.Nutrition/Views/NutritionDashboardPage.xaml`:

```xml
<Page
    x:Class="AaronOS.Modules.Nutrition.Views.NutritionDashboardPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    xmlns:vm="clr-namespace:AaronOS.Modules.Nutrition.ViewModels"
    mc:Ignorable="d">

    <DockPanel Margin="24">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,16">
            <TextBlock Text="What can I make?" FontSize="28" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,0,24,0" />
            <CheckBox Content="Exclude disliked ingredients" IsChecked="{Binding ExcludeDisliked, Mode=TwoWay}" VerticalAlignment="Center" Margin="0,0,16,0" />
            <CheckBox Content="Use it up first" IsChecked="{Binding SortByUseItUp, Mode=TwoWay}" VerticalAlignment="Center" Margin="0,0,16,0" />
            <TextBlock Text="Max cal/serving" VerticalAlignment="Center" Margin="0,0,8,0" />
            <ui:NumberBox Width="100" Value="{Binding MaxCaloriesPerServing, Mode=TwoWay}" Margin="0,0,16,0" />
            <ui:Button Content="Add Recipe" Click="AddRecipeButton_Click" Appearance="Primary" />
        </StackPanel>

        <ScrollViewer>
            <ItemsControl ItemsSource="{Binding VisibleRecipes}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type vm:RecipeCard}">
                        <ui:Card Margin="0,0,0,8">
                            <Button Click="RecipeCard_Click" Background="Transparent" BorderThickness="0" HorizontalContentAlignment="Left">
                                <StackPanel>
                                    <TextBlock Text="{Binding Recipe.Name}" FontWeight="SemiBold" FontSize="16" />
                                    <TextBlock Text="{Binding PerServing.Calories, StringFormat='{}{0:0} cal/serving'}" Opacity="0.7" />
                                    <TextBlock Text="{Binding StockSummary}" Opacity="0.7" />
                                </StackPanel>
                            </Button>
                        </ui:Card>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</Page>
```

- [ ] **Step 3: Create the Dashboard page code-behind**

`src/AaronOS.Modules.Nutrition/Views/NutritionDashboardPage.xaml.cs`:

```csharp
using AaronOS.Core;
using AaronOS.Modules.Nutrition.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

public sealed partial class NutritionDashboardPage : Page
{
    public NutritionDashboardViewModel ViewModel { get; }

    public NutritionDashboardPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<NutritionDashboardViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void AddRecipeButton_Click(object sender, RoutedEventArgs e) =>
        NavigationService?.Navigate(new RecipeEditPage());

    private void RecipeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecipeCard card })
        {
            NavigationService?.Navigate(new RecipeEditPage(card.Recipe.Id));
        }
    }
}
```

- [ ] **Step 4: Replace the placeholder shell with the real button-row-plus-Frame shell**

`src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml` (replaces Task 1's placeholder content entirely):

```xml
<Page
    x:Class="AaronOS.Modules.Nutrition.Views.NutritionShellPage"
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
            <ui:Button Content="Dashboard" Click="Dashboard_Click" Margin="0,0,8,0" />
            <ui:Button Content="Ingredients" Click="Ingredients_Click" Margin="0,0,8,0" />
            <ui:Button Content="Inventory" Click="Inventory_Click" />
        </StackPanel>

        <Frame x:Name="ContentFrame" Grid.Row="1" NavigationUIVisibility="Hidden" />
    </Grid>
</Page>
```

`src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

/// <summary>
/// The module's single nav-pane entry point. Hosts an internal Frame so the shell only needs one
/// NavigationView item — this page provides its own top-level navigation to its three pages, per
/// docs/MODULE_GUIDELINES.md.
/// </summary>
public sealed partial class NutritionShellPage : Page
{
    public NutritionShellPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ContentFrame.Navigate(new NutritionDashboardPage());
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new NutritionDashboardPage());
    private void Ingredients_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new IngredientsPage());
    private void Inventory_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new InventoryPage());
}
```

- [ ] **Step 5: Finalize `NutritionModule.RegisterServices`**

`src/AaronOS.Modules.Nutrition/NutritionModule.cs` (final form — add the one missing registration):

```csharp
using AaronOS.Core;
using AaronOS.Modules.Nutrition.ShelfLife;
using AaronOS.Modules.Nutrition.Usda;
using AaronOS.Modules.Nutrition.ViewModels;
using AaronOS.Modules.Nutrition.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Modules.Nutrition;

public class NutritionModule : IAppModule
{
    public string Id => "nutrition";
    public string DisplayName => "Nutrition";
    public string IconGlyph => "Food24"; // confirm exact Wpf.Ui.Controls.SymbolRegular member — see Step 6
    public Type HomePageType => typeof(NutritionShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<UsdaCredentialStore>();
        services.AddSingleton<UsdaApiClient>();
        services.AddSingleton(_ => ShelfLifeEstimator.LoadFromEmbeddedResource());
        services.AddTransient<NutritionDashboardViewModel>();
        services.AddTransient<IngredientsViewModel>();
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<RecipeEditViewModel>();
    }
}
```

- [ ] **Step 6: Build and confirm the `IconGlyph` value is a real enum member**

Run: `dotnet build AaronOS.slnx`
Expected: builds with no errors.

Run the app. If `"Food24"` isn't a real `Wpf.Ui.Controls.SymbolRegular` member, `MainWindow`'s `Enum.Parse<SymbolRegular>(module.IconGlyph)` throws at startup — the exception message names the bad value. If it throws, open the `SymbolRegular` enum (via Go To Definition on `SymbolRegular` in `MainWindow.xaml.cs`, or Object Browser) and pick any food-related member (e.g. `Food24`, `FoodApple24`, `FoodCarrot24` — whichever actually exists), update `IconGlyph` to match, and rebuild.

- [ ] **Step 7: Full end-to-end manual verification**

With a fresh local database (delete `%LocalAppData%\AaronOS\aaronos.db` if you want a clean slate — otherwise whatever accumulated from Tasks 8–10's manual testing is fine), run the app and walk through:

1. Nutrition nav item → Dashboard loads, preloaded ~135 ingredients exist (seeded silently on this first load).
2. Ingredients page → rate a couple of ingredients (e.g. rate "Mushroom, white raw" as Dislike and tag it "fungi"; add a new ingredient "Truffle" tagged "fungi" and leave it unrated).
3. Inventory page → add "Chicken breast, raw" with today's date, Fridge — confirm a ~2-day expiration is suggested.
4. Dashboard → Add Recipe → build a recipe using both Truffle and Chicken breast — confirm the edit page shows a "Possible dislike (tagged fungi): Truffle" caution note, and the "have everything"/"missing" stock badge is correct once saved.
5. Back on the Dashboard, toggle "Exclude disliked ingredients" and "Use it up first," and confirm the recipe list filters/reorders as expected.

- [ ] **Step 8: Commit**

```bash
git add src/AaronOS.Modules.Nutrition/ViewModels/NutritionDashboardViewModel.cs src/AaronOS.Modules.Nutrition/Views/NutritionDashboardPage.xaml src/AaronOS.Modules.Nutrition/Views/NutritionDashboardPage.xaml.cs src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml src/AaronOS.Modules.Nutrition/Views/NutritionShellPage.xaml.cs src/AaronOS.Modules.Nutrition/NutritionModule.cs
git commit -m "$(cat <<'EOF'
Add Nutrition dashboard and wire up the real module shell

The "what can I make" filterable recipe list — excludes disliked
ingredients by default, filters by max calories, and can sort recipes
by which ones use ingredients expiring soonest. Replaces the
placeholder shell from Task 1 with real Dashboard/Ingredients/Inventory
navigation. This closes out the module's v1 feature set.
EOF
)"
```

---

### Task 12: Full test suite and solution verification

**Files:** none new — this is a verification-only pass.

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test AaronOS.slnx`
Expected: every test across `AaronOS.Modules.Finance.Tests` and `AaronOS.Modules.Nutrition.Tests` passes (Nutrition contributes: 4 `RecipeNutritionCalculatorTests` + 5 `RecipeCompatibilityCheckerTests` + 5 `RecipeStockCheckerTests` + 5 `ShelfLifeEstimatorTests` + 1 `ApiKeyProtectorTests` + 3 `IngredientSeederTests` = 23 tests).

- [ ] **Step 2: Build the full solution from clean**

Run: `dotnet clean AaronOS.slnx && dotnet build AaronOS.slnx`
Expected: builds with no errors or warnings introduced by this module (pre-existing warnings from Finance/BodyMeasurements, if any, aren't this task's concern).

- [ ] **Step 3: Final manual smoke test with a completely fresh database**

Delete `%LocalAppData%\AaronOS\aaronos.db`, run the app, and confirm all three modules' nav items work: Body Measurements (still the default landing page — confirms Task 1's append-only change to the module array didn't disturb it), Finance, and Nutrition (confirms the seed runs cleanly on a genuinely empty database, not just one already populated from earlier manual testing).

- [ ] **Step 4: No commit for this task** — it's verification-only. If Step 1 or 2 surfaces a failure, fix it as part of whichever earlier task actually owns the broken code, and commit there (don't accumulate an unrelated "fix stuff" commit at the end).


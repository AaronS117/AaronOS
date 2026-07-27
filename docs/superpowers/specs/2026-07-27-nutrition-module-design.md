# Nutrition Module — Design

## Context

AaronOS is a modular WPF desktop app (see `docs/MODULE_GUIDELINES.md`). It currently has two
modules, `AaronOS.Modules.BodyMeasurements` and `AaronOS.Modules.Finance`. The user wants a third
module that tracks food preferences (likes/dislikes, preferred form — fresh vs. canned, etc.),
nutrition facts, recipes built from ingredients, and a pantry/fridge inventory with expiration
tracking — a personal tool for picking healthy meals from food the user actually likes and
already has on hand.

This module follows `docs/MODULE_GUIDELINES.md` exactly: a compiled-in `IAppModule`, its own
entities discovered automatically by the shared `AaronOsDbContext`, its own ViewModels/Pages, one
project reference from `AaronOS.App`, one line appended to the module array.

## Scope for v1

- A preloaded ingredient catalog (see "Preloaded ingredient data") you rate, tag, and adjust
  rather than building from scratch.
- Per-ingredient preferences: like/dislike/neutral rating, preferred form (free text), tags
  (free-form, e.g. "fungi", "shellfish"), nutrition facts (per 100g), cost (per 100g).
- USDA FoodData Central lookup when adding an ingredient not in the preload.
- Recipes ("formulas") built from ingredients with quantities, with live nutrition/cost rollup
  and a preference-compatibility check (flags disliked ingredients, tag-based "possible dislike"
  hints, and preferred-form mismatches).
- A pantry/fridge/freezer inventory: add items by typing a name, pick a storage location and date
  acquired, get a suggested expiration date from a bundled shelf-life reference, track what's on
  hand and what's expiring soon.
- The recipe dashboard ties the two halves together: it flags whether you currently have every
  ingredient for a recipe, and supports a "use it up" sort that surfaces recipes consuming
  ingredients closest to expiring.

Explicitly out of scope for v1 (noted so it isn't silently forgotten, not because it's hard):
- Barcode/camera/photo-based inventory intake. The user is planning a separate Purchasing module
  next, which is the more natural home for scan/photo intake at the point of purchase — this
  module's inventory intake is text search only.
- AI-based food image recognition.
- Automatic/background sync of anything.
- Metric units — imperial/US customary only, consistent with the rest of the app
  (`MODULE_GUIDELINES.md`'s units convention).
- Meal planning/scheduling (a calendar of what to eat when) and shopping list generation.
- Reading live data from the Finance module. Cost is tracked independently on ingredients/recipes;
  no `FinanceTransaction` read.

## Module shape

`AaronOS.Modules.Nutrition`, a class library exactly like `Finance`/`BodyMeasurements`:

```csharp
public class NutritionModule : IAppModule
{
    public string Id => "nutrition";
    public string DisplayName => "Nutrition";
    public string IconGlyph => "Food24"; // confirm exact Wpf.Ui.Controls.SymbolRegular member at implementation time
    public Type HomePageType => typeof(NutritionShellPage);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<UsdaApiClient>();
        services.AddSingleton<UsdaCredentialStore>();
        services.AddSingleton<ShelfLifeEstimator>();
        services.AddTransient<NutritionDashboardViewModel>();
        services.AddTransient<IngredientsViewModel>();
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<RecipeEditViewModel>();
    }
}
```

`AaronOS.App` gets a project reference to `AaronOS.Modules.Nutrition`, and one line **appended**
last to the module array in `App.xaml.cs`:

```csharp
IAppModule[] modules = [new BodyMeasurementsModule(), new FinanceModule(), new NutritionModule()];
```

Appending preserves current behavior — `MainWindow` navigates to `NavView.MenuItems[0]` on load,
so Body Measurements stays the default landing page.

### csproj

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<LangVersion>13.0</LangVersion>
<Nullable>enable</Nullable>
```

Package references: `WPF-UI` (pin to the same `4.3.0` Finance/BodyMeasurements use). No charting
library (`LiveChartsCore`) — nothing in this module's scope needs a chart, so it isn't pulled in.

## Data model

Four entities under `AaronOS.Modules.Nutrition/Data/`, each auto-discovered by
`AaronOsDbContext.OnModelCreating` like every other module's entities — no edits to
`AaronOsDbContext` itself, no references to another module's tables.

- **`Ingredient`** — `Id` (int PK), `Name`, `Rating` (nullable enum `Like`/`Dislike`/`Neutral`,
  null = unrated), `PreferredForm` (`string?`, free text — "fresh", "canned", "diced"; no rigid
  enum, since forms vary too much by ingredient to enumerate), `CaloriesPer100g`,
  `ProteinPer100g`, `FatPer100g`, `CarbsPer100g`, `FiberPer100g`, `SodiumMgPer100g` (all
  `decimal?`), `CostPer100g` (`decimal?`), `FdcId` (`int?`, USDA's id, kept for reference only).
- **`Tag`** — `Id`, `Name` (unique, free text). Many-to-many with `Ingredient` via EF Core's
  implicit skip-navigation (`Ingredient.Tags` / `Tag.Ingredients`) — no explicit join entity or
  configuration needed; EF Core 8 handles this natively against SQLite.
- **`Recipe`** — `Id`, `Name`, `Instructions` (`string?`), `Servings` (`int`).
- **`RecipeIngredient`** — `Id`, `RecipeId` (FK), `IngredientId` (FK), `QuantityGrams` (`decimal`,
  the canonical unit all nutrition/cost math runs on), `DisplayAmount` (`string?`, human text like
  "2 cups diced" — display only, not used in calculation), `FormUsed` (`string?`, optional,
  compared against `Ingredient.PreferredForm` for the compatibility check).
- **`InventoryItem`** — `Id`, `IngredientId` (FK), `StorageLocation` (enum `Fridge`/`Freezer`/
  `Pantry`), `DateAcquired` (`DateOnly`), `ExpiresOn` (`DateOnly`, pre-filled by
  `ShelfLifeEstimator` at add-time, always editable/overridable), `QuantityLabel` (`string?`, free
  text, same loose-unit convention as `RecipeIngredient.DisplayAmount`), `Notes` (`string?`).

**Cost convention:** cost is normalized per 100g, the same basis nutrition facts already use, so
recipe-level rollups stay a single consistent unit rather than mixing lb/oz/each. This means
converting a grocery price to "per 100g" once when adding an ingredient.

No EF migrations, consistent with the rest of the app (`EnsureCreatedAsync()` only).

## Preloaded ingredient data

A curated seed file, `Resources/ingredient-seed.json`, embedded as a resource — a few hundred
common grocery ingredients (meat, dairy, produce, grains, pantry staples) sourced from USDA's
public SR Legacy / Foundation Foods data, mapped to this module's schema: `Name`,
`CaloriesPer100g`, `ProteinPer100g`, `FatPer100g`, `CarbsPer100g`, `FiberPer100g`,
`SodiumMgPer100g`, `FdcId`. No `Rating`, `PreferredForm`, or `Tags` are pre-set — that's the part
the user covers themselves after the module ships.

Seeding runs from `NutritionDashboardViewModel`'s load command (the module's home page, so it's
the natural first-touch point): if `db.Ingredients` is empty, bulk-insert the seed file's contents
in one pass, then proceed with normal loading. Idempotent by construction (the emptiness check),
so there's no separate "have I seeded" flag to maintain. This intentionally isn't a change to the
`IAppModule` contract or `App.xaml.cs` — a generic cross-module seeding hook would be speculative
for the two existing modules, which don't need one; if a future module needs the same pattern,
that's the point to consider promoting it to `Core`, not before.

## Nutrition data — USDA FoodData Central

`UsdaApiClient` (static `HttpClient`, mirrors `PlaidApiClient`'s pattern): search-as-you-type
against `/v1/foods/search`, pick a result, fetch `/v1/food/{fdcId}` for full nutrient detail, map
the six tracked nutrients into a new `Ingredient` row (editable afterward — USDA's numbers are a
starting point). Used only when adding something not already in the preloaded catalog. Requires a
free api.data.gov API key (instant self-service signup, no OAuth). `UsdaCredentialStore` stores it
DPAPI-protected under `%LocalAppData%\AaronOS\`, the same mechanism `PlaidCredentialStore` uses in
Finance — duplicated rather than shared, since modules can't reference each other's internals and
one small DPAPI helper isn't worth promoting to `Core` for two callers yet. Adding an ingredient
without a USDA match (homemade items) is plain manual entry.

## Shelf-life reference data

A second embedded resource, `Resources/shelf-life-reference.json` — a hand-curated subset of the
public FDA FoodKeeper dataset: keyword → days-good-for per storage location (e.g. "chicken breast,
raw" → fridge 2 days / freezer 270 days). Loaded once at startup by the `ShelfLifeEstimator`
singleton, which does case-insensitive keyword matching against an ingredient's name. This is a
static resource rather than a seeded database table for the same reason as the units convention in
`MODULE_GUIDELINES.md`'s schema section: this app has no EF migrations, so a seeded table would be
awkward to update later, while a new build with an updated JSON file is not. If no keyword
matches, there's simply no suggested date and the user enters `ExpiresOn` manually.

## Preference & compatibility logic

`RecipeCompatibilityChecker` (small pure-function service, same shape as Finance's
`CategorySpendCalculator`) — given a `Recipe`, for each `RecipeIngredient`:
- Ingredient rated `Dislike` → hard flag: "Contains disliked ingredient: X."
- Unrated ingredient sharing a tag with a `Dislike`-rated ingredient → soft flag: "Possible dislike
  (tagged fungi, like Mushroom): Truffle." This never auto-sets a rating — it's a hint, not an
  assumption.
- `FormUsed` differing from `Ingredient.PreferredForm` (both set) → soft note: "You prefer chicken
  fresh; this recipe uses canned."

`RecipeNutritionCalculator` (pure function) — sums `QuantityGrams / 100 × per-100g value` across a
recipe's ingredients for calories/macros/cost, total and per-serving (`÷ Recipe.Servings`).

`RecipeStockChecker` (pure function) — given a `Recipe` and the current `InventoryItem` list,
reports per-ingredient in-stock/missing, and flags whether any in-stock ingredient is expiring
within a configurable threshold (default 3 days). Backs the dashboard's "have everything" /
"missing: X, Y" badge and its "use it up" sort.

All three are plain functions over in-memory data — no DB or WebView2 dependency — making them
directly unit-testable, matching the precedent Finance set with `CategorySpendCalculator` and
`TransactionSyncMerger`.

## Pages / navigation

`NutritionShellPage` (the module's `HomePageType`) — a row of buttons plus an internal `Frame`,
exactly like the other two modules — navigates via `Frame.Navigate(new SomePage())` between:

- **`NutritionDashboardPage`** — the "what can I make" view: recipe list with an "exclude recipes
  with disliked ingredients" toggle (on by default), a max-calories-per-serving filter, tag
  include/exclude, sort by cost/calories/protein, and the stock/expiring-soon badges and "use it
  up" sort from `RecipeStockChecker`. Filtering itself is plain LINQ in the ViewModel — only the
  dislike/stock *logic* needed its own tested service. Clicking a recipe or "Add Recipe" navigates
  to `RecipeEditPage`.
- **`IngredientsPage`** — browse/search the ingredient catalog (preloaded + anything you've added),
  set rating/tags/preferred form/cost, add new ingredients via USDA search or manual entry.
- **`InventoryPage`** — list of `InventoryItem`s sorted by days-until-expiration (expired items
  flagged red, expiring-soon flagged amber), "Add to Inventory" flow: type an ingredient name
  (matched against the `Ingredient` catalog via the same search-or-create picker `IngredientsPage`
  uses — creating a new `Ingredient` first if it doesn't exist), pick storage location and date
  acquired, confirm or override the suggested expiration date.
- **`RecipeEditPage`** — name, servings, instructions, add/remove `RecipeIngredient` rows, live
  nutrition/cost/compatibility rollup as it's built.

ViewModels derive from `AaronOS.Core.ViewModelBase` (carries `IsBusy`). Each Page resolves its
ViewModel in its constructor via `AppServices.Provider.GetRequiredService<T>()`, sets
`DataContext` explicitly, and kicks off loading from the `Loaded` event — matching
`docs/MODULE_GUIDELINES.md` exactly.

## Error handling

- USDA API failures (network errors, no match found): caught, surfaced as an inline message in the
  search UI; falls back to manual entry, no crash.
- Missing shelf-life match: no suggested date, `ExpiresOn` left for manual entry — not an error
  state, just an absent hint.
- Seed file failing to load/parse at startup: logged and skipped rather than blocking the module
  from loading — an empty catalog you build manually is a safe fallback, not a hard failure.

## Testing

`AaronOS.Modules.Nutrition.Tests` (xunit, plain `net8.0`, no WPF/WebView2 dependency), covering the
real logic as pure functions/services:

1. `RecipeNutritionCalculator` rollup math (total and per-serving).
2. `RecipeCompatibilityChecker` (dislike detection, tag-based soft warning, form mismatch).
3. `RecipeStockChecker` (in-stock/missing detection, expiring-soon threshold).
4. `ShelfLifeEstimator` keyword matching (match found, no match, storage-location variation).
5. DPAPI encrypt/decrypt round-trip for the USDA credential store.

This mirrors the testing approach already established by the Finance module design.

# Avatar Progress Visualization — Design

## Context

The Body Measurements module already tracks check-ins and goals (weight, waist, biceps, etc.)
through plain numbers and progress bars on the Dashboard. The user wants a more motivating,
game-like way to see progress: a body silhouette that reflects their actual current proportions,
a "ghost" outline showing their goal shape overlaid on it, and per-metric stat rings/bars next to
it — closer to an RPG character sheet than a spreadsheet. This is explicitly a stepping stone
toward a future Medical module the user has in mind, but this feature is scoped to Body
Measurements only; it reads no data beyond what that module already owns.

## Approach

### Where this lives

A new page, `AvatarPage`, inside the existing `AaronOS.Modules.BodyMeasurements` module — not a
new module. The feature is built entirely from this module's own `BodyCheckIn` and `Goal`
entities, and per `docs/MODULE_GUIDELINES.md`, modules never reach into another module's data.
`BodyMeasurementsShellPage`'s `CommandBar` gets a sixth button, "Avatar", alongside
Dashboard/Check-In/History/Clothing Sizes/Goals, navigating its internal `Frame` to `AvatarPage`
exactly like the other five.

### Rendering: procedural silhouette, not art assets

A new custom control, `BodySilhouetteControl` (a `UserControl` with a `Canvas`), draws a blocky,
stylized humanoid figure entirely in code-behind — no external art, no new NuGet dependency, just
WinUI shapes (`Rectangle`, `Polygon`, `Ellipse`). Segments:

- **Head** — fixed-size `Ellipse`, doesn't scale (no head measurement is tracked).
- **Neck** — `Rectangle`, width driven by `NeckIn`.
- **Torso** — a tapered `Polygon` from shoulder/chest width down to waist width, then a second
  tapered polygon from waist width to hip width. Driven by `ChestIn`, `WaistIn`, `HipsIn`.
- **Arms** — two rectangles (bicep segments), widths from `BicepLeftIn` / `BicepRightIn`.
- **Legs** — two two-segment legs (thigh + calf), widths from `ThighLeftIn`/`ThighRightIn` and
  `CalfLeftIn`/`CalfRightIn`.

Vertical proportions are fixed at typical human body ratios (head ~1/8 of total height, etc.) —
only segment *widths* scale, via a constant pixels-per-inch factor. `Weight` has no corresponding
visual segment (it isn't a single width); a weight goal is represented only as a progress bar (see
below), never on the silhouette itself.

The control redraws its `Canvas` children whenever its bound data changes (measurement or goal
set updates) — no persistent per-shape data bindings; the draw routine clears and rebuilds the
canvas each time, since recomputing interdependent polygon points imperatively is far simpler
than XAML property bindings.

### Ghost overlay

For every `GoalMetric` that has an active goal AND maps to a silhouette segment (i.e., every
metric except `Weight`), a second copy of that segment is drawn using the goal's *target* value
instead of the current value — rendered translucent/outline-only (stroke, no fill, reduced
opacity) so it reads as a target silhouette layered behind or over the current one. Metrics with
no active goal render only their current-value segment, with no ghost.

### Per-metric progress rings/bars

Positioned beside the silhouette: one row per active goal, each showing the metric name, current
value, target value, and progress percentage — this is the same shape of information the
Dashboard already shows for `ActiveGoals` (see `DashboardViewModel.GoalProgress` /
`ComputeProgress` in `src/AaronOS.Modules.BodyMeasurements/ViewModels/DashboardViewModel.cs`).
That logic gets factored out into a shared helper (e.g. a static method on `GoalMetricExtensions`
or a small new `GoalProgressCalculator` in `Data/`) so `DashboardViewModel` and the new
`AvatarViewModel` compute it identically instead of duplicating it.

No aggregate "level" or combined score — per the earlier discussion, goals that pull in different
directions (losing weight vs. gaining bicep size) aren't naturally comparable into one number, and
inventing a weighting scheme would be arbitrary. Each metric stands on its own.

### Data flow

`AvatarViewModel` follows the same pattern as every other ViewModel in this module: constructor
takes `IDbContextFactory<AaronOsDbContext>`, a `[RelayCommand] LoadAsync()` pulls the latest
`BodyCheckIn` and all active (`!IsAchieved`) `Goal`s via a short-lived `DbContext`, then builds:

- A `Dictionary<GoalMetric, decimal>` of current values (from the latest check-in, via the
  existing `GoalMetricExtensions.GetValue`).
- A `Dictionary<GoalMetric, decimal>` of goal target values (only for metrics with an active
  goal).
- The list of per-metric progress rows for the side panel (via the shared progress helper above).

`AvatarPage` follows the established Page pattern: parameterless constructor resolves
`AvatarViewModel` via `AaronOS.Core.AppServices.Provider.GetRequiredService<AvatarViewModel>()`,
`OnNavigatedTo` triggers `LoadCommand`. `BodySilhouetteControl` is a plain child control on the
page, bound (via code-behind property assignment after load, not x:Bind, since the two
dictionaries aren't simple bindable properties) to the current-values and target-values
dictionaries.

### Explicitly out of scope for this pass

- No aggregate/level score (per the discussion above).
- No swapped character-art states — this is a procedural silhouette only.
- No animation/transition when values change; the figure simply redraws on load.
- No back-view; front view only.

## Verification

- Build the solution; confirm `AvatarPage` compiles and the new "Avatar" nav button appears in
  `BodyMeasurementsShellPage`.
- With no check-ins or goals yet, confirm the silhouette renders a reasonable default figure
  (no crash on empty/null data) and the side panel shows no rows.
- Log a check-in with real measurements and add 2-3 goals across different metrics (including one
  `Weight` goal); confirm the silhouette's segment widths reflect the check-in values, the ghost
  overlay appears only for metrics with an active goal, and the `Weight` goal shows only as a
  progress row with no silhouette effect.
- Confirm `DashboardViewModel`'s existing goal-progress display still matches the shared helper's
  output after the refactor (no behavior change to the Dashboard).

# WinUI 3 → WPF Migration — Design

## Context

WinUI 3 has no design-time XAML canvas in Visual Studio (confirmed against current Microsoft
docs — the classic Design tab explicitly doesn't support WinUI 3 desktop projects), forcing a
run-and-Hot-Reload workflow for every visual tweak. The user wants a real designer to make visual
design decisions, particularly ahead of building a Medical module next. WPF has a mature designer
and is otherwise a comparable, stable framework for this personal desktop app, so the decision is
to migrate the whole solution now, while it's still small, rather than let the cost grow.

## Approach

**Styling library:** `Wpf.Ui` (lepoco) — confirmed actively maintained (2026 copyright, .NET 9
support, digitally signed packages) vs. ModernWpf's abandoned packages (last updated 2016-2022).
Ships `FluentWindow`, `NavigationView`, and `NumberBox`, covering every WinUI control this project
currently depends on.

### Per-project changes

**`AaronOS.Core`**
- Remove `UseWinUI`, `Microsoft.WindowsAppSDK` package reference, `EnforceExtendedAnalyzerRules`
  (that workaround existed only for a WinUI/CsWinRT source-generator conflict, which doesn't
  apply to WPF).
- `TargetFramework` simplifies from `net8.0-windows10.0.26100.0` to `net8.0-windows` (WPF doesn't
  need the Windows SDK contract version suffix). Add `<UseWPF>true</UseWPF>`.
- `IAppModule.Icon` changes from `IconElement` (WinUI-only type) to `IconGlyph` (a plain string).
  This decouples `Core` from any UI framework entirely — a real architectural fix, not just
  migration churn, since a shared module-contract library should never have depended on a
  specific UI framework's types in the first place.
- Re-attempt `[ObservableProperty]` as a partial property (not field-backed) in `ViewModelBase`
  now that the CsWinRT-specific generator conflict no longer applies; fall back to the existing
  field-backed form only if it still doesn't generate correctly.

**`AaronOS.Modules.BodyMeasurements`**
- Same WinUI-stripping as Core.
- `LiveChartsCore.SkiaSharpView.WinUI` → `LiveChartsCore.SkiaSharpView.WPF` (LiveCharts2 supports
  both; charting code in `DashboardViewModel` is unaffected).
- Every view (`DashboardPage`, `CheckInPage`, `HistoryPage`, `ClothingSizesPage`, `GoalsPage`,
  `BodyMeasurementsShellPage`) gets rewritten against WPF + Wpf.Ui controls:
  - `x:Bind` → classic `{Binding}`, with `DataContext` set explicitly in each Page's constructor
    (same place the ViewModel is already resolved via `AppServices.Provider`).
  - `NumberBox` maps directly to `Wpf.Ui.Controls.NumberBox` — no redesign needed there.
  - `CommandBar`/`AppBarButton` (used in the shell page) become a plain row of `Wpf.Ui` buttons.
  - The `WMC9999`/cross-template-`x:Bind` gotcha documented in `MODULE_GUIDELINES.md` doesn't
    exist in WPF's binding model — list-item Delete/Achieved buttons can go back to idiomatic
    `Command` binding via `RelativeSource AncestorType=Page` instead of the `Click`-handler
    workaround, though keeping the existing `Click` handlers is also fine and lower-churn. Default
    to keeping `Click` handlers to minimize the diff; this is a style choice, not a correctness one.
  - ViewModels themselves need no logic changes — they're already framework-agnostic MVVM Toolkit
    classes. The `double.NaN`-as-"not entered" sentinel pattern in `CheckInViewModel`/
    `GoalsViewModel` still works unchanged against `Wpf.Ui.Controls.NumberBox`.

**`AaronOS.App`**
- Becomes a WPF host: `System.Windows.Application`/`Window` instead of the WinUI equivalents.
  `MainWindow` rebuilt on `Wpf.Ui.Controls.FluentWindow` with `WindowBackdropType="Mica"` and a
  `Wpf.Ui.Controls.NavigationView` populated from the registered `IAppModule` list exactly as
  before (icon now built from each module's `IconGlyph` string).
- The Generic Host/DI composition root in `App.xaml.cs` is unaffected — it already has no WinUI
  dependency.
- `SettingsPage`/`SettingsViewModel` get the same `{Binding}` + `DataContext` treatment as the
  module's pages.
- WPF's own `Frame`/`Page` navigation model is nearly identical to WinUI's, so
  `NavFrame.Navigate(typeof(Page))` carries over with only the `Page` base type's namespace
  changing.

### Side benefits of the migration

- The `dotnet-ef` design-time tooling crash documented in `MODULE_GUIDELINES.md` was caused by
  WinRT module initializers in any `Microsoft.WindowsAppSDK`-referencing assembly. WPF has no such
  initializer, so real EF Core migrations become viable again — worth revisiting once the
  migration lands, though not part of this migration itself.
- `MODULE_GUIDELINES.md` needs a full rewrite of its WinUI-specific sections (required csproj
  properties, the `WMC9999` gotcha, the `ObservableProperty` gotcha) once the migration is done,
  since none of those apply to WPF.

## Explicitly out of scope for this pass

- Not re-adding EF Core migrations tooling in this pass — noted as a follow-up, not bundled in.
- Not redesigning any page's layout/content — this is a like-for-like framework swap; visual
  polish using the new designer happens afterward, separately.

## Verification

- Solution builds with zero WinUI/WindowsAppSDK references remaining anywhere.
- App launches as a normal WPF window (no MSIX/WindowsAppSDK bootstrap involved at all).
- Every existing page (Dashboard incl. chart, Check-In, History, Clothing Sizes, Goals, Settings)
  still loads and behaves identically to the WinUI version: log a check-in, add a goal, add a
  clothing size, delete each, confirm data persists across restart.
- Open `AaronOS.slnx` in Visual Studio and confirm the XAML Designer's Design/Split view now
  works for at least one page — this is the entire point of the migration.

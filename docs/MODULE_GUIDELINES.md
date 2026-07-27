# AaronOS Module Guidelines

AaronOS is built to take an open-ended series of feature modules. This document is the contract
every module follows so they compose cleanly under one shell, one database, and one navigation
pane. Read this before adding a new module.

The app is a WPF desktop app (migrated from WinUI 3 — WinUI 3 has no design-time XAML canvas in
Visual Studio, which made visual design work impractical). Styling comes from the `WPF-UI`
NuGet package (`Wpf.Ui.Controls`, namespace `http://schemas.lepo.co/wpfui/2022/xaml`, conventionally
aliased `ui:`), which brings Fluent/Mica styling and a `NavigationView` control to WPF.

## Architecture in one paragraph

Modules are compiled-in class library projects, not runtime-loaded plugins. The shell
(`AaronOS.App`) holds an explicit list of module instances built at startup. Adding a module
means: new project, one project reference from `AaronOS.App`, one line registering it in the
composition root. No reflection scanning, no dynamic assembly loading.

## Adding a new module: checklist

1. Create a new class library project named `AaronOS.Modules.<Name>`:
   ```
   dotnet new classlib -n AaronOS.Modules.<Name> -f net8.0-windows
   ```
2. Copy the property block below into the new csproj (see "Required csproj properties").
3. Add project reference: `AaronOS.Core`. Add package references your module actually needs
   (CommunityToolkit.Mvvm and EF Core come transitively through Core; add `WPF-UI` yourself if
   your views use its controls).
4. Implement `IAppModule` (see "The module contract").
5. Define your entities and their `IEntityTypeConfiguration<T>` classes under `Data/`.
6. Build your ViewModels under `ViewModels/`, deriving from `AaronOS.Core.ViewModelBase`.
7. Build your Pages under `Views/`.
8. In `AaronOS.App`, add a project reference to your module and add one line to the module list
   in the composition root (`App.xaml.cs`).
9. Run the app and confirm your module's nav item appears and its home page loads — your new
   tables are created automatically on next launch (see "Schema creation" below).

## The module contract

```csharp
public interface IAppModule
{
    string Id { get; }
    string DisplayName { get; }
    string IconGlyph { get; }
    Type HomePageType { get; }
    void RegisterServices(IServiceCollection services);
}
```

- `Id`: a short, stable, unique slug (e.g. `"body-measurements"`). Never reuse another module's id.
- `DisplayName`: what shows up in the shell's `NavigationView`.
- `IconGlyph`: the name of a `Wpf.Ui.Controls.SymbolRegular` enum member (e.g. `"Person24"`), kept
  as a plain string so this contract has no compile-time dependency on the UI framework's icon
  type. The shell parses it via `Enum.Parse<SymbolRegular>(module.IconGlyph)`.
- `HomePageType`: the `Page` type the shell navigates to when your nav item is selected. Must have
  a public parameterless constructor — `Wpf.Ui`'s `NavigationView` instantiates it via
  `TargetPageType`, the same way WPF's own `Frame.Navigate(Type)` would.
- `RegisterServices`: register your ViewModels and any module-specific services into the shared
  `IServiceCollection`. Register ViewModels as **transient** (a fresh instance per navigation),
  not singleton — pages new one up each time they're navigated to.

A minimal module looks like:

```csharp
public class WidgetsModule : IAppModule
{
    public string Id => "widgets";
    public string DisplayName => "Widgets";
    public string IconGlyph => "Grid24";
    public Type HomePageType => typeof(WidgetsDashboardPage);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<WidgetsDashboardViewModel>();
    }
}
```

## Required csproj properties

Every module project needs:

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<LangVersion>13.0</LangVersion>
<Nullable>enable</Nullable>
```

`LangVersion 13.0` is needed for pattern-matching syntax used across the codebase (e.g.
`sender is FrameworkElement { DataContext: MyEntity item }`) — it isn't tied to any UI framework,
just to using a modern enough C# version against a `net8.0` target.

**Known gotcha:** the CommunityToolkit.Mvvm partial-property generator
(`public partial bool X { get; set; }`) does not reliably run in this environment — confirmed to
fail identically in a plain `net8.0` class library, so it isn't WPF- or WinUI-specific, just an
environment/tooling quirk. Use the classic field-backed form instead —
`[ObservableProperty] private bool _x;` — and ignore the `MVVMTK0045` AOT-compatibility warning it
produces; this app is never published as Native AOT.

## Data ownership rules

- One shared SQLite database (`AaronOsDbContext`, defined in `AaronOS.Core`) for the whole app.
- Your module owns its own entities and `IEntityTypeConfiguration<T>` classes in its own
  assembly. `AaronOsDbContext.OnModelCreating` discovers them automatically by scanning the
  assemblies of every registered `IAppModule` — you don't touch `AaronOsDbContext` itself.
- **Never reference another module's entities or tables directly.** If two modules genuinely
  need to share data, put the shared shape in `AaronOS.Core` (like `UserProfile`) and have both
  modules depend on Core, not on each other.
- Table names should be prefixed or scoped clearly enough that two modules can't collide by
  accident (EF Core's default pluralized-DbSet naming is normally enough, given entity names are
  already domain-specific).

## Schema creation (no EF migrations, for now)

App startup calls `Database.EnsureCreatedAsync()`, not `Database.MigrateAsync()`, and there are
no `dotnet ef migrations` in this repo yet.

**Why it's like this:** under WinUI 3, `dotnet-ef` design-time tooling couldn't load any project
in this solution at all (every project referenced `Microsoft.WindowsAppSDK`, which throws inside a
WinRT module initializer when loaded outside a real app launch). That specific blocker is gone now
that the solution is WPF — `AaronOS.Core` has zero UI-framework dependency, so `dotnet ef` should
work normally against it. Real migrations haven't been added back yet simply because it wasn't
done as part of the WPF migration; it's a reasonable near-term follow-up, not a hard problem
anymore.

**Consequence while this stands:** `EnsureCreatedAsync()` creates the full current schema from
`OnModelCreating` on first launch, but it does **not** support evolving an existing database's
schema — if you change an entity later, `EnsureCreatedAsync()` won't alter the existing tables.
Evolving the schema during development means deleting the local db file
(`%LocalAppData%\AaronOS\aaronos.db`) and letting it get recreated. That's fine while iterating,
but not once real check-in history exists that you don't want to lose.

## Database access

Inject `IDbContextFactory<AaronOsDbContext>` into your ViewModels (registered once by the shell
via `AddDbContextFactory`), not `AaronOsDbContext` itself — a desktop app's ViewModels live far
longer than a single unit of work, so each operation should create its own short-lived context:

```csharp
await using var db = await _dbContextFactory.CreateDbContextAsync();
```

## MVVM conventions

- One ViewModel per Page. ViewModel constructors take their dependencies (DbContext factory,
  other services) via DI. Because a module can't reference `AaronOS.App` (that would be
  circular), resolve the ViewModel in the Page's constructor via
  `AaronOS.Core.AppServices.Provider.GetRequiredService<T>()` — the shell sets
  `AppServices.Provider` once at startup. Then set `DataContext = ViewModel;` explicitly (WPF's
  classic `{Binding}` resolves against `DataContext`, unlike WinUI's compiled `x:Bind`).
- Use `CommunityToolkit.Mvvm`'s `[ObservableProperty]` (field-backed, see gotcha above) and
  `[RelayCommand]`. No code-behind logic beyond wiring the Page to its ViewModel and handling
  purely visual concerns (animations, focus).
- Derive every ViewModel from `AaronOS.Core.ViewModelBase`, which carries `IsBusy` — set it around
  async DB work so pages can show a loading state consistently.
- Since each `Page` is freshly constructed on every navigation (never reused), do "on navigated to"
  work in the constructor via the `Loaded` event, rather than overriding any navigation-lifecycle
  method:
  ```csharp
  public DashboardPage()
  {
      ViewModel = AppServices.Provider.GetRequiredService<DashboardViewModel>();
      DataContext = ViewModel;
      InitializeComponent();
      Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
  }
  ```
- For a `ListView`/`ItemsControl` item's Delete/Action buttons, use a plain `Click` handler in
  code-behind reading the item off `DataContext`, rather than binding `Command` back to a named
  element outside the `DataTemplate`:
  ```csharp
  private void DeleteButton_Click(object sender, RoutedEventArgs e)
  {
      if (sender is FrameworkElement { DataContext: MyEntity item })
      {
          _ = ViewModel.DeleteCommand.ExecuteAsync(item);
      }
  }
  ```
  (WPF's classic binding model *can* reach an outer `DataContext` from inside a template via
  `RelativeSource AncestorType`, unlike WinUI's compiled `x:Bind`, so this is now a style choice
  rather than a forced workaround — but it's what the existing pages do, so match it for
  consistency unless you have a specific reason not to.)

## Units convention

Imperial only for now — inches and pounds, `decimal` types. There is no unit-toggle
infrastructure; don't build one speculatively. If a future module genuinely needs metric, that's
the point to add a shared conversion/preference service in `AaronOS.Core`, not before.

## Navigation

Each module owns only its own pages. The shell's `ui:NavigationView` gets exactly one top-level
item per registered `IAppModule` (`DisplayName` + `IconGlyph`, built dynamically in
`MainWindow.xaml.cs`), with `TargetPageType` set to `HomePageType` — the shell never knows about
a module's other pages. If your module has more than one page, follow the pattern in
`AaronOS.Modules.BodyMeasurements`: make `HomePageType` a small shell page of your own
(`BodyMeasurementsShellPage`) containing a row of buttons and an internal `Frame`, and have those
buttons navigate the internal Frame between your module's real pages (Dashboard, Check-In, etc.)
via `Frame.Navigate(new SomePage())` — note WPF's `Frame.Navigate` takes an *instance*, not a
`Type`, unlike `ui:NavigationView.Navigate`/WinUI's `Frame.Navigate`. This keeps a module's
internal navigation entirely self-contained.

## WPF-specific control notes

A few stock WPF gaps that `Wpf.Ui.Controls` fills, used throughout this codebase:

- `ui:NumberBox` — numeric input; bind its `Value` (a `double`) two-way. The existing ViewModels
  use `double.NaN` as the "not entered" sentinel (a cleared `NumberBox` reports `NaN`, not
  `null`), converting to `decimal?` at save time — keep that pattern rather than introducing a
  value converter.
- `ui:TextBox` — adds `PlaceholderText`, which stock WPF's `TextBox` lacks.
- `ui:Button` with `Appearance="Primary"` — the accent-styled button, replacing WinUI's
  `AccentButtonStyle`.
- `ui:Card` — a simple elevated surface, useful anywhere you'd have reached for a `Border` with a
  theme background brush.
- Stock WPF's `DatePicker.SelectedDate` is `DateTime?` (not `DateTimeOffset` like WinUI's
  `DatePicker.Date`) — ViewModel date properties are typed accordingly.
- Stock WPF's `Grid`/`StackPanel` have no `ColumnSpacing`/`RowSpacing`/`Spacing`/`Padding`
  properties (WinUI-only conveniences) — use explicit `Margin` on children, or wrap a `Grid` in a
  `Border` when you need padding.

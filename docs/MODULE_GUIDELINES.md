# AaronOS Module Guidelines

AaronOS is built to take an open-ended series of feature modules. This document is the contract
every module follows so they compose cleanly under one shell, one database, and one navigation
pane. Read this before adding a new module.

## Architecture in one paragraph

Modules are compiled-in class library projects, not runtime-loaded plugins. The shell
(`AaronOS.App`) holds an explicit list of module instances built at startup. Adding a module
means: new project, one project reference from `AaronOS.App`, one line registering it in the
composition root. No reflection scanning, no dynamic assembly loading.

## Adding a new module: checklist

1. Create a new WinUI class library project named `AaronOS.Modules.<Name>`:
   ```
   dotnet new winui-lib -n AaronOS.Modules.<Name> -tfm net8.0
   ```
2. Add project references: `AaronOS.Core`. Add package references your module actually needs
   (CommunityToolkit.Mvvm and EF Core come transitively through Core).
3. Copy the property block below into the new csproj (see "Required csproj properties").
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
    IconElement Icon { get; }
    Type HomePageType { get; }
    void RegisterServices(IServiceCollection services);
}
```

- `Id`: a short, stable, unique slug (e.g. `"body-measurements"`). Never reuse another module's id.
- `DisplayName` / `Icon`: what shows up in the shell's `NavigationView`.
- `HomePageType`: the `Page` type the shell navigates to when your nav item is selected.
- `RegisterServices`: register your ViewModels and any module-specific services into the shared
  `IServiceCollection`. Register ViewModels as **transient** (a fresh instance per navigation),
  not singleton — pages new one up each time they're navigated to.

A minimal module looks like:

```csharp
public class WidgetsModule : IAppModule
{
    public string Id => "widgets";
    public string DisplayName => "Widgets";
    public IconElement Icon => new FontIcon { Glyph = "" };
    public Type HomePageType => typeof(WidgetsDashboardPage);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<WidgetsDashboardViewModel>();
    }
}
```

## Required csproj properties

Every module project needs these three settings, in addition to the standard `winui-lib`
template output. Without them the project either won't compile against WinUI types standalone,
or the CommunityToolkit.Mvvm source generator produces incomplete code:

```xml
<LangVersion>13.0</LangVersion>
<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
<Nullable>enable</Nullable>
```
(the `winui-lib` template doesn't turn on nullable reference types by default; every other project in the solution does, so match it.)
plus a direct `PackageReference` to `Microsoft.WindowsAppSDK` (same version as the other
projects) — the `winui-lib` template sets `WinUISDKReferences=false`, which assumes the app
project supplies the WinUI reference, but each project still compiles independently, so it needs
its own reference to resolve `Microsoft.UI.Xaml.*` types.

**Known gotcha:** even with `EnforceExtendedAnalyzerRules` set, the CommunityToolkit.Mvvm
partial-property generator (`public partial bool X { get; set; }`) does not reliably run in
these WinUI/CsWinRT projects. Use the classic field-backed form instead —
`[ObservableProperty] private bool _x;` — and ignore the `MVVMTK0045` AOT-compatibility warning
it produces; this app is never published as Native AOT.

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

## Schema creation (no EF migrations, deliberately)

App startup calls `Database.EnsureCreatedAsync()`, not `Database.MigrateAsync()`, and there are
no `dotnet ef migrations` in this repo.

**Why:** `dotnet-ef` design-time tooling loads the DbContext's assembly (and the startup
project's assembly) into its own process to construct a `DbContext` instance. Any assembly that
references `Microsoft.WindowsAppSDK` — which is every project in this solution, since `IAppModule`
and every module's Pages need WinUI types — throws inside a WinRT/CsWinRT module initializer when
loaded outside a real packaged/unpackaged app launch. `dotnet ef migrations add` fails with
`"The type initializer for '<Module>' threw an exception"` no matter which project it targets, and
a design-time `IDesignTimeDbContextFactory` doesn't help, because the crash happens on assembly
load, before the factory ever runs.

**Consequence:** `EnsureCreatedAsync()` creates the full current schema from
`OnModelCreating` on first launch, but it does **not** support evolving an existing database's
schema — if you change an entity later, `EnsureCreatedAsync()` won't alter the existing tables.
For now, evolving the schema during development means deleting the local db file
(`%LocalAppData%\AaronOS\aaronos.db`) and letting it get recreated. That's an acceptable cost
while iterating on the schema, but not once real check-in history exists that you don't want to
lose.

**Upgrade path, if you need real migrations later:** move `AaronOsDbContext` and every entity out
of the WinUI-dependent projects into a new plain class library (`net8.0`, no `UseWinUI`, no
`Microsoft.WindowsAppSDK` reference) that only the WinUI projects reference. `dotnet ef` can load
a plain assembly like that without hitting the WinRT initializer problem, and `IAppModule.Icon`
would need to become a glyph string (or similar WinUI-free representation) rather than
`IconElement` so modules stay WinUI-free at the point EF needs to enumerate their entity
assemblies. This is a real restructuring, not a quick flag — don't attempt it speculatively;
do it when migrations actually become necessary.

## Database access

Inject `IDbContextFactory<AaronOsDbContext>` into your ViewModels (registered once by the shell
via `AddDbContextFactory`), not `AaronOsDbContext` itself — a desktop app's ViewModels live far
longer than a single unit of work, so each operation should create its own short-lived context:

```csharp
await using var db = await _dbContextFactory.CreateDbContextAsync();
```

## MVVM conventions

- One ViewModel per Page. ViewModel constructors take their dependencies (DbContext factory,
  other services) via DI. Because `Frame.Navigate` requires a parameterless Page constructor,
  and a module can't reference `AaronOS.App` (that would be circular), resolve the ViewModel in
  the Page's constructor via `AaronOS.Core.AppServices.Provider.GetRequiredService<T>()` —
  the shell sets `AppServices.Provider` once at startup.
- Use `CommunityToolkit.Mvvm`'s `[ObservableProperty]` (field-backed, see gotcha above) and
  `[RelayCommand]`. No code-behind logic beyond wiring the Page to its ViewModel and handling
  purely visual concerns (animations, focus).
- Derive every ViewModel from `AaronOS.Core.ViewModelBase`, which carries `IsBusy` — set it around
  async DB work so pages can show a loading state consistently.

**Known gotcha:** inside a `ListView`/`ItemsControl` `DataTemplate`, do not `x:Bind` a button's
`Command` back to a named element outside the template (e.g. `x:Bind Root.ViewModel.DeleteCommand`
on a Page named `x:Name="Root"`). In this WinAppSDK version it fails the XAML compiler with an
opaque `WMC9999 "Xaml Internal Error"` that gives no line number or real cause. Use a plain
`Click` handler in code-behind instead, reading the item off `DataContext`:

```csharp
private void DeleteButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: MyEntity item })
    {
        _ = ViewModel.DeleteCommand.ExecuteAsync(item);
    }
}
```

If you ever hit `WMC9999` again, don't chase the message — it's misleading. Bisect by swapping
suspect XAML files for a trivial stub one at a time until the build succeeds, then reintroduce
the removed markup piece by piece to find the real cause.

## Units convention

Imperial only for now — inches and pounds, `decimal` types. There is no unit-toggle
infrastructure; don't build one speculatively. If a future module genuinely needs metric, that's
the point to add a shared conversion/preference service in `AaronOS.Core`, not before.

## Navigation

Each module owns only its own pages. The shell's `NavigationView` gets exactly one top-level item
per registered `IAppModule` (`DisplayName` + `Icon`), pointing at `HomePageType` — the shell never
knows about a module's other pages. If your module has more than one page, follow the pattern in
`AaronOS.Modules.BodyMeasurements`: make `HomePageType` a small shell page of your own
(`BodyMeasurementsShellPage`) containing a `CommandBar` and an internal `Frame`, and have that
page's buttons navigate the internal Frame between your module's real pages (Dashboard, Check-In,
etc.). This keeps a module's internal navigation entirely self-contained.

# Schedule Module — Plan 3: Notifications

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fire native Windows notifications for overdue routines and a nightly wind-down reminder, driven by a single one-minute timer that runs while the app is open.

**Architecture:** The decision of *what* to notify about is a pure function (`NotificationPlanner`) over the current suggestions, the clock, and a record of what has already been sent today — so the deduplication rules are testable without a timer or a tray icon. Delivery sits behind a one-method `INotificationSink`, implemented by `TrayNotificationSink` as a thin wrapper over `System.Windows.Forms.NotifyIcon`. A `PeriodicTimer` loop (`ScheduleBackgroundWorker`) ticks once a minute. The spec calls this piece `NotificationService`; splitting it into an interface plus one implementation is what lets the tick loop avoid a Windows Forms dependency and lets a future toast implementation drop in.

**Tech Stack:** Unchanged, plus `System.Windows.Forms.NotifyIcon` from the .NET Windows Desktop SDK and `System.Drawing.Icon`. **Task 3 adds `<UseWindowsForms>true</UseWindowsForms>` to the module csproj** — Plan 1 deliberately does not set it, because nothing before this task uses Windows Forms and no sibling module sets it.

**Spec:** `docs/superpowers/specs/2026-07-27-schedule-module-design.md` — this plan covers phase 6.

**Prerequisite:** Plans 1 and 2 complete — `SuggestionEngine`, `SleepPlanner`, `RoutineScheduler`, and `AgendaBuilder` all exist, and 58 tests pass.

## Global Constraints

- Target framework `net8.0-windows`; `UseWPF` true; `LangVersion` `13.0`; `Nullable` `enable`. **`UseWindowsForms` is not set on the module yet — Task 3 adds it**, since this is the first task that uses Windows Forms.
- **Never use the partial-property `[ObservableProperty]` form.**
- Pure services take the current time as a parameter. **`NotificationPlanner` must never read `DateTime.Now`** — the tick loop passes it in. Every dedup rule depends on being testable at an arbitrary instant.
- `NotifyIcon` is a Windows Forms component. It must be created and mutated on the UI thread; a call from the timer's thread pool context throws. Marshal via `System.Windows.Application.Current.Dispatcher`.
- Notifications require the app to be running. **Do not** add a Windows Scheduled Task, a service, or any out-of-process mechanism — that is explicitly out of scope in the spec.
- The tray icon must disappear when the app exits. A leaked `NotifyIcon` leaves a ghost icon in the notification area until the user hovers it, which looks like a crash.
- Run tests with `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`.

## Known ceiling of this approach

Recorded here so the next person does not rediscover it. `NotifyIcon.ShowBalloonTip` gives title-and-text only: **no action buttons, no click-to-navigate, no inline reply**, and the tray icon must exist for a notification to appear. In exchange it needs no NuGet package, no Start-menu shortcut carrying an AUMID, and no COM activator class — which is the entire setup burden of the proper toast API. If actionable toasts later become worth that cost, `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 is the replacement and it swaps in behind `INotificationSink` without touching any caller.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/AaronOS.Core/IAppModule.cs` | Gains an optional `StartAsync` hook (see Task 1) |
| `src/AaronOS.App/App.xaml.cs` | Calls `StartAsync` on each module; disposes the host on exit |
| `src/AaronOS.Modules.Schedule/Notifications/PendingNotification.cs` | One notification to deliver, plus its dedup key |
| `src/AaronOS.Modules.Schedule/Notifications/NotificationPlanner.cs` | Pure decision: what to send now, given what was already sent |
| `src/AaronOS.Modules.Schedule/Notifications/INotificationSink.cs` | The one-method delivery interface |
| `src/AaronOS.Modules.Schedule/Notifications/TrayNotificationSink.cs` | `NotifyIcon` implementation |
| `src/AaronOS.Modules.Schedule/Notifications/ScheduleBackgroundWorker.cs` | One-minute `PeriodicTimer` loop |
| `src/AaronOS.Modules.Schedule.Tests/NotificationPlannerTests.cs` | Dedup and trigger-timing tests |

---

## Task 1: A startup hook on the module contract

**Files:**
- Modify: `src/AaronOS.Core/IAppModule.cs`
- Modify: `src/AaronOS.App/App.xaml.cs`
- Modify: `docs/MODULE_GUIDELINES.md`

**Interfaces:**
- Produces: `Task IAppModule.StartAsync(IServiceProvider services, CancellationToken cancellationToken)` with a default no-op implementation.

**Why this is the right place.** A module that needs background work has nowhere to start it today: `RegisterServices` only populates the container, and a singleton that starts a timer in its own constructor is both untestable and dependent on somebody happening to resolve it. The shell must not reach into a module's internals to start something (`MODULE_GUIDELINES.md`), so the hook belongs on the contract — exactly the reasoning that put `SettingsContentType` there. A default implementation keeps it non-breaking for the four existing modules.

- [ ] **Step 1: Add the contract member**

In `src/AaronOS.Core/IAppModule.cs`, add after `SettingsContentType`:

```csharp
    /// <summary>
    /// Optional: start any long-running background work this module owns (a timer, a poller).
    /// Called once at startup, after every module's <see cref="RegisterServices"/> has run and the
    /// database schema is ready, so an implementation may resolve its own services and query.
    ///
    /// A default no-op implementation keeps this non-breaking for modules with no background work.
    /// It exists as a contract member rather than the shell starting a module's service directly,
    /// because the shell must not reach into a module's internals (see docs/MODULE_GUIDELINES.md).
    ///
    /// Implementations must return promptly: start the work and return, do not await it to
    /// completion, or startup will hang.
    /// </summary>
    Task StartAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;
```

- [ ] **Step 2: Call it from the composition root**

In `src/AaronOS.App/App.xaml.cs`, inside `OnStartup`, after the `SchemaBootstrapper.EnsureSchemaAsync(db)` block and before the window is created:

```csharp
        foreach (var module in Services.GetServices<IAppModule>())
        {
            await module.StartAsync(Services, CancellationToken.None);
        }
```

Add an `OnExit` override so singletons — including anything holding an unmanaged handle like a tray icon — are disposed rather than leaked:

```csharp
    protected override void OnExit(ExitEventArgs e)
    {
        // Disposes every singleton the container owns. Without this a tray icon survives as a
        // ghost in the notification area until the user hovers it, which reads as a crash.
        _host.Dispose();
        base.OnExit(e);
    }
```

Add `using System.Threading;` if it is not already present.

- [ ] **Step 3: Document it**

In `docs/MODULE_GUIDELINES.md`, in the "The module contract" section, add to the bullet list describing each member:

```markdown
- `StartAsync`: optional. Start background work the module owns (a timer, a poller) and return
  promptly — do not await it to completion or startup will hang. Called once after every module's
  `RegisterServices` has run and the schema is ready, so you may resolve your own services and
  query the database. Defaults to a no-op, so a module with no background work ignores it.
```

Also add the interface member to the code block showing `IAppModule` in that document, so the contract shown there matches the real one.

- [ ] **Step 4: Verify nothing broke**

Run: `dotnet build AaronOS.slnx --nologo`
Expected: `Build succeeded`. The four existing modules do not implement `StartAsync` and must still compile — that is what the default implementation is for.

Run: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`
Expected: the app launches as before and every module's nav item still works. Close it and confirm the process exits (Task Manager shows no lingering `AaronOS.App`); `OnExit` disposing the host must not deadlock.

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Core/IAppModule.cs src/AaronOS.App/App.xaml.cs docs/MODULE_GUIDELINES.md
git commit -m "Add optional StartAsync hook to the module contract"
```

---

## Task 2: NotificationPlanner

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Notifications/PendingNotification.cs`
- Create: `src/AaronOS.Modules.Schedule/Notifications/NotificationPlanner.cs`
- Test: `src/AaronOS.Modules.Schedule.Tests/NotificationPlannerTests.cs`

**Interfaces:**
- Consumes: `Suggestion`, `SuggestionKind`, `SuggestionUrgency` from Plan 2.
- Produces:
  - `record PendingNotification(string DedupKey, string Title, string Message)`
  - `static IReadOnlyList<PendingNotification> NotificationPlanner.Decide(IReadOnlyList<Suggestion> suggestions, DateTime? recommendedBedtime, int windDownLeadMinutes, DateTime now, ISet<string> alreadySent)`

  `Decide` does not mutate `alreadySent`; the caller records what it actually delivered, so a delivery failure does not permanently suppress a notification.

- [ ] **Step 1: Write the failing tests**

Create `src/AaronOS.Modules.Schedule.Tests/NotificationPlannerTests.cs`:

```csharp
using AaronOS.Modules.Schedule.Notifications;
using AaronOS.Modules.Schedule.Suggestions;

namespace AaronOS.Modules.Schedule.Tests;

public class NotificationPlannerTests
{
    private static readonly DateTime Evening = new(2026, 7, 6, 22, 40, 0);

    private static Suggestion Routine(string title, SuggestionUrgency urgency, int id) =>
        new(SuggestionKind.Routine, title, urgency == SuggestionUrgency.Overdue ? "Overdue by 2 days" : "Due today",
            urgency, SuggestedStart: null, EstimatedMinutes: null, SourceId: id);

    private static Suggestion Release(string title, int id) =>
        new(SuggestionKind.Release, title, "Out soon", SuggestionUrgency.Informational, null, null, id);

    [Fact]
    public void OverdueRoutine_ProducesANotification()
    {
        var pending = NotificationPlanner.Decide(
            [Routine("Scoop litter box", SuggestionUrgency.Overdue, 1)],
            recommendedBedtime: null, windDownLeadMinutes: 30, Evening, new HashSet<string>());

        var only = Assert.Single(pending);
        Assert.Contains("Scoop litter box", only.Title);
        Assert.Contains("Overdue by 2 days", only.Message);
        Assert.Equal("routine-1-2026-07-06", only.DedupKey);
    }

    [Fact]
    public void MerelyDueRoutine_DoesNotNotify()
    {
        // Only overdue work is worth interrupting for; "due today" is what the Today panel is for.
        var pending = NotificationPlanner.Decide(
            [Routine("Vacuum", SuggestionUrgency.Due, 1)],
            null, 30, Evening, new HashSet<string>());

        Assert.Empty(pending);
    }

    [Fact]
    public void InformationalSuggestions_NeverNotify()
    {
        var pending = NotificationPlanner.Decide(
            [Release("Some Game", 1)],
            null, 30, Evening, new HashSet<string>());

        Assert.Empty(pending);
    }

    [Fact]
    public void AlreadySentKey_SuppressesTheNotification()
    {
        var sent = new HashSet<string> { "routine-1-2026-07-06" };

        var pending = NotificationPlanner.Decide(
            [Routine("Scoop litter box", SuggestionUrgency.Overdue, 1)],
            null, 30, Evening, sent);

        Assert.Empty(pending);
    }

    [Fact]
    public void DedupKeyIsPerDay_SoTomorrowNotifiesAgain()
    {
        var sent = new HashSet<string> { "routine-1-2026-07-06" };
        var tomorrow = Evening.AddDays(1);

        var pending = NotificationPlanner.Decide(
            [Routine("Scoop litter box", SuggestionUrgency.Overdue, 1)],
            null, 30, tomorrow, sent);

        Assert.Equal("routine-1-2026-07-07", Assert.Single(pending).DedupKey);
    }

    [Fact]
    public void Decide_DoesNotMutateTheSentSet()
    {
        var sent = new HashSet<string>();

        NotificationPlanner.Decide(
            [Routine("Scoop litter box", SuggestionUrgency.Overdue, 1)],
            null, 30, Evening, sent);

        // The caller records what it actually delivered; a failed delivery must not be
        // permanently suppressed by the planner having pre-marked it.
        Assert.Empty(sent);
    }

    [Fact]
    public void WindDown_FiresInsideTheLeadWindow()
    {
        var bedtime = new DateTime(2026, 7, 6, 23, 0, 0);

        // 22:40 is 20 minutes out, inside a 30-minute lead.
        var pending = NotificationPlanner.Decide([], bedtime, 30, Evening, new HashSet<string>());

        var only = Assert.Single(pending);
        Assert.Equal("winddown-2026-07-06", only.DedupKey);
        Assert.Contains("11:00 PM", only.Message);
    }

    [Fact]
    public void WindDown_DoesNotFireBeforeTheLeadWindow()
    {
        var bedtime = new DateTime(2026, 7, 6, 23, 0, 0);
        var tooEarly = new DateTime(2026, 7, 6, 22, 10, 0); // 50 minutes out

        Assert.Empty(NotificationPlanner.Decide([], bedtime, 30, tooEarly, new HashSet<string>()));
    }

    [Fact]
    public void WindDown_StillFiresAfterBedtimeHasPassed()
    {
        var bedtime = new DateTime(2026, 7, 6, 23, 0, 0);
        var late = new DateTime(2026, 7, 6, 23, 20, 0);

        // Being late is exactly when the reminder is useful; suppressing it once the moment has
        // passed would mean the one night you most need it is the night it stays silent.
        Assert.Single(NotificationPlanner.Decide([], bedtime, 30, late, new HashSet<string>()));
    }

    [Fact]
    public void WindDown_UsesTheBedtimeDateForItsKey_NotTheCurrentDate()
    {
        // Bedtime after midnight: at 00:10 on the 7th for a 00:30 bedtime on the 7th, the key
        // must be the 7th — and must not fire a second time for the same bedtime.
        var bedtime = new DateTime(2026, 7, 7, 0, 30, 0);
        var now = new DateTime(2026, 7, 7, 0, 10, 0);

        var pending = NotificationPlanner.Decide([], bedtime, 30, now, new HashSet<string>());

        Assert.Equal("winddown-2026-07-07", Assert.Single(pending).DedupKey);
    }

    [Fact]
    public void NoBedtime_MeansNoWindDown()
    {
        Assert.Empty(NotificationPlanner.Decide([], null, 30, Evening, new HashSet<string>()));
    }

    [Fact]
    public void OverdueRoutinesAndWindDown_CanFireTogether()
    {
        var bedtime = new DateTime(2026, 7, 6, 23, 0, 0);

        var pending = NotificationPlanner.Decide(
            [Routine("Scoop litter box", SuggestionUrgency.Overdue, 1), Routine("Trash", SuggestionUrgency.Overdue, 2)],
            bedtime, 30, Evening, new HashSet<string>());

        Assert.Equal(3, pending.Count);
        Assert.Equal("winddown-2026-07-06", pending[^1].DedupKey); // wind-down last
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `CS0246` for `NotificationPlanner` and `PendingNotification`.

- [ ] **Step 3: Write the implementation**

`src/AaronOS.Modules.Schedule/Notifications/PendingNotification.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Notifications;

/// <param name="DedupKey">Stable for the thing-and-day this notification is about, so a
/// once-a-minute tick delivers it once rather than sixty times.</param>
public sealed record PendingNotification(string DedupKey, string Title, string Message);
```

`src/AaronOS.Modules.Schedule/Notifications/NotificationPlanner.cs`:

```csharp
using AaronOS.Modules.Schedule.Suggestions;

namespace AaronOS.Modules.Schedule.Notifications;

/// <summary>
/// Decides what is worth interrupting the user for. Pure: takes the current instant and the set of
/// keys already delivered as parameters, so the timing and dedup rules are testable at any instant
/// without a timer (see NotificationPlannerTests).
///
/// Deliberately conservative — only overdue routines and the nightly wind-down. Everything else the
/// suggestion engine produces belongs on the Today panel, where it costs nothing to ignore.
/// </summary>
public static class NotificationPlanner
{
    public static IReadOnlyList<PendingNotification> Decide(
        IReadOnlyList<Suggestion> suggestions,
        DateTime? recommendedBedtime,
        int windDownLeadMinutes,
        DateTime now,
        ISet<string> alreadySent)
    {
        var pending = new List<PendingNotification>();
        var today = DateOnly.FromDateTime(now);

        foreach (var suggestion in suggestions)
        {
            if (suggestion.Kind != SuggestionKind.Routine) continue;
            if (suggestion.Urgency != SuggestionUrgency.Overdue) continue;
            if (suggestion.SourceId is not { } id) continue;

            var key = $"routine-{id}-{today:yyyy-MM-dd}";
            if (alreadySent.Contains(key)) continue;

            pending.Add(new PendingNotification(key, $"Overdue: {suggestion.Title}", suggestion.Reason));
        }

        if (recommendedBedtime is { } bedtime)
        {
            // Keyed on the bedtime's own date, not today's: a 00:30 bedtime belongs to the 7th
            // even though the reminder fires while it is still the 6th, and keying on `now` would
            // fire it twice across the midnight boundary.
            var key = $"winddown-{DateOnly.FromDateTime(bedtime):yyyy-MM-dd}";

            var leadStart = bedtime.AddMinutes(-windDownLeadMinutes);
            // No upper bound: being past bedtime is exactly when the reminder earns its keep.
            if (now >= leadStart && !alreadySent.Contains(key))
            {
                pending.Add(new PendingNotification(
                    key,
                    "Wind down",
                    $"Aim to be in bed by {bedtime:h:mm tt}."));
            }
        }

        return pending;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 70`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule/Notifications src/AaronOS.Modules.Schedule.Tests/NotificationPlannerTests.cs
git commit -m "Add NotificationPlanner deciding what to notify about"
```

---

## Task 3: Tray notification sink

**Files:**
- Modify: `src/AaronOS.Modules.Schedule/AaronOS.Modules.Schedule.csproj` (add `UseWindowsForms`)
- Create: `src/AaronOS.Modules.Schedule/Notifications/INotificationSink.cs`
- Create: `src/AaronOS.Modules.Schedule/Notifications/TrayNotificationSink.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Produces: `INotificationSink` with `void Show(PendingNotification notification)`, and `TrayNotificationSink : INotificationSink, IDisposable`.

- [ ] **Step 1: Write the interface and implementation**

No unit test: this is a wrapper over an OS handle with nothing to assert that would not be asserting `NotifyIcon` itself. The interface exists so `ScheduleBackgroundWorker` (Task 4) is not welded to Windows Forms, and so a future toast implementation can replace it. Verification is manual, in Task 5.

`src/AaronOS.Modules.Schedule/Notifications/INotificationSink.cs`:

```csharp
namespace AaronOS.Modules.Schedule.Notifications;

/// <summary>
/// Delivery, separated from the decision of what to deliver. One method, so swapping the tray
/// implementation for a real toast API later (Microsoft.Toolkit.Uwp.Notifications, which needs an
/// AUMID shortcut and a COM activator) touches nothing but the registration.
/// </summary>
public interface INotificationSink
{
    void Show(PendingNotification notification);
}
```

`src/AaronOS.Modules.Schedule/Notifications/TrayNotificationSink.cs`:

```csharp
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace AaronOS.Modules.Schedule.Notifications;

/// <summary>
/// Windows notifications via a tray icon balloon tip. Windows 10/11 routes balloon tips through
/// the Action Center, so these appear as ordinary toasts — with no NuGet package, no Start-menu
/// shortcut carrying an AUMID, and no COM activator class.
///
/// The ceiling: title and text only, no action buttons, no click-to-navigate, and the tray icon
/// must exist for anything to appear. See this plan's "Known ceiling" note for the upgrade path.
/// </summary>
public sealed class TrayNotificationSink : INotificationSink, IDisposable
{
    private NotifyIcon? _icon;
    private bool _disposed;

    public void Show(PendingNotification notification)
    {
        if (_disposed) return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return; // no UI (a test host); nothing to show a balloon on

        // NotifyIcon is a Windows Forms component: creating or mutating it off the UI thread
        // throws, and the timer tick arrives on a thread pool thread.
        dispatcher.Invoke(() =>
        {
            EnsureIcon();
            _icon!.BalloonTipTitle = notification.Title;
            _icon.BalloonTipText = notification.Message;
            _icon.ShowBalloonTip(10_000);
        });
    }

    private void EnsureIcon()
    {
        if (_icon is not null) return;

        _icon = new NotifyIcon
        {
            // Reuse the app's own icon so the notification is recognisably from AaronOS.
            // ExtractAssociatedIcon can return null for an unusual host, hence the fallback.
            Icon = TryLoadAppIcon() ?? SystemIcons.Information,
            Text = "AaronOS",
            Visible = true,
        };
    }

    private static Icon? TryLoadAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            return path is null ? null : Icon.ExtractAssociatedIcon(path);
        }
        catch (Exception ex)
        {
            // A missing or unreadable icon must not take down notifications, let alone the app.
            Debug.WriteLine($"TrayNotificationSink: could not load app icon: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Hide before disposing: disposing a visible NotifyIcon leaves a ghost in the
        // notification area until the user hovers over it.
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
```

The icon is created lazily on first notification rather than at startup, so a session with nothing to report never puts an icon in the tray at all.

- [ ] **Step 2: Register it**

In `ScheduleModule.RegisterServices`:

```csharp
        services.AddSingleton<INotificationSink, TrayNotificationSink>();
```

with `using AaronOS.Modules.Schedule.Notifications;` at the top. Registering the interface (not the concrete type) is what lets `_host.Dispose()` in `App.OnExit` dispose it — the container disposes singletons it created.

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build AaronOS.slnx --nologo`
Expected: `Build succeeded`.

**This task owns the `UseWindowsForms` change**, so make it before writing the sink — otherwise the code above fails with `CS0234: The type or namespace name 'Forms' does not exist in the namespace 'System.Windows'`. Add the property to the module csproj's existing `PropertyGroup`, directly after `<UseWPF>true</UseWPF>`:

```xml
    <UseWindowsForms>true</UseWindowsForms>
```

Plan 1 deliberately left it out: no sibling module sets it, it is not in `docs/MODULE_GUIDELINES.md`'s required-properties list, and nothing before this task uses Windows Forms — carrying it earlier was speculative configuration a review correctly rejected.

The `using Application = System.Windows.Application;` alias is load-bearing: `System.Windows.Forms` also defines `Application`, and with both namespaces imported the bare name is ambiguous (`CS0104`).

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 70`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add tray-icon notification sink"
```

---

## Task 4: The one-minute tick

**Files:**
- Create: `src/AaronOS.Modules.Schedule/Notifications/ScheduleBackgroundWorker.cs`
- Modify: `src/AaronOS.Modules.Schedule/ScheduleModule.cs`

**Interfaces:**
- Consumes: `NotificationPlanner.Decide`, `INotificationSink`, `SuggestionEngine.Build`, `SleepPlanner.RecommendedBedtime`, `RoutineScheduler.EvaluateAll`, `AgendaBuilder.Build`.
- Produces: `ScheduleBackgroundWorker` with `void Start(CancellationToken)` and `IDisposable`. `ScheduleModule.StartAsync` calls `Start`.

- [ ] **Step 1: Write the worker**

The gather-then-build sequence duplicates `TodayViewModel.LoadAsync`. That duplication is deliberate rather than extracted: the ViewModel populates observable collections for binding and this returns a value, and a shared "load everything" service would have to serve both shapes for no real gain. If a third caller appears, extract it then.

`src/AaronOS.Modules.Schedule/Notifications/ScheduleBackgroundWorker.cs`:

```csharp
using System.Diagnostics;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;
using AaronOS.Modules.Schedule.Sleep;
using AaronOS.Modules.Schedule.Suggestions;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.Notifications;

/// <summary>
/// One timer for the whole module. Ticks every minute while the app is open and delivers whatever
/// <see cref="NotificationPlanner"/> says is due.
///
/// One PeriodicTimer, not a scheduling framework: the work is "check a handful of rows once a
/// minute", and Quartz or Hangfire would be more configuration than logic.
/// </summary>
public sealed class ScheduleBackgroundWorker(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    INotificationSink sink) : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    /// <summary>Keys delivered this session. In-memory only: a restart may re-notify about an
    /// overdue chore, which is a far better failure than a persisted flag suppressing a
    /// notification the user never actually saw.</summary>
    private readonly HashSet<string> _sent = [];

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public void Start(CancellationToken cancellationToken)
    {
        if (_loop is not null) return; // idempotent — a second Start is a no-op, not a second timer

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        // Fire once immediately so an overdue chore doesn't wait a minute after launch.
        await TickSafelyAsync(cancellationToken);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await TickSafelyAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// A failed tick must never kill the loop or surface a dialog: this runs unattended once a
    /// minute, and an exception escaping here would either take down the timer for the rest of the
    /// session or pop the app's global error dialog over whatever the user was doing.
    /// </summary>
    private async Task TickSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var notification in await PlanAsync(cancellationToken))
            {
                sink.Show(notification);
                // Recorded only after delivery is attempted, so a throwing sink doesn't
                // permanently suppress the notification.
                _sent.Add(notification.DedupKey);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ScheduleBackgroundWorker tick failed: {ex}");
        }
    }

    private async Task<IReadOnlyList<PendingNotification>> PlanAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var tomorrowDate = today.AddDays(1);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var blocks = await db.Set<ScheduleBlock>().Where(b => b.IsActive).ToListAsync(cancellationToken);
        var exceptions = await db.Set<ScheduleException>()
            .Where(e => e.Date >= today.AddDays(-1) && e.Date <= tomorrowDate)
            .ToListAsync(cancellationToken);

        var agenda = AgendaBuilder.Build(today, tomorrowDate, blocks, exceptions, []);

        var routines = await db.Set<Routine>().Where(r => r.IsActive).ToListAsync(cancellationToken);
        var completions = await db.Set<RoutineCompletion>().ToListAsync(cancellationToken);
        var dueStates = RoutineScheduler.EvaluateAll(routines, completions, today);

        var settings = await db.Set<SleepSettings>().FirstOrDefaultAsync(cancellationToken) ?? SleepSettings.Default();
        var bedtime = SleepPlanner.RecommendedBedtime(today, agenda[1], settings);

        // Releases and milestones are not fetched: NotificationPlanner ignores informational
        // suggestions, so querying them would be work with no possible effect.
        var suggestions = SuggestionEngine.Build(new SuggestionInput(
            today, agenda[0], routines, dueStates, [], [], bedtime));

        return NotificationPlanner.Decide(suggestions, bedtime, settings.WindDownLeadMinutes, now, _sent);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }
}
```

`Dispose` cancels the loop without awaiting it. Awaiting a `PeriodicTimer` loop from `Dispose` during WPF shutdown risks blocking the dispatcher; the loop observes cancellation and unwinds on its own, and the process is exiting regardless.

- [ ] **Step 2: Register and start it**

In `ScheduleModule`:

```csharp
        services.AddSingleton<ScheduleBackgroundWorker>();
```

and implement the hook from Task 1:

```csharp
    public Task StartAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        services.GetRequiredService<ScheduleBackgroundWorker>().Start(cancellationToken);
        return Task.CompletedTask;
    }
```

`Start` returns immediately after launching the loop, satisfying the contract's requirement that `StartAsync` not await its work to completion.

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build AaronOS.slnx --nologo`
Expected: `Build succeeded`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo`
Expected: `Passed! - Failed: 0, Passed: 70`

- [ ] **Step 5: Commit**

```bash
git add src/AaronOS.Modules.Schedule
git commit -m "Add one-minute background tick delivering notifications"
```

---

## Task 5: End-to-end verification

**Files:** none — this task changes nothing. It exists because everything in Tasks 3 and 4 is unobservable from a unit test, and shipping it unverified would mean shipping it unknown.

- [ ] **Step 1: Verify an overdue-routine notification**

Set up the condition, then observe it.

1. Run the app: `dotnet run --project src/AaronOS.App/AaronOS.App.csproj`
2. Schedule → Routines. Add a routine `Notification test`, interval `1` day. It is immediately "due today" (never completed), which is **not** enough to notify.
3. Close the app.
4. Make it overdue by backdating a completion directly in the database. From a shell:

```bash
sqlite3 "$LOCALAPPDATA/AaronOS/aaronos.db" \
  "INSERT INTO RoutineCompletions (RoutineId, CompletedAt) VALUES ((SELECT Id FROM Routines WHERE Name='Notification test'), datetime('now','-5 days'));"
```

If `sqlite3` is not installed, use any SQLite browser against `%LocalAppData%\AaronOS\aaronos.db`, or temporarily set the routine's interval such that its last completion is already past due.

5. Run the app again. Within a few seconds of launch — the worker fires one tick immediately rather than waiting a minute — a Windows notification appears titled **"Overdue: Notification test"** with the body "Overdue by 4 days".

Expected: exactly **one** notification. Leave the app open for three minutes and confirm no repeat appears; the dedup key suppresses it for the rest of the day. If it repeats every minute, `_sent.Add` is not being reached — check that `sink.Show` is not throwing.

- [ ] **Step 2: Verify the tray icon lifecycle**

Expected, in order:
1. A tray icon appears in the notification area only when the first notification fires, not at launch.
2. Hovering it shows the tooltip "AaronOS".
3. Closing the app removes the icon immediately, with no ghost left behind.

A ghost icon means `App.OnExit` is not disposing the host, or the sink was registered as a concrete type rather than through `INotificationSink`.

- [ ] **Step 3: Verify the wind-down reminder**

The reminder fires inside the lead window before the recommended bedtime, which is normally late at night. Rather than waiting, move the window to now:

1. Schedule → Sleep. Note the recommended bedtime.
2. Set **Wind-down min** to a value large enough that the lead window already contains the current time — if bedtime is 11:00 PM and it is currently 3:00 PM, set it to `500` (8 hours 20 minutes). Save.
3. Within a minute, a notification appears titled **"Wind down"** reading "Aim to be in bed by 11:00 PM."
4. Set wind-down back to `30` and save.

Expected: one notification, no repeat. If none appears and the Sleep page reads "No commitments tomorrow", there is no bedtime to remind about — add a work block on tomorrow's weekday first.

- [ ] **Step 4: Verify a tick failure does not kill the loop**

This is the one behaviour most likely to break silently in future changes, so confirm it once now.

1. With the app open, delete the routine that was producing notifications.
2. Confirm the app keeps running and the Today page still loads — a tick that finds nothing to do must be a no-op, not an exception.
3. Open Visual Studio's Output window (or attach any debugger) and confirm no `ScheduleBackgroundWorker tick failed` lines are being written. If they are, read the exception; the tick swallowed it by design, which is correct behaviour but means the message is the only evidence.

- [ ] **Step 5: Clean up and commit**

Delete the `Notification test` routine from the Routines page. No code changed in this task, so there is nothing to commit unless Steps 1–4 surfaced a fix — in which case commit that fix with a message describing what the verification caught.

---

## Definition of done for Plan 3

- `dotnet build AaronOS.slnx --nologo` succeeds.
- `dotnet test src/AaronOS.Modules.Schedule.Tests/AaronOS.Modules.Schedule.Tests.csproj --nologo` reports 70 passing tests, 0 failing.
- An overdue routine produces exactly one Windows notification per day while the app is open.
- The wind-down reminder fires once inside its lead window.
- The tray icon appears on first notification and disappears cleanly on exit.
- The four pre-existing modules still compile and run unchanged against the extended `IAppModule`.

## Deferred to later plans

External calendars (Plan 4) and Gmail extraction (Plan 5). Also, deliberately not built here: notifications while the app is closed (out of scope per the spec — it needs a Scheduled Task), actionable toast buttons (needs the toolkit package plus an AUMID shortcut and COM activator), and persisting the sent-keys set across restarts.

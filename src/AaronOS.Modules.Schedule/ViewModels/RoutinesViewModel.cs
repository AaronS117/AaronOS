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

    // ui:NumberBox.Value is double? on the installed WPF-UI 4.3.0 (ValueProperty is registered as
    // typeof(double?), and clearing the box sets it to null, not NaN) — same convention already
    // used in AaronOS.Modules.Nutrition, and the rule in docs/MODULE_GUIDELINES.md. A
    // non-nullable double target here would silently fail to update on clear, since WPF drops a
    // null->double TwoWay conversion instead of throwing.
    [ObservableProperty]
    private double? _newIntervalDays = 2;

    [ObservableProperty]
    private double? _newEstimatedMinutes;

    // Weekday pinning, same seven-checkbox shape the Week page uses for a block's DaysOfWeek.
    // Ticking any day makes this a weekday routine and switches the interval box off, which is how
    // Routine's "exactly one of IntervalDays and PreferredDaysOfWeek" invariant is kept: the two
    // modes cannot both be filled in, so an invalid row can't be built here. Each day notifies
    // IsIntervalEnabled so the box greys out the moment a day is ticked.
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsIntervalEnabled))] private bool _newMonday;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsIntervalEnabled))] private bool _newTuesday;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsIntervalEnabled))] private bool _newWednesday;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsIntervalEnabled))] private bool _newThursday;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsIntervalEnabled))] private bool _newFriday;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsIntervalEnabled))] private bool _newSaturday;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsIntervalEnabled))] private bool _newSunday;

    public bool IsIntervalEnabled => SelectedDays() == DayOfWeekFlags.None;

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

        var days = SelectedDays();
        var interval = NewIntervalDays is > 0 ? (int)NewIntervalDays.Value : 0;

        // Exactly one mode, never both and never neither — RoutineScheduler.Evaluate throws on a
        // routine with neither set, and that throw propagates out of EvaluateAll and would fail the
        // whole page load rather than one row.
        if (days == DayOfWeekFlags.None && interval <= 0)
        {
            ValidationMessage = "Give the routine an interval of at least 1 day, or pick the days it falls on.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new Routine
        {
            Name = NewName.Trim(),
            Category = NewCategory,
            IntervalDays = days == DayOfWeekFlags.None ? interval : null,
            PreferredDaysOfWeek = days == DayOfWeekFlags.None ? null : days,
            EstimatedMinutes = NewEstimatedMinutes is { } minutes ? (int)minutes : null,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        // Clear the day ticks too — otherwise the next routine silently inherits them, and the
        // interval box stays disabled with no visible reason why.
        NewName = "";
        NewMonday = NewTuesday = NewWednesday = NewThursday = NewFriday = NewSaturday = NewSunday = false;
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

    [RelayCommand]
    private async Task DeleteRoutineAsync(RoutineRow row)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        // The row may already be gone (e.g. a second delete click); skip rather than throw.
        var routine = await db.Set<Routine>().FirstOrDefaultAsync(r => r.Id == row.Routine.Id);
        if (routine is null) return;

        db.Remove(routine);
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}

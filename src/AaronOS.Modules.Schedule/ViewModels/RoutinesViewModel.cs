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

        var interval = NewIntervalDays is > 0 ? (int)NewIntervalDays.Value : 0;
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
            EstimatedMinutes = NewEstimatedMinutes is { } minutes ? (int)minutes : null,
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

        // The row may already be gone (e.g. a second delete click); skip rather than throw.
        var routine = await db.Set<Routine>().FirstOrDefaultAsync(r => r.Id == row.Routine.Id);
        if (routine is null) return;

        db.Remove(routine);
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}

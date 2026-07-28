using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Calendar;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

/// <summary>One day column, already laid out. The view binds and draws; it computes nothing.</summary>
public sealed record CalendarDayColumn(
    DateOnly Date,
    string Header,
    bool IsToday,
    IReadOnlyList<CalendarItem> AllDay,
    IReadOnlyList<PositionedItem> Timed);

public partial class CalendarWeekViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<CalendarDayColumn> Columns { get; } = [];

    /// <summary>Hour labels for the gutter. Built once — 00 to 23 never changes.</summary>
    public IReadOnlyList<string> HourLabels { get; } =
        Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();

    [ObservableProperty]
    private DateOnly _weekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Now));

    [ObservableProperty]
    private string _weekHeading = "";

    /// <summary>True when any day in the visible week has an all-day item, so the band can collapse
    /// to nothing rather than leaving an empty strip across the top.</summary>
    [ObservableProperty]
    private bool _hasAllDayItems;

    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7)); // Monday-first

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var end = WeekStart.AddDays(6);
            var today = DateOnly.FromDateTime(DateTime.Now);
            WeekHeading = $"{WeekStart:MMM d} – {end:MMM d, yyyy}";

            await using var db = await dbContextFactory.CreateDbContextAsync();
            var blocks = await db.Set<ScheduleBlock>().Where(b => b.IsActive).ToListAsync();

            // One day back: AgendaBuilder expands a warm-up day so a block wrapping past midnight
            // carries its tail forward, and a cancellation the night before must suppress that tail.
            var exceptions = await db.Set<ScheduleException>()
                .Where(e => e.Date >= WeekStart.AddDays(-1) && e.Date <= end)
                .ToListAsync();

            // Overlap test, NOT a StartsAt range — a multi-day event that began before this window
            // and is still running must appear. See the Global Constraints.
            var windowStart = WeekStart.AddDays(-1).ToDateTime(TimeOnly.MinValue);
            var windowEnd = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var externalRows = await db.Set<ExternalEvent>()
                .Where(e => e.StartsAt < windowEnd && e.EndsAt > windowStart)
                .ToListAsync();

            var days = AgendaBuilder.Build(
                WeekStart, end, blocks, exceptions, ExternalEventProjector.ToAgendaEntries(externalRows));

            Columns.Clear();
            var anyAllDay = false;
            foreach (var day in days)
            {
                var items = CalendarItemMapper.ForDay(day);
                if (items.AllDay.Count > 0) anyAllDay = true;

                Columns.Add(new CalendarDayColumn(
                    day.Date,
                    $"{day.Date:ddd d}",
                    day.Date == today,
                    items.AllDay,
                    TimeGridLayout.Assign(items.Timed)));
            }

            HasAllDayItems = anyAllDay;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        WeekStart = WeekStart.AddDays(-7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        WeekStart = WeekStart.AddDays(7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ThisWeekAsync()
    {
        WeekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Now));
        await LoadAsync();
    }
}

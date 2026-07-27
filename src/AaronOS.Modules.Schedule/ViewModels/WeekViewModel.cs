using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class WeekViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<AgendaDay> Days { get; } = [];
    public ObservableCollection<ScheduleBlock> Blocks { get; } = [];

    public IReadOnlyList<ScheduleBlockKind> Kinds { get; } = Enum.GetValues<ScheduleBlockKind>();

    [ObservableProperty]
    private DateOnly _weekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Now));

    [ObservableProperty]
    private string _weekHeading = "";

    // New-block editor fields.
    [ObservableProperty]
    private string _newLabel = "";

    [ObservableProperty]
    private ScheduleBlockKind _newKind = ScheduleBlockKind.Work;

    [ObservableProperty]
    private bool _newMonday = true;

    [ObservableProperty]
    private bool _newTuesday = true;

    [ObservableProperty]
    private bool _newWednesday = true;

    [ObservableProperty]
    private bool _newThursday = true;

    [ObservableProperty]
    private bool _newFriday = true;

    [ObservableProperty]
    private bool _newSaturday;

    [ObservableProperty]
    private bool _newSunday;

    /// <summary>Entered as "HH:mm" text rather than through a NumberBox pair — a time of day is one
    /// value, and ui:NumberBox's double/NaN handling makes two-box entry worse, not better.</summary>
    [ObservableProperty]
    private string _newStartText = "08:00";

    [ObservableProperty]
    private string _newEndText = "17:00";

    [ObservableProperty]
    private string? _validationMessage;

    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7)); // Monday-first

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var end = WeekStart.AddDays(6);
            WeekHeading = $"{WeekStart:MMM d} – {end:MMM d, yyyy}";

            await using var db = await dbContextFactory.CreateDbContextAsync();
            var blocks = await db.Set<ScheduleBlock>().ToListAsync();
            var exceptions = await db.Set<ScheduleException>()
                .Where(e => e.Date >= WeekStart.AddDays(-1) && e.Date <= end)
                .ToListAsync();

            Blocks.Clear();
            foreach (var block in blocks.OrderBy(b => b.StartTime)) Blocks.Add(block);

            Days.Clear();
            foreach (var day in AgendaBuilder.Build(WeekStart, end, blocks, exceptions, [])) Days.Add(day);
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
    private async Task SaveBlockAsync()
    {
        ValidationMessage = null;

        if (string.IsNullOrWhiteSpace(NewLabel))
        {
            ValidationMessage = "Give the block a label.";
            return;
        }
        if (!TimeSpan.TryParse(NewStartText, out var start) || !TimeSpan.TryParse(NewEndText, out var end))
        {
            ValidationMessage = "Enter times as HH:mm.";
            return;
        }
        if (start == end)
        {
            ValidationMessage = "Start and end can't be the same time.";
            return;
        }

        var days = SelectedDays();
        if (days == DayOfWeekFlags.None)
        {
            ValidationMessage = "Pick at least one day.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new ScheduleBlock
        {
            Kind = NewKind,
            Label = NewLabel.Trim(),
            DaysOfWeek = days,
            StartTime = start,
            EndTime = end,
            EffectiveFrom = WeekStart,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        NewLabel = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteBlockAsync(ScheduleBlock block)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(await db.Set<ScheduleBlock>().SingleAsync(b => b.Id == block.Id));
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    /// <summary>Cancels every block on a date — the PTO case, which is what an exception is
    /// overwhelmingly used for. Finer-grained editing can come later if it's actually wanted.</summary>
    [RelayCommand]
    private async Task AddExceptionAsync(DateOnly date)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var existing = await db.Set<ScheduleException>().Where(e => e.Date == date).ToListAsync();
        db.RemoveRange(existing);

        foreach (var block in Blocks.Where(b => b.Kind != ScheduleBlockKind.Sleep))
        {
            db.Add(new ScheduleException
            {
                Date = date,
                ScheduleBlockId = block.Id,
                IsCancelled = true,
                Note = "Day off",
            });
        }

        await db.SaveChangesAsync();
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
}

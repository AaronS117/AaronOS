using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class TodayViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<AgendaEntry> Entries { get; } = [];
    public ObservableCollection<FreeGap> FreeGaps { get; } = [];

    // ponytail: field-backed [ObservableProperty] — the partial-property generator doesn't run
    // in this environment. See docs/MODULE_GUIDELINES.md.
    [ObservableProperty]
    private string _dateHeading = "";

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            DateHeading = today.ToString("dddd, MMMM d");

            await using var db = await dbContextFactory.CreateDbContextAsync();

            // Materialise before handing to AgendaBuilder: it works on plain lists, and DateOnly
            // comparisons plus the computed properties on these entities are not translatable.
            var blocks = await db.Set<ScheduleBlock>().Where(b => b.IsActive).ToListAsync();
            // Also fetch yesterday's exceptions: AgendaBuilder expands a warm-up day before
            // `today` so a block wrapping past midnight can carry its tail forward, and a
            // cancellation on the night before must be visible to suppress that tail.
            var exceptions = await db.Set<ScheduleException>()
                .Where(e => e.Date == today || e.Date == today.AddDays(-1))
                .ToListAsync();

            // AgendaBuilder walks a warm-up day before `today` (see the exceptions query above), so
            // the external-event window must start there too, or a meeting that started the night
            // before and crosses midnight would vanish. This is an overlap test rather than a
            // StartsAt-only filter so a multi-day event that began before the window but is still
            // ongoing today is still picked up.
            var windowStart = today.AddDays(-1).ToDateTime(TimeOnly.MinValue);
            var windowEnd = today.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var externalRows = await db.Set<ExternalEvent>()
                .Where(e => e.StartsAt < windowEnd && e.EndsAt > windowStart)
                .ToListAsync();

            var day = AgendaBuilder.Build(
                today, today, blocks, exceptions, ExternalEventProjector.ToAgendaEntries(externalRows)).Single();

            Entries.Clear();
            foreach (var entry in day.Entries) Entries.Add(entry);

            FreeGaps.Clear();
            foreach (var gap in day.FreeGaps) FreeGaps.Add(gap);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

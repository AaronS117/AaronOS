using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;
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

            var day = AgendaBuilder.Build(today, today, blocks, exceptions, []).Single();

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

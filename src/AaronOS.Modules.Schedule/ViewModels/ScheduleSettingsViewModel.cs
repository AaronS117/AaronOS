using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Schedule.ViewModels;

public partial class ScheduleSettingsViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    ScheduleSyncService syncService) : ViewModelBase
{
    public ObservableCollection<ExternalCalendar> Calendars { get; } = [];

    [ObservableProperty]
    private string _newIcsUrl = "";

    [ObservableProperty]
    private string _newIcsName = "Work (Outlook)";

    [ObservableProperty]
    private string? _statusMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var calendars = await db.Set<ExternalCalendar>().ToListAsync();

            Calendars.Clear();
            foreach (var calendar in calendars.OrderBy(c => c.Provider).ThenBy(c => c.DisplayName))
            {
                Calendars.Add(calendar);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddIcsCalendarAsync()
    {
        StatusMessage = null;

        if (string.IsNullOrWhiteSpace(NewIcsUrl))
        {
            StatusMessage = "Paste the published calendar's ICS URL.";
            return;
        }
        if (!Uri.TryCreate(NewIcsUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            StatusMessage = "The ICS URL must be an absolute https:// address.";
            return;
        }

        // The single most likely mistake here: Outlook's "Publish a calendar" page hands out both an
        // HTML link (opens a web page) and an ICS link (a feed) side by side, and they look similar.
        // Pasting the HTML one doesn't fail until the next sync, as an opaque parse error — catching
        // it here, with a message naming what to look for, is much cheaper than that.
        if (uri.AbsolutePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "That looks like the HTML calendar page, not the feed. On the " +
                "'Publish a calendar' page, copy the ICS link instead — it ends in .ics.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new ExternalCalendar
        {
            Provider = CalendarProvider.OutlookIcs,
            DisplayName = string.IsNullOrWhiteSpace(NewIcsName) ? "Outlook" : NewIcsName.Trim(),
            IcsUrl = uri.ToString(),
            IsEnabled = true,
        });
        await db.SaveChangesAsync();

        NewIcsUrl = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SyncNowAsync(ExternalCalendar calendar)
    {
        IsBusy = true;
        StatusMessage = $"Syncing {calendar.DisplayName}…";
        try
        {
            await syncService.SyncOneAsync(calendar.Id, CancellationToken.None);
            await LoadAsync();

            var refreshed = Calendars.FirstOrDefault(c => c.Id == calendar.Id);
            StatusMessage = refreshed?.LastError is { } error
                ? $"{calendar.DisplayName} failed: {error}"
                : $"{calendar.DisplayName} synced.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SyncAllAsync()
    {
        IsBusy = true;
        StatusMessage = "Syncing…";
        try
        {
            var succeeded = await syncService.SyncAllAsync(CancellationToken.None);
            await LoadAsync();
            StatusMessage = $"{succeeded} of {Calendars.Count(c => c.IsEnabled)} calendar(s) synced.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(ExternalCalendar calendar)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var tracked = await db.Set<ExternalCalendar>().SingleAsync(c => c.Id == calendar.Id);
        tracked.IsEnabled = !tracked.IsEnabled;
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveCalendarAsync(ExternalCalendar calendar)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        // Cascade removes its cached events too.
        db.Remove(await db.Set<ExternalCalendar>().SingleAsync(c => c.Id == calendar.Id));
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}

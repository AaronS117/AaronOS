using System.Collections.ObjectModel;
using System.Diagnostics;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Medical.Data;
using AaronOS.Modules.Medical.Withings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Medical.ViewModels;

public partial class SleepViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    WithingsCredentialStore credentialStore,
    WithingsApiClient api,
    WithingsSleepImporter importer) : ViewModelBase
{
    public ObservableCollection<SleepNight> Nights { get; } = [];

    [ObservableProperty] private string _clientId = "";
    [ObservableProperty] private string _clientSecret = "";
    [ObservableProperty] private string _authorizationCode = "";

    [ObservableProperty] private bool _hasAppCredentials;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _hasNights;
    [ObservableProperty] private string _connectionStatus = "Not set up yet.";
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty] private int _nightsRecorded;
    [ObservableProperty] private string _averageHours = "—";
    [ObservableProperty] private string _averageScore = "—";
    [ObservableProperty] private string _averageEfficiency = "—";

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var credentials = credentialStore.Load();
            HasAppCredentials = credentials?.HasAppCredentials ?? false;
            IsConnected = credentials?.IsAuthorized ?? false;
            ClientId = credentials?.ClientId ?? "";

            ConnectionStatus = (HasAppCredentials, IsConnected) switch
            {
                (false, _) => "Not set up yet. Add a Withings client ID and secret below.",
                (true, false) => "Client ID saved. Authorize the app to finish connecting.",
                (true, true) => $"Connected{(credentials?.UserId is { } id ? $" as Withings user {id}" : "")}."
            };

            await ReloadNightsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadNightsAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var nights = await db.Set<SleepNight>()
            .AsNoTracking()
            .OrderByDescending(s => s.Date)
            .Take(90)
            .ToListAsync();

        Nights.Clear();
        foreach (var night in nights)
        {
            Nights.Add(night);
        }

        HasNights = nights.Count > 0;
        NightsRecorded = nights.Count;
        AverageHours = nights.Count == 0 ? "—" : $"{nights.Average(n => n.Hours):0.#} h";

        var scored = nights.Where(n => n.SleepScore is not null).ToList();
        AverageScore = scored.Count == 0 ? "—" : $"{scored.Average(n => n.SleepScore!.Value):0}";

        var efficient = nights.Where(n => n.EfficiencyPercent is not null).ToList();
        AverageEfficiency = efficient.Count == 0 ? "—" : $"{efficient.Average(n => n.EfficiencyPercent!.Value):0}%";
    }

    [RelayCommand]
    private async Task SaveAppCredentialsAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            StatusMessage = "Both the client ID and the secret are needed.";
            return;
        }

        // Keeps any existing grant: re-saving the same app credentials should not force a reauthorize.
        var credentials = credentialStore.Load() ?? new WithingsCredentials();
        credentials.ClientId = ClientId.Trim();
        credentials.ClientSecret = ClientSecret.Trim();
        credentialStore.Save(credentials);

        ClientSecret = "";
        StatusMessage = "Saved. Now click Authorize.";
        await LoadAsync();
    }

    [RelayCommand]
    private void Authorize()
    {
        try
        {
            // State is only echoed back to us in the redirect; there is no third party in this flow to
            // defend against, so a fresh opaque value is enough.
            var url = api.BuildAuthorizeUrl(Guid.NewGuid().ToString("N"));
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StatusMessage = "Approve access in the browser, then copy the code= value from the address bar and paste it below.";
        }
        catch (Exception e)
        {
            StatusMessage = e.Message;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(AuthorizationCode))
        {
            StatusMessage = "Paste the code from the browser address bar first.";
            return;
        }

        IsBusy = true;
        try
        {
            await api.ExchangeCodeAsync(ExtractCode(AuthorizationCode));
            AuthorizationCode = "";
            StatusMessage = "Connected. Syncing.";
            await LoadAsync();
            await SyncAsync();
        }
        catch (Exception e)
        {
            StatusMessage = $"Could not connect: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Accepts either a bare code or the whole redirect URL pasted from the address bar, because
    /// pasting the URL is what people actually do.
    /// </summary>
    public static string ExtractCode(string pasted)
    {
        var text = pasted.Trim();
        var marker = text.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return text;
        }

        var value = text[(marker + "code=".Length)..];
        var end = value.IndexOfAny(['&', '#', ' ']);
        return end < 0 ? value : value[..end];
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        IsBusy = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var (from, to) = await importer.NextRangeAsync(today);
            var result = await importer.SyncAsync(from, to);
            StatusMessage = result.Summary;
            await ReloadNightsAsync();
        }
        catch (Exception e)
        {
            StatusMessage = $"Sync failed: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Pulls the full backfill window, for a first sync or after a gap.</summary>
    [RelayCommand]
    private async Task BackfillAsync()
    {
        IsBusy = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var result = await importer.SyncAsync(today.AddDays(-WithingsSleepImporter.MaxBackfillDays), today);
            StatusMessage = result.Summary;
            await ReloadNightsAsync();
        }
        catch (Exception e)
        {
            StatusMessage = $"Backfill failed: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Forgets the tokens and the app secret. Imported nights are left alone — they are measurements,
    /// and disconnecting an account is not a reason to delete history.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        credentialStore.Clear();
        ClientSecret = "";
        StatusMessage = "Disconnected. Recorded nights were kept.";
        await LoadAsync();
    }
}

using System.Collections.ObjectModel;
using System.IO;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Medical.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Medical.ViewModels;

public partial class VisitsViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<MedicalVisit> Visits { get; } = [];
    public ObservableCollection<Provider> Providers { get; } = [];
    public ObservableCollection<MedicalDocument> Documents { get; } = [];

    [ObservableProperty] private bool _hasVisits;
    [ObservableProperty] private bool _hasProviders;
    [ObservableProperty] private bool _hasDocuments;
    [ObservableProperty] private string _statusMessage = "";

    // New visit
    [ObservableProperty] private DateTime? _newVisitDate = DateTime.Now;
    [ObservableProperty] private string _newVisitType = "";
    [ObservableProperty] private string _newVisitReason = "";
    [ObservableProperty] private string _newVisitFacility = "";
    [ObservableProperty] private Provider? _newVisitProvider;

    // New provider
    [ObservableProperty] private string _newProviderName = "";
    [ObservableProperty] private string _newProviderSpecialty = "";
    [ObservableProperty] private string _newProviderPhone = "";
    [ObservableProperty] private string _newProviderFacility = "";

    // New document
    [ObservableProperty] private string _newDocumentTitle = "";
    [ObservableProperty] private string _newDocumentPath = "";
    [ObservableProperty] private MedicalVisit? _newDocumentVisit;
    [ObservableProperty] private bool _hasChosenFile;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            Providers.Clear();
            foreach (var p in await db.Set<Provider>().AsNoTracking().OrderBy(p => p.Name).ToListAsync())
            {
                Providers.Add(p);
            }

            Visits.Clear();
            foreach (var v in await db.Set<MedicalVisit>().AsNoTracking()
                .Include(v => v.Provider)
                .OrderByDescending(v => v.Date)
                .ToListAsync())
            {
                Visits.Add(v);
            }

            Documents.Clear();
            foreach (var d in await db.Set<MedicalDocument>().AsNoTracking()
                .Include(d => d.Visit)
                .OrderByDescending(d => d.AddedOn)
                .ToListAsync())
            {
                Documents.Add(d);
            }

            HasProviders = Providers.Count > 0;
            HasVisits = Visits.Count > 0;
            HasDocuments = Documents.Count > 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string? Trimmed(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Called from the page's code-behind after an OpenFileDialog — file dialogs are a view
    /// concern, so the ViewModel only ever receives the resulting path.</summary>
    public void SetDocumentFile(string path)
    {
        NewDocumentPath = path;
        HasChosenFile = !string.IsNullOrWhiteSpace(path);
        if (string.IsNullOrWhiteSpace(NewDocumentTitle))
        {
            NewDocumentTitle = Path.GetFileNameWithoutExtension(path);
        }
    }

    [RelayCommand]
    private async Task AddVisitAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new MedicalVisit
        {
            Date = NewVisitDate is { } d ? DateOnly.FromDateTime(d.Date) : null,
            VisitType = Trimmed(NewVisitType),
            Reason = Trimmed(NewVisitReason),
            Facility = Trimmed(NewVisitFacility),
            ProviderId = NewVisitProvider?.Id
        });
        await db.SaveChangesAsync();

        StatusMessage = "Visit recorded.";
        NewVisitType = "";
        NewVisitReason = "";
        NewVisitFacility = "";
        NewVisitProvider = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteVisitAsync(MedicalVisit visit)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(visit);
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddProviderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProviderName))
        {
            StatusMessage = "Name the provider.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new Provider
        {
            Name = NewProviderName.Trim(),
            Specialty = Trimmed(NewProviderSpecialty),
            Phone = Trimmed(NewProviderPhone),
            Facility = Trimmed(NewProviderFacility)
        });
        await db.SaveChangesAsync();

        StatusMessage = $"Added {NewProviderName.Trim()}.";
        NewProviderName = "";
        NewProviderSpecialty = "";
        NewProviderPhone = "";
        NewProviderFacility = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteProviderAsync(Provider provider)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(provider);
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDocumentPath) || string.IsNullOrWhiteSpace(NewDocumentTitle))
        {
            StatusMessage = "Choose a file and give it a title.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new MedicalDocument
        {
            Title = NewDocumentTitle.Trim(),
            FilePath = NewDocumentPath,
            AddedOn = DateOnly.FromDateTime(DateTime.Now),
            VisitId = NewDocumentVisit?.Id
        });
        await db.SaveChangesAsync();

        StatusMessage = $"Attached {NewDocumentTitle.Trim()}.";
        NewDocumentTitle = "";
        NewDocumentPath = "";
        NewDocumentVisit = null;
        HasChosenFile = false;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteDocumentAsync(MedicalDocument document)
    {
        // Removes the link only. The file itself is the user's, wherever they keep it.
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(document);
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}

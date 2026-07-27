using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Medical.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace AaronOS.Modules.Medical.ViewModels;

public partial class LabsViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    private List<LabResult> _all = [];

    // LiveCharts2 draws on its own SkiaSharp surface and knows nothing about the app's dark theme, so
    // every paint has to be set explicitly or it falls back to near-black on a dark background —
    // same reason and same colours as the weight trend in BodyMeasurements.
    private static readonly SKColor ReactorCyan = new(0x4C, 0xC2, 0xFF);
    private static readonly SKColor AxisLabel = new(0x9A, 0xA3, 0xB2);
    private static readonly SKColor Separator = new(0x2A, 0x2A, 0x30);

    public ObservableCollection<LabResult> Results { get; } = [];
    public ObservableCollection<string> TestNames { get; } = [];

    // ObservableCollection rather than List: LiveCharts watches collection changes, so the chart
    // refreshes when a reload replaces the series instead of silently keeping the first render.
    public ObservableCollection<ISeries> TrendSeries { get; } = [];
    public ObservableCollection<ICartesianAxis> TrendAxes { get; } = [];

    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _hasTrend;
    [ObservableProperty] private string _trendSummary = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _selectedTestName;

    // New result
    [ObservableProperty] private string _newTestName = "";
    [ObservableProperty] private double? _newValue;
    [ObservableProperty] private string _newValueText = "";
    [ObservableProperty] private string _newUnit = "";
    [ObservableProperty] private double? _newLow;
    [ObservableProperty] private double? _newHigh;
    [ObservableProperty] private DateTime? _newTakenOn = DateTime.Now;

    partial void OnSelectedTestNameChanged(string? value) => RebuildTrend();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            _all = await db.Set<LabResult>().AsNoTracking()
                .OrderByDescending(l => l.TakenOn)
                .ThenBy(l => l.TestName)
                .ToListAsync();

            Results.Clear();
            foreach (var lab in _all)
            {
                Results.Add(lab);
            }
            HasResults = Results.Count > 0;

            var names = _all.Select(l => l.TestName).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n).ToList();

            var previous = SelectedTestName;
            TestNames.Clear();
            foreach (var name in names)
            {
                TestNames.Add(name);
            }

            // Keep the current selection across a reload where possible, so adding a result does not
            // yank the chart away from the test being looked at.
            SelectedTestName = previous is not null && names.Contains(previous, StringComparer.OrdinalIgnoreCase)
                ? previous
                : names.FirstOrDefault();

            RebuildTrend();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildTrend()
    {
        TrendSeries.Clear();
        TrendAxes.Clear();
        HasTrend = false;
        TrendSummary = "";

        if (SelectedTestName is null)
        {
            return;
        }

        // Oldest to newest so the line reads left to right like every other trend in the app.
        var points = _all
            .Where(l => string.Equals(l.TestName, SelectedTestName, StringComparison.OrdinalIgnoreCase)
                        && l.Value is not null && l.TakenOn is not null)
            .OrderBy(l => l.TakenOn)
            .ToList();

        if (points.Count == 0)
        {
            TrendSummary = "No dated numeric results for this test yet.";
            return;
        }

        TrendSeries.Add(new LineSeries<double>
        {
            Values = points.Select(p => (double)p.Value!.Value).ToArray(),
            Name = SelectedTestName,
            Stroke = new SolidColorPaint(ReactorCyan) { StrokeThickness = 2.5f },
            GeometryStroke = new SolidColorPaint(ReactorCyan) { StrokeThickness = 2.5f },
            GeometryFill = new SolidColorPaint(new SKColor(0x20, 0x20, 0x24)),
            GeometrySize = 8,
            Fill = new LinearGradientPaint(
                [ReactorCyan.WithAlpha(70), ReactorCyan.WithAlpha(0)],
                new SKPoint(0.5f, 0f),
                new SKPoint(0.5f, 1f)),
            LineSmoothness = 0.35
        });
        TrendAxes.Add(new Axis
        {
            // Full year rather than "MMM yy": the short form renders as "Jan 26", which reads like a
            // day of the month. (An apostrophe is not the fix — in a .NET custom format string '' is
            // an empty literal, so "MMM ''yy" also produces "Jan 26".)
            Labels = points.Select(p => p.TakenOn!.Value.ToString("MMM yyyy")).ToArray(),
            LabelsPaint = new SolidColorPaint(AxisLabel),
            TextSize = 12,
            SeparatorsPaint = new SolidColorPaint(Separator) { StrokeThickness = 1 }
        });

        HasTrend = true;

        var latest = points[^1];
        var unit = string.IsNullOrWhiteSpace(latest.Unit) ? "" : $" {latest.Unit}";
        TrendSummary = points.Count == 1
            ? $"One result: {latest.Value:0.##}{unit} on {latest.TakenDisplay}"
            : $"{points.Count} results · latest {latest.Value:0.##}{unit} on {latest.TakenDisplay}";
    }

    [RelayCommand]
    private async Task AddResultAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTestName))
        {
            StatusMessage = "Name the test.";
            return;
        }

        if (NewValue is null && string.IsNullOrWhiteSpace(NewValueText))
        {
            StatusMessage = "Enter a number or a text result.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Add(new LabResult
        {
            TestName = NewTestName.Trim(),
            Value = NewValue is { } v ? (decimal)v : null,
            ValueText = string.IsNullOrWhiteSpace(NewValueText) ? null : NewValueText.Trim(),
            Unit = string.IsNullOrWhiteSpace(NewUnit) ? null : NewUnit.Trim(),
            ReferenceLow = NewLow is { } lo ? (decimal)lo : null,
            ReferenceHigh = NewHigh is { } hi ? (decimal)hi : null,
            TakenOn = NewTakenOn is { } d ? DateOnly.FromDateTime(d.Date) : null
        });
        await db.SaveChangesAsync();

        StatusMessage = $"Added {NewTestName.Trim()}.";
        SelectedTestName = NewTestName.Trim();
        NewValue = null;
        NewValueText = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteResultAsync(LabResult result)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(result);
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}

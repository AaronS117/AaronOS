using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Medical.Calculations;
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

public partial class MoodViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    private List<MoodEntry> _all = [];

    /// <summary>Measured hours by night, keyed on the wake date, so it lines up with a mood entry's date.</summary>
    private Dictionary<DateOnly, decimal> _measuredSleep = [];

    // Same paints as the other charts in the app: LiveCharts draws its own Skia surface and knows
    // nothing about the dark theme.
    private static readonly SKColor ReactorCyan = new(0x4C, 0xC2, 0xFF);
    private static readonly SKColor SleepAmber = new(0xE8, 0xB0, 0x4B);
    private static readonly SKColor AxisLabel = new(0x9A, 0xA3, 0xB2);
    private static readonly SKColor Separator = new(0x2A, 0x2A, 0x30);

    public ObservableCollection<MoodEntry> Entries { get; } = [];
    public ObservableCollection<MonthlyMood> Months { get; } = [];
    public ObservableCollection<ISeries> TrendSeries { get; } = [];
    public ObservableCollection<ICartesianAxis> TrendXAxes { get; } = [];
    public ObservableCollection<ICartesianAxis> TrendYAxes { get; } = [];

    [ObservableProperty] private DateTime? _entryDate = DateTime.Now;
    [ObservableProperty] private double _mood;
    [ObservableProperty] private double _energy = 3;
    [ObservableProperty] private double? _sleepHours;
    [ObservableProperty] private string _note = "";

    [ObservableProperty] private string _moodLabel = "Even";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasEntries;
    [ObservableProperty] private bool _hasTrend;
    [ObservableProperty] private bool _hasMonths;
    [ObservableProperty] private bool _editingExistingDay;

    // Summary
    [ObservableProperty] private int _daysLogged;
    [ObservableProperty] private double _averageMood;
    [ObservableProperty] private int _swing;
    [ObservableProperty] private int _lowDays;
    [ObservableProperty] private int _elevatedDays;
    [ObservableProperty] private string _swingDescription = "No entries yet";
    [ObservableProperty] private string _sleepSummary = "—";

    partial void OnMoodChanged(double value)
    {
        MoodLabel = new MoodEntry { Mood = (int)Math.Round(value) }.MoodLabel;
    }

    partial void OnEntryDateChanged(DateTime? value) => LoadDayIntoForm();

    /// <summary>
    /// Pulls an existing entry for the chosen date into the form, so picking a day you already logged
    /// edits it rather than silently failing against the one-per-day constraint.
    /// </summary>
    private void LoadDayIntoForm()
    {
        if (EntryDate is not { } d)
        {
            return;
        }

        var existing = _all.FirstOrDefault(e => e.Date == DateOnly.FromDateTime(d.Date));
        EditingExistingDay = existing is not null;

        if (existing is null)
        {
            return;
        }

        Mood = existing.Mood;
        Energy = existing.Energy;
        SleepHours = existing.SleepHours is { } h ? (double)h : null;
        Note = existing.Note ?? "";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            _all = await db.Set<MoodEntry>().AsNoTracking().OrderBy(e => e.Date).ToListAsync();

            _measuredSleep = await db.Set<SleepNight>()
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Date, s => s.Hours);

            Entries.Clear();
            foreach (var e in _all.OrderByDescending(e => e.Date).Take(60))
            {
                Entries.Add(e);
            }
            HasEntries = _all.Count > 0;

            var today = DateOnly.FromDateTime(DateTime.Now);
            var summary = MoodStatistics.Summarise(_all, today, measuredSleep: _measuredSleep);
            DaysLogged = summary.DaysLogged;
            AverageMood = summary.AverageMood;
            Swing = summary.Swing;
            LowDays = summary.LowDays;
            ElevatedDays = summary.ElevatedDays;
            SwingDescription = summary.SwingDescription;
            SleepSummary = summary.AverageSleepHours is { } s
                ? $"{s:0.#} h average{(_measuredSleep.Count > 0 ? " (measured)" : "")}"
                : "not recorded";

            Months.Clear();
            foreach (var m in MoodStatistics.ByMonth(_all))
            {
                Months.Add(m);
            }
            HasMonths = Months.Count >= 2;

            BuildTrend();
            LoadDayIntoForm();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildTrend()
    {
        TrendSeries.Clear();
        TrendXAxes.Clear();
        TrendYAxes.Clear();
        HasTrend = false;

        // Last 60 days, oldest first so the line reads left to right like every other trend here.
        var cutoff = DateOnly.FromDateTime(DateTime.Now).AddDays(-59);
        var points = _all.Where(e => e.Date >= cutoff).OrderBy(e => e.Date).ToList();
        if (points.Count == 0)
        {
            return;
        }

        TrendSeries.Add(new LineSeries<double?>
        {
            Values = points.Select(p => (double?)p.Mood).ToArray(),
            Name = "Mood",
            Stroke = new SolidColorPaint(ReactorCyan) { StrokeThickness = 2.5f },
            GeometryStroke = new SolidColorPaint(ReactorCyan) { StrokeThickness = 2.5f },
            GeometryFill = new SolidColorPaint(new SKColor(0x20, 0x20, 0x24)),
            GeometrySize = 7,
            LineSmoothness = 0.2,
            ScalesYAt = 0
        });

        // Sleep on its own axis: the two do not share a scale, and seeing them together is the point.
        // Measured nights win over typed ones, which is where the pad earns its keep — the line stops
        // depending on whether anyone remembered to fill the field in.
        var sleepPoints = points
            .Select(p => MoodStatistics.SleepFor(p, _measuredSleep))
            .Select(h => h is { } v ? (double?)v : null)
            .ToArray();

        if (sleepPoints.Any(h => h is not null))
        {
            TrendSeries.Add(new LineSeries<double?>
            {
                Values = sleepPoints,
                Name = "Sleep (h)",
                Stroke = new SolidColorPaint(SleepAmber) { StrokeThickness = 1.8f },
                GeometryStroke = new SolidColorPaint(SleepAmber) { StrokeThickness = 1.8f },
                GeometryFill = new SolidColorPaint(new SKColor(0x20, 0x20, 0x24)),
                GeometrySize = 5,
                LineSmoothness = 0.2,
                ScalesYAt = 1
            });
        }

        TrendXAxes.Add(new Axis
        {
            Labels = points.Select(p => p.Date.ToString("d MMM")).ToArray(),
            LabelsPaint = new SolidColorPaint(AxisLabel),
            TextSize = 11,
            SeparatorsPaint = new SolidColorPaint(Separator) { StrokeThickness = 1 }
        });

        // Pinned to the full scale so a flat stretch does not look like a swing.
        TrendYAxes.Add(new Axis
        {
            Name = "Mood",
            MinLimit = MoodEntry.MoodFloor,
            MaxLimit = MoodEntry.MoodCeiling,
            LabelsPaint = new SolidColorPaint(AxisLabel),
            NamePaint = new SolidColorPaint(AxisLabel),
            TextSize = 11,
            SeparatorsPaint = new SolidColorPaint(Separator) { StrokeThickness = 1 }
        });
        TrendYAxes.Add(new Axis
        {
            Name = "Sleep",
            MinLimit = 0,
            MaxLimit = 14,
            Position = LiveChartsCore.Measure.AxisPosition.End,
            LabelsPaint = new SolidColorPaint(SleepAmber),
            NamePaint = new SolidColorPaint(SleepAmber),
            TextSize = 11,
            ShowSeparatorLines = false
        });

        HasTrend = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (EntryDate is not { } d)
        {
            StatusMessage = "Pick a date.";
            return;
        }

        var date = DateOnly.FromDateTime(d.Date);
        if (date > DateOnly.FromDateTime(DateTime.Now))
        {
            StatusMessage = "That date is in the future.";
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();

        // Upsert by date: one entry per day, so logging the same day again corrects it.
        var entry = await db.Set<MoodEntry>().FirstOrDefaultAsync(e => e.Date == date);
        var isNew = entry is null;
        entry ??= new MoodEntry { Date = date };

        entry.Mood = Math.Clamp((int)Math.Round(Mood), MoodEntry.MoodFloor, MoodEntry.MoodCeiling);
        entry.Energy = Math.Clamp((int)Math.Round(Energy), 1, 5);
        entry.SleepHours = SleepHours is { } h ? (decimal)h : null;
        entry.Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();

        if (isNew)
        {
            db.Add(entry);
        }

        await db.SaveChangesAsync();
        StatusMessage = isNew ? $"Logged {entry.DateDisplay}." : $"Updated {entry.DateDisplay}.";
        Note = "";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(MoodEntry entry)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var tracked = await db.Set<MoodEntry>().FirstOrDefaultAsync(e => e.Id == entry.Id);
        if (tracked is null)
        {
            return;
        }

        db.Remove(tracked);
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}

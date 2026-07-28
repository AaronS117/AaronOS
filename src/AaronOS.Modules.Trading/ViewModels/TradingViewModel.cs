using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Trading.Agent;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace AaronOS.Modules.Trading.ViewModels;

/// <summary>A position as shown on the dashboard.</summary>
public record PositionRow(string Symbol, int Quantity, decimal MarketValue);

public partial class TradingViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    AlpacaClient alpaca,
    AgentProviderRegistry providers,
    TradingScheduler scheduler,
    SnapshotRecorder recorder) : ViewModelBase
{
    private static readonly SKColor ReactorCyan = new(0x4C, 0xC2, 0xFF);
    private static readonly SKColor BenchmarkGrey = new(0x9A, 0xA3, 0xB2);
    private static readonly SKColor AxisLabel = new(0x9A, 0xA3, 0xB2);
    private static readonly SKColor Separator = new(0x2A, 0x2A, 0x30);

    [ObservableProperty]
    private TradingConfig _config = new();

    [ObservableProperty]
    private decimal _equity;

    [ObservableProperty]
    private decimal _cash;

    [ObservableProperty]
    private string _verdict = "Nothing recorded yet.";

    [ObservableProperty]
    private string _returnDisplay = "—";

    [ObservableProperty]
    private string _benchmarkDisplay = "—";

    [ObservableProperty]
    private string _drawdownDisplay = "—";

    [ObservableProperty]
    private string _tradeCountDisplay = "—";

    [ObservableProperty]
    private string _winRateDisplay = "Not enough trades yet";

    [ObservableProperty]
    private bool _isBehindBenchmark;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isSchedulerRunning;

    [ObservableProperty]
    private bool _hasKeys;

    [ObservableProperty]
    private bool _hasChart;

    [ObservableProperty]
    private bool _hasPositions;

    [ObservableProperty]
    private bool _hasOrders;

    [ObservableProperty]
    private bool _hasDecisions;

    [ObservableProperty]
    private string _startedOnDisplay = "not started";

    public ObservableCollection<PositionRow> Positions { get; } = [];
    public ObservableCollection<TradeOrder> Orders { get; } = [];
    public ObservableCollection<AgentDecision> Decisions { get; } = [];

    public IReadOnlyList<string> ProviderNames => providers.Names;

    public List<ISeries> EquitySeries { get; } = [];
    public List<ICartesianAxis> EquityXAxes { get; } = [];
    public List<ICartesianAxis> EquityYAxes { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            IsSchedulerRunning = scheduler.IsRunning;

            await using var db = await dbContextFactory.CreateDbContextAsync();
            Config = await db.Set<TradingConfig>().FirstOrDefaultAsync() ?? new TradingConfig();
            HasKeys = alpaca.IsConfigured && providers.Resolve(Config.Provider).IsConfigured;
            StartedOnDisplay = Config.StartedOn is { } started
                ? started.ToString("d MMM yyyy")
                : "not started";

            var snapshots = await db.Set<PortfolioSnapshot>().OrderBy(s => s.Date).ToListAsync();
            var orders = await db.Set<TradeOrder>()
                .OrderByDescending(o => o.SubmittedAtUtc)
                .Take(100)
                .ToListAsync();
            var decisions = await db.Set<AgentDecision>()
                .OrderByDescending(d => d.RanAtUtc)
                .Take(50)
                .ToListAsync();

            Orders.Clear();
            foreach (var order in orders)
            {
                Orders.Add(order);
            }
            HasOrders = orders.Count > 0;

            Decisions.Clear();
            foreach (var decision in decisions)
            {
                Decisions.Add(decision);
            }
            HasDecisions = decisions.Count > 0;

            var (closed, wins) = RoundTripCounter.Count(await db.Set<TradeOrder>().ToListAsync());
            var summary = PerformanceCalculator.Summarise(snapshots, closed, wins, Config.MinTradesForStats);
            ApplySummary(summary);
            BuildChart(snapshots);

            if (snapshots.Count > 0)
            {
                Equity = snapshots[^1].Equity;
                Cash = snapshots[^1].Cash;
            }

            await LoadPositionsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadPositionsAsync()
    {
        Positions.Clear();
        HasPositions = false;

        if (!alpaca.IsConfigured)
        {
            return;
        }

        try
        {
            foreach (var position in await alpaca.GetPositionsAsync())
            {
                Positions.Add(new PositionRow(position.Symbol, position.Quantity, position.MarketValue));
            }

            HasPositions = Positions.Count > 0;
        }
        catch (AlpacaApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void ApplySummary(PerformanceSummary summary)
    {
        Verdict = summary.Verdict;
        IsBehindBenchmark = summary.IsBehindBenchmark;
        ReturnDisplay = summary.DayCount == 0 ? "—" : $"{summary.StrategyReturnPercent:+0.00;-0.00}%";
        BenchmarkDisplay = summary.BenchmarkReturnPercent is { } benchmark
            ? $"{benchmark:+0.00;-0.00}%"
            : "—";
        DrawdownDisplay = summary.DayCount == 0 ? "—" : $"−{summary.MaxDrawdownPercent:0.00}%";
        TradeCountDisplay = summary.ClosedTradeCount == 1 ? "1 closed trade" : $"{summary.ClosedTradeCount} closed trades";

        // Withheld rather than shown small. A percentage on screen invites being read as a result
        // no matter what caveat sits beside it.
        WinRateDisplay = summary.WinRatePercent is { } rate
            ? $"{rate:0.#}% win rate"
            : $"Win rate hidden until {Config.MinTradesForStats} closed trades";
    }

    /// <summary>
    /// Both lines are indexed to 100 at the first snapshot.
    ///
    /// Plotting a six-figure account balance against a three-figure index price on one axis makes
    /// the strategy look like a flat line at the top of the chart. Indexing both to the same start
    /// is what turns the picture into the comparison it is supposed to be.
    /// </summary>
    private void BuildChart(List<PortfolioSnapshot> snapshots)
    {
        EquitySeries.Clear();
        EquityXAxes.Clear();
        EquityYAxes.Clear();

        var usable = snapshots.Where(s => s.Equity > 0).ToList();
        HasChart = usable.Count >= 2;
        if (!HasChart)
        {
            return;
        }

        var baseEquity = usable[0].Equity;
        EquitySeries.Add(Line("This strategy", usable.Select(s => (double)(s.Equity / baseEquity * 100m)), ReactorCyan, 2.6f));

        var withBenchmark = usable.Where(s => s.BenchmarkClose is > 0).ToList();
        if (withBenchmark.Count >= 2)
        {
            var baseBenchmark = withBenchmark[0].BenchmarkClose!.Value;
            EquitySeries.Add(Line(
                $"{PortfolioSnapshot.BenchmarkSymbol} held",
                withBenchmark.Select(s => (double)(s.BenchmarkClose!.Value / baseBenchmark * 100m)),
                BenchmarkGrey,
                1.8f));
        }

        EquityXAxes.Add(new Axis
        {
            Labels = usable.Select(s => s.Date.ToString("d MMM")).ToArray(),
            LabelsPaint = new SolidColorPaint(AxisLabel),
            TextSize = 12,
            SeparatorsPaint = new SolidColorPaint(Separator) { StrokeThickness = 1 },
        });

        EquityYAxes.Add(new Axis
        {
            Labeler = value => $"{value:0}",
            LabelsPaint = new SolidColorPaint(AxisLabel),
            TextSize = 12,
            SeparatorsPaint = new SolidColorPaint(Separator) { StrokeThickness = 1 },
        });
    }

    private static LineSeries<double> Line(string name, IEnumerable<double> values, SKColor colour, float thickness) =>
        new()
        {
            Values = values.ToArray(),
            Name = name,
            Stroke = new SolidColorPaint(colour) { StrokeThickness = thickness },
            GeometrySize = 0,
            Fill = null,
            LineSmoothness = 0.2,
        };

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var stored = await db.Set<TradingConfig>().FirstOrDefaultAsync();

            if (stored is null)
            {
                db.Add(Config);
            }
            else
            {
                stored.IsEnabled = Config.IsEnabled;
                stored.Watchlist = Config.Watchlist;
                stored.MaxPositionPercent = Config.MaxPositionPercent;
                stored.MaxInvestedPercent = Config.MaxInvestedPercent;
                stored.MaxTradesPerDay = Config.MaxTradesPerDay;
                stored.CycleIntervalMinutes = Config.CycleIntervalMinutes;
                stored.Model = Config.Model;
                stored.Provider = Config.Provider;
                stored.StrategyNotes = Config.StrategyNotes;
                stored.MinTradesForStats = Config.MinTradesForStats;

                // StartedOn is never copied from the editable copy. It is stamped once by the first
                // cycle so the measurement window cannot be moved after a bad run.
            }

            await db.SaveChangesAsync();
            StatusMessage = $"Saved {DateTime.Now:h:mm tt}.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunOnceAsync()
    {
        IsBusy = true;
        StatusMessage = "Running a cycle…";
        try
        {
            var result = await scheduler.RunOnceAsync();
            StatusMessage = result.Error is { } error ? $"Failed: {error}" : result.Summary;
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!Config.IsEnabled)
        {
            StatusMessage = "Switch trading on and save before starting the schedule.";
            return;
        }

        await scheduler.StartAsync();
        IsSchedulerRunning = scheduler.IsRunning;
        StatusMessage = $"Running every {Config.CycleIntervalMinutes} minutes while AaronOS is open.";
    }

    [RelayCommand]
    private void Stop()
    {
        scheduler.Stop();
        IsSchedulerRunning = scheduler.IsRunning;
        StatusMessage = "Stopped. No further cycles will run.";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            await recorder.ReconcileOpenOrdersAsync();
            await recorder.RecordTodayAsync();
            await LoadAsync();
            StatusMessage = "Refreshed from the broker.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

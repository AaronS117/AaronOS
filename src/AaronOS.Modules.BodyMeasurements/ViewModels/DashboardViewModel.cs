using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.BodyMeasurements.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.BodyMeasurements.ViewModels;

public class GoalProgress
{
    public required Goal Goal { get; init; }
    public decimal? CurrentValue { get; init; }
    public double ProgressFraction { get; init; }

    public string Label => $"{Goal.Metric} ({Goal.Direction}) → {Goal.TargetValue} {Goal.Metric.Unit()}";
    public string ProgressText => CurrentValue is null
        ? "No check-in data yet"
        : $"{CurrentValue} / {Goal.TargetValue} {Goal.Metric.Unit()}";
}

public partial class DashboardViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    [ObservableProperty]
    private decimal? _latestWeightLb;

    [ObservableProperty]
    private decimal? _bmi;

    [ObservableProperty]
    private DateOnly? _latestCheckInDate;

    public List<GoalProgress> ActiveGoals { get; } = [];
    public List<ISeries> WeightSeries { get; } = [];
    public List<ICartesianAxis> WeightAxes { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var checkIns = await db.Set<BodyCheckIn>()
                .OrderBy(c => c.Date)
                .ToListAsync();

            var latest = checkIns.LastOrDefault();
            LatestWeightLb = latest?.WeightLb;
            LatestCheckInDate = latest?.Date;

            var profile = await db.UserProfiles.FirstOrDefaultAsync();
            Bmi = BmiCalculator.Calculate(LatestWeightLb, profile?.HeightInches);

            ActiveGoals.Clear();
            var goals = await db.Set<Goal>().Where(g => !g.IsAchieved).ToListAsync();
            foreach (var goal in goals)
            {
                var currentValue = latest is null ? null : goal.Metric.GetValue(latest);
                var fraction = ComputeProgress(goal, currentValue);
                ActiveGoals.Add(new GoalProgress { Goal = goal, CurrentValue = currentValue, ProgressFraction = fraction });
            }

            WeightSeries.Clear();
            WeightAxes.Clear();
            var weighed = checkIns.Where(c => c.WeightLb is not null).ToList();
            if (weighed.Count > 0)
            {
                WeightSeries.Add(new LineSeries<double>
                {
                    Values = weighed.Select(c => (double)c.WeightLb!.Value).ToArray(),
                    Name = "Weight (lb)"
                });
                WeightAxes.Add(new Axis
                {
                    Labels = weighed.Select(c => c.Date.ToString("MMM d")).ToArray()
                });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static double ComputeProgress(Goal goal, decimal? currentValue)
    {
        if (currentValue is null || goal.TargetValue == goal.StartValue)
        {
            return 0;
        }

        var fraction = (double)((currentValue.Value - goal.StartValue) / (goal.TargetValue - goal.StartValue));
        return Math.Clamp(fraction, 0, 1);
    }
}

using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.BodyMeasurements.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.BodyMeasurements.ViewModels;

public partial class GoalsViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<Goal> Goals { get; } = [];
    public IReadOnlyList<GoalMetric> Metrics { get; } = Enum.GetValues<GoalMetric>();
    public IReadOnlyList<GoalDirection> Directions { get; } = Enum.GetValues<GoalDirection>();

    [ObservableProperty]
    private GoalMetric _newMetric = GoalMetric.Weight;

    [ObservableProperty]
    private GoalDirection _newDirection = GoalDirection.Lose;

    [ObservableProperty]
    private double _newStartValue = double.NaN;

    [ObservableProperty]
    private double _newTargetValue = double.NaN;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var goals = await db.Set<Goal>()
                .OrderByDescending(g => g.CreatedDate)
                .ToListAsync();

            Goals.Clear();
            foreach (var goal in goals)
            {
                Goals.Add(goal);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (double.IsNaN(NewStartValue) || double.IsNaN(NewTargetValue))
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var goal = new Goal
        {
            Metric = NewMetric,
            Direction = NewDirection,
            StartValue = (decimal)NewStartValue,
            TargetValue = (decimal)NewTargetValue,
            CreatedDate = DateOnly.FromDateTime(DateTime.Now)
        };
        db.Add(goal);
        await db.SaveChangesAsync();

        Goals.Insert(0, goal);
        NewStartValue = double.NaN;
        NewTargetValue = double.NaN;
    }

    [RelayCommand]
    private async Task MarkAchievedAsync(Goal goal)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Attach(goal);
        goal.IsAchieved = true;
        await db.SaveChangesAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(Goal goal)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Remove(goal);
        await db.SaveChangesAsync();
        Goals.Remove(goal);
    }
}

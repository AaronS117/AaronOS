namespace AaronOS.Modules.BodyMeasurements.Data;

public static class GoalMetricExtensions
{
    public static decimal? GetValue(this GoalMetric metric, BodyCheckIn checkIn) => metric switch
    {
        GoalMetric.Weight => checkIn.WeightLb,
        GoalMetric.Neck => checkIn.NeckIn,
        GoalMetric.Chest => checkIn.ChestIn,
        GoalMetric.Waist => checkIn.WaistIn,
        GoalMetric.Hips => checkIn.HipsIn,
        GoalMetric.BicepLeft => checkIn.BicepLeftIn,
        GoalMetric.BicepRight => checkIn.BicepRightIn,
        GoalMetric.ThighLeft => checkIn.ThighLeftIn,
        GoalMetric.ThighRight => checkIn.ThighRightIn,
        GoalMetric.CalfLeft => checkIn.CalfLeftIn,
        GoalMetric.CalfRight => checkIn.CalfRightIn,
        _ => null
    };

    public static string Unit(this GoalMetric metric) => metric == GoalMetric.Weight ? "lb" : "in";
}

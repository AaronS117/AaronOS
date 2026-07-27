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

    /// <summary>Writes a metric back onto a check-in — the counterpart to <see cref="GetValue"/>, used
    /// when editing a single measurement by clicking it on the 3D figure.</summary>
    public static void SetValue(this GoalMetric metric, BodyCheckIn checkIn, decimal? value)
    {
        switch (metric)
        {
            case GoalMetric.Weight: checkIn.WeightLb = value; break;
            case GoalMetric.Neck: checkIn.NeckIn = value; break;
            case GoalMetric.Chest: checkIn.ChestIn = value; break;
            case GoalMetric.Waist: checkIn.WaistIn = value; break;
            case GoalMetric.Hips: checkIn.HipsIn = value; break;
            case GoalMetric.BicepLeft: checkIn.BicepLeftIn = value; break;
            case GoalMetric.BicepRight: checkIn.BicepRightIn = value; break;
            case GoalMetric.ThighLeft: checkIn.ThighLeftIn = value; break;
            case GoalMetric.ThighRight: checkIn.ThighRightIn = value; break;
            case GoalMetric.CalfLeft: checkIn.CalfLeftIn = value; break;
            case GoalMetric.CalfRight: checkIn.CalfRightIn = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unmapped metric");
        }
    }

    /// <summary>"Bicep Left" rather than "BicepLeft", for labels.</summary>
    public static string Label(this GoalMetric metric) => metric switch
    {
        GoalMetric.BicepLeft => "Bicep, left",
        GoalMetric.BicepRight => "Bicep, right",
        GoalMetric.ThighLeft => "Thigh, left",
        GoalMetric.ThighRight => "Thigh, right",
        GoalMetric.CalfLeft => "Calf, left",
        GoalMetric.CalfRight => "Calf, right",
        _ => metric.ToString()
    };

    public static string Unit(this GoalMetric metric) => metric == GoalMetric.Weight ? "lb" : "in";
}

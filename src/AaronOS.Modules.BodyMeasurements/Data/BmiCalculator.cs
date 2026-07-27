using AaronOS.Core.Data;

namespace AaronOS.Modules.BodyMeasurements.Data;

public static class BmiCalculator
{
    /// <summary>
    /// BMI, or null when it cannot be worked out.
    ///
    /// Height is checked against human range rather than merely against zero. Dividing by height
    /// squared means a wrong height does not produce a slightly wrong BMI, it produces a wildly wrong
    /// one — a height of 6 turned 240 lb into a BMI near 4,700. Showing nothing is the honest answer.
    /// </summary>
    public static decimal? Calculate(decimal? weightLb, decimal? heightInches)
    {
        if (weightLb is not > 0 || heightInches is not { } height || !BodyMetrics.IsPlausibleHeight(height))
        {
            return null;
        }

        return Math.Round(703m * weightLb.Value / (height * height), 1);
    }
}

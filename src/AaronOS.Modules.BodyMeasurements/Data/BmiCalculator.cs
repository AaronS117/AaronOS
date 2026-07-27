namespace AaronOS.Modules.BodyMeasurements.Data;

public static class BmiCalculator
{
    public static decimal? Calculate(decimal? weightLb, decimal? heightInches)
    {
        if (weightLb is not > 0 || heightInches is not > 0)
        {
            return null;
        }

        return Math.Round(703m * weightLb.Value / (heightInches.Value * heightInches.Value), 1);
    }
}

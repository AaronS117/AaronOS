namespace AaronOS.Modules.BodyMeasurements.Data;

public class Goal
{
    public int Id { get; set; }
    public GoalMetric Metric { get; set; }
    public GoalDirection Direction { get; set; }
    public decimal StartValue { get; set; }
    public decimal TargetValue { get; set; }
    public DateOnly? TargetDate { get; set; }
    public DateOnly CreatedDate { get; set; }
    public bool IsAchieved { get; set; }
}

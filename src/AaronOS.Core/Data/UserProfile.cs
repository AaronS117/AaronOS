namespace AaronOS.Core.Data;

/// <summary>Single-row profile table. Height feeds BMI and any future module that needs it.</summary>
public class UserProfile
{
    public int Id { get; set; }
    public decimal HeightInches { get; set; }
}

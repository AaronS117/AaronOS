using AaronOS.Modules.Medical.Import;

namespace AaronOS.Modules.Medical.Tests;

public class Hl7TimeTests
{
    [Fact]
    public void ParsesDateOnly() => Assert.Equal(new DateOnly(2026, 1, 15), Hl7Time.ParseDate("20260115"));

    [Fact]
    public void ParsesFullTimestamp() => Assert.Equal(new DateOnly(2026, 1, 15), Hl7Time.ParseDate("20260115143000"));

    [Fact]
    public void ParsesTimestampWithPositiveZoneOffset() =>
        Assert.Equal(new DateOnly(2026, 1, 15), Hl7Time.ParseDate("20260115143000+0500"));

    [Fact]
    public void ParsesTimestampWithNegativeZoneOffset() =>
        Assert.Equal(new DateOnly(2026, 1, 15), Hl7Time.ParseDate("20260115143000-0800"));

    [Fact]
    public void ParsesYearMonthPrecision() => Assert.Equal(new DateOnly(2026, 1, 1), Hl7Time.ParseDate("202601"));

    [Fact]
    public void ParsesYearPrecision() => Assert.Equal(new DateOnly(2026, 1, 1), Hl7Time.ParseDate("2026"));

    [Fact]
    public void ParsesDespiteSurroundingWhitespace() =>
        Assert.Equal(new DateOnly(2026, 1, 15), Hl7Time.ParseDate("  20260115  "));

    [Fact]
    public void ReturnsNullRatherThanThrowing_ForAnythingUnusable()
    {
        // A single malformed date in a long document must not abort the whole import.
        Assert.Null(Hl7Time.ParseDate(null));
        Assert.Null(Hl7Time.ParseDate(""));
        Assert.Null(Hl7Time.ParseDate("   "));
        Assert.Null(Hl7Time.ParseDate("not-a-date"));
        Assert.Null(Hl7Time.ParseDate("99999999"));
        Assert.Null(Hl7Time.ParseDate("20261301"));  // month 13
        Assert.Null(Hl7Time.ParseDate("20260230"));  // 30th February
    }
}

using System.Globalization;
using System.Windows.Data;
using AaronOS.Modules.Finance.ViewModels;
using AaronOS.Modules.Finance.Views;

namespace AaronOS.Modules.Finance.Tests;

/// <summary>
/// The two pieces of display logic that are easy to get quietly wrong: the null handling a numeric
/// editor needs, and the axis shorthand.
/// </summary>
public class RetirementDisplayTests
{
    private static readonly NullableNumberConverter Converter = new();
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void ANullSourceBecomesANullEditorValueInsteadOfThrowing()
    {
        // WPF's own conversion throws "Null object cannot be converted to a value type" here, which
        // is the whole reason this converter exists.
        Assert.Null(Converter.Convert(null, typeof(double?), null, Culture));
    }

    [Fact]
    public void ADecimalSourceConvertsToTheEditorsDouble()
    {
        Assert.Equal(1_234.5d, Converter.Convert(1_234.5m, typeof(double?), null, Culture));
    }

    [Theory]
    [InlineData(typeof(decimal?))]
    [InlineData(typeof(int?))]
    public void ClearingAnOptionalFieldWritesNull(Type target)
    {
        Assert.Null(Converter.ConvertBack(null, target, null, Culture));
    }

    [Fact]
    public void ClearingARequiredFieldLeavesThePreviousValueAlone()
    {
        // Writing 0 would be inventing a number the user never typed, and failing the conversion
        // would log an error on every keystroke while the box is empty.
        Assert.Equal(Binding.DoNothing, Converter.ConvertBack(null, typeof(decimal), null, Culture));
    }

    [Fact]
    public void ATypedValueRoundTripsBackToTheSourceType()
    {
        Assert.Equal(42m, Converter.ConvertBack(42d, typeof(decimal?), null, Culture));
        Assert.Equal(6, Converter.ConvertBack(6d, typeof(int?), null, Culture));
    }

    [Theory]
    [InlineData(0, "$0")]
    [InlineData(750, "$750")]
    [InlineData(250_000, "$250k")]
    [InlineData(500_000, "$500k")]
    [InlineData(1_000_000, "$1M")]
    [InlineData(1_500_000, "$1.5M")]
    public void AxisLabels_UseOneConsistentShorthand(double value, string expected)
    {
        // The default labeler mixed "500000" with "1.5 M" on the same axis.
        Assert.Equal(expected, RetirementViewModel.FormatMoneyAxis(value));
    }
}

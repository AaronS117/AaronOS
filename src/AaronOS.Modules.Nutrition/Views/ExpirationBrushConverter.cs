using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AaronOS.Modules.Nutrition.Views;

/// <summary>Small, purpose-specific IValueConverter for expiration color-coding — date-vs-today
/// comparison isn't representable by the NumberBox NaN-sentinel pattern used elsewhere in this
/// codebase, so a converter is the right tool here rather than a workaround. Returns
/// DependencyProperty.UnsetValue (NOT a Transparent brush) for the "fine" case, so the TextBlock
/// keeps its normal theme foreground instead of rendering invisible text.</summary>
public class ExpirationBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateOnly expiresOn)
        {
            return DependencyProperty.UnsetValue;
        }

        var daysLeft = expiresOn.DayNumber - DateOnly.FromDateTime(DateTime.Now).DayNumber;
        return daysLeft switch
        {
            < 0 => Brushes.IndianRed,
            <= 3 => Brushes.Orange,
            _ => DependencyProperty.UnsetValue
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AaronOS.Modules.Finance.Views;

/// <summary>
/// Bridges a numeric editor's <c>double?</c> value to a <c>decimal?</c> or <c>int?</c> property.
///
/// WPF's built-in conversion handles decimal to double perfectly well and then throws on null:
/// "Null object cannot be converted to a value type". The binding silently falls back and the field
/// renders empty, so a goal with no target amount looked identical to a broken one. Every numeric
/// editor on the page routes through here rather than only the nullable ones, because the same
/// converter also fixes the reverse case — clearing a required field leaves the previous value in
/// place instead of logging a failed conversion on every keystroke.
/// </summary>
public sealed class NullableNumberConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? null : System.Convert.ChangeType(value, Underlying(targetType), culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not null)
        {
            return System.Convert.ChangeType(value, Underlying(targetType), culture);
        }

        // An empty box means "no value" for a nullable property and nothing at all for a required
        // one, where holding the last good value beats writing a zero the user never typed.
        return IsNullable(targetType) ? null : Binding.DoNothing;
    }

    private static bool IsNullable(Type type) => Nullable.GetUnderlyingType(type) is not null;

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;
}

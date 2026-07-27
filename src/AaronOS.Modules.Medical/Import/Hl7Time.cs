using System.Globalization;

namespace AaronOS.Modules.Medical.Import;

/// <summary>
/// Parses HL7 v3 TS values as they appear in C-CDA: <c>YYYY</c>, <c>YYYYMM</c>, <c>YYYYMMDD</c>,
/// <c>YYYYMMDDHHMMSS</c>, any of them optionally followed by a <c>±ZZZZ</c> zone offset.
///
/// Only the date is kept — every consumer here stores a <see cref="DateOnly"/>, and the time of day
/// a result was drawn is not information this app surfaces. The zone offset is therefore discarded
/// rather than applied: shifting a date across midnight to satisfy a timezone would move a clinical
/// event to the wrong day, which is worse than ignoring an offset the UI never shows.
///
/// Anything unusable returns null instead of throwing, because one malformed date in a long export
/// must not abort an import of hundreds of good records.
/// </summary>
public static class Hl7Time
{
    // Longest first: the format is chosen by how many digits the value actually has.
    private static readonly string[] Formats =
        ["yyyyMMddHHmmss", "yyyyMMddHHmm", "yyyyMMddHH", "yyyyMMdd", "yyyyMM", "yyyy"];

    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = value.Trim();

        // Drop a zone offset if present; the date portion always precedes it.
        var offset = digits.IndexOfAny(['+', '-']);
        if (offset > 0)
        {
            digits = digits[..offset];
        }

        // Keep only the leading run of digits — some producers append fractional seconds.
        var end = 0;
        while (end < digits.Length && char.IsAsciiDigit(digits[end]))
        {
            end++;
        }
        digits = digits[..end];

        // Exactly one attempt, against the format matching the value's precision. Deliberately no
        // cascade to shorter formats: falling back would turn an invalid "20260230" into February
        // 2026 and "99999999" into the year 9999. A wrong date in a medical record is worse than a
        // missing one, so an unparseable value is rejected rather than quietly coarsened.
        var format = Array.Find(Formats, f => digits.Length >= f.Length);
        if (format is null)
        {
            return null;
        }

        return DateTime.TryParseExact(
            digits[..format.Length],
            format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? DateOnly.FromDateTime(parsed)
            : null;
    }
}

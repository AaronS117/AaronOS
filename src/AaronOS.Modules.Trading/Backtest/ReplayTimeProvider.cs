namespace AaronOS.Modules.Trading.Backtest;

/// <summary>
/// A clock the replay advances itself.
///
/// Hand-written rather than taking a dependency on Microsoft's testing TimeProvider, because the only
/// behaviour needed is "return the date I set" — timers, delays and cancellation all stay on the base
/// implementation. The reason the agent needs this at all is the daily order cap: it counts orders
/// stamped since midnight, so a replay reading the real clock would treat six months of trading as a
/// single day and refuse everything after the first few orders.
/// </summary>
public sealed class ReplayTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ReplayTimeProvider(DateOnly startDate) => SetDate(startDate);

    /// <summary>
    /// Positions the clock in the middle of the given trading day. Midday rather than midnight so
    /// that a date read in local time and a date read in UTC agree, whatever the machine's offset.
    /// </summary>
    public void SetDate(DateOnly date) =>
        _now = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

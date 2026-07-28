namespace AaronOS.Modules.Schedule.Calendar;

/// <summary>
/// An item plus where it sits horizontally. <paramref name="LaneCount"/> is the lane count of this
/// item's overlap CLUSTER, not of the whole day — so an isolated block keeps full width even when
/// another pair overlaps elsewhere in the same day.
/// </summary>
public sealed record PositionedItem(CalendarItem Item, int Lane, int LaneCount);

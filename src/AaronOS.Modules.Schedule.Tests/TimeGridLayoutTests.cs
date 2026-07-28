using AaronOS.Modules.Schedule.Calendar;

namespace AaronOS.Modules.Schedule.Tests;

public class TimeGridLayoutTests
{
    private static readonly DateOnly Day = new(2026, 7, 28);

    private static CalendarItem At(string label, int fromHour, int fromMin, int toHour, int toMin) =>
        new(Day, new TimeSpan(fromHour, fromMin, 0), new TimeSpan(toHour, toMin, 0),
            label, CalendarItemKind.Meeting, null);

    private static PositionedItem Find(IReadOnlyList<PositionedItem> all, string label) =>
        all.Single(p => p.Item.Label == label);

    [Fact]
    public void NonOverlappingItemsAllTakeTheFullWidth()
    {
        var result = TimeGridLayout.Assign([At("A", 9, 0, 10, 0), At("B", 11, 0, 12, 0)]);

        Assert.All(result, p => Assert.Equal(0, p.Lane));
        Assert.All(result, p => Assert.Equal(1, p.LaneCount));
    }

    [Fact]
    public void TwoOverlappingItemsSitSideBySide()
    {
        var result = TimeGridLayout.Assign([At("A", 9, 0, 10, 0), At("B", 9, 30, 10, 30)]);

        Assert.Equal(0, Find(result, "A").Lane);
        Assert.Equal(1, Find(result, "B").Lane);
        Assert.All(result, p => Assert.Equal(2, p.LaneCount));
    }

    [Fact]
    public void AnUnrelatedItemKeepsFullWidthWhenAnotherPairOverlaps()
    {
        // The defect this exists to catch: computing LaneCount per DAY would make C half width too.
        var result = TimeGridLayout.Assign(
            [At("A", 9, 0, 10, 0), At("B", 9, 30, 10, 30), At("C", 14, 0, 15, 0)]);

        Assert.Equal(2, Find(result, "A").LaneCount);
        Assert.Equal(2, Find(result, "B").LaneCount);
        Assert.Equal(1, Find(result, "C").LaneCount);
        Assert.Equal(0, Find(result, "C").Lane);
    }

    [Fact]
    public void ABridgedChainIsOneClusterEvenThoughTheEndsDoNotTouch()
    {
        // A and C do not overlap each other, but B overlaps both, so all three share a lane count.
        // A per-pair lane count would give C a count of 2 but A a count of 2 and leave a gap; a
        // naive "reset when the day is clear" approach would split them into separate clusters.
        var result = TimeGridLayout.Assign(
            [At("A", 9, 0, 10, 0), At("B", 9, 30, 11, 0), At("C", 10, 30, 11, 30)]);

        Assert.All(result, p => Assert.Equal(2, p.LaneCount));
        Assert.Equal(0, Find(result, "A").Lane);
        Assert.Equal(1, Find(result, "B").Lane);
        Assert.Equal(0, Find(result, "C").Lane); // A has ended, so lane 0 is free again
    }

    [Fact]
    public void ThreeSimultaneousItemsEachTakeAThird()
    {
        var result = TimeGridLayout.Assign(
            [At("A", 9, 0, 10, 0), At("B", 9, 0, 10, 0), At("C", 9, 0, 10, 0)]);

        Assert.All(result, p => Assert.Equal(3, p.LaneCount));
        Assert.Equal([0, 1, 2], result.Select(p => p.Lane).OrderBy(l => l).ToArray());
    }

    [Fact]
    public void AnEnclosedItemStillGetsItsOwnLane()
    {
        // B sits entirely inside A. This is the case a naive "does it start after the last end"
        // check gets wrong, because B starts and ends within A's span.
        var result = TimeGridLayout.Assign([At("A", 9, 0, 12, 0), At("B", 10, 0, 11, 0)]);

        Assert.Equal(0, Find(result, "A").Lane);
        Assert.Equal(1, Find(result, "B").Lane);
        Assert.All(result, p => Assert.Equal(2, p.LaneCount));
    }

    [Fact]
    public void AdjacentItemsDoNotCountAsOverlapping()
    {
        // A ends exactly when B starts. Back-to-back meetings must each keep full width, or a normal
        // day of consecutive meetings would render as a column of half-width slivers.
        var result = TimeGridLayout.Assign([At("A", 9, 0, 10, 0), At("B", 10, 0, 11, 0)]);

        Assert.All(result, p => Assert.Equal(1, p.LaneCount));
        Assert.All(result, p => Assert.Equal(0, p.Lane));
    }

    [Fact]
    public void InputOrderDoesNotChangeTheResult()
    {
        var forward = TimeGridLayout.Assign([At("A", 9, 0, 10, 0), At("B", 9, 30, 10, 30)]);
        var reversed = TimeGridLayout.Assign([At("B", 9, 30, 10, 30), At("A", 9, 0, 10, 0)]);

        Assert.Equal(Find(forward, "A").Lane, Find(reversed, "A").Lane);
        Assert.Equal(Find(forward, "B").Lane, Find(reversed, "B").Lane);
    }

    [Fact]
    public void EmptyInputReturnsEmpty() => Assert.Empty(TimeGridLayout.Assign([]));
}

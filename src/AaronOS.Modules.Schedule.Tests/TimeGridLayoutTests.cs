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
        // A (09:00-12:00) outlasts B (09:30-10:00), and C (10:30-11:00) starts after B has ended but
        // while A is still running. Ends are non-monotonic in processing order (12:00, 10:00, 11:00),
        // so a cluster-close rule that tracks only the previous item's end — instead of the running
        // max end seen in the cluster — would see B's 10:00 and wrongly close the cluster before C,
        // splitting C into its own cluster with LaneCount 1 and Lane 0. The cluster must instead stay
        // open because A has not ended, giving C a LaneCount of 2 and reusing B's freed lane.
        var result = TimeGridLayout.Assign(
            [At("A", 9, 0, 12, 0), At("B", 9, 30, 10, 0), At("C", 10, 30, 11, 0)]);

        Assert.All(result, p => Assert.Equal(2, p.LaneCount));
        Assert.Equal(0, Find(result, "A").Lane);
        Assert.Equal(1, Find(result, "B").Lane);
        Assert.Equal(1, Find(result, "C").Lane); // B has ended, so C reuses B's lane while A still runs
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
    public void AdjacentItemsShareALaneEvenInsideAnOpenCluster()
    {
        // A (09:00-11:00) overlaps both B and C, so the cluster-close guard never fires here — unlike
        // AdjacentItemsDoNotCountAsOverlapping, where that guard alone would mask a broken lane-reuse
        // comparison. With the cluster forced open by A, whether B (09:30-10:00) and C (10:00-10:30)
        // share a lane depends entirely on the `<=` in the lane-reuse check: C touches B's end exactly,
        // so it must reuse B's lane rather than take a third one.
        var result = TimeGridLayout.Assign(
            [At("A", 9, 0, 11, 0), At("B", 9, 30, 10, 0), At("C", 10, 0, 10, 30)]);

        Assert.All(result, p => Assert.Equal(2, p.LaneCount));
        Assert.Equal(1, Find(result, "C").Lane); // C reuses B's lane: 10:00 <= 10:00
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

    [Fact]
    public void TopIsProportionalToTheStartTime()
    {
        Assert.Equal(0d, TimeGridLayout.TopFor(TimeSpan.Zero));
        Assert.Equal(TimeGridLayout.HourHeight, TimeGridLayout.TopFor(new TimeSpan(1, 0, 0)));
        Assert.Equal(TimeGridLayout.HourHeight * 9.5, TimeGridLayout.TopFor(new TimeSpan(9, 30, 0)));
    }

    [Fact]
    public void HeightIsProportionalToDuration()
    {
        var half = new CalendarItem(new DateOnly(2026, 7, 28), new TimeSpan(9, 0, 0),
            new TimeSpan(9, 30, 0), "x", CalendarItemKind.Other, null);

        Assert.Equal(TimeGridLayout.HourHeight / 2, TimeGridLayout.HeightFor(half));
    }

    [Fact]
    public void AVeryShortItemIsFlooredToTheMinimumHeight()
    {
        // 15 minutes at PixelsPerMinute is 12px, below the 14px a label needs for one line of text.
        var short_ = new CalendarItem(new DateOnly(2026, 7, 28), new TimeSpan(8, 30, 0),
            new TimeSpan(8, 45, 0), "x", CalendarItemKind.Other, null);

        Assert.Equal(TimeGridLayout.MinItemHeight, TimeGridLayout.HeightFor(short_));
    }

    [Fact]
    public void ALongItemIsNotAffectedByTheMinimumHeightFloor()
    {
        // Guards against a floor applied unconditionally to every item, not just short ones.
        var hour = new CalendarItem(new DateOnly(2026, 7, 28), new TimeSpan(9, 0, 0),
            new TimeSpan(10, 0, 0), "x", CalendarItemKind.Other, null);

        Assert.Equal(TimeGridLayout.HourHeight, TimeGridLayout.HeightFor(hour));
    }

    [Fact]
    public void AFullDayIsTheWholeGridHeight()
    {
        Assert.Equal(TimeGridLayout.DayHeight, TimeGridLayout.TopFor(TimeSpan.FromHours(24)));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(48, 1, 0)]        // exactly 09:00-equivalent: one hour down
    [InlineData(52, 1, 0)]        // 5 minutes in, snaps back to the hour
    [InlineData(58, 1, 15)]       // 12.5 minutes in, snaps forward to :15
    public void TimeAtSnapsToTheNearestQuarterHour(double y, int expectedHours, int expectedMinutes)
    {
        Assert.Equal(new TimeSpan(expectedHours, expectedMinutes, 0), TimeGridLayout.TimeAt(y));
    }

    [Fact]
    public void TimeAtIsClampedToTheDay()
    {
        // A click below the last row, or a negative y from a transform, must not produce a time
        // outside the day — a block starting at 25:00 would silently never render.
        Assert.Equal(TimeSpan.Zero, TimeGridLayout.TimeAt(-40));
        Assert.Equal(new TimeSpan(23, 45, 0), TimeGridLayout.TimeAt(TimeGridLayout.DayHeight + 500));
    }
}

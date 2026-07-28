namespace AaronOS.Modules.Schedule.Calendar;

/// <summary>
/// Where each item sits in a day column, and how pixels map to times. Pure: takes values, returns
/// values, no clock and no visual types — which is what makes the awkward cases testable.
/// </summary>
public static class TimeGridLayout
{
    public static IReadOnlyList<PositionedItem> Assign(IReadOnlyList<CalendarItem> itemsForOneDay)
    {
        if (itemsForOneDay.Count == 0) return [];

        // Longest-first on a tie so a long block takes the left lane and short ones stack to its
        // right, which is what Outlook does and reads better than the reverse.
        var ordered = itemsForOneDay
            .OrderBy(i => i.Start)
            .ThenByDescending(i => i.End)
            .ThenBy(i => i.Label, StringComparer.Ordinal) // total order: same input -> same output
            .ToList();

        var result = new List<PositionedItem>(ordered.Count);

        var laneEnds = new List<TimeSpan>();   // when the item currently in each lane finishes
        var cluster = new List<int>();         // indices into `result` for the open cluster
        var clusterEnd = TimeSpan.MinValue;    // latest end seen in the open cluster

        foreach (var item in ordered)
        {
            // A gap with nothing running closes the cluster: its lane count is now known.
            if (item.Start >= clusterEnd && cluster.Count > 0)
            {
                Close(result, cluster, laneEnds.Count);
                laneEnds.Clear();
                cluster.Clear();
                clusterEnd = TimeSpan.MinValue;
            }

            // Lowest lane whose occupant has finished. Strictly `<=` so back-to-back items share a
            // lane rather than being treated as an overlap.
            var lane = laneEnds.FindIndex(end => end <= item.Start);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(item.End);
            }
            else
            {
                laneEnds[lane] = item.End;
            }

            cluster.Add(result.Count);
            result.Add(new PositionedItem(item, lane, 1)); // LaneCount patched when the cluster closes
            if (item.End > clusterEnd) clusterEnd = item.End;
        }

        Close(result, cluster, laneEnds.Count);
        return result;

        static void Close(List<PositionedItem> all, List<int> indices, int laneCount)
        {
            foreach (var i in indices) all[i] = all[i] with { LaneCount = laneCount };
        }
    }
}

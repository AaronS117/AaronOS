using System.Windows;
using System.Windows.Controls;
using AaronOS.Modules.Schedule.Calendar;

namespace AaronOS.Modules.Schedule.Views;

/// <summary>
/// Arranges one day column's items by time and lane. Each child's DataContext is a PositionedItem;
/// vertical geometry comes from TimeGridLayout, horizontal from the lane fraction of the panel's own
/// width — which is why this is a Panel and not a set of bindings.
/// </summary>
public sealed class TimeGridPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            // Height is dictated by the item's duration, so give the child exactly that and let it
            // clip its own content rather than letting a long label stretch the row.
            var item = ItemOf(child);
            var height = item is null ? 0 : TimeGridLayout.HeightFor(item.Item);
            child.Measure(new Size(availableSize.Width, height));
        }

        // Always the full day tall: the column must scroll as one with the time gutter beside it,
        // so its height must not depend on how many items happen to be present.
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, TimeGridLayout.DayHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var positioned = ItemOf(child);
            if (positioned is null) { child.Arrange(new Rect(0, 0, 0, 0)); continue; }

            var laneWidth = finalSize.Width / Math.Max(positioned.LaneCount, 1);

            child.Arrange(new Rect(
                positioned.Lane * laneWidth,
                TimeGridLayout.TopFor(positioned.Item.Start),
                laneWidth,
                TimeGridLayout.HeightFor(positioned.Item)));
        }

        return new Size(finalSize.Width, TimeGridLayout.DayHeight);
    }

    /// <summary>ItemsControl wraps each item in a container, so read through to the DataContext.</summary>
    private static PositionedItem? ItemOf(UIElement child) =>
        (child as FrameworkElement)?.DataContext as PositionedItem;
}

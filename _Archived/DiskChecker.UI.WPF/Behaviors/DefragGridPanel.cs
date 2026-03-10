using System.Windows;
using System.Windows.Controls;

namespace DiskChecker.UI.WPF.Behaviors;

/// <summary>
/// Custom panel for placing items in a 100x10 grid
/// </summary>
public class DefragGridPanel : Panel
{
    private const int ColumnCount = 100;
    private const int RowCount = 10;

    protected override Size MeasureOverride(Size availableSize)
    {
        // If the available size is infinite in any dimension we must return a finite DesiredSize.
        // Measure children using per-cell constraints when possible and compute a sensible
        // desired size based on the largest child measured size.

        double perCellWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width / ColumnCount;
        double perCellHeight = double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : availableSize.Height / RowCount;

        double maxChildWidth = 0.0;
        double maxChildHeight = 0.0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(perCellWidth, perCellHeight));
            Size d = child.DesiredSize;
            if (!double.IsNaN(d.Width) && !double.IsInfinity(d.Width))
                maxChildWidth = Math.Max(maxChildWidth, d.Width);
            if (!double.IsNaN(d.Height) && !double.IsInfinity(d.Height))
                maxChildHeight = Math.Max(maxChildHeight, d.Height);
        }

        double desiredWidth = double.IsInfinity(availableSize.Width) ? (ColumnCount * maxChildWidth) : availableSize.Width;
        double desiredHeight = double.IsInfinity(availableSize.Height) ? (RowCount * maxChildHeight) : availableSize.Height;

        // Ensure finite, non-negative results
        if (double.IsNaN(desiredWidth) || double.IsInfinity(desiredWidth) || desiredWidth < 0)
            desiredWidth = ColumnCount * (double.IsNaN(maxChildWidth) || double.IsInfinity(maxChildWidth) ? 0.0 : maxChildWidth);
        if (double.IsNaN(desiredHeight) || double.IsInfinity(desiredHeight) || desiredHeight < 0)
            desiredHeight = RowCount * (double.IsNaN(maxChildHeight) || double.IsInfinity(maxChildHeight) ? 0.0 : maxChildHeight);

        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double cellWidth = finalSize.Width / ColumnCount;
        double cellHeight = finalSize.Height / RowCount;

        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (index >= RowCount * ColumnCount)
                break;

            int row = index / ColumnCount;
            int col = index % ColumnCount;

            double x = col * cellWidth;
            double y = row * cellHeight;

            child.Arrange(new Rect(x, y, cellWidth, cellHeight));
            index++;
        }

        return finalSize;
    }
}

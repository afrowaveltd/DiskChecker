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
        double cellWidth = availableSize.Width / ColumnCount;
        double cellHeight = availableSize.Height / RowCount;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(cellWidth, cellHeight));
        }

        return availableSize;
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

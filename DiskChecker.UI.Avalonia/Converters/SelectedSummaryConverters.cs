using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DiskChecker.UI.Avalonia.Converters;

/// <summary>
/// Multi-value converter that returns a background brush for a measurement list item,
/// highlighting the currently selected summary. Values: [item, selectedSummary].
/// </summary>
public class SelectedSummaryBrushConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSelected = values.Count >= 2 && ReferenceEquals(values[0], values[1]);
        return isSelected
            ? new SolidColorBrush(Color.Parse("#334B93")) // subtle primary tint
            : Brushes.Transparent;
    }
}

/// <summary>
/// Multi-value converter that returns a border brush for a measurement list item.
/// Values: [item, selectedSummary].
/// </summary>
public class SelectedSummaryBorderConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSelected = values.Count >= 2 && ReferenceEquals(values[0], values[1]);
        return isSelected
            ? new SolidColorBrush(Color.Parse("#004B93"))
            : Brushes.Transparent;
    }
}

/// <summary>
/// Multi-value converter that returns a border thickness for a measurement list item.
/// Values: [item, selectedSummary].
/// </summary>
public class SelectedSummaryThicknessConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSelected = values.Count >= 2 && ReferenceEquals(values[0], values[1]);
        return isSelected ? new Thickness(1) : new Thickness(0);
    }
}

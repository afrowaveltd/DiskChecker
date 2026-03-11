using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DiskChecker.UI.Avalonia.Converters;

/// <summary>
/// Converts speed value (0-100 MB/s) to a height for the graph bar (0-60px)
/// </summary>
public class SpeedToHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double speed)
        {
            // Clamp speed to 0-100 range and scale to 5-60px height
            var clampedSpeed = Math.Max(0, Math.Min(100, speed));
            var height = 5 + (clampedSpeed / 100.0) * 55;
            return height;
        }
        return 5.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
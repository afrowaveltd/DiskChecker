using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace DiskChecker.UI.Avalonia.Converters
{
    /// <summary>
    /// Converts a string of space-separated "x,y" point pairs into an IList of Avalonia Points.
    /// Used for Polyline.Points binding from ViewModel string properties.
    /// </summary>
    public class PointsStringConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string pointsStr || string.IsNullOrWhiteSpace(pointsStr))
            {
                return new List<Point>();
            }

            var result = new List<Point>();
            var pairs = pointsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in pairs)
            {
                var parts = pair.Split(',');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    result.Add(new Point(x, y));
                }
            }

            return result;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

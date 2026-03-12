using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace DiskChecker.UI.Avalonia.Converters;

/// <summary>
/// Converts grade letters to corresponding colors.
/// </summary>
public class GradeToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string grade)
        {
            return grade.ToUpperInvariant() switch
            {
                "A" => Brushes.DarkGreen,
                "B" => Brushes.Green,
                "C" => Brushes.YellowGreen,
                "D" => Brushes.Orange,
                "E" => Brushes.OrangeRed,
                "F" => Brushes.Red,
                _ => Brushes.Gray
            };
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
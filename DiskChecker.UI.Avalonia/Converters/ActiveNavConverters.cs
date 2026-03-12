using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace DiskChecker.UI.Avalonia.Converters;

/// <summary>
/// Converts boolean IsOn* property to background brush for navigation buttons.
/// Active button gets highlighted background, inactive gets transparent.
/// </summary>
public class ActiveNavConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive && isActive)
        {
            return new SolidColorBrush(Color.FromRgb(0x00, 0x4B, 0x93)); // Active: #004B93 (blue)
        }
        return new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA)); // Inactive: #F8F9FA (light gray)
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean IsOn* property to foreground brush for navigation buttons.
/// Active button gets white text, inactive gets dark text.
/// </summary>
public class ActiveNavTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive && isActive)
        {
            return new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)); // Active: White
        }
        return new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)); // Inactive: #1A1A1A (dark)
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
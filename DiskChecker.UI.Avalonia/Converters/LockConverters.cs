using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DiskChecker.UI.Avalonia.Converters;

/// <summary>
/// Converts boolean IsLocked to lock/unlock button text.
/// </summary>
public class LockTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isLocked)
        {
            return isLocked ? "🔓 Odemknout" : "🔒 Zamknout";
        }
        return "🔒 Zamknout";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean to background color for lock button.
/// </summary>
public class LockButtonConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isLocked)
        {
            return isLocked ? "#E74C3C" : "#27AE60";
        }
        return "#27AE60";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
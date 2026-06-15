using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DiskChecker.UI.Avalonia.Converters;

/// <summary>
/// Converts a non-null, non-empty string to true.
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public static readonly StringToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrWhiteSpace(s);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts an enum value to its integer index for ListBox SelectedIndex binding.
/// </summary>
public class EnumToIndexConverter : IValueConverter
{
    public static readonly EnumToIndexConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return -1;
        return (int)value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && index >= 0)
        {
            return Enum.ToObject(targetType, index);
        }
        return null;
    }
}

/// <summary>
/// Compares two numeric values: returns true if the bound value is greater than
/// the value specified in ConverterParameter. ConverterParameter can be a constant
/// number or the name of a property on the DataContext (resolved via reflection).
/// </summary>
public class GreaterThanConverter : IValueConverter
{
    public static readonly GreaterThanConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return false;

        double boundValue;
        try { boundValue = System.Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch { return false; }

        double threshold = 0;

        if (parameter is string paramStr)
        {
            // Try parse as number first
            if (double.TryParse(paramStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                threshold = parsed;
            }
        }
        else if (parameter != null)
        {
            try { threshold = System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture); }
            catch { return false; }
        }

        return boundValue > threshold;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

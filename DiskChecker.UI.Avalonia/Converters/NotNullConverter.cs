using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DiskChecker.UI.Avalonia.Converters;

/// <summary>
/// Converter that returns true if the value is not null.
/// </summary>
public class NotNullConverter : IValueConverter
{
    public static readonly NotNullConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DiskChecker.UI.Avalonia.Converters;

/// <summary>
/// Converts int to bool (true if > 0). Supports Invert parameter.
/// </summary>
public class IntToBoolConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool result = value is int i && i > 0;
        
        // Check if Invert is set via parameter or property
        if (parameter is string param && param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            return !result;
        
        return Invert ? !result : result;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
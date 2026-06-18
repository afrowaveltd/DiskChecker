using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DiskChecker.UI.Avalonia.Converters;

/// <summary>
/// Returns true when the bound enum value equals the ConverterParameter string.
/// Usage: {Binding Phase, Converter={StaticResource EnumEqConverter}, ConverterParameter=Idle}
/// </summary>
public class EnumEqConverter : IValueConverter
{
    public static readonly EnumEqConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue && parameter is string paramStr)
        {
            return enumValue.ToString().Equals(paramStr, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns true when the bound enum value does NOT equal the ConverterParameter string.
/// Usage: {Binding Phase, Converter={StaticResource EnumNeqConverter}, ConverterParameter=Idle}
/// </summary>
public class EnumNeqConverter : IValueConverter
{
    public static readonly EnumNeqConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue && parameter is string paramStr)
        {
            return !enumValue.ToString().Equals(paramStr, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns true when the bound enum value is one of the comma-separated values in ConverterParameter.
/// Usage: {Binding Phase, Converter={StaticResource EnumInConverter}, ConverterParameter=Running,Verifying}
/// </summary>
public class EnumInConverter : IValueConverter
{
    public static readonly EnumInConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue && parameter is string paramStr)
        {
            var parts = paramStr.Split(',', StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (enumValue.ToString().Equals(part, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts an enum value to a human-readable display string.
/// Usage: {Binding Phase, Converter={StaticResource EnumDisplayConverter}}
/// </summary>
public class EnumDisplayConverter : IValueConverter
{
    public static readonly EnumDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            return enumValue.ToString() switch
            {
                "Idle" => "Nečinný",
                "Scanning" => "Skenování",
                "Ready" => "Připraven",
                "Running" => "Probíhá",
                "Verifying" => "Ověřování",
                "Completed" => "Dokončeno",
                "Failed" => "Selhalo",
                "Cancelled" => "Přerušeno",
                "Calculating" => "Kalkulace",
                _ => enumValue.ToString()
            };
        }
        return value?.ToString() ?? "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

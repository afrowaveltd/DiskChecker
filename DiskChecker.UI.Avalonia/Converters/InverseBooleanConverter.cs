using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DiskChecker.UI.Avalonia.Converters
{
    public class InverseBooleanConverter : IValueConverter
    {
        public static InverseBooleanConverter Instance { get; } = new InverseBooleanConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool booleanValue)
            {
                return !booleanValue;
            }
            if (value is int intValue)
            {
                return intValue == 0;
            }
            return true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool booleanValue)
            {
                return !booleanValue;
            }
            return true;
        }
    }
}
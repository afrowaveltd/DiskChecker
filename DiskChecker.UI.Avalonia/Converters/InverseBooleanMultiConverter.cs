using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DiskChecker.UI.Avalonia.Converters
{
    /// <summary>
    /// Multi-value converter that returns true if count is 0 (inverse logic for empty collections).
    /// </summary>
    public class InverseBooleanMultiConverter : IMultiValueConverter
    {
        public static InverseBooleanMultiConverter Instance { get; } = new InverseBooleanMultiConverter();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null || values.Count == 0)
                return true;
            
            // Pokud je hodnota count a je 0, vrátí true (visibile pro empty state)
            if (values[0] is int count)
            {
                return count == 0;
            }
            
            return true;
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            return Array.Empty<object>();
        }
    }
}
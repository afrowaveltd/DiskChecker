using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DiskChecker.UI.Avalonia.Converters
{
    /// <summary>
    /// Converts a boolean to a CSS class name for Avalonia styling.
    /// Usage: Classes="{Binding IsSelected, Converter={StaticResource BooleanToClassesConverter}}"
    /// Returns "selected" when true, empty string when false.
    /// </summary>
    public class BooleanToClassesConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // Default class name is "selected", can be overridden via parameter
            var className = parameter as string ?? "selected";
            
            if (value is bool boolValue && boolValue)
            {
                return className;
            }
            
            return string.Empty;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
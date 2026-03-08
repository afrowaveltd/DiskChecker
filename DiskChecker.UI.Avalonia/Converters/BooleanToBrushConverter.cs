using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DiskChecker.UI.Avalonia.Converters
{
    public class BooleanToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string param)
            {
                var parts = param.Split(';');
                foreach (var part in parts)
                {
                    var keyValue = part.Split(':');
                    if (keyValue.Length == 2)
                    {
                        var key = keyValue[0].Trim();
                        var color = keyValue[1].Trim();
                        
                        if (boolValue && key.Equals("Selected", StringComparison.OrdinalIgnoreCase))
                        {
                            return ParseColor(color);
                        }
                        else if (!boolValue && key.Equals("Normal", StringComparison.OrdinalIgnoreCase))
                        {
                            return ParseColor(color);
                        }
                    }
                }
            }
            
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static SolidColorBrush ParseColor(string colorString)
        {
            if (!string.IsNullOrEmpty(colorString) && colorString[0] == '#')
            {
                return SolidColorBrush.Parse(colorString);
            }

            return new SolidColorBrush(Colors.Black);
        }
    }
}
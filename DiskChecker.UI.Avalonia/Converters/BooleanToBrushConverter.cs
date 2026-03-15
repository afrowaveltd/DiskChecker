using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

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
                        
                        // Check if we should use this value
                        bool shouldUse = (boolValue && key.Equals("Selected", StringComparison.OrdinalIgnoreCase)) ||
                                        (!boolValue && key.Equals("Normal", StringComparison.OrdinalIgnoreCase));
                        
                        if (shouldUse)
                        {
                            return GetThemeBrush(color);
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
        
        private static IBrush GetThemeBrush(string resourceKey)
        {
            // First try to get from actual resources (respects runtime theme changes)
            var brush = TryGetResourceBrush(resourceKey);
            if (brush != null)
                return brush;
            
            // Fallback to theme-aware static colors
            bool isDark = IsDarkTheme();
            
            return resourceKey switch
            {
                "ThemePrimaryLightBrush" => isDark 
                    ? new SolidColorBrush(Color.Parse("#1A3A5C"))
                    : new SolidColorBrush(Color.Parse("#E3F2FD")),
                "ThemeCardBackgroundBrush" => isDark 
                    ? new SolidColorBrush(Color.Parse("#21262D"))
                    : new SolidColorBrush(Color.Parse("#FFFFFF")),
                "ThemePrimaryBrush" => isDark 
                    ? new SolidColorBrush(Color.Parse("#58A6FF"))
                    : new SolidColorBrush(Color.Parse("#004B93")),
                "ThemeBorderBrush" => isDark 
                    ? new SolidColorBrush(Color.Parse("#30363D"))
                    : new SolidColorBrush(Color.Parse("#DEE2E6")),
                "ThemeTextPrimaryBrush" => isDark 
                    ? new SolidColorBrush(Color.Parse("#F0F6FC"))
                    : new SolidColorBrush(Color.Parse("#1A1A1A")),
                "ThemeTextSecondaryBrush" => isDark 
                    ? new SolidColorBrush(Color.Parse("#C9D1D9"))
                    : new SolidColorBrush(Color.Parse("#495057")),
                "ThemeTextMutedBrush" => isDark 
                    ? new SolidColorBrush(Color.Parse("#8B949E"))
                    : new SolidColorBrush(Color.Parse("#6C757D")),
                _ => Brushes.Transparent
            };
        }
        
        private static IBrush? TryGetResourceBrush(string resourceKey)
        {
            try
            {
                // Use global:: to avoid namespace conflict with DiskChecker.UI.Avalonia
                var app = global::Avalonia.Application.Current;
                if (app != null && app.Resources != null)
                {
                    if (app.Resources.TryGetResource(resourceKey, null, out var resource))
                    {
                        if (resource is IBrush brush)
                            return brush;
                        if (resource is Color color)
                            return new SolidColorBrush(color);
                    }
                }
            }
            catch
            {
                // Ignore and fall back to static colors
            }
            return null;
        }
        
        private static bool IsDarkTheme()
        {
            try
            {
                var app = global::Avalonia.Application.Current;
                if (app == null)
                    return false;
                
                // Check ActualThemeVariant which updates at runtime
                if (app.ActualThemeVariant == ThemeVariant.Dark)
                    return true;
                
                // Also check requested theme
                if (app.RequestedThemeVariant == ThemeVariant.Dark)
                    return true;
                
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
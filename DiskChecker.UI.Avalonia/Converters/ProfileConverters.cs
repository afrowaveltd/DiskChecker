using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DiskChecker.UI.Avalonia.Converters
{
    /// <summary>
    /// Multi-value converter for profile button background.
    /// Values: [IsSelected, SelectedBrush, NormalBrush]
    /// </summary>
    public class ProfileBackgroundConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 3 && values[0] is bool isSelected)
            {
                var selectedBrush = values[1] as IBrush;
                var normalBrush = values[2] as IBrush;
                
                return isSelected ? selectedBrush : normalBrush;
            }
            
            return Brushes.Transparent;
        }
    }
    
    /// <summary>
    /// Multi-value converter for profile button border.
    /// Values: [IsSelected, SelectedBrush, NormalBrush]
    /// </summary>
    public class ProfileBorderConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 3 && values[0] is bool isSelected)
            {
                var selectedBrush = values[1] as IBrush;
                var normalBrush = values[2] as IBrush;
                
                return isSelected ? selectedBrush : normalBrush;
            }
            
            return Brushes.Transparent;
        }
    }
}
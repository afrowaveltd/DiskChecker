using System;
using Avalonia.Data.Converters;
using System.Globalization;
using Avalonia.Media;

namespace DiskChecker.UI.Avalonia.Converters
{
    public class FileSystemToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var fs = (value as string) ?? string.Empty;
            fs = fs.Trim().ToUpperInvariant();

            if (fs.Contains("NTFS"))
                return new SolidColorBrush(Color.Parse("#E8F5E9")); // light green
            if (fs.Contains("FAT") || fs.Contains("FAT32"))
                return new SolidColorBrush(Color.Parse("#FFF8E1")); // light yellow
            if (fs.Contains("EXFAT"))
                return new SolidColorBrush(Color.Parse("#E3F2FD")); // light blue
            if (fs.Contains("EXT") || fs.Contains("EXT4") || fs.Contains("EXT3"))
                return new SolidColorBrush(Color.Parse("#F3E5F5")); // light purple
            if (fs.Contains("ISO9660") || fs.Contains("UDF"))
                return new SolidColorBrush(Color.Parse("#ECEFF1")); // light gray
            if (fs.Contains("LVM") || fs.Contains("BTRFS") || fs.Contains("XFS"))
                return new SolidColorBrush(Color.Parse("#FFF3E0")); // light orange

            return new SolidColorBrush(Color.Parse("#FFFFFF")); // default white
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

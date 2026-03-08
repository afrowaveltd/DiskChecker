using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DiskChecker.UI.Avalonia.Converters;

/// <summary>
/// Converts disk properties to background colors for visual identification:
/// - System disk (boot): Light green (#E8F5E9)
/// - NTFS: Blue-ish (#E3F2FD)
/// - EXT (ext2/3/4): Orange-ish (#FFF3E0)
/// - APFS: Purple-ish (#F3E5F5)
/// - Other/Empty: Gray (#F5F5F5)
/// </summary>
public class DiskCardBackgroundConverter : IMultiValueConverter
{
    public static readonly DiskCardBackgroundConverter Instance = new();
    
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] == null || values[1] == null)
            return new SolidColorBrush(Color.Parse("#F5F5F5")); // Default gray
        
        try
        {
            var isSystemDisk = values[0] is bool b && b;
            var fileSystem = values[1]?.ToString() ?? string.Empty;
            var volumeInfo = values.Count > 2 ? values[2]?.ToString() ?? string.Empty : string.Empty;
            
            // System disk gets priority green
            if (isSystemDisk)
            {
                return new SolidColorBrush(Color.Parse("#E8F5E9")); // Light green
            }
            
            // Check file system type
            var fs = fileSystem.ToUpperInvariant();
            
            if (fs.Contains("NTFS"))
            {
                return new SolidColorBrush(Color.Parse("#E3F2FD")); // Light blue
            }
            else if (fs.Contains("EXT"))
            {
                return new SolidColorBrush(Color.Parse("#FFF3E0")); // Light orange
            }
            else if (fs.Contains("APFS"))
            {
                return new SolidColorBrush(Color.Parse("#F3E5F5")); // Light purple
            }
            else if (fs.Contains("FAT") || fs.Contains("EXFAT"))
            {
                return new SolidColorBrush(Color.Parse("#E0F7FA")); // Light cyan
            }
            else if (string.IsNullOrEmpty(volumeInfo))
            {
                // No volume info = empty/unallocated disk
                return new SolidColorBrush(Color.Parse("#FAFAFA")); // Very light gray
            }
            
            return new SolidColorBrush(Color.Parse("#F5F5F5")); // Default gray
        }
        catch
        {
            return new SolidColorBrush(Color.Parse("#F5F5F5")); // Default gray
        }
    }
}

/// <summary>
/// Converts file system type to accent color for border/indicator:
/// - System: Green (#4CAF50)
/// - NTFS: Blue (#2196F3)
/// - EXT: Orange (#FF9800)
/// - APFS: Purple (#9C27B0)
/// - Other: Gray (#9E9E9E)
/// </summary>
public class FileSystemColorConverter : IValueConverter
{
    public static readonly FileSystemColorConverter Instance = new();
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return new SolidColorBrush(Color.Parse("#9E9E9E"));
            
        var fs = value.ToString()?.ToUpperInvariant() ?? string.Empty;
        
        if (fs.Contains("NTFS"))
            return new SolidColorBrush(Color.Parse("#2196F3")); // Blue
        else if (fs.Contains("EXT"))
            return new SolidColorBrush(Color.Parse("#FF9800")); // Orange
        else if (fs.Contains("APFS"))
            return new SolidColorBrush(Color.Parse("#9C27B0")); // Purple
        else if (fs.Contains("FAT") || fs.Contains("EXFAT"))
            return new SolidColorBrush(Color.Parse("#00BCD4")); // Cyan
        else if (string.IsNullOrEmpty(fs))
            return new SolidColorBrush(Color.Parse("#BDBDBD")); // Light gray
        
        return new SolidColorBrush(Color.Parse("#9E9E9E")); // Gray
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts IsSystemDisk to a badge color
/// </summary>
public class SystemDiskBadgeConverter : IValueConverter
{
    public static readonly SystemDiskBadgeConverter Instance = new();
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSystem && isSystem)
        {
            return new SolidColorBrush(Color.Parse("#4CAF50")); // Green badge
        }
        return new SolidColorBrush(Colors.Transparent);
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bytes to human readable size (KB, MB, GB, TB)
/// </summary>
public class SizeConverter : IValueConverter
{
    public static readonly SizeConverter Instance = new();
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes)
            return "N/A";
            
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int suffixIndex = 0;
        double size = bytes;
        
        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }
        
        return $"{size:0.##} {suffixes[suffixIndex]}";
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

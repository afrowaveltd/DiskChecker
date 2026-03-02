using System.Globalization;
using System.Windows.Data;

namespace DiskChecker.UI.WPF.Converters;

/// <summary>
/// Converts bytes to human-readable string format (B, KB, MB, GB, TB).
/// </summary>
public class BytesToStringConverter : IValueConverter
{
    /// <summary>
    /// Converts bytes to human-readable string.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes)
            return "0 B";

        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;
        const long tb = gb * 1024;

        if (bytes >= tb)
            return $"{bytes / (double)tb:F2} TB";
        if (bytes >= gb)
            return $"{bytes / (double)gb:F2} GB";
        if (bytes >= mb)
            return $"{bytes / (double)mb:F2} MB";
        if (bytes >= kb)
            return $"{bytes / (double)kb:F2} KB";
        return $"{bytes} B";
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts double to formatted percentage string.
/// </summary>
public class PercentageConverter : IValueConverter
{
    /// <summary>
    /// Converts double to percentage format.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
            return $"{percent:F1}%";
        return "0.0%";
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts double to formatted MB/s string.
/// </summary>
public class MbpsConverter : IValueConverter
{
    /// <summary>
    /// Converts double to MB/s format.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double mbps)
            return $"{mbps:F1} MB/s";
        return "0.0 MB/s";
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts int to block label.
/// </summary>
public class BlockLabelConverter : IValueConverter
{
    /// <summary>
    /// Converts block index to label.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index)
            return $"Block {index}";
        return "Block 0";
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts int collection count to label.
/// </summary>
public class BlockCountLabelConverter : IValueConverter
{
    /// <summary>
    /// Converts count to label.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
            return $"Celkem bloků: {count}";
        return "Celkem bloků: 0";
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value.
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    /// <summary>
    /// Inverts boolean value.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }

    /// <summary>
    /// Inverts boolean value back.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }
}

/// <summary>
/// Checks if a value is not null.
/// </summary>
public class NotNullConverter : IValueConverter
{
    /// <summary>
    /// Converts value to non-null check result.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts status number to color brush.
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    /// <summary>
    /// Converts status to color.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int status)
        {
            return status switch
            {
                0 => "#E0E0E0",  // Untested - light gray
                1 => "#FFC107",  // Processing - yellow
                2 => "#4A90E2",  // Write OK - blue
                3 => "#28A745",  // Read OK - green
                4 => "#DC3545",  // Error - red
                _ => "#E0E0E0"
            };
        }
        return "#E0E0E0";
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts timespan to display format.
/// </summary>
public class TimeSpanConverter : IValueConverter
{
    /// <summary>
    /// Converts timespan to formatted string.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
                return timeSpan.ToString("hh\\:mm\\:ss");
            if (timeSpan.TotalMinutes >= 1)
                return timeSpan.ToString("mm\\:ss");
            return timeSpan.ToString("ss\\s");
        }
        return "0s";
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

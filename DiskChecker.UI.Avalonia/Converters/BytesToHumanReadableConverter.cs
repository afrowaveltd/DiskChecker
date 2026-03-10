using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace DiskChecker.UI.Avalonia.Converters
{
    public class BytesToHumanReadableConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return "-";
            if (!(value is long) && !(value is int) && !(value is double) && !(value is decimal))
                return value.ToString() ?? "-";

            double bytes = System.Convert.ToDouble(value);
            if (bytes <= 0) return "0 B";
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB" };
            int idx = (int)Math.Floor(Math.Log(bytes, 1024));
            idx = Math.Min(idx, suf.Length - 1);
            double val = Math.Round(bytes / Math.Pow(1024, idx), 2);
            return $"{val} {suf[idx]}";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

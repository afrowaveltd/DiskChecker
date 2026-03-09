using System;
namespace DiskChecker.Application.Extensions {
    public static class StringExtensions {
        public static string ToSafeString(this string? s) => s ?? string.Empty;
        public static int? ToIntOrNull(this string? s) => int.TryParse(s, out var i) ? i : (int?)null;
    }
}
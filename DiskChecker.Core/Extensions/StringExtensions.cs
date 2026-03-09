
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Extensions
{
    public static class StringExtensions
    {
        public static string ToSafeString(this string? value)
        {
            return value ?? string.Empty;
        }

        public static int? ToIntOrNull(this string? value)
        {
            if (int.TryParse(value, out var result))
            {
                return result;
            }
            return null;
        }

        public static SmartaSelfTestStatus? ToSmartaSelfTestStatus(this string? value)
        {
            if (value == null) return null;
            return value.ToLowerInvariant() switch
            {
                "completed without error" => SmartaSelfTestStatus.CompletedWithoutError,
                "aborted" => SmartaSelfTestStatus.AbortedByUser,
                "interrupted" => SmartaSelfTestStatus.AbortedByHost,
                "fatal" => SmartaSelfTestStatus.FatalError,
                "electrical" => SmartaSelfTestStatus.ErrorElectrical,
                "servo" => SmartaSelfTestStatus.ErrorServo,
                "read" => SmartaSelfTestStatus.ErrorRead,
                "handling" => SmartaSelfTestStatus.ErrorHandling,
                "in progress" => SmartaSelfTestStatus.InProgress,
                _ => SmartaSelfTestStatus.ErrorUnknown
            };
        }
    }
}

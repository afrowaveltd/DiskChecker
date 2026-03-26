using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace DiskChecker.Application.Services;

/// <summary>
/// Helper for building unique drive identity keys.
/// </summary>
public static class DriveIdentityResolver
{
    private const int MaxIdentityLength = 120;
    private static readonly string[] UnreliableSerialTokens =
    [
        "UNKNOWN",
        "N/A",
        "NONE",
        "NOTAVAILABLE",
        "TOBEFILLED",
        "NOTSPECIFIED",
        "SERIALNUMBER"
    ];

    /// <summary>
    /// Builds an identity key from drive information.
    /// </summary>
    public static string BuildIdentityKey(string drivePath, string? serialNumber, string deviceModel, string? firmwareVersion)
    {
        var normalizedSerial = NormalizeSerial(serialNumber);

        if (IsReliableSerialNumber(normalizedSerial))
        {
            return LimitLength(normalizedSerial);
        }

        var fingerprint = string.Concat(
            NormalizeToken(drivePath), "|",
            NormalizeToken(deviceModel), "|",
            NormalizeToken(firmwareVersion));

        var hash = ComputeHashHex(fingerprint);
        return $"NOSN-{hash[..24]}";
    }

    internal static string BuildLegacyIdentityKey(string drivePath, string? serialNumber, string deviceModel, string? firmwareVersion)
    {
        // Use serial number as primary identifier if available
        var normalizedSerial = NormalizeSerial(serialNumber);
        if (IsReliableSerialNumber(normalizedSerial))
        {
            return $"{normalizedSerial}_{NormalizeToken(deviceModel)}_{NormalizeToken(firmwareVersion)}";
        }
        
        // Fall back to path-based identifier
        if (!string.IsNullOrWhiteSpace(deviceModel))
        {
            return $"{NormalizeToken(deviceModel)}_{NormalizeToken(drivePath).Replace("\\", "_").Replace("/", "_")}_{NormalizeToken(firmwareVersion)}";
        }
        
        // Final fallback to path only
        return NormalizeToken(drivePath);
    }

    public static string NormalizeSerial(string? serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            return string.Empty;
        }

        var trimmed = serialNumber.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (!char.IsWhiteSpace(ch) && ch != '-' && ch != '_')
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return sb.ToString();
    }

    public static bool IsReliableSerialNumber(string? serialNumber)
    {
        var normalized = NormalizeSerial(serialNumber);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.StartsWith("NOSN", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.Length < 4)
        {
            return false;
        }

        if (UnreliableSerialTokens.Any(t => string.Equals(normalized, t, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Reject obvious placeholders like 00000000 or XXXXXXXX.
        if (normalized.All(ch => ch == normalized[0]))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
    }

    private static string LimitLength(string value)
    {
        if (value.Length <= MaxIdentityLength)
        {
            return value;
        }

        var hash = ComputeHashHex(value)[..16];
        var prefixLength = MaxIdentityLength - (hash.Length + 1);
        return $"{value[..prefixLength]}-{hash}";
    }

    private static string ComputeHashHex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

using System.Security.Cryptography;
using System.Text;

namespace DiskChecker.Application.Services;

/// <summary>
/// Helper for building unique drive identity keys.
/// </summary>
public static class DriveIdentityResolver
{
    private const int MaxIdentityLength = 120;

    /// <summary>
    /// Builds an identity key from drive information.
    /// </summary>
    public static string BuildIdentityKey(string drivePath, string? serialNumber, string deviceModel, string? firmwareVersion)
    {
        var normalizedSerial = Normalize(serialNumber);

        if (!string.IsNullOrWhiteSpace(normalizedSerial))
        {
            return LimitLength(normalizedSerial);
        }

        var fingerprint = string.Concat(
            Normalize(drivePath), "|",
            Normalize(deviceModel), "|",
            Normalize(firmwareVersion));

        var hash = ComputeHashHex(fingerprint);
        return $"NOSN-{hash[..24]}";
    }

    internal static string BuildLegacyIdentityKey(string drivePath, string? serialNumber, string deviceModel, string? firmwareVersion)
    {
        // Use serial number as primary identifier if available
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"{serialNumber}_{deviceModel}_{firmwareVersion ?? "unknown"}";
        }
        
        // Fall back to path-based identifier
        if (!string.IsNullOrWhiteSpace(deviceModel))
        {
            return $"{deviceModel}_{drivePath.Replace("\\", "_").Replace("/", "_")}_{firmwareVersion ?? "unknown"}";
        }
        
        // Final fallback to path only
        return drivePath;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

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
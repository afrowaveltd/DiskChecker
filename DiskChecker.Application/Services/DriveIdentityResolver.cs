namespace DiskChecker.Application.Services;

/// <summary>
/// Helper for building unique drive identity keys.
/// </summary>
public static class DriveIdentityResolver
{
    /// <summary>
    /// Builds an identity key from drive information.
    /// </summary>
    public static string BuildIdentityKey(string drivePath, string? serialNumber, string deviceModel, string? firmwareVersion)
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
}
namespace DiskChecker.Application.Services;

internal static class DriveIdentityResolver
{
    internal static string BuildIdentityKey(string? drivePath, string? serialNumber, string? model, string? firmware)
    {
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"SER:{serialNumber.Trim().ToUpperInvariant()}";
        }

        var normalizedPath = NormalizeDrivePath(drivePath);
        if (!string.IsNullOrWhiteSpace(normalizedPath))
        {
            return $"PATH:{normalizedPath}";
        }

        var modelPart = string.IsNullOrWhiteSpace(model) ? "UNKNOWN-MODEL" : model.Trim().ToUpperInvariant();
        var firmwarePart = string.IsNullOrWhiteSpace(firmware) ? "UNKNOWN-FW" : firmware.Trim().ToUpperInvariant();
        return $"SIG:{modelPart}|{firmwarePart}";
    }

    internal static string NormalizeDrivePath(string? drivePath)
    {
        if (string.IsNullOrWhiteSpace(drivePath))
        {
            return string.Empty;
        }

        return drivePath
            .Replace("\\\\.\\", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToUpperInvariant();
    }
}

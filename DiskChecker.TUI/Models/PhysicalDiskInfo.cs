namespace DiskChecker.TUI.Models;

/// <summary>
/// Represents a physical disk detected in the system.
/// </summary>
public sealed class PhysicalDiskInfo
{
    public int Index { get; init; }
    public string DevicePath { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string FirmwareRevision { get; init; } = string.Empty;
    public ulong CapacityBytes { get; init; }
    public string InterfaceType { get; init; } = string.Empty;
    public bool IsRemovable { get; init; }
    public bool IsUsb { get; init; }

    public string CapacityFormatted
    {
        get
        {
            double gb = CapacityBytes / 1_000_000_000.0;
            if (gb >= 1000)
                return $"{gb / 1000:F2} TB";
            return $"{gb:F1} GB";
        }
    }

    public string DisplayName => $"[{Index}] {Model} ({CapacityFormatted})";
    public string ShortName => $"{Model} [{Index}]";
}

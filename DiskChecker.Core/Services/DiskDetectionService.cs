using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Service for detecting connected/disconnected disks by comparing states.
/// Useful for identifying a specific test disk in a USB framework.
/// </summary>
public class DiskDetectionService
{
    /// <summary>
    /// Snapshot of disk state at a point in time.
    /// </summary>
    public class DiskSnapshot
    {
        /// <summary>
        /// Timestamp when snapshot was taken.
        /// </summary>
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// All disks present at this moment.
        /// </summary>
        public List<DiskIdentifier> DisksPresent { get; set; } = new();

        /// <summary>
        /// Simple string representation for debugging.
        /// </summary>
        public override string ToString() => $"{DisksPresent.Count} disks at {CapturedAt:HH:mm:ss}";
    }

    /// <summary>
    /// Unique disk identifier combining hardware and volume info.
    /// </summary>
    public class DiskIdentifier : IEquatable<DiskIdentifier>
    {
        /// <summary>
        /// Physical disk path (e.g., "\\.\PHYSICALDRIVE1" or "/dev/sdb").
        /// </summary>
        public required string Path { get; set; }

        /// <summary>
        /// Drive name from DriveInfo (e.g., "D:\").
        /// </summary>
        public string? DriveLetter { get; set; }

        /// <summary>
        /// Serial number from SMART if available.
        /// </summary>
        public string? SerialNumber { get; set; }

        /// <summary>
        /// Device model if available.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Total capacity in bytes.
        /// </summary>
        public long TotalBytes { get; set; }

        public bool Equals(DiskIdentifier? other)
        {
            if (other is null) return false;
            return Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj) => Equals(obj as DiskIdentifier);
        public override int GetHashCode() => Path.GetHashCode(StringComparison.OrdinalIgnoreCase);
        public override string ToString() => $"{Model ?? "Unknown"} ({Path}) - {TotalBytes / (1024L * 1024L * 1024L)}GB";
    }

    /// <summary>
    /// Result of comparing two snapshots.
    /// </summary>
    public class ComparisonResult
    {
        /// <summary>
        /// Disks that were present in first snapshot but not in second.
        /// </summary>
        public List<DiskIdentifier> Removed { get; set; } = new();

        /// <summary>
        /// Disks that were not present in first snapshot but are in second.
        /// </summary>
        public List<DiskIdentifier> Added { get; set; } = new();

        /// <summary>
        /// Disks present in both snapshots.
        /// </summary>
        public List<DiskIdentifier> Unchanged { get; set; } = new();

        public bool HasChanges => Removed.Any() || Added.Any();

        public override string ToString()
        {
            return $"Removed: {Removed.Count}, Added: {Added.Count}, Unchanged: {Unchanged.Count}";
        }
    }

    /// <summary>
    /// Takes a snapshot of currently connected disks.
    /// </summary>
    public DiskSnapshot CreateSnapshot(IEnumerable<CoreDriveInfo> drives)
    {
        var snapshot = new DiskSnapshot();

        foreach (var drive in drives)
        {
            var identifier = new DiskIdentifier
            {
                Path = drive.Path,
                DriveLetter = ExtractDriveLetter(drive.Path),
                TotalBytes = drive.TotalSize,
                Model = drive.Name
            };

            snapshot.DisksPresent.Add(identifier);
        }

        return snapshot;
    }

    /// <summary>
    /// Compares two snapshots to find what changed.
    /// </summary>
    public ComparisonResult Compare(DiskSnapshot before, DiskSnapshot after)
    {
        var result = new ComparisonResult();

        // Find removed disks
        foreach (var disk in before.DisksPresent)
        {
            if (!after.DisksPresent.Contains(disk))
            {
                result.Removed.Add(disk);
            }
        }

        // Find added disks
        foreach (var disk in after.DisksPresent)
        {
            if (!before.DisksPresent.Contains(disk))
            {
                result.Added.Add(disk);
            }
        }

        // Find unchanged disks
        foreach (var disk in before.DisksPresent)
        {
            if (after.DisksPresent.Contains(disk))
            {
                result.Unchanged.Add(disk);
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts drive letter from Windows path format (e.g., "E:\").
    /// </summary>
    private static string? ExtractDriveLetter(string path)
    {
        if (path.Length >= 2 && path[1] == ':')
        {
            return path.Substring(0, 2);
        }

        return null;
    }
}

using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>A snapshot of the physical target selected before a destructive operation.</summary>
public sealed record DestructiveTargetIdentity(string Path, long Capacity, string Serial, bool WasSystemDisk, bool WasPhysical)
{
    public static DestructiveTargetIdentity Capture(CoreDriveInfo drive) =>
        new(drive.Path, drive.TotalSize, DriveIdentityResolver.NormalizeSerial(drive.SerialNumber),
            drive.IsSystemDisk, drive.IsPhysical);
}

/// <summary>
/// Re-enumerates devices without using the UI cache before destructive I/O.
/// This is a phase-boundary check; it does not bind an OS handle to an identity.
/// </summary>
public sealed class DestructiveTargetGuard(IDiskDetectionService detection, ISettingsService settings)
{
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task VerifyAsync(DestructiveTargetIdentity expected, string requestedPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (expected.WasSystemDisk || !expected.WasPhysical || expected.Capacity <= 0 ||
            string.IsNullOrWhiteSpace(expected.Path) ||
            !string.Equals(expected.Path, requestedPath, PathComparison))
            throw new InvalidOperationException("Destructive operation refused: invalid or protected original target.");

        if (!DriveIdentityResolver.IsReliableSerialNumber(expected.Serial))
            throw new InvalidOperationException("Destructive operation refused: the original disk has no reliable serial number.");

        var locked = await settings.GetLockedDisksAsync();
        if (locked.Any(path => string.Equals(path, requestedPath, PathComparison)))
            throw new InvalidOperationException("Destructive operation refused: the selected disk is locked.");

        var live = await detection.GetDriveAsync(requestedPath, ct);
        ct.ThrowIfCancellationRequested();
        if (live == null || !live.IsPhysical || !live.IsReady || live.IsSystemDisk ||
            !string.Equals(live.Path, expected.Path, PathComparison))
            throw new InvalidOperationException("Destructive operation refused: the disk is absent, unavailable or protected.");

        if (live.TotalSize != expected.Capacity ||
            !DriveIdentityResolver.IsReliableSerialNumber(live.SerialNumber) ||
            !string.Equals(DriveIdentityResolver.NormalizeSerial(live.SerialNumber), expected.Serial, StringComparison.Ordinal))
            throw new InvalidOperationException("Destructive operation refused: the device identity has changed or cannot be verified.");
    }
}

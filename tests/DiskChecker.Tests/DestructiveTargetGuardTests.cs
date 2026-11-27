using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public sealed class DestructiveTargetGuardTests
{
    private static CoreDriveInfo Disk(string serial = "SERIAL123") => new()
    {
        Path = "/dev/sdb", TotalSize = 500_000_000_000, SerialNumber = serial,
        IsPhysical = true, IsReady = true
    };

    private static ISettingsService Settings(params string[] locked)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.GetLockedDisksAsync().Returns(Task.FromResult(new List<string>(locked)));
        return settings;
    }

    [Fact]
    public async Task UnchangedDevicePasses()
    {
        var detection = Substitute.For<IDiskDetectionService>();
        var original = Disk();
        detection.GetDriveAsync(original.Path, Arg.Any<CancellationToken>()).Returns(Task.FromResult<CoreDriveInfo?>(Disk()));
        await new DestructiveTargetGuard(detection, Settings())
            .VerifyAsync(DestructiveTargetIdentity.Capture(original), original.Path, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("capacity")]
    [InlineData("system")]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("locked")]
    [InlineData("original-system")]
    [InlineData("original-unknown")]
    public async Task RejectsChangedOrUnverifiableDevice(string failure)
    {
        var detection = Substitute.For<IDiskDetectionService>();
        var original = Disk();
        CoreDriveInfo? current = Disk();
        switch (failure)
        {
            case "replacement": current.SerialNumber = "OTHER987"; break;
            case "capacity": current.TotalSize--; break;
            case "system": current.IsSystemDisk = true; break;
            case "missing": current = null; break;
            case "unknown": current.SerialNumber = "UNKNOWN"; break;
            case "original-system": original.IsSystemDisk = true; break;
            case "original-unknown": original.SerialNumber = null; break;
        }
        detection.GetDriveAsync(original.Path, Arg.Any<CancellationToken>()).Returns(Task.FromResult(current));
        var settings = failure == "locked" ? Settings(original.Path) : Settings();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DestructiveTargetGuard(detection, settings).VerifyAsync(
                DestructiveTargetIdentity.Capture(original), original.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IdentitySnapshotIsNotChangedWhenUiSelectionObjectIsMutated()
    {
        var detection = Substitute.For<IDiskDetectionService>();
        var original = Disk();
        var identity = DestructiveTargetIdentity.Capture(original);
        original.SerialNumber = "OTHER987";
        detection.GetDriveAsync(original.Path, Arg.Any<CancellationToken>()).Returns(Task.FromResult<CoreDriveInfo?>(original));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DestructiveTargetGuard(detection, Settings()).VerifyAsync(
                identity, original.Path, TestContext.Current.CancellationToken));
    }
}

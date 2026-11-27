using System;
using DiskChecker.Infrastructure.Hardware;
using Xunit;

namespace DiskChecker.Tests;

public sealed class WindowsBackupVolumeSafetyTests
{
    [Fact]
    public void SeparateVolumeIsAllowed()
    {
        Assert.True(WindowsBackupVolumeSafety.IsIndependentVolume(@"\\.\PhysicalDrive2", new uint[] { 0, 1 }));
    }

    [Fact]
    public void EveryExtentMustBeIndependent()
    {
        Assert.False(WindowsBackupVolumeSafety.IsIndependentVolume(@"\\.\PHYSICALDRIVE2", new uint[] { 0, 2, 4 }));
        Assert.False(WindowsBackupVolumeSafety.IsIndependentVolume(@"\\.\PhysicalDrive2", new uint[] { 2 }));
    }

    [Fact]
    public void UnknownSourceOrDestinationFailsClosed()
    {
        Assert.False(WindowsBackupVolumeSafety.IsIndependentVolume(@"D:\", new uint[] { 0 }));
        Assert.False(WindowsBackupVolumeSafety.IsIndependentVolume(@"\\.\PhysicalDrive2", null));
        Assert.False(WindowsBackupVolumeSafety.IsIndependentVolume(@"\\.\PhysicalDrive2", Array.Empty<uint>()));
        Assert.False(WindowsBackupVolumeSafety.IsIndependentVolume(null, new uint[] { 0 }));
    }
}

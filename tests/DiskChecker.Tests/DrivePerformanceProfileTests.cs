using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Xunit;

namespace DiskChecker.Tests;

public class DrivePerformanceProfileTests
{
    [Theory]
    [InlineData(80_000_000_000L, "SATA", 25)]
    [InlineData(250_000_000_000L, "SATA", 35)]
    [InlineData(750_000_000_000L, "SATA", 45)]
    [InlineData(2_000_000_000_000L, "SATA", 55)]
    [InlineData(250_000_000_000L, "IDE", 30)]
    [InlineData(80_000_000_000L, "USB", 20)]
    public void RotatingDiskFloorUsesCapacityAndConnection(long bytes, string bus, double floor)
    {
        var profile = DrivePerformanceProfile.For("HDD", bus, bytes);
        Assert.Equal(floor, profile.WriteFloor);
        Assert.Equal(floor, profile.ReadFloor);
    }

    [Fact]
    public void UnknownMediaIsNotAssumedToBeRotating()
    {
        Assert.Equal("Unknown", DrivePerformanceProfile.ResolveMedia(null, "Generic device", "SATA"));
        Assert.Equal(0d, DrivePerformanceProfile.For("Unknown", "SATA", 80_000_000_000L).WriteFloor);
    }

    [Fact]
    public void WdVeteranMeetsBothFloorsAndNormalZonalDrop()
    {
        var smart = new SmartaData { DeviceModel = "WDC WD800JD", ModelFamily = "Western Digital Caviar SE" };
        Assert.Equal("HDD", DrivePerformanceProfile.ResolveMedia("Unknown", smart.DeviceModel, "SATA", smart));
        var profile = DrivePerformanceProfile.For("HDD", "SATA", 80_030_000_000L);
        Assert.True(48.3 >= profile.WriteFloor);
        Assert.True(48.1 >= profile.ReadFloor);
        Assert.True(1 - 32d / 58d <= profile.NormalMinPeakDrop);
    }

    [Fact]
    public void UsbFlashHasNoInventedSpeedFloor()
    {
        Assert.Equal(0d, DrivePerformanceProfile.For("SSD", "USB", 128_000_000_000L).WriteFloor);
        Assert.Equal("SSD", DrivePerformanceProfile.ResolveMedia(null, "Generic", "NVMe"));
    }
}

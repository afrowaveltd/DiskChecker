using DiskChecker.Application.Services;
using Xunit;

namespace DiskChecker.Tests;

public class DriveIdentityResolverTests
{
    [Fact]
    public void BuildIdentityKey_UsesNormalizedReliableSerial()
    {
        var key = DriveIdentityResolver.BuildIdentityKey(
            @"\\.\PHYSICALDRIVE1",
            "  ab-12 34_cd ",
            "Model X",
            "FW1");

        Assert.Equal("AB1234CD", key);
    }

    [Fact]
    public void BuildIdentityKey_UsesFingerprintWhenSerialIsUnreliable()
    {
        var key1 = DriveIdentityResolver.BuildIdentityKey(
            @"\\.\PHYSICALDRIVE0",
            "Unknown",
            "Model A",
            "FW1");
        var key2 = DriveIdentityResolver.BuildIdentityKey(
            @"\\.\PHYSICALDRIVE1",
            "Unknown",
            "Model A",
            "FW1");

        Assert.StartsWith("NOSN-", key1);
        Assert.StartsWith("NOSN-", key2);
        Assert.NotEqual(key1, key2);
    }

    [Theory]
    [InlineData("UNKNOWN", false)]
    [InlineData("NOSN-1234", false)]
    [InlineData("00000000", false)]
    [InlineData("A1B2C3D4", true)]
    public void IsReliableSerialNumber_FiltersPlaceholders(string serial, bool expected)
    {
        Assert.Equal(expected, DriveIdentityResolver.IsReliableSerialNumber(serial));
    }
}

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
    [InlineData("0000000000000000", false)]
    [InlineData("FFFFFFFF", false)]
    [InlineData("DEADBEEF", false)]
    [InlineData("GENERIC", false)]
    [InlineData("DEFAULT", false)]
    [InlineData("NODEVICE", false)]
    [InlineData("N/A", false)]
    [InlineData("NONE", false)]
    [InlineData("NOTAVAILABLE", false)]
    [InlineData("TOBEFILLED", false)]
    [InlineData("NOTSPECIFIED", false)]
    [InlineData("SERIALNUMBER", false)]
    [InlineData("12345678901234567890", false)]
    [InlineData("0123456789ABCDEF", false)]
    [InlineData("A1B2C3D4", true)]
    [InlineData("WD-WCC7K1234567", true)]
    [InlineData("S3Z8NB0M123456", true)]
    [InlineData("Z1Z123450000C123ABCD", true)]
    public void IsReliableSerialNumber_FiltersPlaceholders(string serial, bool expected)
    {
        Assert.Equal(expected, DriveIdentityResolver.IsReliableSerialNumber(serial));
    }

    [Fact]
    public void NormalizeSerial_RemovesWhitespaceAndUppercases()
    {
        var result = DriveIdentityResolver.NormalizeSerial("  ab-cd 12_34 ");
        Assert.Equal("ABCD1234", result);
    }

    [Fact]
    public void NormalizeSerial_NullReturnsEmpty()
    {
        var result = DriveIdentityResolver.NormalizeSerial(null);
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeSerial_EmptyReturnsEmpty()
    {
        var result = DriveIdentityResolver.NormalizeSerial("");
        Assert.Equal("", result);
    }

    [Fact]
    public void BuildIdentityKey_WithNullSerial_UsesFingerprint()
    {
        var key = DriveIdentityResolver.BuildIdentityKey(
            @"\\.\PHYSICALDRIVE2",
            null,
            "Model B",
            "FW2");

        Assert.StartsWith("NOSN-", key);
    }

    [Fact]
    public void BuildIdentityKey_WithEmptySerial_UsesFingerprint()
    {
        var key = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            "",
            "Model C",
            "FW3");

        Assert.StartsWith("NOSN-", key);
    }

    [Fact]
    public void BuildIdentityKey_WithWhitespaceOnlySerial_UsesFingerprint()
    {
        var key = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sdb",
            "   ",
            "Model D",
            "FW4");

        Assert.StartsWith("NOSN-", key);
    }

    [Fact]
    public void BuildIdentityKey_SameDiskDifferentPath_ProducesSameKey()
    {
        // When serial is reliable, path doesn't matter - only serial is used
        var key1 = DriveIdentityResolver.BuildIdentityKey(
            @"\\.\PHYSICALDRIVE0",
            "ABC123",
            "Model X",
            "FW1");
        var key2 = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            "ABC123",
            "Model X",
            "FW1");

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void BuildIdentityKey_DifferentSerial_ProducesDifferentKey()
    {
        var key1 = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            "ABC123",
            "Model X",
            "FW1");
        var key2 = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            "DEF456",
            "Model X",
            "FW1");

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void BuildIdentityKey_DifferentModelSameSerial_ProducesSameKey()
    {
        // When serial is reliable, model doesn't affect the key
        var key1 = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            "ABC123",
            "Model X",
            "FW1");
        var key2 = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            "ABC123",
            "Model Y",
            "FW1");

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void BuildIdentityKey_DifferentFirmwareSameSerial_ProducesSameKey()
    {
        // When serial is reliable, firmware doesn't affect the key
        var key1 = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            "ABC123",
            "Model X",
            "FW1");
        var key2 = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            "ABC123",
            "Model X",
            "FW2");

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void BuildIdentityKey_NullFirmware_HandlesGracefully()
    {
        var key = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            "ABC123",
            "Model X",
            null!);

        Assert.Equal("ABC123", key);
    }

    [Fact]
    public void BuildIdentityKey_NullModel_HandlesGracefully()
    {
        var key = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            "ABC123",
            null!,
            "FW1");

        Assert.Equal("ABC123", key);
    }

    [Fact]
    public void BuildIdentityKey_AllNull_UsesFingerprint()
    {
        var key = DriveIdentityResolver.BuildIdentityKey(
            "/dev/sda",
            null!,
            null!,
            null!);

        Assert.StartsWith("NOSN-", key);
    }
}


using DiskChecker.Infrastructure.Hardware.Sanitization;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Post-test formatting capability checks must not touch a real disk.
/// Bad selections are rejected before inspecting the supplied device path.
/// </summary>
public sealed class PostTestFormatSafetyTests
{
    [Fact]
    public async Task WindowsFormatter_RejectsNonNtfsWithoutOpeningDevice()
    {
        var service = new WindowsDiskSanitizationService();
        var result = await service.CreatePartitionWithFileSystemAsync(
            "THIS_DEVICE_MUST_NOT_BE_OPENED", "ext4",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("NTFS", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LinuxFormatter_RejectsUnavailableFormatWithoutOpeningDevice()
    {
        var service = new LinuxDiskSanitizationService();
        var result = await service.CreatePartitionWithFileSystemAsync(
            "/dev/THIS_DEVICE_MUST_NOT_BE_OPENED", "not-a-file-system",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not-a-file-system", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WindowsCapabilities_ListOnlyNtfs()
    {
        var service = new WindowsDiskSanitizationService();
        var supported = await service.GetSupportedFileSystemsAsync(
            TestContext.Current.CancellationToken);

        Assert.Single(supported);
        Assert.Equal("NTFS", supported[0]);
    }

    [Fact]
    public async Task LinuxCapabilities_NeverAdvertiseUnknownFormat()
    {
        var service = new LinuxDiskSanitizationService();
        var supported = await service.GetSupportedFileSystemsAsync(
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("not-a-file-system", supported);
        Assert.Equal(supported.Count, supported.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

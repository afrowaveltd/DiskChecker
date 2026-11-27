using DiskChecker.Infrastructure.Hardware.Sanitization;
using Xunit;

namespace DiskChecker.Tests;

public sealed class LinuxDirectIoFallbackSafetyTests
{
    [Theory]
    [InlineData(22, true)] // EINVAL
    [InlineData(95, true)] // EOPNOTSUPP
    [InlineData(2, false)] // ENOENT
    [InlineData(5, false)] // EIO
    [InlineData(6, false)] // ENXIO
    [InlineData(13, false)] // EACCES
    [InlineData(16, false)] // EBUSY
    [InlineData(19, false)] // ENODEV
    [InlineData(123, false)] // ENOMEDIUM
    public void OnlyUnsupportedDirectOpenMayFallBack(int errno, bool expected)
    {
        Assert.Equal(expected, LinuxDiskSanitizationService.CanFallbackFromDirectOpenError(errno));
    }
}

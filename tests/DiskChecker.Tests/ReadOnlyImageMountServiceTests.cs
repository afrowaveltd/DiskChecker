using System;
using System.IO;
using System.Threading.Tasks;
using DiskChecker.UI.Avalonia.Services;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// These tests must never attach actual host devices; native mounting is
/// exercised manually on Windows and Debian with disposable test images.
/// </summary>
public class ReadOnlyImageMountServiceTests
{
    [Fact]
    public async Task MissingImage_NeverReportedAsVerifiedOrMounted()
    {
        var missing = Path.Combine(Path.GetTempPath(),
            "missing-" + Guid.NewGuid().ToString("N") + ".vhdx");
        var result = await ReadOnlyImageMountService.TryAttachAsync(missing, TestContext.Current.CancellationToken);
        Assert.False(result.Verified);
        Assert.Null(result.Mount);
        Assert.Contains("neexistuje", result.Message);
    }

    [Fact]
    public async Task NonOwnedImage_IsNeverDetachedByDiskChecker()
    {
        var alreadyOwnedByUser = new ReadOnlyImageMountService.MountedImage(
            "user-image.vhdx", "Z:", null, Array.Empty<string>(), false);
        // Must be a harmless no-op on every OS, including Linux CI.
        await ReadOnlyImageMountService.DetachAsync(alreadyOwnedByUser, TestContext.Current.CancellationToken);
    }
}

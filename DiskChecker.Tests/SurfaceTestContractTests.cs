using DiskChecker.Core.Models;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public class SurfaceTestContractTests
{
    [Fact]
    public void SurfaceTestRequest_HasDefaults()
    {
        var request = new SurfaceTestRequest();

        Assert.NotNull(request.Drive);
        Assert.Equal(SurfaceTestProfile.HddFull, request.Profile);
        Assert.Equal(SurfaceTestOperation.WriteZeroFill, request.Operation);
        Assert.Equal(1024 * 1024, request.BlockSizeBytes);
        Assert.Equal(128, request.SampleIntervalBlocks);
        Assert.False(request.AllowDeviceWrite);
        Assert.False(request.SecureErase);
    }

    [Fact]
    public void SurfaceTestResult_HasSampleList()
    {
        var result = new SurfaceTestResult();

        Assert.NotNull(result.Samples);
        Assert.Empty(result.Samples);
    }

    [Fact]
    public void SurfaceTestProgress_AllowsProgressReporting()
    {
        var progress = Substitute.For<IProgress<SurfaceTestProgress>>();
        var sample = new SurfaceTestProgress { PercentComplete = 10 };

        progress.Report(sample);

        progress.Received(1).Report(sample);
    }
}

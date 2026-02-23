using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Hardware;
using Xunit;

namespace DiskChecker.Tests;

public class SurfaceTestExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReadOnlyFile_ReturnsSamples()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var data = new byte[1024 * 1024];
            new Random(1).NextBytes(data);
            await File.WriteAllBytesAsync(tempFile, data);

            var executor = new SurfaceTestExecutor();
            var request = new SurfaceTestRequest
            {
                Drive = new CoreDriveInfo { Path = tempFile, Name = "Temp", TotalSize = data.Length },
                Technology = DriveTechnology.Hdd,
                Profile = SurfaceTestProfile.HddFull,
                Operation = SurfaceTestOperation.ReadOnly,
                BlockSizeBytes = 1024,
                SampleIntervalBlocks = 1,
                MaxBytesToTest = data.Length
            };

            var result = await executor.ExecuteAsync(request);

            Assert.Equal(0, result.ErrorCount);
            Assert.True(result.TotalBytesTested > 0);
            Assert.NotEmpty(result.Samples);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WriteZeroFillFile_VerifiesSuccessfully()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var data = new byte[512 * 1024];
            new Random(2).NextBytes(data);
            await File.WriteAllBytesAsync(tempFile, data);

            var executor = new SurfaceTestExecutor();
            var request = new SurfaceTestRequest
            {
                Drive = new CoreDriveInfo { Path = tempFile, Name = "Temp", TotalSize = data.Length },
                Technology = DriveTechnology.Hdd,
                Profile = SurfaceTestProfile.HddFull,
                Operation = SurfaceTestOperation.WriteZeroFill,
                BlockSizeBytes = 4096,
                SampleIntervalBlocks = 4,
                MaxBytesToTest = data.Length
            };

            var result = await executor.ExecuteAsync(request);

            Assert.Equal(0, result.ErrorCount);
            Assert.True(result.TotalBytesTested > 0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DeviceSecureEraseBlocked()
    {
        var executor = new SurfaceTestExecutor();
        var request = new SurfaceTestRequest
        {
            Drive = new CoreDriveInfo { Path = "/dev/fake", Name = "Disk" },
            Technology = DriveTechnology.Hdd,
            Profile = SurfaceTestProfile.HddFull,
            Operation = SurfaceTestOperation.WriteZeroFill,
            SecureErase = true,
            AllowDeviceWrite = true,
            MaxBytesToTest = 1024
        };

        var result = await executor.ExecuteAsync(request);

        Assert.Equal(1, result.ErrorCount);
        Assert.Contains("Secure erase", result.Notes);
    }

    [Fact]
    public async Task ExecuteAsync_DeviceWriteRequiresSize()
    {
        var executor = new SurfaceTestExecutor();
        var request = new SurfaceTestRequest
        {
            Drive = new CoreDriveInfo { Path = "/dev/fake", Name = "Disk", TotalSize = 0 },
            Technology = DriveTechnology.Hdd,
            Profile = SurfaceTestProfile.HddFull,
            Operation = SurfaceTestOperation.WriteZeroFill,
            AllowDeviceWrite = true
        };

        var result = await executor.ExecuteAsync(request);

        Assert.Equal(1, result.ErrorCount);
        Assert.Contains("velikost", result.Notes);
    }

    [Fact]
    public async Task ExecuteAsync_DeviceWriteRequiresConfirmation()
    {
        var executor = new SurfaceTestExecutor();
        var request = new SurfaceTestRequest
        {
            Drive = new CoreDriveInfo { Path = "/dev/fake", Name = "Disk" },
            Technology = DriveTechnology.Hdd,
            Profile = SurfaceTestProfile.HddFull,
            Operation = SurfaceTestOperation.WriteZeroFill,
            AllowDeviceWrite = false
        };

        var result = await executor.ExecuteAsync(request);

        Assert.Equal(1, result.ErrorCount);
        Assert.Contains("potvrzení", result.Notes);
    }

    [Fact]
    public async Task ExecuteAsync_SecureEraseFile_PerformsErase()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var data = new byte[256 * 1024];
            new Random(3).NextBytes(data);
            await File.WriteAllBytesAsync(tempFile, data);

            var executor = new SurfaceTestExecutor();
            var request = new SurfaceTestRequest
            {
                Drive = new CoreDriveInfo { Path = tempFile, Name = "Temp", TotalSize = data.Length },
                Technology = DriveTechnology.Hdd,
                Profile = SurfaceTestProfile.HddFull,
                Operation = SurfaceTestOperation.WriteZeroFill,
                BlockSizeBytes = 4096,
                SampleIntervalBlocks = 4,
                MaxBytesToTest = data.Length,
                SecureErase = true
            };

            var result = await executor.ExecuteAsync(request);

            Assert.Equal(0, result.ErrorCount);
            Assert.True(result.SecureErasePerformed);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WriteOperation_ReturnsError()
    {
        var executor = new SurfaceTestExecutor();
        var request = new SurfaceTestRequest
        {
            Drive = new CoreDriveInfo { Path = "C:\\not-used", Name = "Disk" },
            Technology = DriveTechnology.Hdd,
            Profile = SurfaceTestProfile.HddFull,
            Operation = SurfaceTestOperation.WriteZeroFill
        };

        var result = await executor.ExecuteAsync(request);

        Assert.Equal(1, result.ErrorCount);
        Assert.False(string.IsNullOrWhiteSpace(result.Notes));
    }
}

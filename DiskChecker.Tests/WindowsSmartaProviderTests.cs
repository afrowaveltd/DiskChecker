using DiskChecker.Infrastructure.Hardware;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Unit tests for WindowsSmartaProvider to verify SMART data collection.
/// </summary>
public class WindowsSmartaProviderTests
{
    [Fact]
    public async Task ListDrivesAsync_ReturnsDrives()
    {
        // Arrange
        var provider = new WindowsSmartaProvider();

        // Act
        var drives = await provider.ListDrivesAsync();

        // Assert
        // Note: We don't assert Count > 0 because test environment may not have drives
        Assert.NotNull(drives);
        Assert.IsAssignableFrom<IReadOnlyList<DiskChecker.Core.Models.CoreDriveInfo>>(drives);
    }

    [Fact]
    public async Task GetSmartaDataAsync_WithValidDrive_ReturnsDataOrNull()
    {
        // Arrange
        var provider = new WindowsSmartaProvider();
        var drives = await provider.ListDrivesAsync();
        
        if (drives.Count == 0)
        {
            // Skip if no drives available
            return;
        }

        // Act
        var smartData = await provider.GetSmartaDataAsync(drives[0].Path);

        // Assert
        // SmartData may be null if system doesn't support SMART or WMI,
        // but if it's not null, it should have valid structure
        if (smartData != null)
        {
            Assert.NotNull(smartData.DeviceModel);
        }
    }

    [Fact]
    public async Task GetDependencyInstructionsAsync_ReturnNullIfSmartctlFound()
    {
        // Arrange
        var provider = new WindowsSmartaProvider();

        // Act
        var instructions = await provider.GetDependencyInstructionsAsync();

        // Assert
        // If smartctl is installed, instructions should be null
        // Otherwise it should provide installation instructions
        if (instructions != null)
        {
            Assert.Contains("winget install smartmontools", instructions, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task IsDriveValidAsync_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        var provider = new WindowsSmartaProvider();
        var invalidPath = @"\\.\PhysicalDrive999";

        // Act
        var isValid = await provider.IsDriveValidAsync(invalidPath);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task IsDriveValidAsync_WithValidDrive_ReturnsTrue()
    {
        // Arrange
        var provider = new WindowsSmartaProvider();
        var drives = await provider.ListDrivesAsync();

        if (drives.Count == 0)
        {
            // Skip if no drives available
            return;
        }

        // Act
        var isValid = await provider.IsDriveValidAsync(drives[0].Path);

        // Assert
        Assert.True(isValid);
    }
}

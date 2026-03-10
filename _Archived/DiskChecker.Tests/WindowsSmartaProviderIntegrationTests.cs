using Xunit;
using DiskChecker.Infrastructure.Hardware;

namespace DiskChecker.Tests;

public class WindowsSmartaProviderIntegrationTests
{
    [Fact(Skip = "Integration test - requires Windows admin")]
    public async Task ListDrivesAsync_ShouldReturnDrives()
    {
        var provider = new WindowsSmartaProvider();
        var drives = await provider.ListDrivesAsync();
        
        Assert.NotEmpty(drives);
        Assert.All(drives, d => Assert.False(string.IsNullOrEmpty(d.Path)));
    }

    [Fact(Skip = "Integration test - requires Windows admin")]
    public async Task GetSmartaDataAsync_ShouldReturnData()
    {
        var provider = new WindowsSmartaProvider();
        var drives = await provider.ListDrivesAsync();
        
        if (drives.Count == 0)
        {
            Assert.True(false, "No drives found");
        }

        var smartData = await provider.GetSmartaDataAsync(drives[0].Path);
        Assert.NotNull(smartData);
    }
}

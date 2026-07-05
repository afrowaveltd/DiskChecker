using DiskChecker.UI.Avalonia.ViewModels;
using Xunit;

namespace DiskChecker.Tests;

public class DiskCardsViewModelTests
{
    [Fact]
    public void ResolveDisplaySerial_DoesNotReplaceStoredSerialWithoutIdentityMatch()
    {
        var displayed = DiskCardsViewModel.ResolveDisplaySerial("CARD-SERIAL-123", identityMatchedDetectedSerial: null);

        Assert.Equal("CARD-SERIAL-123", displayed);
    }

    [Fact]
    public void ResolveDisplaySerial_UsesDetectedSerialOnlyForIdentityMatchedDrive()
    {
        var displayed = DiskCardsViewModel.ResolveDisplaySerial("CARD-SERIAL-123", "LIVE-SERIAL-999");

        Assert.Equal("LIVE-SERIAL-999", displayed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NOSN-ABCDEF012345")]
    public void ResolveDisplaySerial_HidesFallbackIdentitySerials(string storedSerial)
    {
        var displayed = DiskCardsViewModel.ResolveDisplaySerial(storedSerial, identityMatchedDetectedSerial: null);

        Assert.Equal("N/A", displayed);
    }
}

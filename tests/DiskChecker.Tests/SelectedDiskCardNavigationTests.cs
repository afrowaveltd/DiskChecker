using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// A disk card's database identity must not leak into navigation for another
/// physical drive, whose OS path can be reused after device replacement.
/// </summary>
public class SelectedDiskCardNavigationTests
{
    [Fact]
    public void SelectingAnotherPhysicalDrive_ClearsPreviouslySelectedCardId()
    {
        var selected = new SelectedDiskService
        {
            SelectedDisk = new CoreDriveInfo { Path = @"\\.\PHYSICALDRIVE2" },
            SelectedDiskCardId = 42
        };

        Assert.Equal(42, selected.SelectedDiskCardId);

        selected.SelectedDisk = new CoreDriveInfo { Path = @"\\.\PHYSICALDRIVE3" };

        Assert.Null(selected.SelectedDiskCardId);
    }

    [Fact]
    public void CardId_CanBeSelectedAfterSettingCardDrive()
    {
        var selected = new SelectedDiskService();
        selected.SelectedDisk = new CoreDriveInfo { Path = @"\\.\PHYSICALDRIVE2" };
        selected.SelectedDiskCardId = 123;

        Assert.Equal(123, selected.SelectedDiskCardId);
    }
}

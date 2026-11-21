using DiskChecker.Core.Interfaces;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public sealed class BackupNavigationActivityTests
{
    [Fact]
    public void BackupScanningCalculatingAndWritingPreventLeavingTheView()
    {
        using var vm = new BackupViewModel(
            Substitute.For<INavigationService>(),
            Substitute.For<ISelectedDiskService>(),
            Substitute.For<IDialogService>(),
            Substitute.For<ISmartaProvider>());

        Assert.False(vm.IsNavigationBlocked);
        vm.Phase = BackupPhase.Scanning;
        Assert.True(vm.IsNavigationBlocked);
        vm.Phase = BackupPhase.Calculating;
        Assert.True(vm.IsNavigationBlocked);
        vm.Phase = BackupPhase.Running;
        Assert.True(vm.IsNavigationBlocked);
        vm.Phase = BackupPhase.Completed;
        Assert.False(vm.IsNavigationBlocked);
    }
}

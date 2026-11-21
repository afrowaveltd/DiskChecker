using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using DiskChecker.UI.Avalonia.Views;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>Prove that template buttons in Avalonia resolve the parent commands.</summary>
[Collection("Seek UI")]
public sealed class DiskComparisonViewBindingTests(SeekUiFixture fixture)
{
    [Fact]
    public Task AvailableAndSelectedTemplatesBindTheirCommands()
        => fixture.Session.Dispatch(async () =>
        {
            var vm = new DiskComparisonViewModel(
                Substitute.For<IDiskCardRepository>(),
                Substitute.For<IDiskComparisonService>(),
                Substitute.For<INavigationService>(),
                Substitute.For<IDialogService>());
            vm.AvailableDisks.Add(new DiskCard
            {
                Id = 77, ModelName = "Disposable testing SSD", SerialNumber = "TEST"
            });
            var window = new Window
            {
                Width = 1100,
                Height = 900,
                Content = new DiskComparisonView { DataContext = vm }
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                await Task.Delay(30);

                var add = window.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b => b.CommandParameter is DiskCard);
                Assert.NotNull(add);
                Assert.NotNull(add.Command);
                Assert.True(add.Command.CanExecute(add.CommandParameter));
                add.Command.Execute(add.CommandParameter);
                Assert.Single(vm.SelectedDisks);

                window.UpdateLayout();
                await Task.Delay(20);
                var remove = window.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b => b.CommandParameter is DiskComparisonItem);
                Assert.NotNull(remove);
                Assert.NotNull(remove.Command);
                remove.Command.Execute(remove.CommandParameter);
                Assert.Empty(vm.SelectedDisks);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
}

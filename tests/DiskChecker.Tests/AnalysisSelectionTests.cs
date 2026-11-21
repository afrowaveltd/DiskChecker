using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.UI.Avalonia.Converters;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public class AnalysisSelectionTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void SelectedSummaryBrushConverter_HighlightsSelectedItem()
    {
        var converter = new SelectedSummaryBrushConverter();
        var item = new TestAnalysisSummary { TestSessionId = 1 };

        var selected = converter.Convert(new List<object?> { item, item }, typeof(IBrush), null, Invariant);
        var notSelected = converter.Convert(new List<object?> { item, new TestAnalysisSummary { TestSessionId = 2 } }, typeof(IBrush), null, Invariant);

        Assert.IsType<SolidColorBrush>(selected);
        Assert.Equal(Brushes.Transparent, notSelected);
    }

    [Fact]
    public void SelectedSummaryBorderConverter_HighlightsSelectedItem()
    {
        var converter = new SelectedSummaryBorderConverter();
        var item = new TestAnalysisSummary { TestSessionId = 1 };

        var selected = converter.Convert(new List<object?> { item, item }, typeof(IBrush), null, Invariant);
        var notSelected = converter.Convert(new List<object?> { item, new TestAnalysisSummary { TestSessionId = 2 } }, typeof(IBrush), null, Invariant);

        Assert.IsType<SolidColorBrush>(selected);
        Assert.Equal(Brushes.Transparent, notSelected);
    }

    [Fact]
    public void SelectedSummaryThicknessConverter_ReturnsThicknessForSelectedItem()
    {
        var converter = new SelectedSummaryThicknessConverter();
        var item = new TestAnalysisSummary { TestSessionId = 1 };

        var selected = converter.Convert(new List<object?> { item, item }, typeof(Thickness), null, Invariant);
        var notSelected = converter.Convert(new List<object?> { item, new TestAnalysisSummary { TestSessionId = 2 } }, typeof(Thickness), null, Invariant);

        Assert.Equal(new Thickness(1), selected);
        Assert.Equal(new Thickness(0), notSelected);
    }

    [Fact]
    public void SelectSummaryCommand_SetsSelectedSummary()
    {
        var vm = CreateViewModel();
        var summary = new TestAnalysisSummary { TestSessionId = 42, DiskCardId = 7 };

        vm.SelectSummaryCommand.Execute(summary);

        Assert.Same(summary, vm.SelectedSummary);
    }

    [Fact]
    public void SelectSummaryCommand_IgnoresNull()
    {
        var vm = CreateViewModel();
        var original = vm.SelectedSummary;

        vm.SelectSummaryCommand.Execute(null);

        Assert.Same(original, vm.SelectedSummary);
    }

    [Fact]
    public async Task SlowAnalysisRequest_CannotOverwriteNewlySelectedTest()
    {
        var dialog = Substitute.For<IDialogService>();
        var analysis = Substitute.For<ITestAnalysisDataService>();
        var repo = Substitute.For<IDiskCardRepository>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetAnalysisWorkspaceModeAsync().Returns(Task.FromResult(AnalysisWorkspaceMode.Compact));
        repo.GetAllAsync().Returns(Task.FromResult(new List<DiskCard>()));
        repo.GetSmartSnapshotsAsync(Arg.Any<int>()).Returns(Task.FromResult(new List<SmartSnapshotRecord>()));

        var oldQuery = new TaskCompletionSource<TestAnalysisData?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        analysis.GetAnalysisDataAsync(1, Arg.Any<CancellationToken>())
            .Returns(_ => oldQuery.Task);
        analysis.GetAnalysisDataAsync(2, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TestAnalysisData?>(new TestAnalysisData { Session = new TestSession { Id = 2 } }));

        var vm = new AnalysisViewModel(
            dialog, analysis, repo, settings, new SmartTrendService(repo));
        vm.SelectedSummary = new TestAnalysisSummary { TestSessionId = 1, DiskCardId = 7 };
        vm.SelectedSummary = new TestAnalysisSummary { TestSessionId = 2, DiskCardId = 7 };

        for (int i = 0; i < 200 && vm.AnalysisData?.Session.Id != 2; i++)
            await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.Equal(2, vm.AnalysisData?.Session.Id);

        // Finish the old request *after* the newer one. The selected test must
        // not change, and no stale notes/SMART data may get published.
        oldQuery.SetResult(new TestAnalysisData { Session = new TestSession { Id = 1 } });
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(2, vm.AnalysisData?.Session.Id);
        Assert.Equal(2, vm.SelectedSummary?.TestSessionId);
    }

    [Fact]
    public async Task ClearingSelectionWhileRequestRuns_ReleasesBusyStateAndRejectsLateResult()
    {
        var dialog = Substitute.For<IDialogService>();
        var analysis = Substitute.For<ITestAnalysisDataService>();
        var repo = Substitute.For<IDiskCardRepository>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetAnalysisWorkspaceModeAsync().Returns(Task.FromResult(AnalysisWorkspaceMode.Compact));
        repo.GetAllAsync().Returns(Task.FromResult(new List<DiskCard>()));

        var pending = new TaskCompletionSource<TestAnalysisData?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        analysis.GetAnalysisDataAsync(99, Arg.Any<CancellationToken>())
            .Returns(_ => pending.Task);

        var vm = new AnalysisViewModel(
            dialog, analysis, repo, settings, new SmartTrendService(repo));
        vm.SelectedSummary = new TestAnalysisSummary { TestSessionId = 99, DiskCardId = 7 };
        Assert.True(vm.IsLoadingAnalysis);

        vm.SelectedSummary = null;
        Assert.Null(vm.AnalysisData);
        Assert.False(vm.IsLoadingAnalysis);

        pending.SetResult(new TestAnalysisData { Session = new TestSession { Id = 99 } });
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Null(vm.AnalysisData);
        Assert.False(vm.IsLoadingAnalysis);
    }

    private static AnalysisViewModel CreateViewModel()
    {
        var dialog = Substitute.For<IDialogService>();
        var analysisData = Substitute.For<ITestAnalysisDataService>();
        var repository = Substitute.For<IDiskCardRepository>();
        var settings = Substitute.For<ISettingsService>();
        var smartTrend = new SmartTrendService(repository);

        return new AnalysisViewModel(dialog, analysisData, repository, settings, smartTrend);
    }
}

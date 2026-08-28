using System.Collections.Generic;
using System.Globalization;
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

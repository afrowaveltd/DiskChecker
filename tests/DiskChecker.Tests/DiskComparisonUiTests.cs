using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using NSubstitute;
using OxyPlot.Series;
using Xunit;

namespace DiskChecker.Tests;

public sealed class DiskComparisonUiTests
{
    [Fact]
    public async Task SelectedDisksDriveRealResultsAndSeparateChartAxes()
    {
        var repo = Substitute.For<IDiskCardRepository>();
        var service = Substitute.For<IDiskComparisonService>();
        using var ignored = new System.IO.MemoryStream();
        var vm = new DiskComparisonViewModel(repo, service,
            Substitute.For<INavigationService>(), Substitute.For<IDialogService>());
        var a = new DiskCard { Id = 101, ModelName = "SSD One", SerialNumber = "ONE" };
        var b = new DiskCard { Id = 102, ModelName = "SSD Two", SerialNumber = "TWO" };
        vm.AddDiskCommand.Execute(a);
        vm.AddDiskCommand.Execute(b);
        Assert.True(vm.CanCompare);

        service.CompareAsync(Arg.Any<List<int>>()).Returns(Task.FromResult(new List<DiskComparisonResult>
        {
            new() { Disk = a, Rank = 1, Score = 95, Grade = "A",
                AvgReadSpeed = 500, HasReadMeasurement = true, HasWriteMeasurement = false },
            new() { Disk = b, Rank = 2, Score = 70, Grade = "C",
                AvgWriteSpeed = 90, HasWriteMeasurement = true }
        }));
        await vm.CompareDisksCommand.ExecuteAsync(null);

        Assert.True(vm.HasComparison);
        Assert.True(vm.CanExport);
        Assert.Equal(2, vm.ComparisonResults.Count);
        Assert.Equal(100, vm.ComparisonChartModel.Axes.Last().Maximum);
        Assert.Equal(0, vm.SpeedChartModel.Axes.Last().Minimum);
        Assert.Empty(((ColumnSeries)vm.SpeedChartModel.Series[0]).Items.Where(i => i.CategoryIndex == 0));
        Assert.Equal("N/A", vm.ComparisonResults[0].WriteSpeedText);
        Assert.Single(((ColumnSeries)vm.SpeedChartModel.Series[1]).Items);

        vm.RemoveDiskCommand.Execute(vm.SelectedDisks[0]);
        Assert.False(vm.CanCompare);
        Assert.False(vm.HasComparison);
        Assert.False(vm.CanExport);
        Assert.Empty(vm.ComparisonChartModel.Series);
        Assert.Empty(vm.SpeedChartModel.Series);
    }

    [Fact]
    public void CsvQuotesFormulaLikeTextAndMissingMeasurements()
    {
        var csv = DiskComparisonCsvExporter.Build(new[]
        {
            new DiskComparisonResult
            {
                Disk = new DiskCard { Id = 5, ModelName = "=HYPERLINK(\"evil\")",
                    SerialNumber = "  +cmd" },
                Grade = "A", Rank = 1, Score = 91.5,
                AvgReadSpeed = 120.5, HasReadMeasurement = true,
                HasWriteMeasurement = false,
                Recommendation = "@malicious"
            }
        });
        Assert.Contains("\"'=HYPERLINK", csv);
        Assert.Contains("\"'  +cmd\"", csv);
        Assert.Contains(",\"N/A\",\"120.5\"", csv);
        Assert.Contains("\"'@malicious\"", csv);
    }
}

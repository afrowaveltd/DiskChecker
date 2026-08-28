using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Avalonia;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.UI.Avalonia.Converters;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Regression tests for the Analysis card charts. The point strings fed to
/// <see cref="PointsStringConverter"/> must be culture-invariant (decimal dot),
/// otherwise a locale such as cs-CZ (comma decimal separator) produces ambiguous
/// "x,y" pairs that the converter cannot parse, leaving the charts blank.
/// </summary>
public class AnalysisChartCultureTests
{
    [Fact]
    public void ThroughputPoints_AreCultureInvariant_AndParseable()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");

            var vm = CreateViewModel();
            vm.AnalysisData = new TestAnalysisData
            {
                TelemetrySamples = new List<TestTelemetrySample>
                {
                    new() { Phase = TelemetrySamplePhase.Write, SequenceIndex = 1, ProgressPercent = 0.0, SpeedMBps = 123.4 },
                    new() { Phase = TelemetrySamplePhase.Write, SequenceIndex = 2, ProgressPercent = 50.0, SpeedMBps = 200.6 },
                    new() { Phase = TelemetrySamplePhase.Write, SequenceIndex = 3, ProgressPercent = 100.0, SpeedMBps = 150.2 },
                }
            };

            var points = vm.ThroughputProgressWritePoints;
            Assert.False(string.IsNullOrWhiteSpace(points));

            // The string must use '.' as the decimal separator, not ','.
            Assert.DoesNotContain("123,4", points);

            var parsed = (List<Point>)new PointsStringConverter().Convert(points, typeof(IList<Point>), null, CultureInfo.InvariantCulture)!;
            Assert.Equal(3, parsed.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void SeekPoints_AreCultureInvariant_AndParseable()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");

            var vm = CreateViewModel();
            vm.AnalysisData = new TestAnalysisData
            {
                SeekSamples = new List<SeekSampleRecord>
                {
                    new() { Index = 1, LatencyMs = 4.0052, HasError = false },
                    new() { Index = 2, LatencyMs = 29.5817, HasError = false },
                    new() { Index = 3, LatencyMs = 26.2226, HasError = false },
                }
            };

            var points = vm.SeekLatencyPoints;
            Assert.False(string.IsNullOrWhiteSpace(points));
            Assert.DoesNotContain("4,0", points);

            var parsed = (List<Point>)new PointsStringConverter().Convert(points, typeof(IList<Point>), null, CultureInfo.InvariantCulture)!;
            Assert.Equal(3, parsed.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void TemperaturePoints_AreCultureInvariant_AndParseable()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");

            var vm = CreateViewModel();
            vm.AnalysisData = new TestAnalysisData
            {
                TemperatureSamples = new List<TemperatureSample>
                {
                    new() { TemperatureCelsius = 30, ProgressPercent = 0 },
                    new() { TemperatureCelsius = 45, ProgressPercent = 50 },
                    new() { TemperatureCelsius = 40, ProgressPercent = 100 },
                }
            };

            var points = vm.TemperaturePoints;
            Assert.False(string.IsNullOrWhiteSpace(points));

            var parsed = (List<Point>)new PointsStringConverter().Convert(points, typeof(IList<Point>), null, CultureInfo.InvariantCulture)!;
            Assert.Equal(3, parsed.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
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

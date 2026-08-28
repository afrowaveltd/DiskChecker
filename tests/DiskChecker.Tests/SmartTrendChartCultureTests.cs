using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.UI.Avalonia.Converters;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Regression test for <see cref="SmartTrendService.BuildChartData"/>: the generated
/// polyline points must be culture-invariant so the Analysis card SMART trend charts
/// render under locales that use a comma decimal separator (e.g. cs-CZ).
/// </summary>
public class SmartTrendChartCultureTests
{
    [Fact]
    public void BuildChartData_ProducesCultureInvariantPoints()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");

            var repository = Substitute.For<IDiskCardRepository>();
            var service = new SmartTrendService(repository);

            var trend = new SmartMetricTrend
            {
                MetricName = "Teplota",
                Unit = "°C",
                Points = new List<SmartTrendPoint>
                {
                    new() { Value = 30.5 },
                    new() { Value = 45.2 },
                    new() { Value = 40.7 },
                }
            };

            var chart = service.BuildChartData(trend, 520, 180);

            Assert.False(string.IsNullOrWhiteSpace(chart.PolylinePoints));
            Assert.DoesNotContain("30,5", chart.PolylinePoints);

            var parsed = (List<Point>)new PointsStringConverter().Convert(chart.PolylinePoints, typeof(IList<Point>), null, CultureInfo.InvariantCulture)!;
            Assert.Equal(3, parsed.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

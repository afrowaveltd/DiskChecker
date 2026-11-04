using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class TuiFormatsTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024L * 1024L, "1.00 MB")]
    [InlineData(512L * 1024L * 1024L * 1024L, "512.00 GB")]
    [InlineData(2L * 1024L * 1024L * 1024L * 1024L, "2.00 TB")]
    public void Bytes_UsesBinaryUnits(long value, string expected) =>
        Assert.Equal(expected, TuiFormats.Bytes(value));

    [Theory]
    [InlineData(0, "Unknown")]
    [InlineData(256L * 1024 * 1024 * 1024, "256.0 GB")]
    [InlineData(2L * 1024 * 1024 * 1024 * 1024, "2.0 TB")]
    public void Capacity_MatchesDesktopFormatting(long value, string expected) =>
        Assert.Equal(expected, TuiFormats.Capacity(value));

    [Theory]
    [InlineData(1500, "1.5 KB")]
    [InlineData(2_000_000, "2.00 MB")]
    [InlineData(1_500_000_000, "1.50 GB")]
    public void DecimalSize_UsesDecimalUnits(long value, string expected) =>
        Assert.Equal(expected, TuiFormats.DecimalSize(value));

    [Fact]
    public void Duration_FormatsHoursMinutesSeconds() =>
        Assert.Equal("02:03:04", TuiFormats.Duration(new TimeSpan(2, 3, 4)));

    [Fact]
    public void ShortDuration_FormatsMinutesSeconds() =>
        Assert.Equal("03:04", TuiFormats.ShortDuration(new TimeSpan(0, 3, 4)));

    [Fact]
    public void Eta_ReturnsDashWhenUnknown() => Assert.Equal("—", TuiFormats.Eta(null));

    [Fact]
    public void Eta_UsesHoursWhenNeeded() =>
        Assert.Equal("01:00:00", TuiFormats.Eta(TimeSpan.FromHours(1)));

    [Fact]
    public void Eta_UsesMinutesForShortRemaining() =>
        Assert.Equal("04:05", TuiFormats.Eta(TimeSpan.FromSeconds(245)));

    [Theory]
    [InlineData(100, "A")]
    [InlineData(90, "A")]
    [InlineData(89, "B")]
    [InlineData(70, "C")]
    [InlineData(60, "D")]
    [InlineData(40, "E")]
    [InlineData(39, "F")]
    public void GradeFromScore_MatchesSharedThresholds(double score, string expected) =>
        Assert.Equal(expected, TuiFormats.GradeFromScore(score));

    [Theory]
    [InlineData("A", "OK")]
    [InlineData("C", "Warning")]
    [InlineData("F", "Critical")]
    [InlineData(null, "Unknown")]
    public void HealthStatus_MapsGrades(string? grade, string expected) =>
        Assert.Equal(expected, TuiFormats.HealthStatus(grade));

    [Theory]
    [InlineData("A", true)]
    [InlineData("C", true)]
    [InlineData("D", false)]
    [InlineData(null, false)]
    public void IsHealthy_AcceptsGradesUpToC(string? grade, bool expected) =>
        Assert.Equal(expected, TuiFormats.IsHealthy(grade));

    [Fact]
    public void Bar_FillsRequestedWidth()
    {
        string bar = TuiFormats.Bar(50, 10);
        Assert.Equal(10, bar.Length);
        Assert.Equal(5, bar.Count(c => c == '█'));
    }

    [Fact]
    public void Ellipsis_ShortensLongText()
    {
        Assert.Equal("abc…", TuiFormats.Ellipsis("abcdef", 4));
        Assert.Equal(string.Empty, TuiFormats.Ellipsis(null, 4));
        Assert.Equal("abc", TuiFormats.Ellipsis("abc", 4));
    }

    [Theory]
    [InlineData(CoreBusType.Nvme, "NVMe")]
    [InlineData(CoreBusType.Usb, "USB")]
    [InlineData(CoreBusType.Sata, "SATA")]
    [InlineData(CoreBusType.Unknown, "SATA")]
    public void BusType_UsesSharedNames(CoreBusType busType, string expected) =>
        Assert.Equal(expected, TuiFormats.BusType(busType));

    [Fact]
    public void BusTypeDisplay_PrefersModelHints()
    {
        var drive = new CoreDriveInfo { Name = "Samsung SSD 990 NVMe", BusType = CoreBusType.Sata };
        Assert.Equal("NVMe", TuiFormats.BusTypeDisplay(drive));
    }

    [Fact]
    public void Temperature_FormatsAndGuardsMissingValues()
    {
        Assert.Equal("N/A", TuiFormats.Temperature(null));
        Assert.Equal("N/A", TuiFormats.Temperature(0));
        Assert.Equal("41°C", TuiFormats.Temperature(41));
    }

    [Fact]
    public void Speed_FormatsMegabytesPerSecond()
    {
        Assert.Equal("128.4 MB/s", TuiFormats.Speed(128.44));
        Assert.Equal("—", TuiFormats.Speed(double.NaN));
    }
}

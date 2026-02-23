using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using Xunit;

namespace DiskChecker.Tests;

public class TestReportExportServiceTests
{
    [Fact]
    public void GenerateText_ReturnsContent()
    {
        var service = new TestReportExportService();
        var report = CreateReport();

        var text = service.GenerateText(report);

        Assert.Contains("DiskChecker", text);
        Assert.Contains("Grade", text);
    }

    [Fact]
    public void GenerateCsv_ReturnsHeader()
    {
        var service = new TestReportExportService();
        var report = CreateReport();

        var csv = service.GenerateCsv(report);

        Assert.StartsWith("Date,DriveName", csv);
    }

    [Fact]
    public void GenerateCertificateHtml_IncludesGrade()
    {
        var service = new TestReportExportService();
        var report = CreateReport();

        var html = service.GenerateCertificateHtml(report);

        Assert.Contains(">A<", html);
        Assert.Contains("Certifikát kvality disku", html);
    }

    private static TestReportData CreateReport()
    {
        var smartCheck = new SmartCheckResult
        {
            Drive = new CoreDriveInfo { Name = "Disk", Path = "/dev/sda" },
            SmartaData = new SmartaData
            {
                DeviceModel = "Model",
                SerialNumber = "SN",
                FirmwareVersion = "FW",
                PowerOnHours = 100,
                ReallocatedSectorCount = 0,
                PendingSectorCount = 0,
                UncorrectableErrorCount = 0,
                Temperature = 30
            },
            Rating = new QualityRating { Grade = QualityGrade.A, Score = 99 },
            TestDate = DateTime.UtcNow
        };

        var surface = new SurfaceTestResult
        {
            Drive = smartCheck.Drive,
            Technology = DriveTechnology.Hdd,
            Profile = SurfaceTestProfile.HddFull,
            Operation = SurfaceTestOperation.ReadOnly,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletedAtUtc = DateTime.UtcNow,
            TotalBytesTested = 1024,
            AverageSpeedMbps = 100,
            PeakSpeedMbps = 120,
            MinSpeedMbps = 80,
            ErrorCount = 0,
            Samples = new List<SurfaceTestSample>
            {
                new() { OffsetBytes = 0, BlockSizeBytes = 512, ThroughputMbps = 100, TimestampUtc = DateTime.UtcNow },
                new() { OffsetBytes = 512, BlockSizeBytes = 512, ThroughputMbps = 120, TimestampUtc = DateTime.UtcNow }
            }
        };

        return new TestReportData { SmartCheck = smartCheck, SurfaceTest = surface };
    }
}

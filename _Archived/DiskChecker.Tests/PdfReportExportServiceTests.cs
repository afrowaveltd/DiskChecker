using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public class PdfReportExportServiceTests
{
    [Fact]
    public void GenerateCertificatePdf_ReturnsBytes()
    {
        var exporter = Substitute.For<ITestReportExporter>();
        exporter.GenerateCertificateHtml(Arg.Any<TestReportData>())
            .Returns("<html><body><h1>Test</h1></body></html>");

        var service = new PdfReportExportService(exporter);
        var report = new TestReportData
        {
            SmartCheck = new SmartCheckResult
            {
                Drive = new CoreDriveInfo { Name = "Disk", Path = "/dev/sda" },
                SmartaData = new SmartaData(),
                Rating = new QualityRating(),
                TestDate = DateTime.UtcNow
            }
        };

        var bytes = service.GenerateCertificatePdf(report);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }
}

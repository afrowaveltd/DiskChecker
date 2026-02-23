using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public class ReportEmailServiceTests
{
    [Fact]
    public async Task SendReportAsync_UsesCertificateHtmlWhenRequested()
    {
        var exporter = Substitute.For<ITestReportExporter>();
        exporter.GenerateText(Arg.Any<TestReportData>()).Returns("text");
        exporter.GenerateHtml(Arg.Any<TestReportData>()).Returns("html");
        exporter.GenerateCertificateHtml(Arg.Any<TestReportData>()).Returns("cert");

        var sender = Substitute.For<IEmailSender>();
        var service = new ReportEmailService(exporter, sender);

        var report = CreateReport();
        await service.SendReportAsync(report, "user@test", true);

        await sender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.HtmlBody == "cert" && m.TextBody == "text"),
            Arg.Any<CancellationToken>());
    }

    private static TestReportData CreateReport()
    {
        return new TestReportData
        {
            SmartCheck = new SmartCheckResult
            {
                Drive = new CoreDriveInfo { Name = "Disk", Path = "/dev/sda" },
                SmartaData = new SmartaData(),
                Rating = new QualityRating(),
                TestDate = DateTime.UtcNow
            }
        };
    }
}

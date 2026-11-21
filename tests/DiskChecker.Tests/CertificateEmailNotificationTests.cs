using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public sealed class CertificateEmailNotificationTests
{
    private static TestCompletionNotificationService Create(
        IEmailSender sender, IEmailSettingsService smtp)
    {
        var locale = Substitute.For<ILocaleProvider>();
        locale.GetString(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => call.ArgAt<string>(1));
        return new TestCompletionNotificationService(sender, smtp,
            NullLogger<TestCompletionNotificationService>.Instance, locale);
    }

    [Fact]
    public async Task CompletedTestSendsItsCertificatePdfToExplicitRecipient()
    {
        var sender = Substitute.For<IEmailSender>();
        var smtp = Substitute.For<IEmailSettingsService>();
        smtp.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new EmailSettings
        {
            FromAddress = "sender@example.net",
            IncludeCertificateAttachment = true
        }));
        var notification = Create(sender, smtp);
        var pdf = new byte[] { 37, 80, 68, 70, 45, 49, 46, 55 };
        await notification.SendTestCompletionWithReportAsync(
            new SurfaceTestResult(), "technician@example.net", pdf,
            diskName: "SSD Test", certificate: new DiskCertificate { DiskModel = "SSD Test" },
            attachmentFileName: "certificate.pdf",
            cancellationToken: TestContext.Current.CancellationToken);

        await sender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(message =>
                message.ToAddress == "technician@example.net" &&
                message.Attachments.Count == 1 &&
                message.Attachments[0].FileName == "certificate.pdf" &&
                message.Attachments[0].ContentType == "application/pdf" &&
                message.Attachments[0].Content.Length == 8),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingPdfMustNotBeMisreportedAsSuccessfulAttachment()
    {
        var sender = Substitute.For<IEmailSender>();
        var smtp = Substitute.For<IEmailSettingsService>();
        smtp.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EmailSettings { IncludeCertificateAttachment = true }));
        var notification = Create(sender, smtp);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            notification.SendTestCompletionWithReportAsync(
                new SurfaceTestResult(), "technician@example.net",
                new byte[] { 0, 1, 2, 3, 4, 5 },
                cancellationToken: TestContext.Current.CancellationToken));
        await sender.DidNotReceiveWithAnyArgs().SendAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectedSmtpTransactionPropagatesToCaller()
    {
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SMTP transaction rejected")));
        var smtp = Substitute.For<IEmailSettingsService>();
        smtp.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EmailSettings { IncludeCertificateAttachment = false }));
        var notification = Create(sender, smtp);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            notification.SendTestCompletionWithReportAsync(
                new SurfaceTestResult(), "technician@example.net", null,
                cancellationToken: TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task StoredSeekCertificateUsesItsRealPdfAndNoFakeSurfaceSamples()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"dc-mail-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(temp, new byte[] { 37, 80, 68, 70, 45, 49, 46, 52 });
            var sender = Substitute.For<IEmailSender>();
            var smtp = Substitute.For<IEmailSettingsService>();
            smtp.GetAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new EmailSettings { IncludeCertificateAttachment = true }));
            var appSettings = Substitute.For<ISettingsService>();
            appSettings.GetReportRecipientEmailAsync()
                .Returns(Task.FromResult("recipient@example.net"));
            var generator = Substitute.For<ICertificateGenerator>();
            generator.GeneratePdfAsync(Arg.Any<DiskCertificate>()).Returns(Task.FromResult(temp));
            var locale = Substitute.For<ILocaleProvider>();
            locale.GetString(Arg.Any<string>(), Arg.Any<string>())
                .Returns(call => call.ArgAt<string>(1));
            var notify = new TestCompletionNotificationService(sender, smtp,
                NullLogger<TestCompletionNotificationService>.Instance,
                locale, appSettings, generator);
            var cert = new DiskCertificate
            {
                DiskCardId = 11, TestSessionId = 22, Grade = "B", Score = 82,
                DiskModel = "Seek SSD", TestType = "Seek", SeekErrorCount = 3
            };

            Assert.True(await notify.SendStoredCertificateIfConfiguredAsync(
                cert, TestContext.Current.CancellationToken));
            await sender.Received(1).SendAsync(
                Arg.Is<EmailMessage>(msg =>
                    msg.ToAddress == "recipient@example.net" &&
                    msg.Attachments.Count == 1 &&
                    msg.Attachments[0].ContentType == "application/pdf" &&
                    msg.Subject.Contains("82/100") &&
                    msg.HtmlBody!.Contains("N/A") &&
                    !msg.HtmlBody.Contains("Vzorky:")), Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [Fact]
    public async Task NoRecipientNeverSendsStoredCertificate()
    {
        var sender = Substitute.For<IEmailSender>();
        var smtp = Substitute.For<IEmailSettingsService>();
        var appSettings = Substitute.For<ISettingsService>();
        appSettings.GetReportRecipientEmailAsync()
            .Returns(Task.FromResult(string.Empty));
        var generator = Substitute.For<ICertificateGenerator>();
        var locale = Substitute.For<ILocaleProvider>();
        var notify = new TestCompletionNotificationService(sender, smtp,
            NullLogger<TestCompletionNotificationService>.Instance,
            locale, appSettings, generator);

        Assert.False(await notify.SendStoredCertificateIfConfiguredAsync(
            new DiskCertificate { DiskCardId = 1, TestSessionId = 2 },
            TestContext.Current.CancellationToken));
        await sender.DidNotReceiveWithAnyArgs().SendAsync(default!, Arg.Any<CancellationToken>());
        await generator.DidNotReceiveWithAnyArgs().GeneratePdfAsync(default!);
    }

}

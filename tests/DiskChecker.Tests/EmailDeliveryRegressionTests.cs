using System;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public sealed class EmailDeliveryRegressionTests
{
    [Fact]
    public void SmtpTransportNeverDowngradesConfiguredTls()
    {
        Assert.Equal(SecureSocketOptions.StartTls, SmtpEmailSender.ResolveSocketSecurity(
            new EmailSettings { Host = "mail.test.invalid", Port = 587, UseSsl = true }));
        Assert.Equal(SecureSocketOptions.SslOnConnect, SmtpEmailSender.ResolveSocketSecurity(
            new EmailSettings { Host = "mail.test.invalid", Port = 465, UseSsl = true }));
        Assert.Equal(SecureSocketOptions.None, SmtpEmailSender.ResolveSocketSecurity(
            new EmailSettings { Host = "localhost", Port = 2525, UseSsl = false }));
    }

    [Fact]
    public async Task CompletionWithCertificateUsesActualPdfBytesAndCorrectMimeType()
    {
        var sender = Substitute.For<IEmailSender>();
        var settings = Substitute.For<IEmailSettingsService>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EmailSettings { Host = "smtp.test.invalid",
                FromAddress = "sender@test.invalid", IncludeCertificateAttachment = true }));
        var service = new TestCompletionNotificationService(sender, settings,
            NullLogger<TestCompletionNotificationService>.Instance, Substitute.For<ILocaleProvider>());
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
        await service.SendTestCompletionWithReportAsync(
            new SurfaceTestResult(), "recipient@test.invalid", pdf,
            "Test SSD", new DiskCertificate { DiskModel = "Test SSD", CertificateNumber = "CERT-123" },
            "CERT-123.pdf", TestContext.Current.CancellationToken);
        await sender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.ToAddress == "recipient@test.invalid" &&
                m.Attachments.Count == 1 && m.Attachments[0].FileName == "CERT-123.pdf" &&
                m.Attachments[0].ContentType == "application/pdf" &&
                m.Attachments[0].Content == pdf),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SmtpDeliveryErrorMustReachCallerRatherThanBecomeFakeSuccess()
    {
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SMTP not configured")));
        var settings = Substitute.For<IEmailSettingsService>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EmailSettings { FromAddress = "sender@test.invalid" }));
        var service = new TestCompletionNotificationService(sender, settings,
            NullLogger<TestCompletionNotificationService>.Instance, Substitute.For<ILocaleProvider>());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTestCompletionWithReportAsync(
                new SurfaceTestResult(), "receiver@test.invalid",
                new byte[] { 37, 80, 68, 70 }, "Disk", null, "disk.pdf",
                TestContext.Current.CancellationToken));
    }
}

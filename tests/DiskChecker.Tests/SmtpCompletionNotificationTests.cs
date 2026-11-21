using System;
using System.Linq;
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

public sealed class SmtpCompletionNotificationTests
{
    [Fact]
    public void ExplicitPlaintextAndRequiredTlsNeverDowngrade()
    {
        Assert.Equal(SecureSocketOptions.None,
            SmtpEmailSender.GetSocketOptions(new EmailSettings { UseSsl = false, Port = 25 }));
        Assert.Equal(SecureSocketOptions.StartTls,
            SmtpEmailSender.GetSocketOptions(new EmailSettings { UseSsl = true, Port = 587 }));
        Assert.Equal(SecureSocketOptions.SslOnConnect,
            SmtpEmailSender.GetSocketOptions(new EmailSettings { UseSsl = true, Port = 465 }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletionEmailIncludesPdfExactlyWhenEnabled(bool includePdf)
    {
        var settings = Substitute.For<IEmailSettingsService>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EmailSettings
            {
                FromAddress = "sender@example.test",
                IncludeCertificateAttachment = includePdf
            }));
        var sender = Substitute.For<IEmailSender>();
        var locale = Substitute.For<ILocaleProvider>();
        locale.GetString(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => ci.ArgAt<string>(1));
        var service = new TestCompletionNotificationService(sender, settings,
            NullLogger<TestCompletionNotificationService>.Instance, locale);
        var pdf = new byte[] { 37, 80, 68, 70, 45, 49, 46, 55 };

        await service.SendTestCompletionWithReportAsync(
            new SurfaceTestResult(), "recipient@example.test", pdf, "Test disk",
            attachmentFileName: "TestCertificate.pdf",
            cancellationToken: TestContext.Current.CancellationToken);

        await sender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(message =>
                message.ToAddress == "recipient@example.test" &&
                message.Attachments.Count == (includePdf ? 1 : 0) &&
                (!includePdf || (message.Attachments[0].FileName == "TestCertificate.pdf" &&
                    message.Attachments[0].ContentType == "application/pdf" &&
                    message.Attachments[0].Content.SequenceEqual(pdf)))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliveryFailureIsNotSilentlyReportedAsSuccess()
    {
        var settings = Substitute.For<IEmailSettingsService>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EmailSettings
            {
                FromAddress = "sender@example.test",
                IncludeCertificateAttachment = true
            }));
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SMTP not available")));
        var locale = Substitute.For<ILocaleProvider>();
        locale.GetString(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => ci.ArgAt<string>(1));
        var service = new TestCompletionNotificationService(sender, settings,
            NullLogger<TestCompletionNotificationService>.Instance, locale);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTestCompletionWithReportAsync(
                new SurfaceTestResult(), "recipient@example.test",
                new byte[] { 37, 80, 68, 70 }, cancellationToken: TestContext.Current.CancellationToken));
    }
}

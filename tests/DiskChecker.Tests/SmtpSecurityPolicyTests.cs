using System;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using MailKit.Security;
using Xunit;

namespace DiskChecker.Tests;

public sealed class SmtpSecurityPolicyTests
{
    [Fact]
    public void Tls465AlwaysUsesImplicitTls()
    {
        Assert.Equal(SecureSocketOptions.SslOnConnect,
            SmtpEmailSender.SecurityOption(new EmailSettings { Port = 465, UseSsl = true }));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(587)]
    [InlineData(2525)]
    public void TlsOtherPortsRequireStartTls(int port)
    {
        Assert.Equal(SecureSocketOptions.StartTls,
            SmtpEmailSender.SecurityOption(new EmailSettings { Port = port, UseSsl = true }));
    }

    [Fact]
    public void PlaintextLoginIsNeverAllowed()
    {
        var settings = new EmailSettings { Port = 25, UseSsl = false, UserName = "admin" };
        Assert.Throws<InvalidOperationException>(() => SmtpEmailSender.SecurityOption(settings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void InvalidPortIsRejected(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SmtpEmailSender.SecurityOption(new EmailSettings { Port = port }));
    }
}

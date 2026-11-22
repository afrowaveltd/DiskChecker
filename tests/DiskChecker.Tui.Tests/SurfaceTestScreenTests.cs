using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using NSubstitute;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class SurfaceTestScreenTests
{
    private static TestHarness CreateHarness(FakeSmartaProvider provider, FakeSurfaceRunner runner)
    {
        var detection = new FakeDiskDetectionService();
        detection.Drives.Add(FakeDiskDetectionService.Sample());
        return TestHarness.Create(configure: services =>
        {
            services.RemoveAll<ISmartaProvider>();
            services.AddSingleton<ISmartaProvider>(provider);
            services.RemoveAll<IDiskDetectionService>();
            services.AddSingleton<IDiskDetectionService>(detection);
            services.RemoveAll<ITuiSurfaceTestRunner>();
            services.AddSingleton<ITuiSurfaceTestRunner>(runner);
        });
    }

    [Fact]
    public async Task ReadTestRunsAndPersistsSession()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        using var harness = CreateHarness(provider, new FakeSurfaceRunner());
        harness.Context.Selection.SelectedDisk = FakeDiskDetectionService.Sample();
        harness.Io.EnqueueChoice("read");
        harness.Io.EnqueueChoice("back");

        var screen = new SurfaceTestScreen();
        var result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Contains("Test dokončen", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(harness.Io.LastProgress);
        Assert.Equal(100, harness.Io.LastProgress!.Percent, 3);
    }

    [Fact]
    public async Task WriteTestRequiresConfirmation()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        var runner = new FakeSurfaceRunner();
        using var harness = CreateHarness(provider, runner);
        harness.Context.Selection.SelectedDisk = FakeDiskDetectionService.Sample();
        harness.Io.EnqueueChoice("write");
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice("back");

        var screen = new SurfaceTestScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Contains("Destruktivní test povrchu", harness.Io.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SanitizationRunsThroughSanitizationService()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        var sanitization = new FakeSanitizationService();
        var detection = new FakeDiskDetectionService();
        detection.Drives.Add(FakeDiskDetectionService.Sample());

        using var harness = TestHarness.Create(configure: services =>
        {
            services.RemoveAll<ISmartaProvider>();
            services.AddSingleton<ISmartaProvider>(provider);
            services.RemoveAll<IDiskDetectionService>();
            services.AddSingleton<IDiskDetectionService>(detection);
            services.RemoveAll<ISurfaceTestService>();
            services.RemoveAll<IDiskSanitizationService>();
            services.AddSingleton<IDiskSanitizationService>(sanitization);
        });

        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("sanitize");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice("back");

        var screen = new SurfaceTestScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(1, sanitization.SanitizeCalls);
        Assert.Contains("Sanitizace", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompletedSanitizationEmailsItsStoredPdfWhenRecipientConfigured()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        var sanitization = new FakeSanitizationService();
        var detection = new FakeDiskDetectionService();
        detection.Drives.Add(FakeDiskDetectionService.Sample());
        var sender = Substitute.For<IEmailSender>();
        var smtpSettings = Substitute.For<IEmailSettingsService>();
        smtpSettings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EmailSettings { IncludeCertificateAttachment = true }));

        using var harness = TestHarness.Create(configure: services =>
        {
            services.RemoveAll<ISmartaProvider>();
            services.AddSingleton<ISmartaProvider>(provider);
            services.RemoveAll<IDiskDetectionService>();
            services.AddSingleton<IDiskDetectionService>(detection);
            services.RemoveAll<IDiskSanitizationService>();
            services.AddSingleton<IDiskSanitizationService>(sanitization);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
            services.RemoveAll<IEmailSettingsService>();
            services.AddSingleton<IEmailSettingsService>(smtpSettings);
        });
        await harness.GetRequiredService<ISettingsService>()
            .SetReportRecipientEmailAsync("technician@example.net");

        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("sanitize");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueAsk("0");
        harness.Io.EnqueueConfirm(false); // Do not partition/form at end.
        harness.Io.EnqueueChoice("back");

        await new SurfaceTestScreen().ShowAsync(
            harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(1, sanitization.SanitizeCalls);
        await sender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(email =>
                email.ToAddress == "technician@example.net" &&
                email.Attachments.Count == 1 &&
                email.Attachments[0].ContentType == "application/pdf" &&
                email.Attachments[0].Content.Length > 5 &&
                email.Attachments[0].Content[0] == (byte)'%'),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("5", 5)]
    public async Task SanitizationPausePromptKeepsZeroDefaultAndAcceptsFiveSeconds(
        string seconds, int? expectedPacing)
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        var sanitization = new FakeSanitizationService();
        var detection = new FakeDiskDetectionService();
        detection.Drives.Add(FakeDiskDetectionService.Sample());

        using var harness = TestHarness.Create(configure: services =>
        {
            services.RemoveAll<ISmartaProvider>();
            services.AddSingleton<ISmartaProvider>(provider);
            services.RemoveAll<IDiskDetectionService>();
            services.AddSingleton<IDiskDetectionService>(detection);
            services.RemoveAll<IDiskSanitizationService>();
            services.AddSingleton<IDiskSanitizationService>(sanitization);
        });

        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("sanitize");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueAsk(seconds);
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice("back");

        await new SurfaceTestScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);
        Assert.Equal(1, sanitization.SanitizeCalls);
        Assert.Equal(expectedPacing, sanitization.LastPacingSeconds);
    }

    [Fact]
    public async Task DiskPickerIsOfferedWhenNoDiskSelected()
    {
        var provider = new FakeSmartaProvider { Data = null };
        using var harness = CreateHarness(provider, new FakeSurfaceRunner());
        harness.Io.EnqueueChoice("/dev/sdz");
        harness.Io.EnqueueChoice("back");

        var screen = new SurfaceTestScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.NotNull(harness.Context.Selection.SelectedDisk);
    }
}

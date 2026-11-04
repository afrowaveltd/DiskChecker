using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Screens;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class ReportScreenTests
{
    [Fact]
    public async Task RendersEverySessionWithScoreAndTotals()
    {
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1, TestType.FullReadWrite, "A", 96, DataFakes.BaseDate))
            .WithSession(DataFakes.Session(2, 1, TestType.SmartShort, "C", 74, DataFakes.BaseDate.AddDays(-2)));
        using var harness = CreateHarness(DataFakes.Repository(fixture));

        var screen = new ReportScreen();
        ScreenResult result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenIds.Report, screen.Id);
        Assert.Equal("Nav.Report", screen.TitleKey);
        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("Samsung 870 EVO"));
        Assert.True(harness.Io.Contains("S6PENX0T123456"));
        Assert.True(harness.Io.Contains("Testů: 2"));
        Assert.True(harness.Io.Contains("Průměrné skóre: 85.0"));
    }

    [Fact]
    public async Task EmptyCatalogRendersNotice()
    {
        using var harness = CreateHarness(DataFakes.Repository());

        var screen = new ReportScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Nejsou k dispozici žádné testy."));
    }

    [Fact]
    public async Task SelectingARowRendersKpiDetail()
    {
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1, TestType.FullReadWrite, "A", 96, DataFakes.BaseDate, errors: 3));
        using var harness = CreateHarness(DataFakes.Repository(fixture));

        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("row:0");

        var screen = new ReportScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("KPI vybraného testu"));
        Assert.True(harness.Io.Contains("Zápis Ø/MIN/MAX: 512.5 MB/s"));
        Assert.True(harness.Io.Contains("Chyby zápis/čtení/verifikace: 3 / 0 / 0"));
    }

    [Fact]
    public async Task SearchActionFiltersRows()
    {
        var samsung = DataFakes.Card().WithSession(DataFakes.Session(1, 1, TestType.FullReadWrite, "A", 96, DataFakes.BaseDate));
        var western = DataFakes.Card(id: 2, model: "WD Blue 3D", serial: "WD-0002", devicePath: "/dev/sdb")
            .WithSession(DataFakes.Session(2, 2, TestType.FullReadWrite, "B", 84, DataFakes.BaseDate.AddDays(-3)));
        using var harness = CreateHarness(DataFakes.Repository(samsung, western));

        harness.Io.EnqueueChoice("search");
        harness.Io.EnqueueAsk("WD Blue");

        var screen = new ReportScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Filtr: WD Blue"));
        Assert.True(harness.Io.Contains("Testů: 1"));
    }

    [Fact]
    public async Task FullReportActionWritesReadableTextFile()
    {
        string exportDirectory = DataFakes.CreateExportDirectory();
        try
        {
            var fixture = DataFakes.Card()
                .WithSession(DataFakes.Session(1, 1, TestType.FullReadWrite, "A", 96, DataFakes.BaseDate));
            using var harness = CreateHarness(DataFakes.Repository(fixture), exportDirectory);

            harness.Io.EnqueueChoice("select");
            harness.Io.EnqueueChoice("row:0");
            harness.Io.EnqueueChoice("fullreport");

            var screen = new ReportScreen();
            ScreenResult result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

            string[] files = Directory.GetFiles(exportDirectory, "Report_*.txt");
            Assert.Single(files);
            string content = await File.ReadAllTextAsync(files[0], TestContext.Current.CancellationToken);
            Assert.Contains("ZPRÁVA O TESTU DISKU", content);
            Assert.Contains("Samsung 870 EVO", content);
            Assert.Contains("Hodnocení: A (96/100)", content);
            Assert.True(harness.Io.Contains("Uloženo do:"));
            Assert.Equal(ScreenAction.Back, result.Action);
        }
        finally
        {
            DataFakes.DeleteDirectory(exportDirectory);
        }
    }

    [Fact]
    public async Task FullReportActionUsesReportGenerationServiceWhenAvailable()
    {
        string exportDirectory = DataFakes.CreateExportDirectory();
        try
        {
            var fixture = DataFakes.Card()
                .WithSession(DataFakes.Session(1, 1, TestType.FullReadWrite, "A", 96, DataFakes.BaseDate));
            var repository = DataFakes.Repository(fixture);

            using var harness = TestHarness.Create(configure: services =>
            {
                services.RemoveAll<IDiskCardRepository>();
                services.AddScoped<IDiskCardRepository>(_ => repository);
                services.RemoveAll<ISettingsService>();
                services.AddSingleton(CreateSettings(exportDirectory));
                services.RemoveAll<ReportGenerationService>();
                services.AddScoped<ReportGenerationService>(_ => new ReportGenerationService(
                    repository,
                    new TestReportAnalysisService(),
                    Substitute.For<ITestReportExporter>()));
            });

            harness.Io.EnqueueChoice("select");
            harness.Io.EnqueueChoice("row:0");
            harness.Io.EnqueueChoice("fullreport");

            var screen = new ReportScreen();
            await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

            string[] files = Directory.GetFiles(exportDirectory, "Report_*.txt");
            Assert.Single(files);
            string content = await File.ReadAllTextAsync(files[0], TestContext.Current.CancellationToken);
            Assert.Contains("CELKOVÉ HODNOCENÍ", content);
        }
        finally
        {
            DataFakes.DeleteDirectory(exportDirectory);
        }
    }

    [Fact]
    public async Task CertificateActionExportsPdfThroughCertificateExportService()
    {
        var generator = new StubCertificateGenerator();
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1, TestType.FullReadWrite, "A", 96, DataFakes.BaseDate));

        using var harness = TestHarness.Create(configure: services =>
        {
            services.RemoveAll<IDiskCardRepository>();
            services.AddScoped<IDiskCardRepository>(_ => DataFakes.Repository(fixture));
            services.RemoveAll<ICertificateGenerator>();
            services.AddScoped<ICertificateGenerator>(_ => generator);
        });

        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("row:0");
        harness.Io.EnqueueChoice("certificate");

        var screen = new ReportScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(1, generator.PdfGenerationCount);
        Assert.NotNull(generator.LastPdfPath);
        Assert.True(harness.Io.Contains("Certifikát vytvořen: CERT-1-1"));
        Assert.True(harness.Io.Contains("PDF certifikátu uloženo:"));
    }

    [Fact]
    public async Task DeleteActionDeletesTheSessionAfterConfirmation()
    {
        var session = DataFakes.Session(4, 1, TestType.SurfaceScan, "B", 83, DataFakes.BaseDate);
        var repository = DataFakes.Repository(DataFakes.Card().WithSession(session));
        using var harness = CreateHarness(repository);

        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("row:0");
        harness.Io.EnqueueChoice("delete");
        harness.Io.EnqueueConfirm(true);

        var screen = new ReportScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        await repository.Received(1).DeleteTestSessionAsync(4);
        Assert.True(harness.Io.Contains("Test byl smazán."));
    }

    [Fact]
    public async Task FullReportWithoutSelectionShowsWarning()
    {
        var fixture = DataFakes.Card().WithSession(DataFakes.Session(1, 1));
        using var harness = CreateHarness(DataFakes.Repository(fixture));

        harness.Io.EnqueueChoice("fullreport");

        var screen = new ReportScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Nejprve vyberte test."));
    }

    private static TestHarness CreateHarness(IDiskCardRepository repository, string? exportDirectory = null) =>
        TestHarness.Create(configure: services =>
        {
            services.RemoveAll<IDiskCardRepository>();
            services.AddScoped<IDiskCardRepository>(_ => repository);
            if (exportDirectory is not null)
            {
                services.RemoveAll<ISettingsService>();
                services.AddSingleton(CreateSettings(exportDirectory));
            }
        });

    private static ISettingsService CreateSettings(string exportDirectory)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.GetDefaultExportPathAsync().Returns(Task.FromResult(exportDirectory));
        return settings;
    }
}

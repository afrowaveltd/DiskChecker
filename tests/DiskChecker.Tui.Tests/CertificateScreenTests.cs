using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Screens;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class CertificateScreenTests
{
    [Fact]
    public async Task LoadsPersistedCertificateFromTheSelection()
    {
        var fixture = CardWithCertificate(out var session, out var certificate);
        using var harness = CreateHarness(DataFakes.Repository(fixture), new StubCertificateGenerator());
        harness.Context.Selection.SelectedCertificateId = certificate.Id;

        var screen = new CertificateScreen();
        ScreenResult result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenIds.Certificate, screen.Id);
        Assert.Equal("Nav.Certificates", screen.TitleKey);
        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.DiskCardDetail, result.Target);
        Assert.True(harness.Io.Contains("CERT-2024-0001"));
        Assert.True(harness.Io.Contains("S6PENX0T123456"));
        Assert.True(harness.Io.Contains("KONEČNÁ ZNÁMKA"));
        Assert.True(harness.Io.Contains("SMART souhrn"));
        Assert.True(harness.Io.Contains("Komentář technika"));
        Assert.Equal(session.Id, harness.Context.Selection.SelectedTestSessionId!.Value);
    }

    [Fact]
    public async Task GeneratesACertificateInMemoryWhenOnlyASessionIsSelected()
    {
        var session = DataFakes.Session(7, 1, TestType.FullReadWrite, "B", 88, DataFakes.BaseDate);
        var fixture = DataFakes.Card().WithSession(session);
        var generator = new StubCertificateGenerator();
        using var harness = CreateHarness(DataFakes.Repository(fixture), generator);
        harness.Context.Selection.SelectedTestSessionId = session.Id;

        var screen = new CertificateScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Single(generator.Generated);
        Assert.True(harness.Io.Contains("CERT-1-7"));
        Assert.True(harness.Io.Contains("FullReadWrite"));
    }

    [Fact]
    public async Task LoadsTheLatestCertificateWhenTheSelectedDiskResolvesTheCard()
    {
        var fixture = CardWithCertificate(out _, out var certificate);
        using var harness = CreateHarness(DataFakes.Repository(fixture), new StubCertificateGenerator());
        harness.Context.Selection.SelectedDisk = new CoreDriveInfo { Path = "/dev/sda", Name = "Samsung 870 EVO" };

        var screen = new CertificateScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains(certificate.CertificateNumber));
        Assert.Equal(1, harness.Context.Selection.SelectedDiskCardId!.Value);
    }

    [Fact]
    public async Task SwitchingTheSessionRegeneratesTheCertificateInMemory()
    {
        var newer = DataFakes.Session(7, 1, TestType.FullReadWrite, "A", 96, DataFakes.BaseDate, certificateId: 5);
        var older = DataFakes.Session(8, 1, TestType.SurfaceScan, "C", 71, DataFakes.BaseDate.AddDays(-5));
        var certificate = DataFakes.Certificate(5, 1, newer.Id, notes: "Bez závad.");
        var fixture = DataFakes.Card().WithSession(newer).WithSession(older).WithCertificate(certificate);
        var generator = new StubCertificateGenerator();
        using var harness = CreateHarness(DataFakes.Repository(fixture), generator);
        harness.Context.Selection.SelectedCertificateId = certificate.Id;

        harness.Io.EnqueueChoice("session");
        harness.Io.EnqueueChoice("session:8");

        var screen = new CertificateScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Single(generator.Generated);
        Assert.Equal(8, harness.Context.Selection.SelectedTestSessionId!.Value);
        Assert.True(harness.Io.Contains("SurfaceScan"));
    }

    [Fact]
    public async Task SavePdfUsesTheCertificateGenerator()
    {
        var fixture = CardWithCertificate(out _, out var certificate);
        var generator = new StubCertificateGenerator();
        using var harness = CreateHarness(DataFakes.Repository(fixture), generator);
        harness.Context.Selection.SelectedCertificateId = certificate.Id;

        harness.Io.EnqueueChoice("pdf");

        var screen = new CertificateScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(1, generator.PdfGenerationCount);
        Assert.True(harness.Io.Contains("PDF uloženo:"));
    }

    [Fact]
    public async Task TechnicianNoteIsPersistedThroughTheRepository()
    {
        var fixture = CardWithCertificate(out _, out var certificate);
        var repository = DataFakes.Repository(fixture);
        using var harness = CreateHarness(repository, new StubCertificateGenerator());
        harness.Context.Selection.SelectedCertificateId = certificate.Id;

        harness.Io.EnqueueChoice("note");
        harness.Io.EnqueueAsk("Disk v minulosti přehřát, nyní bez chyb.");

        var screen = new CertificateScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        await repository.Received(1).UpdateCertificateTechnicianAssessmentAsync(
            certificate.Id,
            "Disk v minulosti přehřát, nyní bez chyb.");
        Assert.True(harness.Io.Contains("Komentář technika byl uložen"));
        Assert.True(harness.Io.Contains("Disk v minulosti přehřát, nyní bez chyb."));
    }

    [Fact]
    public async Task MissingCertificateShowsNoticeAndFallsBackToTheCardDetail()
    {
        using var harness = CreateHarness(DataFakes.Repository(), new StubCertificateGenerator());

        var screen = new CertificateScreen();
        ScreenResult result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.DiskCardDetail, result.Target);
        Assert.True(harness.Io.Contains("Žádný certifikát nenalezen"));
        Assert.Equal(1, harness.Io.PauseCount);
    }

    private static CardFixture CardWithCertificate(out TestSession session, out DiskCertificate certificate)
    {
        session = DataFakes.Session(7, 1, TestType.FullReadWrite, "A", 96, DataFakes.BaseDate, certificateId: 5);
        certificate = DataFakes.Certificate(5, 1, session.Id, notes: "propad rychlosti při 62 %; nestabilní průběh");
        return DataFakes.Card().WithSession(session).WithCertificate(certificate);
    }

    private static TestHarness CreateHarness(IDiskCardRepository repository, StubCertificateGenerator generator) =>
        TestHarness.Create(configure: services =>
        {
            services.RemoveAll<IDiskCardRepository>();
            services.AddScoped<IDiskCardRepository>(_ => repository);
            services.RemoveAll<ICertificateGenerator>();
            services.AddScoped<ICertificateGenerator>(_ => generator);
        });
}

public sealed class CertificateBrowserScreenTests
{
    [Fact]
    public async Task ListsEveryCertificateOfEveryCard()
    {
        var first = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1))
            .WithCertificate(DataFakes.Certificate(5, 1, 1, number: "CERT-2024-0001"));
        var second = DataFakes.Card(id: 2, model: "WD Blue 3D", serial: "WD-0002", devicePath: "/dev/sdb")
            .WithSession(DataFakes.Session(2, 2))
            .WithCertificate(DataFakes.Certificate(6, 2, 2, number: "CERT-2024-0002", model: "WD Blue 3D", serial: "WD-0002"));
        using var harness = CreateHarness(DataFakes.Repository(first, second), new StubCertificateGenerator());

        var screen = new CertificateBrowserScreen();
        ScreenResult result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenIds.Certificates, screen.Id);
        Assert.Equal("Nav.Certificates", screen.TitleKey);
        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("CERT-2024-0001"));
        Assert.True(harness.Io.Contains("CERT-2024-0002"));
        Assert.True(harness.Io.Contains("Zobrazeno 2 z 2 certifikátů"));
    }

    [Fact]
    public async Task SelectingACertificateRendersTheWholeDocument()
    {
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1))
            .WithCertificate(DataFakes.Certificate(5, 1, 1, notes: "propad rychlosti při 62 %"));
        using var harness = CreateHarness(DataFakes.Repository(fixture), new StubCertificateGenerator());

        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("row:0");

        var screen = new CertificateBrowserScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("CERTIFIKÁT KVALITY DISKU"));
        Assert.True(harness.Io.Contains("S6PENX0T123456"));
        Assert.True(harness.Io.Contains("SMART souhrn"));
        Assert.True(harness.Io.Contains("Porovnání sanitizací") || harness.Io.Contains("VAROVNÉ SIGNÁLY"));
        Assert.True(harness.Io.Contains("VAROVNÉ SIGNÁLY"));
        Assert.True(harness.Io.Contains("Komentář technika"));
    }

    [Fact]
    public async Task TextFilterNarrowsTheList()
    {
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1))
            .WithCertificate(DataFakes.Certificate(5, 1, 1));
        using var harness = CreateHarness(DataFakes.Repository(fixture), new StubCertificateGenerator());

        harness.Io.EnqueueChoice("search");
        harness.Io.EnqueueAsk("EVO");

        var screen = new CertificateBrowserScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Filtr: EVO"));
        Assert.True(harness.Io.Contains("Zobrazeno 1 z 1 certifikátů"));
    }

    [Fact]
    public async Task GradeFilterNarrowsTheList()
    {
        var first = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1))
            .WithCertificate(DataFakes.Certificate(5, 1, 1, number: "CERT-2024-0001", grade: "A"));
        var second = DataFakes.Card(id: 2, model: "WD Blue 3D", serial: "WD-0002", devicePath: "/dev/sdb")
            .WithSession(DataFakes.Session(2, 2))
            .WithCertificate(DataFakes.Certificate(6, 2, 2, number: "CERT-2024-0002", grade: "C"));
        using var harness = CreateHarness(DataFakes.Repository(first, second), new StubCertificateGenerator());

        harness.Io.EnqueueChoice("grade");
        harness.Io.EnqueueChoice("A");

        var screen = new CertificateBrowserScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Známka: A"));
        Assert.True(harness.Io.Contains("Zobrazeno 1 z 2 certifikátů"));
    }

    [Fact]
    public async Task ExportPdfGeneratesThePdfAndPersistsTheCertificate()
    {
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1))
            .WithCertificate(DataFakes.Certificate(5, 1, 1));
        var repository = DataFakes.Repository(fixture);
        var generator = new StubCertificateGenerator();
        using var harness = CreateHarness(repository, generator);

        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("row:0");
        harness.Io.EnqueueChoice("pdf");

        var screen = new CertificateBrowserScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(1, generator.PdfGenerationCount);
        await repository.Received(1).UpdateCertificateAsync(Arg.Any<DiskCertificate>());
        Assert.True(harness.Io.Contains("PDF exportováno:"));
    }

    [Fact]
    public async Task TechnicianCommentIsSavedForTheSelectedCertificate()
    {
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1))
            .WithCertificate(DataFakes.Certificate(5, 1, 1));
        var repository = DataFakes.Repository(fixture);
        using var harness = CreateHarness(repository, new StubCertificateGenerator());

        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("row:0");
        harness.Io.EnqueueChoice("technician");
        harness.Io.EnqueueAsk("Historické přehřátí, aktuální test bez chyb.");

        var screen = new CertificateBrowserScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        await repository.Received(1).UpdateCertificateTechnicianAssessmentAsync(
            5,
            "Historické přehřátí, aktuální test bez chyb.");
        Assert.True(harness.Io.Contains("Komentář technika byl uložen"));
    }

    [Fact]
    public async Task OpenDiskCardNavigatesToTheCardDetail()
    {
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1))
            .WithCertificate(DataFakes.Certificate(5, 1, 1));
        using var harness = CreateHarness(DataFakes.Repository(fixture), new StubCertificateGenerator());

        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("row:0");
        harness.Io.EnqueueChoice("card");

        var screen = new CertificateBrowserScreen();
        ScreenResult result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.DiskCardDetail, result.Target);
        Assert.Equal(1, harness.Context.Selection.SelectedDiskCardId!.Value);
    }

    [Fact]
    public async Task EmptyCatalogRendersNotice()
    {
        using var harness = CreateHarness(DataFakes.Repository(), new StubCertificateGenerator());

        var screen = new CertificateBrowserScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Žádné certifikáty"));
    }

    [Fact]
    public async Task ExportPdfWithoutSelectionShowsWarning()
    {
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1))
            .WithCertificate(DataFakes.Certificate(5, 1, 1));
        using var harness = CreateHarness(DataFakes.Repository(fixture), new StubCertificateGenerator());

        harness.Io.EnqueueChoice("pdf");

        var screen = new CertificateBrowserScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Nejprve vyberte certifikát."));
    }

    private static TestHarness CreateHarness(IDiskCardRepository repository, StubCertificateGenerator generator) =>
        TestHarness.Create(configure: services =>
        {
            services.RemoveAll<IDiskCardRepository>();
            services.AddScoped<IDiskCardRepository>(_ => repository);
            services.RemoveAll<ICertificateGenerator>();
            services.AddScoped<ICertificateGenerator>(_ => generator);
        });
}

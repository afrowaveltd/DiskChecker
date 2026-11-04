using DiskChecker.Core.Models;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Xunit;

namespace DiskChecker.Tui.Tests;

/// <summary>End to end tests of the disk card browser and the disk card detail screen.</summary>
public sealed class DiskCardsScreenTests
{
    private const long Gigabyte = 1024L * 1024L * 1024L;

    private static DiskFakes CreateCardWorld()
    {
        var fakes = new DiskFakes();

        DiskCard alpha = DiskFakes.CreateCard(
            DiskFakes.CreateDrive("/dev/sda", "TestCardAlpha", 500 * Gigabyte, serialNumber: "ALPHASER0001"),
            grade: "A",
            score: 96);
        alpha.Id = 1;
        alpha.TestCount = 2;
        alpha.LastTestedAt = new DateTime(2025, 3, 12, 14, 30, 0, DateTimeKind.Utc);

        DiskCard beta = DiskFakes.CreateCard(
            DiskFakes.CreateDrive("/dev/sdb", "TestCardBeta", 2000 * Gigabyte, serialNumber: "BETASER0002"),
            grade: "F",
            score: 18);
        beta.Id = 2;
        beta.TestCount = 1;
        beta.LastTestedAt = new DateTime(2025, 2, 2, 9, 15, 0, DateTimeKind.Utc);

        fakes.Repository.Cards.Add(alpha);
        fakes.Repository.Cards.Add(beta);
        return fakes;
    }

    [Fact]
    public async Task RendersTheCardTable()
    {
        DiskFakes fakes = CreateCardWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("TestCardAlpha"));
        Assert.True(harness.Io.Contains("TestCardBeta"));
        Assert.True(harness.Io.Contains("Výrobce"));
        Assert.True(harness.Io.Contains("ALPHASER0001"));
        Assert.True(harness.Io.Contains("Poslední test"));
        Assert.True(harness.Io.Contains("12.03.2025"));
        Assert.True(harness.Io.Contains("Celkem 2 karet disků"));
    }

    [Fact]
    public async Task SearchFilterKeepsOnlyMatchingCards()
    {
        DiskFakes fakes = CreateCardWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("search");
        harness.Io.EnqueueAsk("Alpha");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == "card:0" && choice.Label.Contains("TestCardAlpha", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Io.LastChoices, choice => choice.Label.Contains("TestCardBeta", StringComparison.Ordinal));
        Assert.True(harness.Io.Contains("Hledat: Alpha"));
        Assert.True(harness.Io.Contains("Filtr hledání: Alpha"));
    }

    [Fact]
    public async Task GradeFilterKeepsOnlyMatchingCards()
    {
        DiskFakes fakes = CreateCardWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("grade");
        harness.Io.EnqueueChoice("grade:F");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Contains(harness.Io.LastChoices, choice => choice.Label.Contains("TestCardBeta", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Io.LastChoices, choice => choice.Label.Contains("TestCardAlpha", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LockedFilterKeepsOnlyLockedCards()
    {
        DiskFakes fakes = CreateCardWorld();
        fakes.Repository.Cards[1].IsLocked = true;
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("locked");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Contains(harness.Io.LastChoices, choice => choice.Label.Contains("TestCardBeta", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Io.LastChoices, choice => choice.Label.Contains("TestCardAlpha", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArchivedFilterKeepsOnlyArchivedCards()
    {
        DiskFakes fakes = CreateCardWorld();
        fakes.Repository.Cards[0].IsArchived = true;
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("archived");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Contains(harness.Io.LastChoices, choice => choice.Label.Contains("TestCardAlpha", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Io.LastChoices, choice => choice.Label.Contains("TestCardBeta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DetailWritesTheSelectionAndNavigates()
    {
        DiskFakes fakes = CreateCardWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("card:0");
        harness.Io.EnqueueChoice("detail");

        ScreenResult result = await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.DiskCardDetail, result.Target);
        Assert.Equal(1, harness.Context.Selection.SelectedDiskCardId);
        Assert.Equal("/dev/sda", harness.Context.Selection.SelectedDisk!.Path);
        Assert.Equal("TestCardAlpha", harness.Context.Selection.SelectedDiskDisplayName);
    }

    [Fact]
    public async Task ArchiveConfirmsAndStoresTheReason()
    {
        DiskFakes fakes = CreateCardWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("card:0");
        harness.Io.EnqueueChoice("archive");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueAsk("Vadný");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Equal(new[] { "Failed" }, fakes.Repository.Archived);
        Assert.True(fakes.Repository.Cards[0].IsArchived);
        Assert.True(harness.Io.Contains("Disk archivován"));
    }

    [Fact]
    public async Task ArchiveIsCancelledWithoutConfirmation()
    {
        DiskFakes fakes = CreateCardWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("card:0");
        harness.Io.EnqueueChoice("archive");
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Empty(fakes.Repository.Archived);
        Assert.False(fakes.Repository.Cards[0].IsArchived);
    }

    [Fact]
    public async Task RestoreReactivatesAnArchivedCard()
    {
        DiskFakes fakes = CreateCardWorld();
        fakes.Repository.Cards[0].IsArchived = true;
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("card:0");
        harness.Io.EnqueueChoice("restore");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.False(fakes.Repository.Cards[0].IsArchived);
        Assert.Single(fakes.Repository.Restored);
        Assert.True(harness.Io.Contains("Disk obnoven"));
    }

    [Fact]
    public async Task CertificateActionSetsTheLatestSessionAndCertificate()
    {
        DiskFakes fakes = CreateCardWorld();
        DiskCard card = fakes.Repository.Cards[0];
        TestSession session = DiskFakes.CreateSession(card, 77, new DateTime(2025, 3, 12, 14, 30, 0, DateTimeKind.Utc));
        fakes.Repository.Sessions.Add(session);
        fakes.Repository.Certificates.Add(new DiskCertificate
        {
            Id = 9,
            DiskCardId = card.Id,
            TestSessionId = session.Id,
            CertificateNumber = "CERT-ALPHA-1",
            GeneratedAt = new DateTime(2025, 3, 13, 8, 0, 0, DateTimeKind.Utc),
            Grade = "A",
            Score = 96,
        });

        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("card:0");
        harness.Io.EnqueueChoice("certificate");

        ScreenResult result = await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.Certificate, result.Target);
        Assert.Equal(77, harness.Context.Selection.SelectedTestSessionId);
        Assert.Equal(9, harness.Context.Selection.SelectedCertificateId);
    }

    [Fact]
    public async Task CertificateActionWarnsWhenTheCardHasNoTests()
    {
        DiskFakes fakes = CreateCardWorld();
        fakes.Repository.Cards[0].TestCount = 0;
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("card:0");
        harness.Io.EnqueueChoice("certificate");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("ještě nebyl testován"));
    }

    [Fact]
    public async Task RefreshMergesDuplicateCards()
    {
        DiskFakes fakes = CreateCardWorld();
        fakes.Repository.MergeResult = 2;
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("refresh");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(1, fakes.Repository.MergeCallCount);
        Assert.True(harness.Io.Contains("Sloučeno duplicitních karet: 2"));
    }

    [Fact]
    public async Task InitialLoadDoesNotMergeDuplicates()
    {
        DiskFakes fakes = CreateCardWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(0, fakes.Repository.MergeCallCount);
    }

    [Fact]
    public async Task MainMenuAndBackAreOffered()
    {
        DiskFakes fakes = CreateCardWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice(TuiKeys.MainMenu);

        ScreenResult result = await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Menu, result.Action);
        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == "search");
        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == "grade");
        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == "locked");
        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == "archived");
        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == TuiKeys.Back);
    }

    [Fact]
    public async Task RendersInAnEightyColumnTerminal()
    {
        DiskFakes fakes = CreateCardWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply, terminalWidth: 80);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskCardsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("TestCardAlpha"));
        Assert.True(harness.Io.Contains("Poslední test"));
    }

    [Theory]
    [InlineData("Vadný", ArchiveReason.Failed)]
    [InlineData("Prodáno", ArchiveReason.Sold)]
    [InlineData("Darováno", ArchiveReason.Donated)]
    [InlineData("Recyklováno", ArchiveReason.Recycled)]
    [InlineData("Nahrazeno", ArchiveReason.Replaced)]
    [InlineData("Vlastní důvod", ArchiveReason.Other)]
    [InlineData("", ArchiveReason.Other)]
    public void ArchiveReasonMapsFromTheLocalizedText(string reason, ArchiveReason expected)
    {
        using var harness = TestHarness.Create();

        Assert.Equal(expected, DiskCardsScreen.MapArchiveReason(harness.Context, reason));
    }

    [Fact]
    public void CardFiltersMatchTheDesktopRules()
    {
        DiskCard alpha = DiskFakes.CreateCard(
            DiskFakes.CreateDrive("/dev/sda", "AlphaDisk", 500 * Gigabyte, serialNumber: "ALPHASER0001"),
            grade: "A");
        DiskCard beta = DiskFakes.CreateCard(
            DiskFakes.CreateDrive("/dev/sdb", "BetaDisk", 500 * Gigabyte, serialNumber: "BETASER0002"),
            grade: "F",
            archived: true);
        beta.Notes = "vadné sektory";
        var cards = new[] { alpha, beta };

        Assert.Equal(new[] { alpha, beta }, DiskCardsScreen.FilterCards(cards, null, null, false, false));
        Assert.Equal(new[] { beta }, DiskCardsScreen.FilterCards(cards, "beta", null, false, false));
        Assert.Equal(new[] { beta }, DiskCardsScreen.FilterCards(cards, "vadné", null, false, false));
        Assert.Equal(new[] { alpha }, DiskCardsScreen.FilterCards(cards, null, "A", false, false));
        Assert.Empty(DiskCardsScreen.FilterCards(cards, null, null, true, false));
        Assert.Equal(new[] { beta }, DiskCardsScreen.FilterCards(cards, null, null, false, true));
    }
}

/// <summary>End to end tests of the disk card detail screen.</summary>
public sealed class DiskCardDetailScreenTests
{
    private const long Gigabyte = 1024L * 1024L * 1024L;

    private static (DiskFakes Fakes, DiskCard Card, TestSession Latest) CreateDetailWorld()
    {
        var fakes = new DiskFakes();
        CoreDriveInfo drive = DiskFakes.CreateDrive("/dev/sda", "DetailDisk", 500 * Gigabyte, serialNumber: "DETAILSER01");
        DiskCard card = DiskFakes.CreateCard(drive, grade: "B", score: 84);
        card.Id = 11;
        card.FirmwareVersion = "FW01";
        card.TestCount = 2;
        fakes.Repository.Cards.Add(card);

        TestSession older = DiskFakes.CreateSession(card, 21, new DateTime(2025, 2, 1, 10, 0, 0, DateTimeKind.Utc), grade: "C", score: 70);
        older.TemperatureSamples.Clear();
        TestSession latest = DiskFakes.CreateSession(card, 22, new DateTime(2025, 3, 12, 14, 30, 0, DateTimeKind.Utc), grade: "B", score: 84);
        fakes.Repository.Sessions.Add(older);
        fakes.Repository.Sessions.Add(latest);

        fakes.Repository.Certificates.Add(new DiskCertificate
        {
            Id = 5,
            DiskCardId = card.Id,
            TestSessionId = latest.Id,
            CertificateNumber = "CERT-2025-0001",
            GeneratedAt = new DateTime(2025, 3, 13, 8, 0, 0, DateTimeKind.Utc),
            Grade = "B",
            Score = 84,
            HealthStatus = "OK",
            PdfGenerated = true,
        });

        return (fakes, card, latest);
    }

    [Fact]
    public async Task RendersIdentitySessionsAndCertificates()
    {
        (DiskFakes fakes, DiskCard card, _) = CreateDetailWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Context.Selection.SelectedDiskCardId = card.Id;
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskCardDetailScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.DiskCards, result.Target);
        Assert.True(harness.Io.Contains("DetailDisk"));
        Assert.True(harness.Io.Contains("DETAILSER01"));
        Assert.True(harness.Io.Contains("Historie testů"));
        Assert.True(harness.Io.Contains("12.03.2025 14:30"));
        Assert.True(harness.Io.Contains("01.02.2025 10:00"));
        Assert.True(harness.Io.Contains("CERT-2025-0001"));
        Assert.True(harness.Io.Contains("KONEČNÁ ZNÁMKA"));
    }

    [Fact]
    public async Task FallsBackToTheSelectedDiskWhenNoCardIdIsKnown()
    {
        (DiskFakes fakes, DiskCard card, _) = CreateDetailWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Context.Selection.SelectedDisk = new CoreDriveInfo
        {
            Path = card.DevicePath,
            Name = card.ModelName,
            TotalSize = card.Capacity,
            SerialNumber = card.SerialNumber,
            FirmwareVersion = card.FirmwareVersion,
        };
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardDetailScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("CERT-2025-0001"));
    }

    [Fact]
    public async Task UnknownCardIsReportedInsteadOfCrashing()
    {
        (DiskFakes fakes, _, _) = CreateDetailWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Context.Selection.SelectedDiskCardId = 999;
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskCardDetailScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.True(harness.Io.Contains("Karta nenalezena"));
    }

    [Fact]
    public async Task SessionSelectionAndDetailShowSmartAndErrors()
    {
        (DiskFakes fakes, DiskCard card, _) = CreateDetailWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Context.Selection.SelectedDiskCardId = card.Id;
        harness.Io.EnqueueChoice("session:1");
        harness.Io.EnqueueChoice("detail");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardDetailScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("nejsou evidované detailní chyby"));
        Assert.True(harness.Io.Contains("SMART snímek"));
        Assert.True(harness.Io.Contains("37°C"));
    }

    [Fact]
    public async Task SaveNotesUpdatesTheStoredCard()
    {
        (DiskFakes fakes, DiskCard card, _) = CreateDetailWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Context.Selection.SelectedDiskCardId = card.Id;
        harness.Io.EnqueueChoice("notes");
        harness.Io.EnqueueAsk("Poznámka z terminálu");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskCardDetailScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal("Poznámka z terminálu", card.Notes);
        Assert.Equal(1, fakes.Repository.UpdateCallCount);
        Assert.True(harness.Io.Contains("Poznámka uložena"));
    }

    [Fact]
    public async Task GenerateCertificateExportsTheSelectedSessionAndNavigates()
    {
        (DiskFakes fakes, DiskCard card, TestSession latest) = CreateDetailWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Context.Selection.SelectedDiskCardId = card.Id;
        harness.Io.EnqueueChoice("certificate");

        ScreenResult result = await new DiskCardDetailScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.Certificate, result.Target);
        Assert.Equal(new[] { latest.Id }, fakes.CertificateGenerator.GeneratedSessions);
        Assert.Equal(2, fakes.Repository.Certificates.Count);
        Assert.Equal(latest.Id, harness.Context.Selection.SelectedTestSessionId);
        Assert.NotNull(harness.Context.Selection.SelectedCertificateId);
        Assert.NotNull(harness.Io.LastProgress);
        Assert.True(harness.Io.Contains("Generuji certifikát"));
    }

    [Fact]
    public async Task GenerateCertificateWithoutSessionsReportsAnError()
    {
        var fakes = new DiskFakes();
        DiskCard card = DiskFakes.CreateCard(DiskFakes.CreateDrive("/dev/sdc", "NoTestDisk", 128 * Gigabyte, serialNumber: "NOTESTSER01"));
        card.Id = 31;
        card.TestCount = 0;
        fakes.Repository.Cards.Add(card);

        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Context.Selection.SelectedDiskCardId = card.Id;
        harness.Io.EnqueueChoice("certificate");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskCardDetailScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.DiskCards, result.Target);
        Assert.Empty(fakes.CertificateGenerator.GeneratedSessions);
        Assert.True(harness.Io.Contains("Neexistuje žádný test"));
    }

    [Fact]
    public void GradeSealContainsGradeScoreAndHealth()
    {
        (_, DiskCard card, _) = CreateDetailWorld();
        using var harness = TestHarness.Create();

        string seal = DiskCardDetailScreen.BuildGradeSeal(harness.Context, card);

        Assert.Contains("B", seal, StringComparison.Ordinal);
        Assert.Contains("84", seal, StringComparison.Ordinal);
        Assert.Contains("OK", seal, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorSummaryListsEachStoredError()
    {
        (DiskFakes fakes, DiskCard card, TestSession latest) = CreateDetailWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        latest.Errors.Add(new TestError
        {
            Timestamp = new DateTime(2025, 3, 12, 14, 40, 0, DateTimeKind.Utc),
            ErrorCode = "IO_READ",
            Message = "Chyba čtení",
            Phase = "Read",
            IsCritical = true,
            Details = "sektor 4096",
        });

        var builder = new System.Text.StringBuilder();
        DiskCardDetailScreen.AppendErrors(harness.Context, builder, latest);

        Assert.Contains("IO_READ", builder.ToString(), StringComparison.Ordinal);
        Assert.Contains("Chyba čtení", builder.ToString(), StringComparison.Ordinal);
        Assert.Contains("sektor 4096", builder.ToString(), StringComparison.Ordinal);
        Assert.Equal(11, card.Id);
    }
}

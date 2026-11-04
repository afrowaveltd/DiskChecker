using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.TUI.Screens;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class HistoryScreenTests
{
    [Fact]
    public async Task RendersReportsWithSummaryTotals()
    {
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1, TestType.FullReadWrite, "A", 96, DataFakes.BaseDate))
            .WithSession(DataFakes.Session(2, 1, TestType.SmartShort, "C", 72, DataFakes.BaseDate.AddDays(-1)));
        var repository = DataFakes.Repository(fixture);

        using var harness = CreateHarness(repository);
        var screen = new HistoryScreen();

        ScreenResult result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenIds.History, screen.Id);
        Assert.Equal("Nav.History", screen.TitleKey);
        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("Samsung 870 EVO"));
        Assert.True(harness.Io.Contains("Záznamů: 2"));
        Assert.True(harness.Io.Contains("Průměrné skóre: 84.0"));
    }

    [Fact]
    public async Task FallsBackToLegacyHistoryRowsWhenNoTestSessionsExist()
    {
        var repository = DataFakes.Repository();
        using var harness = CreateHarness(repository);
        SeedLegacyTest(harness, "Old Disk 123", "LEGACY01");

        var screen = new HistoryScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Old Disk 123"));
        Assert.True(harness.Io.Contains("FullReadWrite"));
        Assert.True(harness.Io.Contains("Záznamů: 1"));
    }

    [Fact]
    public async Task EmptyHistoryRendersNotice()
    {
        using var harness = CreateHarness(DataFakes.Repository());

        var screen = new HistoryScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Historie je prázdná"));
    }

    [Fact]
    public async Task FilterActionLimitsRowsToTheChosenTestType()
    {
        var fixture = DataFakes.Card()
            .WithSession(DataFakes.Session(1, 1, TestType.FullReadWrite, "A", 96, DataFakes.BaseDate))
            .WithSession(DataFakes.Session(2, 1, TestType.SmartShort, "C", 72, DataFakes.BaseDate.AddDays(-1)));
        using var harness = CreateHarness(DataFakes.Repository(fixture));

        harness.Io.EnqueueChoice("filter");
        harness.Io.EnqueueChoice("smart");

        var screen = new HistoryScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Filtr: SMART"));
        Assert.True(harness.Io.Contains("Záznamů: 1"));
    }

    [Fact]
    public async Task DeleteSelectedRemovesTheTestSessionAfterConfirmation()
    {
        var older = DataFakes.Session(1, 1, TestType.FullReadWrite, "B", 88, DataFakes.BaseDate);
        var newer = DataFakes.Session(2, 1, TestType.SurfaceScan, "A", 96, DataFakes.BaseDate.AddDays(1));
        var fixture = DataFakes.Card().WithSession(older).WithSession(newer);
        var repository = DataFakes.Repository(fixture);
        using var harness = CreateHarness(repository);

        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("row:0");
        harness.Io.EnqueueChoice("delete");
        harness.Io.EnqueueConfirm(true);

        var screen = new HistoryScreen();
        ScreenResult result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        await repository.Received(1).DeleteTestSessionAsync(newer.Id);
        Assert.True(harness.Io.Contains("Test úspěšně smazán"));
        Assert.Equal(ScreenAction.Back, result.Action);
    }

    [Fact]
    public async Task DeleteSelectedIsIgnoredWhenNoRowIsSelected()
    {
        var fixture = DataFakes.Card().WithSession(DataFakes.Session(1, 1));
        var repository = DataFakes.Repository(fixture);
        using var harness = CreateHarness(repository);

        harness.Io.EnqueueChoice("delete");

        var screen = new HistoryScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Nejprve vyberte záznam historie."));
        await repository.DidNotReceive().DeleteTestSessionAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task DetailNavigatesToCertificateWhenSerialResolvesACard()
    {
        var session = DataFakes.Session(7, 1, TestType.FullReadWrite, "A", 96, certificateId: 11);
        var fixture = DataFakes.Card().WithSession(session);
        using var harness = CreateHarness(DataFakes.Repository(fixture));

        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("row:0");
        harness.Io.EnqueueChoice("detail");

        var screen = new HistoryScreen();
        ScreenResult result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.Certificate, result.Target);
        Assert.Equal(7, harness.Context.Selection.SelectedTestSessionId!.Value);
        Assert.Equal(11, harness.Context.Selection.SelectedCertificateId!.Value);
        Assert.Equal(1, harness.Context.Selection.SelectedDiskCardId!.Value);
    }

    [Fact]
    public async Task DetailRendersTextPanelWhenNoCardMatchesTheSerialNumber()
    {
        using var harness = CreateHarness(DataFakes.Repository());
        SeedLegacyTest(harness, "Old Disk 123", "LEGACY01");

        harness.Io.EnqueueChoice("select");
        harness.Io.EnqueueChoice("row:0");
        harness.Io.EnqueueChoice("detail");

        var screen = new HistoryScreen();
        ScreenResult result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("Detail historie"));
        Assert.True(harness.Io.Contains("Hodnocení: B (81)"));
    }

    [Fact]
    public async Task ClearAllRemovesLegacyHistoryAfterConfirmation()
    {
        using var harness = CreateHarness(DataFakes.Repository());
        SeedLegacyTest(harness, "Old Disk 123", "LEGACY01");

        harness.Io.EnqueueChoice("clear");
        harness.Io.EnqueueConfirm(true);

        var screen = new HistoryScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Historie vymazána (1 záznamů)"));
    }

    private static TestHarness CreateHarness(IDiskCardRepository repository) =>
        TestHarness.Create(configure: services =>
        {
            services.RemoveAll<IDiskCardRepository>();
            services.AddScoped<IDiskCardRepository>(_ => repository);
        });

    private static void SeedLegacyTest(TestHarness harness, string model, string serial)
    {
        using var scope = harness.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
        var drive = new DriveRecord
        {
            Id = Guid.NewGuid(),
            Path = "/dev/sdz",
            Name = model,
            DeviceModel = model,
            SerialNumber = serial,
            FirmwareVersion = "1.0",
            FileSystem = "ext4",
            FirstSeen = DataFakes.BaseDate,
            LastSeen = DataFakes.BaseDate,
        };

        database.DriveRecords.Add(drive);
        database.TestRecords.Add(new TestRecord
        {
            Id = Guid.NewGuid(),
            DriveId = drive.Id,
            TestDate = DataFakes.BaseDate,
            TestType = "FullReadWrite",
            Grade = "B",
            Score = 81,
            Errors = 2,
            IsCompleted = true,
            Drive = drive,
        });

        database.SaveChanges();
    }
}

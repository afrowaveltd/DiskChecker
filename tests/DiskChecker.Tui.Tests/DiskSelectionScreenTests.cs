using DiskChecker.Core.Models;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Xunit;

namespace DiskChecker.Tui.Tests;

/// <summary>End to end tests of the disk selection screen driven through a scripted terminal.</summary>
public sealed class DiskSelectionScreenTests
{
    private const long Gigabyte = 1024L * 1024L * 1024L;

    private static DiskFakes CreateTwoDiskWorld()
    {
        var fakes = new DiskFakes();
        fakes.Detection.Drives.Add(DiskFakes.CreateDrive("/dev/sda", "SystemDisk", 500 * Gigabyte, isSystemDisk: true, serialNumber: "SYSSERIAL0001"));
        fakes.Detection.Drives.Add(DiskFakes.CreateDrive("/dev/sdb", "DataDisk", 1000 * Gigabyte, serialNumber: "DATASERIAL0002"));
        fakes.Smart.Data["/dev/sda"] = DiskFakes.CreateSmart("SystemDisk", "SYSSERIAL0001", temperature: 35);
        fakes.Smart.Data["/dev/sdb"] = DiskFakes.CreateSmart("DataDisk", "DATASERIAL0002", temperature: 44);
        fakes.Quality.RatingsByModel["SystemDisk"] = new QualityRating(QualityGrade.A, 96);
        fakes.Quality.RatingsByModel["DataDisk"] = new QualityRating(QualityGrade.F, 20);
        return fakes;
    }

    [Fact]
    public async Task RendersDashboardStatisticsAndDiskTable()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("SystemDisk"));
        Assert.True(harness.Io.Contains("DataDisk"));
        // Table only content (headers + device path).
        Assert.True(harness.Io.Contains("Sběrnice"));
        Assert.True(harness.Io.Contains("/dev/sdb"));
        Assert.True(harness.Io.Contains("SYS"));
        Assert.True(harness.Io.Contains("35°C"));
        // Dashboard: two graded disks, only one of them healthy.
        Assert.True(harness.Io.Contains("Zdravé: 1/2"));
        Assert.True(harness.Io.Contains("potřebuje pozornost"));
        Assert.True(harness.Io.Contains("Systémový disk: SystemDisk"));
    }

    [Fact]
    public async Task HealthyDisksProduceTheAllOkSummary()
    {
        var fakes = new DiskFakes();
        fakes.Detection.Drives.Add(DiskFakes.CreateDrive("/dev/sda", "GoodDisk", 256 * Gigabyte, serialNumber: "GOODSERIAL01"));
        fakes.Smart.Data["/dev/sda"] = DiskFakes.CreateSmart("GoodDisk", "GOODSERIAL01");
        fakes.Quality.RatingsByModel["GoodDisk"] = new QualityRating(QualityGrade.A, 98);
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Všechny disky v pořádku"));
        Assert.True(harness.Io.Contains("Zdravé: 1/1"));
    }

    [Fact]
    public async Task DisksWithoutSmartSupportAreNotProbedAndAreGradedAsUnknown()
    {
        var fakes = new DiskFakes();
        fakes.Detection.Drives.Add(DiskFakes.CreateDrive("/dev/sdc", "UsbStick", 32 * Gigabyte, supportsSmart: false, serialNumber: "USB0000001"));
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("/dev/sdc", fakes.Smart.ProbedPaths);
        Assert.True(harness.Io.Contains("SMART data nedostupná – disky nelze hodnotit"));
        Assert.True(harness.Io.Contains("N/A"));
    }

    [Fact]
    public async Task ToleratesAFailingSmartProbeForOneDisk()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        fakes.Smart.Handler = path => path == "/dev/sda"
            ? throw new InvalidOperationException("smartctl failed")
            : fakes.Smart.Data[path];
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Contains("/dev/sda", fakes.Smart.ProbedPaths);
        Assert.True(harness.Io.Contains("DataDisk"));
    }

    [Fact]
    public async Task SelectingADiskWritesTheSelectionAndNavigatesToSmartCheck()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("disk:1");
        harness.Io.EnqueueChoice("open");

        ScreenResult result = await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.SmartCheck, result.Target);
        Assert.Same(fakes.Detection.Drives[1], harness.Context.Selection.SelectedDisk);
        Assert.Equal("DataDisk", harness.Context.Selection.SelectedDiskDisplayName);
        Assert.False(harness.Context.Selection.IsSelectedDiskLocked);
    }

    [Fact]
    public async Task SystemDiskIsImplicitlyLockedAndCannotBeUnlocked()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("disk:0");
        harness.Io.EnqueueChoice("unlock");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Empty(fakes.Settings.UnlockCalls);
        Assert.True(harness.Io.Contains("nelze odemknout"));
        Assert.True(harness.Io.Contains("LOCK"));
    }

    [Fact]
    public async Task LockAndUnlockPersistThroughTheSettingsService()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("disk:1");
        harness.Io.EnqueueChoice("lock");
        harness.Io.EnqueueChoice("disk:1");
        harness.Io.EnqueueChoice("unlock");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Equal(new[] { "/dev/sdb" }, fakes.Settings.LockCalls);
        Assert.Equal(new[] { "/dev/sdb" }, fakes.Settings.UnlockCalls);
        Assert.Empty(fakes.Settings.LockedDisks);
    }

    [Fact]
    public async Task InitiallyLockedDiskIsReportedAsLocked()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        fakes.Settings.LockedDisks.Add("/dev/sdb");
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("disk:1");
        harness.Io.EnqueueChoice("unlock");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "/dev/sdb" }, fakes.Settings.UnlockCalls);
    }

    [Fact]
    public async Task TestHistoryRendersTheStoredSessions()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        DiskCard card = DiskFakes.CreateCard(fakes.Detection.Drives[1], grade: "B", score: 88);
        card.Id = 7;
        fakes.Repository.Cards.Add(card);
        fakes.Repository.Sessions.Add(DiskFakes.CreateSession(card, 42, new DateTime(2025, 3, 12, 14, 30, 0, DateTimeKind.Utc), grade: "B", score: 88));

        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("disk:1");
        harness.Io.EnqueueChoice("history");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("Historie testů"));
        Assert.True(harness.Io.Contains("12.03.2025 14:30"));
        Assert.True(harness.Io.Contains("SurfaceScan"));
        Assert.True(harness.Io.Contains("88/100"));
        Assert.True(harness.Io.Contains("HIST"));
    }

    [Fact]
    public async Task TestHistoryWithoutCardExplainsThatNoCardExists()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("disk:1");
        harness.Io.EnqueueChoice("history");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Žádná historie"));
        Assert.True(harness.Io.Contains("ještě nemá uloženou kartu"));
    }

    [Fact]
    public async Task SmartSummaryActionShowsTheProbedValues()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("disk:1");
        harness.Io.EnqueueChoice("smart");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("44°C"));
        Assert.True(harness.Io.Contains("Provozní hodiny: 1200"));
    }

    [Fact]
    public async Task PermissionDeniedSmartAccessIsShownAsAWarning()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        fakes.Smart.LastOperationWasPermissionDenied = true;
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("právy root"));
    }

    [Fact]
    public async Task RefreshForcesANewDiskDetection()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice("refresh");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(2, fakes.Detection.GetDrivesCallCount);
    }

    [Fact]
    public async Task MainMenuAndBackAreOffered()
    {
        DiskFakes fakes = CreateTwoDiskWorld();
        using var harness = TestHarness.Create(configure: fakes.Apply);
        harness.Io.EnqueueChoice(TuiKeys.MainMenu);

        ScreenResult result = await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Menu, result.Action);
        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == TuiKeys.Back);
        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == "refresh");
        Assert.Contains(harness.Io.LastChoices, choice => choice.Key == "disk:0");
    }

    [Fact]
    public async Task RendersInAnEightyColumnTerminal()
    {
        var fakes = new DiskFakes();
        fakes.Detection.Drives.Add(DiskFakes.CreateDrive("/dev/sda", "Disk80", 500 * Gigabyte, serialNumber: "SER80AAAA1"));
        fakes.Smart.Data["/dev/sda"] = DiskFakes.CreateSmart("Disk80", "SER80AAAA1");
        using var harness = TestHarness.Create(configure: fakes.Apply, terminalWidth: 80);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        ScreenResult result = await new DiskSelectionScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("Disk80"));
        Assert.True(harness.Io.Contains("Sběrnice"));
    }

    [Theory]
    [InlineData(0, 0, 0, "Žádné disky nenalezeny")]
    [InlineData(2, 0, 0, "SMART data nedostupná – disky nelze hodnotit")]
    [InlineData(2, 2, 2, "Všechny disky v pořádku")]
    [InlineData(3, 1, 1, "Hodnocené disky v pořádku (2× bez SMART)")]
    [InlineData(2, 2, 1, "1 disků potřebuje pozornost")]
    public void HealthSummaryFollowsTheDesktopRules(int disks, int graded, int healthy, string expected)
    {
        using var harness = TestHarness.Create();

        string summary = DiskSelectionScreen.BuildHealthSummary(harness.Context, disks, graded, healthy);

        Assert.Equal(expected, summary);
    }

    [Theory]
    [InlineData("/dev/sda", "/dev/sda", true)]
    [InlineData("PhysicalDrive0", "PhysicalDrive0", true)]
    [InlineData("PhysicalDrive0", "disk0", true)]
    [InlineData("PhysicalDrive0", "PhysicalDrive1", false)]
    [InlineData("/dev/sda", "C:", false)]
    [InlineData("", "/dev/sda", false)]
    public void LockedDiskIdentifiersMatchLikeTheDesktop(string first, string second, bool expected)
    {
        Assert.Equal(expected, DiskSelectionScreen.IsSameDisk(first, second));
    }
}

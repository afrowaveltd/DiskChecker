using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class SettingsScreenTests
{
    [Fact]
    public void ScreenExposesSettingsIdentity()
    {
        var screen = new SettingsScreen();

        Assert.Equal(ScreenIds.Settings, screen.Id);
        Assert.Equal("Nav.Settings", screen.TitleKey);
    }

    [Fact]
    public async Task ScreenShowsTheCurrentValuesAndGoesBack()
    {
        using var harness = TestHarness.Create();
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("Nastavení aplikace"));
        Assert.True(harness.Io.Contains("Sqlite"));
    }

    [Fact]
    public async Task SavingPersistsTheReportRecipient()
    {
        using var harness = TestHarness.Create();
        var settings = harness.GetRequiredService<ISettingsService>();

        harness.Io.EnqueueChoice(SettingsScreen.ActionEmail);
        harness.Io.EnqueueAsk("servis@example.test");
        harness.Io.EnqueueChoice(SettingsScreen.ActionSave);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal("servis@example.test", await settings.GetReportRecipientEmailAsync());
        Assert.True(harness.Io.Contains("Nastavení bylo uloženo"));
    }

    [Fact]
    public async Task ResetRestoresTheDefaultValues()
    {
        using var harness = TestHarness.Create();
        var settings = harness.GetRequiredService<ISettingsService>();
        await settings.SetReportRecipientEmailAsync("pred@example.test");
        await settings.SetSmartCacheTtlMinutesAsyncPersistent(99);

        harness.Io.EnqueueChoice(SettingsScreen.ActionReset);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.NotEqual("pred@example.test", await settings.GetReportRecipientEmailAsync());
        Assert.NotEqual(99, await settings.GetSmartCacheTtlMinutesAsync());
    }

    [Fact]
    public async Task DatabaseInformationIsShownWithoutCredentials()
    {
        using var harness = TestHarness.Create();
        await harness.GetRequiredService<ISettingsService>()
            .SetDatabaseStorageSettingsAsync(new DatabaseStorageSettings
            {
                Provider = DatabaseProviderKind.Sqlite,
                ConnectionString = "Data Source=" + Path.Combine(harness.Root, "DiskChecker.db"),
            });

        harness.Io.EnqueueChoice(SettingsScreen.ActionDatabaseInfo);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("DiskChecker.db"));
        Assert.True(harness.Io.Contains("Umístění"));
        Assert.False(harness.Io.Contains("Password="));
    }

    [Fact]
    public async Task SmartProbeValuesCanBeEdited()
    {
        using var harness = TestHarness.Create();
        var settings = harness.GetRequiredService<ISettingsService>();

        harness.Io.EnqueueChoice(SettingsScreen.ActionSmart);
        harness.Io.EnqueueChoice(SettingsScreen.ActionTtl);
        harness.Io.EnqueueAsk("42");
        harness.Io.EnqueueChoice(SettingsScreen.ActionSave);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(42, await settings.GetSmartCacheTtlMinutesAsync());
    }

    [Fact]
    public async Task WorkspaceModeCanBeChanged()
    {
        using var harness = TestHarness.Create();
        var settings = harness.GetRequiredService<ISettingsService>();

        harness.Io.EnqueueChoice(SettingsScreen.ActionWorkspace);
        harness.Io.EnqueueChoice(nameof(AnalysisWorkspaceMode.Compact));
        harness.Io.EnqueueChoice(SettingsScreen.ActionSave);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisWorkspaceMode.Compact, await settings.GetAnalysisWorkspaceModeAsync());
    }

    [Fact]
    public async Task ApplicationDataBackupCreatesAZipArchive()
    {
        using var harness = TestHarness.Create();
        harness.Io.EnqueueChoice(SettingsScreen.ActionDataBackup);
        harness.Io.EnqueueChoice(SettingsScreen.ActionCreateBackup);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        string backups = Path.Combine(harness.Root, "settings", "Backups");
        Assert.True(Directory.Exists(backups));
        Assert.NotEmpty(Directory.GetFiles(backups, "DiskChecker_Backup_*.zip"));
    }

    [Fact]
    public async Task RestoringAnApplicationDataBackupKeepsAPreRestoreCopy()
    {
        using var harness = TestHarness.Create();
        var backupService = CreateDataBackupService(harness);
        string archive = await backupService.CreateBackupAsync(null, TestContext.Current.CancellationToken);

        harness.Io.EnqueueChoice(SettingsScreen.ActionDataBackup);
        harness.Io.EnqueueChoice(SettingsScreen.ActionRestoreBackup);
        harness.Io.EnqueueChoice(archive);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        string livePath = Path.Combine(harness.Root, "DiskChecker.db");
        Assert.True(File.Exists(livePath));
        Assert.NotEmpty(Directory.GetFiles(harness.Root, "DiskChecker.db.pre_restore_*"));
        Assert.True(harness.Io.Contains("obnovena"));
    }

    [Fact]
    public async Task JsonExportWritesTheExpectedEnvelope()
    {
        using var harness = TestHarness.Create();
        await CreateDiskCardAsync(harness);
        string exportPath = Path.Combine(BackupTestFixtures.NewDirectory(harness.Root, "exports"), "export.json");

        harness.Io.EnqueueChoice(SettingsScreen.ActionTransfer);
        harness.Io.EnqueueChoice(SettingsScreen.ActionExport);
        harness.Io.EnqueueAsk(exportPath);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(exportPath));
        string json = await File.ReadAllTextAsync(exportPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"metadata\"", json, StringComparison.Ordinal);
        Assert.Contains("\"diskCards\"", json, StringComparison.Ordinal);
        Assert.Contains("\"testSessions\"", json, StringComparison.Ordinal);
        Assert.Contains("\"certificates\"", json, StringComparison.Ordinal);

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("metadata").GetProperty("diskCount").GetInt32());
        Assert.Equal(1, root.GetProperty("diskCards").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("diskCards")[0].GetProperty("serialNumber").GetString()));
    }

    [Fact]
    public async Task JsonImportMergesWithoutDuplicatingDisks()
    {
        using var harness = TestHarness.Create();
        await CreateDiskCardAsync(harness);
        string exportPath = Path.Combine(BackupTestFixtures.NewDirectory(harness.Root, "exports"), "merge.json");

        await harness.Context.UseScopedAsync(async provider =>
        {
            var transfer = new TuiDataTransferService(provider.GetRequiredService<IDiskCardRepository>());
            await transfer.ExportAsync(exportPath, TestContext.Current.CancellationToken);
        });

        harness.Io.EnqueueChoice(SettingsScreen.ActionTransfer);
        harness.Io.EnqueueChoice(SettingsScreen.ActionImport);
        harness.Io.EnqueueAsk(exportPath);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        int cards = await harness.Context.UseScopedAsync(async provider =>
        {
            var repository = provider.GetRequiredService<IDiskCardRepository>();
            var all = await repository.GetAllAsync();
            return all.Count;
        });

        Assert.Equal(1, cards);
        Assert.True(harness.Io.Contains("Import dokončen"));
    }

    [Fact]
    public async Task LocalSqliteConnectionTestSucceeds()
    {
        using var harness = TestHarness.Create();
        string databasePath = Path.Combine(harness.Root, "DiskChecker.db");
        await harness.GetRequiredService<ISettingsService>()
            .SetDatabaseStorageSettingsAsync(new DatabaseStorageSettings
            {
                Provider = DatabaseProviderKind.Sqlite,
                ConnectionString = "Data Source=" + databasePath,
            });

        harness.Io.EnqueueChoice(SettingsScreen.ActionDatabase);
        harness.Io.EnqueueChoice(SettingsScreen.ActionTestConnection);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Připojení k databázi je v pořádku"));
    }

    [Fact]
    public async Task LanguageCanBeSwitchedImmediately()
    {
        using var harness = TestHarness.Create();
        harness.Io.EnqueueChoice(SettingsScreen.ActionLanguage);
        harness.Io.EnqueueChoice("en");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await new SettingsScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal("en", harness.Locale.CurrentLocale);
    }

    private static TuiDataBackupService CreateDataBackupService(TestHarness harness) =>
        new(
            harness.GetRequiredService<IServiceScopeFactory>(),
            harness.GetRequiredService<SettingsService>().SettingsFilePath);

    private static async Task CreateDiskCardAsync(TestHarness harness) =>
        await harness.Context.UseScopedAsync(async provider =>
        {
            var repository = provider.GetRequiredService<IDiskCardRepository>();
            await repository.CreateAsync(new DiskCard
            {
                SerialNumber = "SN-TEST-0001",
                ModelName = "Testovací disk X1",
                DevicePath = "/dev/test",
                Capacity = 1000,
            });
        });
}

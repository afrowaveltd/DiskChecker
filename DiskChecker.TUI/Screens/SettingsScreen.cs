using System.Globalization;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.TUI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Terminal settings screen. Every value is read and written through
/// <see cref="ISettingsService"/>; the screen adds the terminal-only actions: database
/// diagnostics, an application data ZIP backup and JSON export/import of the measured data.
/// Credentials are never printed.
/// </summary>
public sealed class SettingsScreen : ITuiScreen
{
    internal const string ActionLanguage = "language";
    internal const string ActionEmail = "email";
    internal const string ActionDatabase = "database";
    internal const string ActionSmart = "smart";
    internal const string ActionUsb = "usb";
    internal const string ActionExportPath = "export-path";
    internal const string ActionCertificatePath = "certificate-path";
    internal const string ActionWorkspace = "workspace";
    internal const string ActionSave = "save";
    internal const string ActionReset = "reset";
    internal const string ActionDatabaseInfo = "database-info";
    internal const string ActionDataBackup = "data-backup";
    internal const string ActionTransfer = "transfer";
    internal const string ActionProvider = "provider";
    internal const string ActionConnection = "connection";
    internal const string ActionTestConnection = "test-connection";
    internal const string ActionTtl = "ttl";
    internal const string ActionTimeout = "timeout";
    internal const string ActionParallelism = "parallelism";
    internal const string ActionCreateBackup = "create-backup";
    internal const string ActionRestoreBackup = "restore-backup";
    internal const string ActionExport = "export";
    internal const string ActionImport = "import";

    public string Id => ScreenIds.Settings;

    public string TitleKey => "Nav.Settings";

    public Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken) =>
        ShowCoreAsync(context, cancellationToken);

    private async Task<ScreenResult> ShowCoreAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = context.GetRequiredService<ISettingsService>();
        var draft = new SettingsDraft();
        await LoadAsync(context, settings, draft).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            RenderDraft(context, settings, draft);

            var choices = new List<TuiChoice>
            {
                new(ActionLanguage, context.L("Tui.Settings.Action.Language")),
                new(ActionEmail, context.L("Tui.Settings.Action.Email")),
                new(ActionDatabase, context.L("Tui.Settings.Action.Database")),
                new(ActionSmart, context.L("Tui.Settings.Action.Smart")),
                new(ActionUsb, context.L("Tui.Settings.UsbRecoveryRetry")),
                new(ActionExportPath, context.L("Tui.Settings.Action.ExportPath")),
                new(ActionCertificatePath, context.L("Tui.Settings.Action.CertificatePath")),
                new(ActionWorkspace, context.L("Tui.Settings.Action.Workspace")),
                new(ActionSave, context.L("Settings.SaveButton")),
                new(ActionReset, context.L("Settings.ResetButton")),
                new(ActionDatabaseInfo, context.L("Tui.Settings.Action.DatabaseInfo")),
                new(ActionDataBackup, context.L("Tui.Settings.Action.DataBackup")),
                new(ActionTransfer, context.L("Tui.Settings.Action.Transfer")),
                new(TuiKeys.Back, context.L("Common.Back")),
            };

            string? choice = context.Io.Choose(context.L("Settings.Title"), choices, pageSize: choices.Count);
            switch (choice)
            {
                case ActionLanguage:
                    ChooseLanguage(context, draft);
                    break;

                case ActionEmail:
                    draft.ReportRecipientEmail = AskText(context, context.L("Settings.EmailRecipient"), draft.ReportRecipientEmail);
                    break;

                case ActionDatabase:
                    await ChooseDatabaseActionAsync(context, draft, cancellationToken).ConfigureAwait(false);
                    break;

                case ActionSmart:
                    ChooseSmartAction(context, draft);
                    break;

                case ActionUsb:
                    draft.UsbRecoveryRetryCount = AskInt(context, context.L("Settings.UsbRecoveryRetry"), draft.UsbRecoveryRetryCount, 0, 100);
                    break;

                case ActionExportPath:
                    draft.DefaultExportPath = AskText(context, context.L("Settings.DefaultExportPath"), draft.DefaultExportPath);
                    break;

                case ActionCertificatePath:
                    draft.CertificatePath = AskText(context, context.L("Tui.Settings.CertificatePathAsk"), draft.CertificatePath);
                    break;

                case ActionWorkspace:
                    draft.WorkspaceMode = ChooseWorkspaceMode(context, draft.WorkspaceMode);
                    break;

                case ActionSave:
                    await SaveAsync(context, settings, draft).ConfigureAwait(false);
                    break;

                case ActionReset:
                    await ResetAsync(context, settings, draft).ConfigureAwait(false);
                    break;

                case ActionDatabaseInfo:
                    await ShowDatabaseInfoAsync(context).ConfigureAwait(false);
                    break;

                case ActionDataBackup:
                    await ChooseDataBackupAsync(context, cancellationToken).ConfigureAwait(false);
                    break;

                case ActionTransfer:
                    await ChooseTransferAsync(context, draft, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    return ScreenResult.Back();
            }
        }

        return ScreenResult.Back();
    }

    private static async Task LoadAsync(TuiContext context, ISettingsService settings, SettingsDraft draft)
    {
        try
        {
            draft.Language = await settings.GetLanguageAsync().ConfigureAwait(false);
            draft.ReportRecipientEmail = await settings.GetReportRecipientEmailAsync().ConfigureAwait(false);
            var database = await settings.GetDatabaseStorageSettingsAsync().ConfigureAwait(false);
            draft.Provider = database.Provider;
            draft.ConnectionString = database.ConnectionString ?? DatabaseStorageSettings.Default.ConnectionString ?? string.Empty;
            draft.SmartCacheTtlMinutes = await settings.GetSmartCacheTtlMinutesAsync().ConfigureAwait(false);
            draft.SmartProbeTimeoutSeconds = await settings.GetSmartProbeTimeoutSecondsAsync().ConfigureAwait(false);
            draft.SmartProbeParallelism = await settings.GetSmartProbeParallelismAsync().ConfigureAwait(false);
            draft.UsbRecoveryRetryCount = await settings.GetUsbRecoveryRetryCountAsync().ConfigureAwait(false);
            draft.DefaultExportPath = await settings.GetDefaultExportPathAsync().ConfigureAwait(false);
            draft.CertificatePath = await settings.GetCertificatePathAsync().ConfigureAwait(false) ?? string.Empty;
            draft.WorkspaceMode = await settings.GetAnalysisWorkspaceModeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
        {
            context.StatusMessage = context.L("Tui.Settings.LoadFailed", ex.Message);
            context.Dialogs.Error(context.L("Common.Error"), context.StatusMessage);
        }
    }

    private static async Task SaveAsync(TuiContext context, ISettingsService settings, SettingsDraft draft)
    {
        try
        {
            await settings.SetLanguageAsync(draft.Language).ConfigureAwait(false);
            await settings.SetReportRecipientEmailAsync(draft.ReportRecipientEmail).ConfigureAwait(false);
            await settings.SetDatabaseStorageSettingsAsync(new DatabaseStorageSettings
            {
                Provider = draft.Provider,
                ConnectionString = draft.ConnectionString,
            }).ConfigureAwait(false);
            await settings.SetSmartCacheTtlMinutesAsyncPersistent(draft.SmartCacheTtlMinutes).ConfigureAwait(false);
            await settings.SetSmartProbeTimeoutSecondsAsync(draft.SmartProbeTimeoutSeconds).ConfigureAwait(false);
            await settings.SetSmartProbeParallelismAsync(draft.SmartProbeParallelism).ConfigureAwait(false);
            await settings.SetUsbRecoveryRetryCountAsync(draft.UsbRecoveryRetryCount).ConfigureAwait(false);
            await settings.SetDefaultExportPathAsync(draft.DefaultExportPath).ConfigureAwait(false);
            await settings.SetCertificatePathAsync(string.IsNullOrWhiteSpace(draft.CertificatePath) ? null : draft.CertificatePath).ConfigureAwait(false);
            await settings.SetAnalysisWorkspaceModeAsync(draft.WorkspaceMode).ConfigureAwait(false);

            context.StatusMessage = context.L("Settings.Saved");
            context.Dialogs.Success(context.L("Settings.SaveButton"), context.L("Tui.Settings.SavedBody"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
        {
            context.StatusMessage = context.L("Tui.Settings.SaveFailed", ex.Message);
            context.Dialogs.Error(context.L("Common.Error"), context.StatusMessage);
        }
    }

    private static async Task ResetAsync(TuiContext context, ISettingsService settings, SettingsDraft draft)
    {
        if (!context.Dialogs.Confirm(context.L("Settings.ConfirmResetTitle"), context.L("Settings.ConfirmResetMessage"), danger: true))
        {
            return;
        }

        try
        {
            await settings.ResetToDefaultsAsync().ConfigureAwait(false);
            await LoadAsync(context, settings, draft).ConfigureAwait(false);
            context.StatusMessage = context.L("Settings.ResetDone");
            context.Dialogs.Success(context.L("Settings.ResetButton"), context.L("Settings.ResetDone"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            context.StatusMessage = context.L("Tui.Settings.ResetFailed", ex.Message);
            context.Dialogs.Error(context.L("Common.Error"), context.StatusMessage);
        }
    }

    private static void ChooseLanguage(TuiContext context, SettingsDraft draft)
    {
        var available = context.Locale.AvailableLocales;
        var choices = available
            .Select(locale => new TuiChoice(
                locale,
                context.Locale.GetString("Nav.Language." + locale, locale)))
            .ToList();
        choices.Add(new TuiChoice(TuiKeys.Back, context.L("Common.Back")));

        string? choice = context.Io.Choose(context.L("Settings.LanguageSelect"), choices, pageSize: choices.Count);
        if (choice is null || choice == TuiKeys.Back)
        {
            return;
        }

        draft.Language = choice;
        context.Locale.SetLocale(choice);
        context.StatusMessage = context.L("Settings.LanguageChanged", choice);
    }

    private static async Task ChooseDatabaseActionAsync(TuiContext context, SettingsDraft draft, CancellationToken cancellationToken)
    {
        var choices = new List<TuiChoice>
        {
            new(ActionProvider, context.L("Tui.Settings.Action.Provider")),
            new(ActionConnection, context.L("Tui.Settings.Action.Connection")),
            new(ActionTestConnection, context.L("Tui.Settings.Action.TestConnection")),
            new(TuiKeys.Back, context.L("Common.Back")),
        };

        string? choice = context.Io.Choose(context.L("Tui.Settings.DatabaseTitle"), choices, pageSize: choices.Count);
        switch (choice)
        {
            case ActionProvider:
            {
                var providers = Enum.GetValues<DatabaseProviderKind>()
                    .Select(kind => new TuiChoice(kind.ToString(), kind.ToString()))
                    .ToList();
                providers.Add(new TuiChoice(TuiKeys.Back, context.L("Common.Back")));
                string? provider = context.Io.Choose(context.L("SettingsView.DbBackend"), providers, pageSize: providers.Count);
                if (provider is not null && provider != TuiKeys.Back &&
                    Enum.TryParse<DatabaseProviderKind>(provider, ignoreCase: true, out var parsed))
                {
                    draft.Provider = parsed;
                }

                break;
            }

            case ActionConnection:
                draft.ConnectionString = AskText(context, context.L("SettingsView.ConnectionString"), draft.ConnectionString);
                break;

            case ActionTestConnection:
                await TestConnectionAsync(context, draft, cancellationToken).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private static async Task TestConnectionAsync(TuiContext context, SettingsDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            var builder = new DbContextOptionsBuilder<DiskCheckerDbContext>();
            DatabaseProviderConfiguration.Configure(builder, new DatabaseStorageSettings
            {
                Provider = draft.Provider,
                ConnectionString = draft.ConnectionString,
            });

            await using var dbContext = new DiskCheckerDbContext(builder.Options);
            bool connected = await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            if (connected)
            {
                context.StatusMessage = context.L("Tui.Settings.ConnectionOk");
                context.Dialogs.Success(context.L("Common.Database"), context.StatusMessage);
            }
            else
            {
                context.StatusMessage = context.L("Tui.Settings.ConnectionFailed");
                context.Dialogs.Warning(context.L("Common.Database"), context.StatusMessage);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or IOException)
        {
            context.StatusMessage = context.L("Tui.Settings.ConnectionError", ex.Message);
            context.Dialogs.Error(context.L("Common.Database"), context.StatusMessage);
        }
    }

    private static void ChooseSmartAction(TuiContext context, SettingsDraft draft)
    {
        var choices = new List<TuiChoice>
        {
            new(ActionTtl, context.L("Settings.SmartCacheTTL")),
            new(ActionTimeout, context.L("Settings.SmartProbeTimeout")),
            new(ActionParallelism, context.L("Settings.SmartProbeParallelism")),
            new(TuiKeys.Back, context.L("Common.Back")),
        };

        string? choice = context.Io.Choose(context.L("Tui.Settings.SmartTitle"), choices, pageSize: choices.Count);
        switch (choice)
        {
            case ActionTtl:
                draft.SmartCacheTtlMinutes = AskInt(context, context.L("Settings.SmartCacheTTL"), draft.SmartCacheTtlMinutes, 1, 1440);
                break;
            case ActionTimeout:
                draft.SmartProbeTimeoutSeconds = AskInt(context, context.L("Settings.SmartProbeTimeout"), draft.SmartProbeTimeoutSeconds, 1, 600);
                break;
            case ActionParallelism:
                draft.SmartProbeParallelism = AskInt(context, context.L("Settings.SmartProbeParallelism"), draft.SmartProbeParallelism, 0, 64);
                break;
            default:
                break;
        }
    }

    private static AnalysisWorkspaceMode ChooseWorkspaceMode(TuiContext context, AnalysisWorkspaceMode current)
    {
        var choices = Enum.GetValues<AnalysisWorkspaceMode>()
            .Select(mode => new TuiChoice(mode.ToString(), context.L("Tui.Settings.Workspace." + mode)))
            .ToList();
        choices.Add(new TuiChoice(TuiKeys.Back, context.L("Common.Back")));

        string? choice = context.Io.Choose(context.L("Tui.Settings.WorkspaceTitle"), choices, pageSize: choices.Count);
        if (choice is null || choice == TuiKeys.Back ||
            !Enum.TryParse<AnalysisWorkspaceMode>(choice, ignoreCase: true, out var parsed))
        {
            return current;
        }

        return parsed;
    }

    private static async Task ShowDatabaseInfoAsync(TuiContext context)
    {
        var service = CreateDataBackupService(context);
        if (service is null)
        {
            context.Dialogs.Error(context.L("Common.Database"), context.L("Tui.Settings.BackupUnavailable"));
            return;
        }

        try
        {
            var info = await service.GetDatabaseInfoAsync().ConfigureAwait(false);
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderStyle(new Style(foreground: TuiTheme.Primary))
                .AddColumn(new TableColumn("[bold]" + Markup.Escape(context.L("Common.Database")) + "[/]"));

            table.AddRow(Markup.Escape(context.L("SettingsView.DbBackend") + ": " + info.Provider));
            table.AddRow(Markup.Escape(context.L("Settings.DatabaseSize") + " " + TuiFormats.DecimalSize(info.SizeBytes)));
            table.AddRow(Markup.Escape(context.L("Tui.Settings.Label.Location") + ": " + info.Location));
            table.AddRow("[grey]" + Markup.Escape(info.Description) + "[/]");
            context.Io.Write(table);
            context.Io.Pause();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException)
        {
            context.Dialogs.Error(context.L("Common.Database"), ex.Message);
        }
    }

    private static async Task ChooseDataBackupAsync(TuiContext context, CancellationToken cancellationToken)
    {
        var choices = new List<TuiChoice>
        {
            new(ActionCreateBackup, context.L("Settings.CreateBackupButton")),
            new(ActionRestoreBackup, context.L("Settings.RestoreBackupButton")),
            new(TuiKeys.Back, context.L("Common.Back")),
        };

        string? choice = context.Io.Choose(context.L("Settings.BackupRestore"), choices, pageSize: choices.Count);
        var service = CreateDataBackupService(context);
        if (service is null)
        {
            context.Dialogs.Error(context.L("Common.Error"), context.L("Tui.Settings.BackupUnavailable"));
            return;
        }

        switch (choice)
        {
            case ActionCreateBackup:
            {
                try
                {
                    string path = await service.CreateBackupAsync(null, cancellationToken).ConfigureAwait(false);
                    context.StatusMessage = context.L("Settings.BackupCreatedMsg", path);
                    context.Dialogs.Success(context.L("Settings.BackupCreated"), context.StatusMessage);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
                {
                    context.StatusMessage = context.L("Settings.BackupError", ex.Message);
                    context.Dialogs.Error(context.L("Common.Error"), context.StatusMessage);
                }

                break;
            }

            case ActionRestoreBackup:
            {
                try
                {
                    var backups = await service.GetAvailableBackupsAsync(cancellationToken).ConfigureAwait(false);
                    if (backups.Count == 0)
                    {
                        context.Dialogs.Warning(context.L("Settings.RestoreBackup"), context.L("Tui.Settings.NoBackups"));
                        break;
                    }

                    var items = backups
                        .Select(backup => new TuiChoice(
                            backup.FilePath,
                            backup.FileName,
                            backup.CreatedAt.ToString("g", CultureInfo.CurrentCulture)))
                        .ToList();
                    items.Add(new TuiChoice(TuiKeys.Back, context.L("Common.Back")));

                    string? file = context.Io.Choose(context.L("Settings.RestoreBackup"), items, pageSize: items.Count);
                    if (file is null || file == TuiKeys.Back)
                    {
                        break;
                    }

                    if (!context.Dialogs.Confirm(
                            context.L("Settings.RestoreConfirmTitle"),
                            context.L("Settings.RestoreConfirmMsg", Path.GetFileName(file), string.Empty),
                            danger: true))
                    {
                        break;
                    }

                    var outcome = await service.RestoreAsync(file, cancellationToken).ConfigureAwait(false);
                    context.StatusMessage = outcome.Message;
                    context.Dialogs.Success(context.L("Settings.RestoreCompleted"), outcome.Message);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
                {
                    context.StatusMessage = context.L("Settings.RestoreError", ex.Message);
                    context.Dialogs.Error(context.L("Common.Error"), context.StatusMessage);
                }

                break;
            }

            default:
                break;
        }
    }

    private static async Task ChooseTransferAsync(TuiContext context, SettingsDraft draft, CancellationToken cancellationToken)
    {
        var choices = new List<TuiChoice>
        {
            new(ActionExport, context.L("Common.Export")),
            new(ActionImport, context.L("Common.Import")),
            new(TuiKeys.Back, context.L("Common.Back")),
        };

        string? choice = context.Io.Choose(context.L("Tui.Settings.TransferTitle"), choices, pageSize: choices.Count);
        if (choice is null || choice == TuiKeys.Back)
        {
            return;
        }

        string exportDirectory = ResolveExportDirectory(context, draft);
        string defaultPath = Path.Combine(
            exportDirectory,
            "DiskChecker_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json");

        if (choice == ActionExport)
        {
            string target = AskText(context, context.L("Tui.Settings.ExportAsk"), defaultPath);
            try
            {
                await context.UseScopedAsync(async provider =>
                {
                    var repository = provider.GetRequiredService<IDiskCardRepository>();
                    var transfer = new TuiDataTransferService(repository, exportDirectory);
                    var result = await transfer.ExportAsync(target, cancellationToken).ConfigureAwait(false);
                    context.StatusMessage = result.Message;
                    context.Dialogs.Success(context.L("Common.Export"), result.Message);
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                context.StatusMessage = context.L("Tui.Settings.TransferFailed", ex.Message);
                context.Dialogs.Error(context.L("Common.Error"), context.StatusMessage);
            }

            return;
        }

        string source = AskText(context, context.L("Tui.Settings.ImportAsk"), defaultPath);
        try
        {
            await context.UseScopedAsync(async provider =>
            {
                var repository = provider.GetRequiredService<IDiskCardRepository>();
                var transfer = new TuiDataTransferService(repository, exportDirectory);
                var result = await transfer.ImportAsync(source, cancellationToken).ConfigureAwait(false);
                context.StatusMessage = result.Message;
                if (result.Success)
                {
                    context.Dialogs.Success(context.L("Common.Import"), result.Message);
                }
                else
                {
                    context.Dialogs.Error(context.L("Common.Error"), result.Message);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            context.StatusMessage = context.L("Tui.Settings.TransferFailed", ex.Message);
            context.Dialogs.Error(context.L("Common.Error"), context.StatusMessage);
        }
    }

    /// <summary>Writable directory used when the operator does not name an export path.</summary>
    private static string ResolveExportDirectory(TuiContext context, SettingsDraft draft)
    {
        var settingsService = context.GetService<SettingsService>();
        string fallback = settingsService is null
            ? Path.Combine(Path.GetTempPath(), "DiskChecker", "Exports")
            : Path.Combine(Path.GetDirectoryName(settingsService.SettingsFilePath) ?? Path.GetTempPath(), "Exports");

        string directory = string.IsNullOrWhiteSpace(draft.DefaultExportPath) ? fallback : draft.DefaultExportPath;
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall back to the temporary directory when the configured one is not writable.
            directory = Path.Combine(Path.GetTempPath(), "DiskChecker", "Exports");
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private static TuiDataBackupService? CreateDataBackupService(TuiContext context)
    {
        var settingsService = context.GetService<SettingsService>();
        var scopeFactory = context.GetService<IServiceScopeFactory>();
        if (settingsService is null || scopeFactory is null)
        {
            return null;
        }

        return new TuiDataBackupService(scopeFactory, settingsService.SettingsFilePath);
    }

    private static void RenderDraft(TuiContext context, ISettingsService settings, SettingsDraft draft)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(foreground: TuiTheme.Primary))
            .AddColumn(new TableColumn("[bold]" + Markup.Escape(context.L("Settings.Title")) + "[/]"));

        void Row(string label, string value) =>
            table.AddRow("[grey]" + Markup.Escape(label) + ":[/] " + Markup.Escape(TuiFormats.Ellipsis(value, Math.Max(16, context.Io.Width - 30))));

        Row(context.L("Settings.Language"), draft.Language);
        Row(context.L("Settings.EmailRecipient"), string.IsNullOrWhiteSpace(draft.ReportRecipientEmail) ? "—" : draft.ReportRecipientEmail);
        Row(context.L("SettingsView.DbBackend"), draft.Provider.ToString());
        Row(context.L("SettingsView.ConnectionString"), MaskConnectionString(draft));
        Row(context.L("Settings.SmartCacheTTL"), draft.SmartCacheTtlMinutes.ToString(CultureInfo.InvariantCulture));
        Row(context.L("Settings.SmartProbeTimeout"), draft.SmartProbeTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        Row(context.L("Settings.SmartProbeParallelism"), draft.SmartProbeParallelism == 0 ? context.L("Settings.ParallelismAuto") : draft.SmartProbeParallelism.ToString(CultureInfo.InvariantCulture));
        Row(context.L("Settings.UsbRecoveryRetry"), draft.UsbRecoveryRetryCount.ToString(CultureInfo.InvariantCulture));
        Row(context.L("Settings.DefaultExportPath"), string.IsNullOrWhiteSpace(draft.DefaultExportPath) ? "—" : draft.DefaultExportPath);
        Row(context.L("Tui.Settings.CertificatePath"), string.IsNullOrWhiteSpace(draft.CertificatePath) ? "—" : draft.CertificatePath);
        Row(context.L("Tui.Settings.Workspace"), context.L("Tui.Settings.Workspace." + draft.WorkspaceMode));
        Row(context.L("Settings.Database"), DescribeActiveDatabase(context, settings));

        context.Io.Write(table);
        context.Io.Line();
    }

    private static string DescribeActiveDatabase(TuiContext context, ISettingsService settings) =>
        settings is SettingsService concrete && concrete.StartupDatabaseStorageSettings.Provider == DatabaseProviderKind.Sqlite
            ? concrete.StartupDatabaseStorageSettings.ConnectionString ?? context.L("Common.NA")
            : (settings as SettingsService)?.StartupDatabaseStorageSettings.Provider.ToString() ?? context.L("Common.NA");

    /// <summary>Connection strings may embed credentials; only SQLite file paths are shown.</summary>
    private static string MaskConnectionString(SettingsDraft draft) =>
        draft.Provider == DatabaseProviderKind.Sqlite ? draft.ConnectionString : "••••••";

    private static string AskText(TuiContext context, string question, string current)
    {
        string? answer = context.Io.Ask(question, current);
        return string.IsNullOrWhiteSpace(answer) ? current : answer.Trim();
    }

    private static int AskInt(TuiContext context, string question, int current, int min, int max)
    {
        string? answer = context.Io.Ask(question, current.ToString(CultureInfo.InvariantCulture));
        if (int.TryParse(answer, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max)
        {
            return value;
        }

        if (!string.IsNullOrWhiteSpace(answer))
        {
            context.Dialogs.Warning(context.L("Common.Error"), context.L("Tui.Settings.InvalidNumber", min, max));
        }

        return current;
    }

    private sealed class SettingsDraft
    {
        public string Language { get; set; } = "cs";

        public string ReportRecipientEmail { get; set; } = string.Empty;

        public DatabaseProviderKind Provider { get; set; } = DatabaseProviderKind.Sqlite;

        public string ConnectionString { get; set; } = string.Empty;

        public int SmartCacheTtlMinutes { get; set; } = 10;

        public int SmartProbeTimeoutSeconds { get; set; } = 15;

        public int SmartProbeParallelism { get; set; }

        public int UsbRecoveryRetryCount { get; set; } = 2;

        public string DefaultExportPath { get; set; } = string.Empty;

        public string CertificatePath { get; set; } = string.Empty;

        public AnalysisWorkspaceMode WorkspaceMode { get; set; } = AnalysisWorkspaceMode.Auto;
    }
}

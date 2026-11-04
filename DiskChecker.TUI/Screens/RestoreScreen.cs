using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Terminal restore screen: finds <c>DiskChecker_*Backup_*</c> folders with a manifest and
/// streams the image back onto a target disk. Restore is the only writer in the terminal
/// UI and therefore demands a strict double confirmation that names the exact target.
/// </summary>
public sealed class RestoreScreen : ITuiScreen
{
    internal const string ActionManual = "manual";
    internal const string ActionCustomTarget = "custom";

    /// <summary>Overrides the scanned roots (tests and operator scripts).</summary>
    internal IReadOnlyList<string>? ScanRoots { get; set; }

    public string Id => ScreenIds.Restore;

    public string TitleKey => "Nav.Restore";

    public Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken) =>
        ShowCoreAsync(context, cancellationToken);

    private async Task<ScreenResult> ShowCoreAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var service = new TuiImageBackupService((key, args) => args.Length == 0 ? context.L(key) : context.L(key, args));

        while (!cancellationToken.IsCancellationRequested)
        {
            var backups = TuiImageBackupService.ScanBackups(ScanRoots);
            RenderBackups(context, backups);

            var choices = new List<TuiChoice>();
            foreach (var backup in backups)
            {
                choices.Add(new TuiChoice(
                    backup.BackupRoot,
                    string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        "{0} · {1} · {2}",
                        TuiFormats.Ellipsis(backup.SourceModel, 28),
                        TuiFormats.Ellipsis(backup.BackupDate, 19),
                        TuiFormats.DecimalSize(backup.TotalBytes)),
                    TuiFormats.Ellipsis(backup.BackupRoot, Math.Max(16, context.Io.Width - 20))));
            }

            choices.Add(new TuiChoice(ActionManual, context.L("Tui.Restore.Manual")));
            choices.Add(new TuiChoice(TuiKeys.Back, context.L("Common.Back")));

            string? choice = context.Io.Choose(context.L("Tui.Restore.SelectBackup"), choices, pageSize: 12);
            if (choice is null || choice == TuiKeys.Back)
            {
                return ScreenResult.Back();
            }

            TuiDiscoveredBackup? selected;
            if (choice == ActionManual)
            {
                string? manifestPath = context.Io.Ask(context.L("Tui.Restore.ManifestAsk"));
                selected = string.IsNullOrWhiteSpace(manifestPath) ? null : LoadManual(manifestPath.Trim());
                if (selected is null)
                {
                    context.Dialogs.Error(context.L("Common.Error"), context.L("Tui.Restore.InvalidBackup"));
                    continue;
                }
            }
            else
            {
                selected = backups.FirstOrDefault(backup =>
                    string.Equals(backup.BackupRoot, choice, StringComparison.OrdinalIgnoreCase));
                if (selected is null)
                {
                    continue;
                }
            }

            if (selected.ImagePath is null)
            {
                context.Dialogs.Warning(context.L("Restore.Title"), context.L("Tui.Restore.NotImage"));
                continue;
            }

            string? target = await ChooseTargetAsync(context, selected, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            if (!ConfirmRestore(context, selected, target))
            {
                context.StatusMessage = context.L("Restore.Cancelled");
                continue;
            }

            await RunRestoreAsync(context, service, selected, target, cancellationToken).ConfigureAwait(false);
            context.Io.Pause();
        }

        return ScreenResult.Back();
    }

    private static void RenderBackups(TuiContext context, IReadOnlyList<TuiDiscoveredBackup> backups)
    {
        if (backups.Count == 0)
        {
            var panel = new Panel(new Markup("[grey]" + Markup.Escape(context.L("Tui.Restore.NoBackups")) + "[/]"))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: TuiTheme.Warning),
                Padding = new Padding(1, 0, 1, 0),
            };
            context.Io.Write(panel);
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(foreground: TuiTheme.Primary))
            .AddColumn(new TableColumn("[bold]" + Markup.Escape(context.L("Tui.Restore.Label.Model")) + "[/]"))
            .AddColumn(new TableColumn("[bold]" + Markup.Escape(context.L("Tui.Restore.Label.Date")) + "[/]"))
            .AddColumn(new TableColumn("[bold]" + Markup.Escape(context.L("Tui.Restore.Label.Mode")) + "[/]"))
            .AddColumn(new TableColumn("[bold]" + Markup.Escape(context.L("Tui.Restore.Label.Size")) + "[/]"))
            .AddColumn(new TableColumn("[bold]" + Markup.Escape(context.L("Backup.Parts")) + "[/]"));

        int width = Math.Max(80, context.Io.Width);
        foreach (var backup in backups)
        {
            table.AddRow(
                Markup.Escape(TuiFormats.Ellipsis(backup.SourceModel, Math.Max(10, width / 5))),
                Markup.Escape(TuiFormats.Ellipsis(backup.BackupDate, Math.Max(10, width / 5))),
                Markup.Escape(backup.Mode),
                Markup.Escape(TuiFormats.DecimalSize(backup.TotalBytes)),
                Markup.Escape(backup.Parts.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        context.Io.Write(table);
        context.Io.Line();
    }

    private static TuiDiscoveredBackup? LoadManual(string path)
    {
        if (File.Exists(path))
        {
            if (Path.GetFileName(path).Equals("backup_manifest.json", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
                return string.IsNullOrEmpty(directory) ? null : TuiImageBackupService.ReadBackup(directory);
            }
        }

        return Directory.Exists(path) ? TuiImageBackupService.ReadBackup(path) : null;
    }

    private static async Task<string?> ChooseTargetAsync(
        TuiContext context,
        TuiDiscoveredBackup backup,
        CancellationToken cancellationToken)
    {
        var choices = new List<TuiChoice>();

        try
        {
            var drives = await context.DiskCache.GetDrivesAsync().ConfigureAwait(false);
            foreach (var drive in drives.Where(IsRestorableTarget))
            {
                choices.Add(new TuiChoice(
                    drive.Path,
                    TuiFormats.Ellipsis(drive.Name ?? drive.Path, 34),
                    TuiFormats.Bytes(drive.TotalSize)));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Disk detection is best effort: the operator can always type the path.
        }

        choices.Add(new TuiChoice(ActionCustomTarget, context.L("Tui.Restore.TargetCustom")));
        choices.Add(new TuiChoice(TuiKeys.Back, context.L("Common.Back")));

        context.Io.Line(
            "[grey]" + Markup.Escape(context.L("Restore.SourceDisk")) + "[/] " +
            Markup.Escape(TuiFormats.Ellipsis(backup.BackupRoot, Math.Max(20, context.Io.Width - 20))));

        string? choice = context.Io.Choose(context.L("Tui.Restore.TargetTitle"), choices, pageSize: 12);
        if (choice is null || choice == TuiKeys.Back)
        {
            return null;
        }

        if (choice != ActionCustomTarget)
        {
            return choice;
        }

        string? typed = context.Io.Ask(context.L("Tui.Restore.TargetAsk"));
        return string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();
    }

    private static bool IsRestorableTarget(CoreDriveInfo drive) =>
        drive.IsPhysical ||
        drive.Path.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase) ||
        drive.Path.Contains("PhysicalDrive", StringComparison.OrdinalIgnoreCase);
    private static bool ConfirmRestore(TuiContext context, TuiDiscoveredBackup backup, string target)
    {
        string details = context.L(
            "Tui.Restore.ConfirmOneBody",
            backup.BackupRoot,
            target,
            TuiFormats.DecimalSize(backup.TotalBytes));

        if (!context.Dialogs.Confirm(context.L("Restore.ConfirmTitle"), details, danger: true))
        {
            return false;
        }

        return context.Dialogs.Confirm(
            context.L("Restore.ConfirmStart"),
            context.L("Tui.Restore.ConfirmTwoBody", target),
            danger: true);
    }

    private static async Task RunRestoreAsync(
        TuiContext context,
        TuiImageBackupService service,
        TuiDiscoveredBackup backup,
        string target,
        CancellationToken cancellationToken)
    {
        var images = TuiImageBackupService.ResolveImagePaths(backup);
        if (images.Count == 0)
        {
            context.Dialogs.Error(context.L("Restore.Title"), context.L("Tui.Restore.NoImage"));
            return;
        }

        var cancellation = context.GetRequiredService<TuiCancellation>();
        var operation = cancellation.BeginOperation();
        var progress = new TuiProgress(context.L("Restore.Title")) { ShowChart = false, MaxLogLines = 6 };
        TuiRestoreResult? result = null;
        Exception? failure = null;

        using (context.LockNavigation(context.L("Common.CannotLeaveDuringRestore")))
        {
            try
            {
                await context.Io.RunLiveAsync(
                    context.L("Restore.Running"),
                    progress,
                    async report =>
                    {
                        result = await service.RestoreAsync(
                            new TuiRestoreRequest
                            {
                                ImagePaths = images,
                                TargetPath = target,
                                TotalBytes = backup.TotalBytes,
                                DataStartOffset = backup.DataStartOffset,
                                ExpectedSha256 = backup.Sha256,
                            },
                            report,
                            operation.Token).ConfigureAwait(false);
                    },
                    operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                context.StatusMessage = context.L("Restore.Cancelled");
                context.Dialogs.Warning(context.L("Restore.Title"), context.L("Restore.Cancelled"));
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
            {
                failure = ex;
            }
            finally
            {
                cancellation.EndOperation(operation);
            }
        }

        if (failure is not null || result is null)
        {
            context.StatusMessage = context.L("Tui.Restore.Failed", failure?.Message ?? string.Empty);
            context.Dialogs.Error(context.L("Common.Error"), context.StatusMessage);
            return;
        }

        string body = context.L(
            "Tui.Restore.DoneBody",
            target,
            TuiFormats.DecimalSize(result.BytesWritten),
            result.Message ?? string.Empty);

        if (string.IsNullOrWhiteSpace(backup.Sha256))
        {
            context.Dialogs.Warning(context.L("Restore.Completed"), body);
        }
        else if (result.Verified)
        {
            context.Dialogs.Success(context.L("Restore.Completed"), body);
        }
        else
        {
            context.Dialogs.Error(context.L("Tui.Restore.VerifyFailedTitle"), body);
        }

        context.StatusMessage = context.L("Restore.Completed") + ": " + target;
    }
}

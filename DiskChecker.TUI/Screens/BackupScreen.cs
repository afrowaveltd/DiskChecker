using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Terminal backup screen: streams a failing disk into a raw image, a fixed VHDx image or
/// a file level copy of a chosen directory. The source device is only ever opened
/// read-only; the only writer in the terminal UI is the restore screen.
/// </summary>
public sealed class BackupScreen : ITuiScreen
{
    internal const string ActionMode = "mode";
    internal const string ActionTarget = "target";
    internal const string ActionStart = "start";
    internal const string ActionRestore = "restore";
    internal const string ActionCustomTarget = "custom";
    internal const string ModeRaw = "raw";
    internal const string ModeVhdx = "vhdx";
    internal const string ModeFile = "file";

    public string Id => ScreenIds.Backup;

    public string TitleKey => "Nav.Backup";

    public Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken) =>
        ShowCoreAsync(context, cancellationToken);

    private async Task<ScreenResult> ShowCoreAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var disk = context.Selection.SelectedDisk;
        if (disk is null)
        {
            context.Dialogs.Warning(context.L("Backup.Title"), context.L("Tui.Backup.NoDisk"));
            context.Io.Choose(
                context.L("Common.Confirmation"),
                new[] { new TuiChoice(TuiKeys.Back, context.L("Common.Back")) });
            return ScreenResult.Back();
        }

        var targets = TuiImageBackupService.GetWritableTargets(disk.Path);
        string? targetRoot = targets.Count > 0 ? targets[0].Root : null;
        var mode = TuiBackupMode.RawImage;
        string? sourceDirectory = null;
        var service = new TuiImageBackupService((key, args) => Format(context, key, args));

        while (!cancellationToken.IsCancellationRequested)
        {
            RenderSummary(context, disk, mode, targetRoot);

            var choices = new List<TuiChoice>
            {
                new(ActionMode, context.L("Tui.Backup.Action.Mode"), DescribeMode(context, mode)),
                new(ActionTarget, context.L("Tui.Backup.Action.Target"), targetRoot ?? context.L("Common.NotAvailable")),
                new(ActionStart, context.L("Backup.StartButton")),
                new(ActionRestore, context.L("Backup.RestoreButton")),
                new(TuiKeys.Back, context.L("Common.Back")),
            };

            string? choice = context.Io.Choose(context.L("Backup.Title"), choices, pageSize: choices.Count);
            switch (choice)
            {
                case ActionMode:
                    mode = ChooseMode(context, mode, ref sourceDirectory);
                    break;

                case ActionTarget:
                    targetRoot = ChooseTarget(context, targets, targetRoot);
                    break;

                case ActionStart:
                {
                    var completed = await StartBackupAsync(context, service, disk, mode, targetRoot, sourceDirectory, cancellationToken)
                        .ConfigureAwait(false);
                    if (completed is not null)
                    {
                        return completed.Value;
                    }

                    break;
                }

                case ActionRestore:
                    return ScreenResult.Navigate(ScreenIds.Restore);

                default:
                    return ScreenResult.Back();
            }
        }

        return ScreenResult.Back();
    }

    private static void RenderSummary(TuiContext context, CoreDriveInfo disk, TuiBackupMode mode, string? targetRoot)
    {
        long required = TuiImageBackupService.GetRequiredBytes(disk.TotalSize, mode);
        long? free = TuiImageBackupService.GetFreeSpaceAt(targetRoot);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(foreground: TuiTheme.Primary))
            .AddColumn(new TableColumn("[bold]" + Markup.Escape(context.L("Tui.Backup.SourceHeader")) + "[/]"));

        table.AddRow(Label(context, context.L("Tui.Backup.Label.Model"), TuiFormats.Ellipsis(disk.Name, Math.Max(16, context.Io.Width - 26))));
        table.AddRow(Label(context, context.L("Tui.Backup.Label.Path"), TuiFormats.Ellipsis(disk.Path, Math.Max(16, context.Io.Width - 26))));
        table.AddRow(Label(context, context.L("Tui.Backup.Label.Size"), TuiFormats.Bytes(disk.TotalSize)));
        table.AddRow(Label(context, context.L("Backup.Mode"), DescribeMode(context, mode)));
        table.AddRow(Label(context, context.L("Tui.Backup.Label.Target"), targetRoot ?? context.L("Common.NotAvailable")));
        table.AddRow(Label(context, context.L("Backup.Space.Needed"), TuiFormats.DecimalSize(required)));
        if (free is not null)
        {
            table.AddRow(Label(context, context.L("Tui.Backup.Label.Free"), TuiFormats.DecimalSize(free.Value)));
        }

        table.AddRow(Label(context, context.L("Backup.Space.Reserve"), TuiFormats.DecimalSize(TuiImageBackupService.SystemReserveBytes)));

        context.Io.Write(table);
        context.Io.Line();
    }

    private static string Label(TuiContext context, string label, string value) =>
        "[grey]" + Markup.Escape(label) + ":[/] " + Markup.Escape(value);

    private static string DescribeMode(TuiContext context, TuiBackupMode mode) => mode switch
    {
        TuiBackupMode.VhdxImage => context.L("Backup.Mode.Vhdx"),
        TuiBackupMode.FileLevel => context.L("Backup.Mode.FileLevel"),
        _ => context.L("Backup.Mode.Raw"),
    };

    private static TuiBackupMode ChooseMode(TuiContext context, TuiBackupMode current, ref string? sourceDirectory)
    {
        var choices = new List<TuiChoice>
        {
            new(ModeRaw, context.L("Backup.Mode.Raw"), context.L("Backup.Mode.RawDesc")),
            new(ModeVhdx, context.L("Backup.Mode.Vhdx")),
            new(ModeFile, context.L("Backup.Mode.FileLevel")),
            new(TuiKeys.Back, context.L("Common.Back")),
        };

        string? choice = context.Io.Choose(context.L("Tui.Backup.ModeTitle"), choices, pageSize: choices.Count);
        switch (choice)
        {
            case ModeRaw:
                return TuiBackupMode.RawImage;
            case ModeVhdx:
                return TuiBackupMode.VhdxImage;
            case ModeFile:
                sourceDirectory ??= context.Io.Ask(context.L("Tui.Backup.SourceFolderAsk"), DefaultSourceFolder());
                return TuiBackupMode.FileLevel;
            default:
                return current;
        }
    }

    private static string DefaultSourceFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? Environment.CurrentDirectory : home;
    }

    private static string? ChooseTarget(TuiContext context, IReadOnlyList<TuiBackupTarget> targets, string? current)
    {
        var choices = targets
            .Select(target => new TuiChoice(target.Root, target.Label, TuiFormats.DecimalSize(target.FreeBytes)))
            .ToList();
        choices.Add(new TuiChoice(ActionCustomTarget, context.L("Tui.Backup.TargetCustom")));
        choices.Add(new TuiChoice(TuiKeys.Back, context.L("Common.Back")));

        string? choice = context.Io.Choose(context.L("Tui.Backup.TargetTitle"), choices, pageSize: choices.Count);
        if (choice is null || choice == TuiKeys.Back)
        {
            return current;
        }

        if (choice == ActionCustomTarget)
        {
            string? typed = context.Io.Ask(context.L("Tui.Backup.TargetAsk"), current);
            return string.IsNullOrWhiteSpace(typed) ? current : typed.Trim();
        }

        return choice;
    }

    private static async Task<ScreenResult?> StartBackupAsync(
        TuiContext context,
        TuiImageBackupService service,
        CoreDriveInfo disk,
        TuiBackupMode mode,
        string? targetRoot,
        string? sourceDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            context.Dialogs.Error(context.L("Backup.Title"), context.L("Tui.Backup.NoTarget"));
            return null;
        }

        if (mode == TuiBackupMode.FileLevel && string.IsNullOrWhiteSpace(sourceDirectory))
        {
            context.Dialogs.Error(context.L("Backup.Title"), context.L("Tui.Backup.NoSourceFolder"));
            return null;
        }

        long required = mode == TuiBackupMode.FileLevel
            ? EstimateDirectoryBytes(sourceDirectory)
            : TuiImageBackupService.GetRequiredBytes(disk.TotalSize, mode);

        long? free = TuiImageBackupService.GetFreeSpaceAt(targetRoot);
        if (free is not null && free.Value - TuiImageBackupService.SystemReserveBytes < required)
        {
            context.Dialogs.Warning(
                context.L("Backup.Title"),
                context.L(
                    "Tui.Backup.SpaceRefused",
                    TuiFormats.DecimalSize(required),
                    TuiFormats.DecimalSize(Math.Max(0, free.Value - TuiImageBackupService.SystemReserveBytes))));
            return null;
        }

        bool confirmed = context.Dialogs.Confirm(
            context.L("Backup.ConfirmTitle"),
            context.L(
                "Tui.Backup.ConfirmBody",
                disk.Name,
                disk.Path,
                DescribeMode(context, mode),
                targetRoot,
                TuiFormats.DecimalSize(required),
                TuiFormats.DecimalSize(TuiImageBackupService.SystemReserveBytes)));
        if (!confirmed)
        {
            return null;
        }

        var cancellation = context.GetRequiredService<TuiCancellation>();
        var operation = cancellation.BeginOperation();
        var progress = new TuiProgress(context.L("Backup.Title")) { ShowChart = false, MaxLogLines = 6 };
        TuiBackupResult? result = null;
        Exception? failure = null;

        using (context.LockNavigation(context.L("Common.CannotLeaveDuringBackup")))
        {
            try
            {
                await context.Io.RunLiveAsync(
                    context.L("Backup.Running"),
                    progress,
                    async report =>
                    {
                        result = await service.RunAsync(
                            new TuiBackupRequest
                            {
                                SourcePath = disk.Path,
                                SourceModel = disk.Name,
                                TotalSize = disk.TotalSize,
                                Mode = mode,
                                TargetRoot = targetRoot,
                                SourceDirectory = sourceDirectory,
                                EstimatedBytes = required,
                            },
                            report,
                            operation.Token).ConfigureAwait(false);
                    },
                    operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                failure = null;
                context.StatusMessage = context.L("Backup.Cancelled");
                context.Dialogs.Warning(context.L("Backup.Title"), context.L("Backup.Cancelled"));
                return null;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
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
            context.StatusMessage = context.L("Tui.Backup.Failed", failure?.Message ?? string.Empty);
            context.Dialogs.Error(context.L("Common.Error"), context.StatusMessage);
            return null;
        }

        string body = context.L(
            "Tui.Backup.DoneBody",
            result.BackupRoot,
            TuiFormats.DecimalSize(result.TotalBytes),
            TuiFormats.DecimalSize(result.UnreadableBytes),
            result.Sha256 ?? "—");

        if (!result.Verified && result.Mode != TuiBackupMode.FileLevel)
        {
            body += Environment.NewLine + context.L("Tui.Backup.VerifyFailed");
            context.Dialogs.Warning(context.L("Backup.Completed"), body);
        }
        else
        {
            context.Dialogs.Success(context.L("Backup.Completed"), body);
        }

        context.StatusMessage = context.L("Backup.Completed") + ": " + result.BackupRoot;
        return null;
    }

    private static long EstimateDirectoryBytes(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return 0;
        }

        try
        {
            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string Format(TuiContext context, string key, object[] args) =>
        args.Length == 0 ? context.L(key) : context.L(key, args);
}

using System.Globalization;
using System.Text;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Landing screen of the terminal UI: the detected physical disks with their SMART
/// grade, the dashboard statistics, the lock management and the per-disk actions.
/// The behaviour mirrors the desktop <c>DiskSelectionViewModel</c>.
/// </summary>
public sealed class DiskSelectionScreen : ITuiScreen
{
    /// <summary>Action key that forces a new disk detection.</summary>
    internal const string ActionRefresh = "refresh";

    /// <summary>Prefix of the per-disk choice keys.</summary>
    internal const string DiskKeyPrefix = "disk:";

    internal const string DiskActionOpen = "open";
    internal const string DiskActionSmart = "smart";
    internal const string DiskActionHistory = "history";
    internal const string DiskActionLock = "lock";
    internal const string DiskActionUnlock = "unlock";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly TimeSpan SmartProbeTimeout = TimeSpan.FromSeconds(20);

    public string Id => ScreenIds.DiskSelection;

    public string TitleKey => "Nav.DiskSelection";

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        DiskBoard board = await LoadAsync(context, forceRefresh: false, cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Render(context, board);

            string? choice = context.Io.Choose(context.L("DiskSelection.SelectDisk"), BuildChoices(context, board));
            if (choice is null || choice == TuiKeys.Back)
            {
                return ScreenResult.Back();
            }

            if (choice == TuiKeys.Exit)
            {
                return ScreenResult.Exit();
            }

            if (choice == TuiKeys.MainMenu)
            {
                return ScreenResult.Menu();
            }

            if (choice == ActionRefresh)
            {
                board = await LoadAsync(context, forceRefresh: true, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!TryResolveDisk(choice, board, out DiskEntry entry))
            {
                continue;
            }

            ScreenResult? navigation = await RunDiskActionsAsync(context, entry, cancellationToken).ConfigureAwait(false);
            if (navigation is { } result)
            {
                return result;
            }
        }

        return ScreenResult.Back();
    }

    // ---------------------------------------------------------------- loading

    private static async Task<DiskBoard> LoadAsync(TuiContext context, bool forceRefresh, CancellationToken cancellationToken)
    {
        var settings = context.GetRequiredService<ISettingsService>();
        var smartProvider = context.GetRequiredService<ISmartaProvider>();
        var qualityCalculator = context.GetRequiredService<IQualityCalculator>();

        var board = new DiskBoard();
        List<string> lockedDisks;
        try
        {
            lockedDisks = await settings.GetLockedDisksAsync().ConfigureAwait(false) ?? new List<string>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lockedDisks = new List<string>();
        }

        IReadOnlyList<CoreDriveInfo> drives;
        try
        {
            drives = await context.DiskCache.GetDrivesAsync(forceRefresh).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            drives = Array.Empty<CoreDriveInfo>();
            board.LoadError = ex.Message;
        }

        foreach (CoreDriveInfo drive in drives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SmartaData? smart = await ProbeSmartAsync(smartProvider, drive, cancellationToken).ConfigureAwait(false);
            ApplySmartIdentity(drive, smart);

            QualityRating? quality = smart is null ? null : qualityCalculator.CalculateQuality(smart);
            bool isSystemDisk = drive.IsSystemDisk;

            // The system disk is implicitly locked and can never be unlocked.
            bool isLocked = isSystemDisk || lockedDisks.Any(identifier => IsSameDisk(identifier, drive.Path));

            board.Entries.Add(new DiskEntry
            {
                Drive = drive,
                DisplayName = !string.IsNullOrWhiteSpace(smart?.DeviceModel)
                    ? smart!.DeviceModel
                    : string.IsNullOrWhiteSpace(drive.Name) ? "Unknown" : drive.Name,
                SerialNumber = ResolveSerialNumber(drive, smart),
                BusType = TuiFormats.BusTypeDisplay(drive),
                CapacityText = TuiFormats.Capacity(drive.TotalSize),
                Grade = quality?.Grade.ToString(),
                Score = quality?.Score,
                Smart = smart,
                VolumesDisplay = BuildVolumesDisplay(drive),
                IsSystemDisk = isSystemDisk,
                IsLocked = isLocked,
            });
        }

        await LoadHistoryAsync(context, board).ConfigureAwait(false);

        board.SmartPermissionDenied = smartProvider.LastOperationWasPermissionDenied;
        context.StatusMessage = board.Entries.Count == 0
            ? context.L("Tui.DiskSelection.HealthSummary.NoDisks")
            : context.L("Tui.DiskSelection.Loaded", board.Entries.Count.ToString(Invariant));
        return board;
    }

    /// <summary>Per-disk SMART probe with a hard timeout so one bad disk cannot freeze the list.</summary>
    private static async Task<SmartaData?> ProbeSmartAsync(ISmartaProvider provider, CoreDriveInfo drive, CancellationToken cancellationToken)
    {
        if (!drive.SupportsSmart)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SmartProbeTimeout);

        try
        {
            return await provider.GetSmartaDataAsync(drive.Path, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static void ApplySmartIdentity(CoreDriveInfo drive, SmartaData? smart)
    {
        if (smart is null)
        {
            return;
        }

        if (DriveIdentityResolver.IsReliableSerialNumber(smart.SerialNumber))
        {
            drive.SerialNumber = DriveIdentityResolver.NormalizeSerial(smart.SerialNumber);
        }

        if (!string.IsNullOrWhiteSpace(smart.FirmwareVersion))
        {
            drive.FirmwareVersion = smart.FirmwareVersion;
        }

        if (!string.IsNullOrWhiteSpace(smart.DeviceModel))
        {
            drive.Model = smart.DeviceModel;
        }
    }

    private static string ResolveSerialNumber(CoreDriveInfo drive, SmartaData? smart)
    {
        if (DriveIdentityResolver.IsReliableSerialNumber(smart?.SerialNumber))
        {
            return smart!.SerialNumber.Trim();
        }

        return DriveIdentityResolver.IsReliableSerialNumber(drive.SerialNumber)
            ? drive.SerialNumber!.Trim()
            : "N/A";
    }

    /// <summary>Resolves the stored test history of every disk inside a single database scope.</summary>
    private static async Task LoadHistoryAsync(TuiContext context, DiskBoard board)
    {
        if (board.Entries.Count == 0)
        {
            return;
        }

        try
        {
            await context.UseScopedAsync(async services =>
            {
                var repository = services.GetRequiredService<IDiskCardRepository>();
                foreach (DiskEntry entry in board.Entries)
                {
                    DiskCard? card = await FindCardAsync(repository, entry.Drive, entry.Smart).ConfigureAwait(false);
                    if (card is null)
                    {
                        continue;
                    }

                    List<TestSession> sessions = await repository.GetTestSessionsAsync(card.Id).ConfigureAwait(false);
                    entry.CardId = card.Id;
                    entry.Sessions = sessions
                        .OrderByDescending(session => session.TestedAt)
                        .ToList();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            board.LoadError = ex.Message;
        }
    }

    /// <summary>
    /// Finds the stored disk card. Serial numbers are the identity; a device path is a
    /// transient OS slot, so it may only be reused when the model and capacity still match.
    /// </summary>
    private static async Task<DiskCard?> FindCardAsync(IDiskCardRepository repository, CoreDriveInfo drive, SmartaData? smart)
    {
        string serial = DriveIdentityResolver.IsReliableSerialNumber(smart?.SerialNumber)
            ? smart!.SerialNumber
            : drive.SerialNumber ?? string.Empty;
        string model = !string.IsNullOrWhiteSpace(smart?.DeviceModel) ? smart!.DeviceModel : drive.Name ?? "Unknown";
        string firmware = !string.IsNullOrWhiteSpace(smart?.FirmwareVersion) ? smart!.FirmwareVersion : drive.FirmwareVersion ?? string.Empty;

        string identityKey = DriveIdentityResolver.BuildIdentityKey(drive.Path, serial, model, firmware);
        DiskCard? bySerial = await repository.GetBySerialNumberAsync(identityKey).ConfigureAwait(false);
        if (bySerial is not null)
        {
            return bySerial;
        }

        if (DriveIdentityResolver.IsReliableSerialNumber(serial))
        {
            return null;
        }

        DiskCard? byPath = await repository.GetByDevicePathAsync(drive.Path).ConfigureAwait(false);
        if (byPath is null)
        {
            return null;
        }

        bool sameModel = string.Equals(
            NormalizeToken(byPath.ModelName),
            NormalizeToken(model),
            StringComparison.Ordinal);
        bool sameCapacity = byPath.Capacity <= 0
            || drive.TotalSize <= 0
            || Math.Abs(byPath.Capacity - drive.TotalSize) < 32L * 1024L * 1024L;
        bool pathFallbackAllowed = sameModel && sameCapacity && !DriveIdentityResolver.IsReliableSerialNumber(byPath.SerialNumber);
        return pathFallbackAllowed ? byPath : null;
    }

    private static string NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string BuildVolumesDisplay(CoreDriveInfo drive)
    {
        if (drive.Volumes.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (CoreDriveInfo volume in drive.Volumes.Take(4))
        {
            if (string.IsNullOrWhiteSpace(volume.Path))
            {
                continue;
            }

            string size = TuiFormats.Capacity(volume.TotalSize);
            parts.Add(string.IsNullOrWhiteSpace(volume.FileSystem)
                ? volume.Path + " (" + size + ")"
                : volume.Path + " (" + volume.FileSystem + ", " + size + ")");
        }

        string result = string.Join(" | ", parts);
        if (drive.Volumes.Count > 4)
        {
            result += " +" + (drive.Volumes.Count - 4).ToString(Invariant);
        }

        return result;
    }

    /// <summary>Path comparison used by the desktop lock list (path or drive number).</summary>
    internal static bool IsSameDisk(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int? firstNumber = ExtractDriveNumber(first);
        int? secondNumber = ExtractDriveNumber(second);
        return firstNumber is not null && secondNumber is not null && firstNumber.Value == secondNumber.Value;
    }

    private static int? ExtractDriveNumber(string path)
    {
        var digits = new StringBuilder(path.Length);
        foreach (char character in path)
        {
            if (char.IsDigit(character))
            {
                digits.Append(character);
            }
        }

        return int.TryParse(digits.ToString(), NumberStyles.Integer, Invariant, out int number) ? number : null;
    }

    // --------------------------------------------------------------- rendering

    private static void Render(TuiContext context, DiskBoard board)
    {
        ITuiIo io = context.Io;
        io.Line();
        io.Line($"[bold]{Markup.Escape(context.L("DiskSelection.Title"))}[/]");
        io.Line($"[grey]{Markup.Escape(context.L("DiskSelection.Description"))}[/]");
        RenderDashboard(context, board);
        RenderTable(context, board);

        if (!string.IsNullOrWhiteSpace(board.LoadError))
        {
            io.Line($"[{TuiTheme.Warning.ToMarkup()}]⚠ {Markup.Escape(context.L("Common.LoadFailed"))} {Markup.Escape(board.LoadError)}[/]");
        }

        if (board.SmartPermissionDenied)
        {
            io.Line($"[{TuiTheme.Warning.ToMarkup()}]⚠ {Markup.Escape(context.L("Tui.DiskSelection.SmartPermissionDenied"))}[/]");
        }

        io.Line($"[grey]{Markup.Escape(BuildSystemInfoLine(context))}[/]");
        context.RenderStatusBar();
    }

    private static void RenderDashboard(TuiContext context, DiskBoard board)
    {
        var body = new StringBuilder();
        body.AppendLine(
            Markup.Escape(context.L("Disk.PhysicalDisks")) + ": [bold]" +
            board.Entries.Count.ToString(Invariant) + "[/]   " +
            Markup.Escape(context.L("Tui.DiskSelection.TotalCapacity")) + ": [bold]" +
            Markup.Escape(TuiFormats.Capacity(board.TotalCapacity)) + "[/]   " +
            Markup.Escape(context.L("Tui.DiskSelection.Healthy")) + ": [bold]" +
            board.HealthyCount.ToString(Invariant) + "[/]/" + board.Entries.Count.ToString(Invariant));
        body.AppendLine(
            Markup.Escape(context.L("Tui.DiskSelection.Health")) + ": " +
            Markup.Escape(BuildHealthSummary(context, board.Entries.Count, board.GradedCount, board.HealthyCount)));

        DiskEntry? systemDisk = board.SystemDisk;
        body.Append(
            Markup.Escape(context.L("Disk.SystemDisk")) + ": " +
            (systemDisk is null
                ? "[grey]" + Markup.Escape(context.L("Disk.SystemDiskNotFound")) + "[/]"
                : Markup.Escape(TuiFormats.Ellipsis(systemDisk.DisplayName, 40)) + " · " +
                  TuiTheme.GradeMarkup(systemDisk.Grade) + " · " +
                  TuiTheme.TemperatureMarkup(systemDisk.Temperature)));

        context.Io.Write(new Panel(new Markup(body.ToString()))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: TuiTheme.Primary),
            Header = new PanelHeader($"[bold]{Markup.Escape(context.L("DiskSelection.TestSetupStatus"))}[/]", Justify.Left),
            Padding = new Padding(1, 0, 1, 0),
        });
    }

    /// <summary>
    /// Health summary sentence using exactly the desktop rules: a disk without SMART
    /// data is unknown, never "needing attention".
    /// </summary>
    internal static string BuildHealthSummary(TuiContext context, int diskCount, int gradedCount, int healthyCount)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (diskCount == 0)
        {
            return context.L("Tui.DiskSelection.HealthSummary.NoDisks");
        }

        if (gradedCount == 0)
        {
            return context.L("Tui.DiskSelection.HealthSummary.SmartUnavailable");
        }

        if (healthyCount == gradedCount)
        {
            int unknown = diskCount - gradedCount;
            return unknown > 0
                ? context.L("Tui.DiskSelection.HealthSummary.HealthyWithUnknown", unknown)
                : context.L("Tui.DiskSelection.HealthSummary.AllOk");
        }

        return context.L("Tui.DiskSelection.HealthSummary.Attention", gradedCount - healthyCount);
    }

    private static void RenderTable(TuiContext context, DiskBoard board)
    {
        ITuiIo io = context.Io;
        if (board.Entries.Count == 0)
        {
            io.Line($"[yellow]{Markup.Escape(context.L("Tui.DiskSelection.HealthSummary.NoDisks"))}[/]");
            return;
        }

        int width = Math.Max(80, io.Width);
        int budget = Math.Max(24, width - BordersAndPadding);
        bool compact = width < 96;

        // The widths are allocated so the rendered table always fits the terminal:
        // no cell is ever wrapped and the column set shrinks on narrow terminals.
        int[] sizes = compact
            ? AllocateWidths(budget - FixedColumnsWidth, new[] { 5, 3 }, new[] { 0.62, 0.38 })
            : AllocateWidths(budget - FixedColumnsWidth, new[] { 5, 5, 3, 7, 8 }, new[] { 0.42, 0.16, 0.18, 0.12, 0.12 });

        var table = new Table
        {
            Border = TableBorder.Rounded,
            BorderStyle = new Style(foreground: TuiTheme.Muted),
        };

        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Index")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Model")) { NoWrap = true });
        if (!compact)
        {
            table.AddColumn(new TableColumn(Header(context, "Tui.Column.Path")) { NoWrap = true });
        }

        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Serial")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Bus")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Capacity")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Grade")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Temperature")) { NoWrap = true });
        if (!compact)
        {
            table.AddColumn(new TableColumn(Header(context, "Tui.Column.Volumes")) { NoWrap = true });
            table.AddColumn(new TableColumn(Header(context, "Tui.Column.Flags")) { NoWrap = true });
        }

        int modelWidth = sizes[0];
        int pathWidth = compact ? 0 : sizes[1];
        int serialWidth = compact ? sizes[1] : sizes[2];
        int volumesWidth = compact ? 0 : sizes[3];
        int flagsWidth = compact ? 0 : sizes[4];

        int index = 1;
        foreach (DiskEntry entry in board.Entries)
        {
            var cells = new List<IRenderable>
            {
                new Markup(index.ToString(Invariant)),
                new Markup(Cell(entry.DisplayName, modelWidth)),
            };

            if (!compact)
            {
                cells.Add(new Markup(Cell(entry.Drive.Path, pathWidth)));
            }

            cells.Add(new Markup(Cell(entry.SerialNumber, serialWidth)));
            cells.Add(new Markup(Cell(entry.BusType, BusWidth)));
            cells.Add(new Markup(Markup.Escape(entry.CapacityText)));
            cells.Add(new Markup(TuiTheme.GradeMarkup(entry.Grade)));
            cells.Add(new Markup(TuiTheme.TemperatureMarkup(entry.Temperature)));

            if (!compact)
            {
                cells.Add(new Markup(Cell(entry.VolumesDisplay, volumesWidth)));
                cells.Add(new Markup(BuildFlags(context, entry, flagsWidth)));
            }

            table.AddRow(cells);
            index++;
        }

        io.Write(table);
    }

    /// <summary>Cell width taken by the border and the padding of the rendered table.</summary>
    internal const int BordersAndPadding = 31;

    private const int BusWidth = 8;
    private const int CapacityWidth = 8;
    private const int GradeWidth = 6;
    private const int TemperatureWidth = 7;
    private const int IndexWidth = 1;

    /// <summary>Columns that keep their natural width in both layouts.</summary>
    private const int FixedColumnsWidth = IndexWidth + BusWidth + CapacityWidth + GradeWidth + TemperatureWidth;

    /// <summary>
    /// Distributes <paramref name="budget"/> content columns over the flexible columns
    /// starting from <paramref name="minimums"/>. The result always sums to the budget
    /// so the rendered table never needs to wrap or clip.
    /// </summary>
    internal static int[] AllocateWidths(int budget, int[] minimums, double[] weights)
    {
        ArgumentNullException.ThrowIfNull(minimums);
        ArgumentNullException.ThrowIfNull(weights);
        var widths = (int[])minimums.Clone();
        int leftover = budget - minimums.Sum();
        if (leftover <= 0 || weights.Length == 0)
        {
            return widths;
        }

        double total = weights.Sum();
        if (total <= 0)
        {
            return widths;
        }

        int assigned = 0;
        int limit = Math.Min(weights.Length, widths.Length);
        for (int index = 0; index < limit; index++)
        {
            int share = (int)Math.Round(leftover * (weights[index] / total), MidpointRounding.AwayFromZero);
            widths[index] += share;
            assigned += share;
        }

        widths[0] += leftover - assigned;
        return widths;
    }

    private static string Header(TuiContext context, string key) =>
        "[grey]" + Markup.Escape(context.L(key)) + "[/]";

    private static string Cell(string? value, int width) =>
        string.IsNullOrWhiteSpace(value) ? "[grey]–[/]" : Markup.Escape(TuiFormats.Ellipsis(value, width));

    private static string BuildFlags(TuiContext context, DiskEntry entry, int width)
    {
        var flags = new List<string>();
        if (entry.IsSystemDisk)
        {
            flags.Add(context.L("Tui.Flag.System"));
        }

        if (entry.IsLocked)
        {
            flags.Add(context.L("Tui.Flag.Locked"));
        }

        if (entry.Sessions.Count > 0)
        {
            flags.Add(context.L("Tui.Flag.History"));
        }

        if (flags.Count == 0)
        {
            return "[grey]–[/]";
        }

        // Truncate the plain text first; markup must never be cut in half.
        string text = TuiFormats.Ellipsis(string.Join(" ", flags), width);
        Color color = entry.IsLocked ? TuiTheme.Danger : TuiTheme.Warning;
        return $"[{color.ToMarkup()}]{Markup.Escape(text)}[/]";
    }

    /// <summary>OS / CPU / RAM line, the terminal counterpart of the desktop info bar.</summary>
    internal static string BuildSystemInfoLine(TuiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TuiSystemInfo info = context.SystemInfo;
        return string.Create(
            Invariant,
            $"{info.OsName} · {info.CpuInfo} · {info.RamInfo}");
    }

    // ---------------------------------------------------------------- actions

    private static List<TuiChoice> BuildChoices(TuiContext context, DiskBoard board)
    {
        var choices = new List<TuiChoice>
        {
            new(ActionRefresh, context.L("DiskSelection.RefreshDisks")),
        };

        for (int index = 0; index < board.Entries.Count; index++)
        {
            DiskEntry entry = board.Entries[index];
            var label = new StringBuilder();
            label.Append(index + 1).Append(". ").Append(TuiFormats.Ellipsis(entry.DisplayName, 34));
            label.Append(" · ").Append(entry.CapacityText);
            label.Append(" · ").Append(entry.Grade ?? "N/A");
            if (entry.IsSystemDisk)
            {
                label.Append(" · ").Append(context.L("Tui.Flag.System"));
            }

            if (entry.IsLocked)
            {
                label.Append(' ').Append(context.L("DiskSelection.LockedLabel"));
            }

            choices.Add(new TuiChoice(
                DiskKeyPrefix + index.ToString(Invariant),
                label.ToString(),
                entry.VolumesDisplay));
        }

        choices.Add(new TuiChoice(TuiKeys.MainMenu, "☰ " + context.L("Tui.Common.Menu")));
        choices.Add(new TuiChoice(TuiKeys.Back, context.L("Common.Back")));
        return choices;
    }

    private static bool TryResolveDisk(string choice, DiskBoard board, out DiskEntry entry)
    {
        entry = null!;
        if (!choice.StartsWith(DiskKeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(choice[DiskKeyPrefix.Length..], NumberStyles.Integer, Invariant, out int index))
        {
            return false;
        }

        if (index < 0 || index >= board.Entries.Count)
        {
            return false;
        }

        entry = board.Entries[index];
        return true;
    }

    /// <summary>
    /// Offers the per-disk actions. Returns a navigation result when the screen has to be
    /// left, <c>null</c> when the screen should simply re-render.
    /// </summary>
    private static async Task<ScreenResult?> RunDiskActionsAsync(TuiContext context, DiskEntry entry, CancellationToken cancellationToken)
    {
        var choices = new List<TuiChoice>
        {
            new(DiskActionOpen, "💿 " + context.L("Tui.DiskSelection.Open")),
            new(DiskActionSmart, "🔬 " + context.L("Tui.DiskSelection.Smart")),
            new(DiskActionHistory, "📜 " + context.L("Tui.DiskSelection.History")),
            entry.IsLocked
                ? new TuiChoice(DiskActionUnlock, "🔓 " + context.L("Tui.DiskSelection.Unlock"))
                : new TuiChoice(DiskActionLock, "🔒 " + context.L("Tui.DiskSelection.Lock")),
            new(TuiKeys.Back, context.L("Common.Back")),
        };

        string? choice = context.Io.Choose(
            context.L("Tui.DiskSelection.DiskActions", entry.DisplayName),
            choices);

        if (choice is null || choice == TuiKeys.Back)
        {
            return null;
        }

        switch (choice)
        {
            case DiskActionOpen:
                SelectDisk(context, entry);
                return ScreenResult.Navigate(ScreenIds.SmartCheck);

            case DiskActionSmart:
                ShowSmartSummary(context, entry);
                return null;

            case DiskActionHistory:
                ShowHistory(context, entry);
                return null;

            case DiskActionLock:
                await SetLockAsync(context, entry, locked: true).ConfigureAwait(false);
                return null;

            case DiskActionUnlock:
                await SetLockAsync(context, entry, locked: false).ConfigureAwait(false);
                return null;

            default:
                return null;
        }
    }

    private static void SelectDisk(TuiContext context, DiskEntry entry)
    {
        context.Selection.SelectedDisk = entry.Drive;
        context.Selection.SelectedDiskDisplayName = entry.DisplayName;
        context.Selection.IsSelectedDiskLocked = entry.IsLocked;
        context.StatusMessage = context.L("Tui.DiskSelection.Selected", entry.DisplayName);
    }

    private static async Task SetLockAsync(TuiContext context, DiskEntry entry, bool locked)
    {
        if (!locked)
        {
            // The system disk is protected automatically and can never be unlocked.
            if (entry.IsSystemDisk)
            {
                context.Dialogs.Warning(context.L("Disk.SystemDisk"), context.L("Disk.SystemDiskLocked"));
                return;
            }

            await context.GetRequiredService<ISettingsService>().UnlockDiskAsync(entry.Drive.Path).ConfigureAwait(false);
            entry.IsLocked = false;
            context.StatusMessage = context.L("Tui.DiskSelection.Unlocked", entry.DisplayName);
            return;
        }

        await context.GetRequiredService<ISettingsService>().LockDiskAsync(entry.Drive.Path).ConfigureAwait(false);
        entry.IsLocked = true;
        context.StatusMessage = context.L("Tui.DiskSelection.Locked", entry.DisplayName);
    }

    private static void ShowSmartSummary(TuiContext context, DiskEntry entry)
    {
        SmartaData? smart = entry.Smart;
        if (smart is null)
        {
            context.Dialogs.Info(context.L("DiskSelection.Smart"), context.L("Tui.DiskSelection.NoSmartData"));
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine(context.L("DiskCardDetailView.Model") + ": " + smart.DeviceModel);
        builder.AppendLine(context.L("DiskCardDetailView.SerialNumber") + ": " + (string.IsNullOrWhiteSpace(smart.SerialNumber) ? "N/A" : smart.SerialNumber));
        builder.AppendLine(context.L("DiskCardDetailView.Firmware") + ": " + (string.IsNullOrWhiteSpace(smart.FirmwareVersion) ? "N/A" : smart.FirmwareVersion));
        builder.AppendLine(context.L("DiskCardDetailView.Grade") + ": " + (entry.Grade ?? "N/A"));
        builder.AppendLine(context.L("DiskCardDetailView.Temperature") + ": " + TuiFormats.Temperature(smart.Temperature));
        builder.AppendLine(context.L("DiskCardDetailView.PowerOnHours") + ": " + FormatOptional(smart.PowerOnHours));
        builder.AppendLine(context.L("DiskCardDetailView.Reallocated") + ": " + FormatOptional(smart.ReallocatedSectorCount));
        builder.AppendLine(context.L("DiskCardDetailView.Pending") + ": " + FormatOptional(smart.PendingSectorCount));
        builder.Append(context.L("DiskCardDetailView.Uncorrectable") + ": " + FormatOptional(smart.UncorrectableErrorCount));

        context.Dialogs.Notice(context.L("DiskSelection.Smart"), builder.ToString());
    }

    private static void ShowHistory(TuiContext context, DiskEntry entry)
    {
        if (entry.Sessions.Count == 0)
        {
            context.Dialogs.Info(
                context.L("Common.NoHistory"),
                entry.CardId is null ? context.L("Common.DiskNoCard") : context.L("Common.DiskNotTested"));
            return;
        }

        var builder = new StringBuilder();
        int position = 1;
        foreach (TestSession session in entry.Sessions.Take(3))
        {
            builder.AppendLine(string.Create(
                Invariant,
                $"{position}. {session.TestedAt.ToString("dd.MM.yyyy HH:mm", Invariant)} · {session.TestType} · " +
                $"{session.Grade} · {session.Score.ToString("F0", Invariant)}/100 · {session.ErrorCount}×"));
            position++;
        }

        TestSession latest = entry.Sessions[0];
        builder.AppendLine();
        builder.AppendLine(context.L("DiskCardDetailView.Type") + ": " + latest.TestType);
        builder.AppendLine(context.L("DiskCardDetailView.Duration") + ": " + TuiFormats.Duration(latest.Duration));
        builder.AppendLine(context.L("DiskCardDetailView.Errors") + ": " + latest.ErrorCount.ToString(Invariant));
        if (!string.IsNullOrWhiteSpace(latest.Notes))
        {
            builder.AppendLine(context.L("DiskCardDetailView.Notes") + ": " + TuiFormats.Ellipsis(latest.Notes, 200));
        }

        context.Dialogs.Notice(context.L("DiskSelection.HistoryTitle"), builder.ToString());
    }

    private static string FormatOptional(int? value) =>
        value is null ? "N/A" : value.Value.ToString(Invariant);

    /// <summary>Mutable state of one screen run.</summary>
    private sealed class DiskBoard
    {
        public List<DiskEntry> Entries { get; } = new();

        public bool SmartPermissionDenied { get; set; }

        public string? LoadError { get; set; }

        public int GradedCount => Entries.Count(entry => entry.Grade is not null);

        public int HealthyCount => Entries.Count(entry => TuiFormats.IsHealthy(entry.Grade));

        public long TotalCapacity => Entries.Sum(entry => entry.Drive.TotalSize);

        public DiskEntry? SystemDisk => Entries.FirstOrDefault(entry => entry.IsSystemDisk);
    }

    /// <summary>One row of the disk table together with its resolved history.</summary>
    private sealed class DiskEntry
    {
        public required CoreDriveInfo Drive { get; init; }

        public required string DisplayName { get; init; }

        public required string SerialNumber { get; init; }

        public required string BusType { get; init; }

        public required string CapacityText { get; init; }

        public required string VolumesDisplay { get; init; }

        public required bool IsSystemDisk { get; init; }

        public string? Grade { get; init; }

        public double? Score { get; init; }

        public SmartaData? Smart { get; init; }

        public bool IsLocked { get; set; }

        public int? CardId { get; set; }

        public IReadOnlyList<TestSession> Sessions { get; set; } = Array.Empty<TestSession>();

        public int? Temperature => Smart?.Temperature is > 0 ? Smart.Temperature : null;
    }
}

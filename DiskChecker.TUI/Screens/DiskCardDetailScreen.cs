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
/// Terminal counterpart of the desktop <c>DiskCardDetailViewModel</c>: the identity of
/// one disk card, its test session history, its certificates and the actions that can be
/// performed on them.
/// </summary>
public sealed class DiskCardDetailScreen : ITuiScreen
{
    internal const string ActionRefresh = "refresh";
    internal const string ActionSessionDetail = "detail";
    internal const string ActionCertificate = "certificate";
    internal const string ActionNotes = "notes";
    internal const string SessionKeyPrefix = "session:";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public string Id => ScreenIds.DiskCardDetail;

    public string TitleKey => "Nav.DiskCards";

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = new DiskCardDetailState();
        await LoadAsync(context, state).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Render(context, state);

            string? choice = context.Io.Choose(context.L("Tui.DiskCardDetail.Actions"), BuildChoices(context, state));
            if (choice is null || choice == TuiKeys.Back)
            {
                return ScreenResult.Navigate(ScreenIds.DiskCards);
            }

            if (choice == TuiKeys.Exit)
            {
                return ScreenResult.Exit();
            }

            if (choice == TuiKeys.MainMenu)
            {
                return ScreenResult.Menu();
            }

            switch (choice)
            {
                case ActionRefresh:
                    await LoadAsync(context, state).ConfigureAwait(false);
                    continue;

                case ActionSessionDetail:
                    ShowSessionDetail(context, state);
                    continue;

                case ActionNotes:
                    await SaveNotesAsync(context, state).ConfigureAwait(false);
                    continue;

                case ActionCertificate:
                    ScreenResult? navigation = await GenerateCertificateAsync(context, state, cancellationToken).ConfigureAwait(false);
                    if (navigation is { } result)
                    {
                        return result;
                    }

                    continue;
            }

            if (TryResolveSession(choice, state, out int sessionIndex))
            {
                state.SelectedIndex = sessionIndex;
                TestSession session = state.Sessions[sessionIndex];
                context.Selection.SelectedTestSessionId = session.Id;
                context.StatusMessage = context.L(
                    "DiskCardDetail.Status.TestSelected",
                    session.TestType.ToString(),
                    session.StartedAt.ToString("dd.MM.yyyy HH:mm", Invariant));
            }
        }

        return ScreenResult.Navigate(ScreenIds.DiskCards);
    }

    // ---------------------------------------------------------------- loading

    private static async Task LoadAsync(TuiContext context, DiskCardDetailState state)
    {
        int? cardId = context.Selection.SelectedDiskCardId;
        CoreDriveInfo? disk = context.Selection.SelectedDisk;

        try
        {
            await context.UseScopedAsync(async services =>
            {
                var repository = services.GetRequiredService<IDiskCardRepository>();

                DiskCard? card = null;
                if (cardId is { } id)
                {
                    card = await repository.GetByIdAsync(id).ConfigureAwait(false);
                }

                if (card is null && disk is not null)
                {
                    card = await FindCardForDiskAsync(repository, disk).ConfigureAwait(false);
                }

                state.Card = card;
                state.LoadError = null;
                if (card is null)
                {
                    state.Sessions = Array.Empty<TestSession>();
                    state.Certificates = Array.Empty<DiskCertificate>();
                    state.SelectedIndex = -1;
                    return;
                }

                state.CardId = card.Id;
                List<TestSession> sessions = await repository.GetTestSessionsAsync(card.Id).ConfigureAwait(false);
                state.Sessions = sessions
                    .OrderByDescending(session => session.StartedAt)
                    .ToList();

                List<DiskCertificate> certificates = await repository.GetCertificatesAsync(card.Id).ConfigureAwait(false);
                state.Certificates = certificates
                    .OrderByDescending(certificate => certificate.GeneratedAt)
                    .ToList();

                state.SelectedIndex = state.Sessions.Count > 0
                    ? Math.Clamp(state.SelectedIndex, 0, state.Sessions.Count - 1)
                    : -1;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state.Card = null;
            state.LoadError = ex.Message;
            context.Dialogs.Error(context.L("Common.Error"), context.L("DiskCardDetail.Error.LoadCard", ex.Message));
            return;
        }

        context.StatusMessage = state.Card is null
            ? context.L("DiskCardDetail.Status.CardNotFound")
            : context.L("DiskCardDetail.Status.CardLoadedName", state.Card.ModelName);
    }

    /// <summary>
    /// Resolves the card of a live device selection: the serial number is the identity,
    /// a path may only be reused when the model and the capacity still match.
    /// </summary>
    private static async Task<DiskCard?> FindCardForDiskAsync(IDiskCardRepository repository, CoreDriveInfo disk)
    {
        string identityKey = DriveIdentityResolver.BuildIdentityKey(
            disk.Path,
            disk.SerialNumber,
            disk.Name ?? "Unknown",
            disk.FirmwareVersion);
        DiskCard? card = await repository.GetBySerialNumberAsync(identityKey).ConfigureAwait(false);
        if (card is not null)
        {
            return card;
        }

        if (DriveIdentityResolver.IsReliableSerialNumber(disk.SerialNumber))
        {
            return null;
        }

        DiskCard? byPath = await repository.GetByDevicePathAsync(disk.Path).ConfigureAwait(false);
        if (byPath is null || DriveIdentityResolver.IsReliableSerialNumber(byPath.SerialNumber))
        {
            return null;
        }

        bool sameModel = string.Equals(
            (byPath.ModelName ?? string.Empty).Trim(),
            (disk.Name ?? "Unknown").Trim(),
            StringComparison.OrdinalIgnoreCase);
        bool sameCapacity = Math.Abs(byPath.Capacity - disk.TotalSize) < 32L * 1024L * 1024L;
        return sameModel && sameCapacity ? byPath : null;
    }

    // --------------------------------------------------------------- rendering

    private static void Render(TuiContext context, DiskCardDetailState state)
    {
        ITuiIo io = context.Io;
        io.Line();

        if (state.Card is null)
        {
            io.Line($"[yellow]{Markup.Escape(context.L("DiskCardDetail.Status.CardNotFound"))}[/]");
            if (!string.IsNullOrWhiteSpace(state.LoadError))
            {
                io.Line($"[{TuiTheme.Warning.ToMarkup()}]⚠ {Markup.Escape(state.LoadError)}[/]");
            }

            context.RenderStatusBar();
            return;
        }

        RenderIdentity(context, state.Card);
        RenderSessions(context, state);
        RenderCertificates(context, state);
        io.Line($"[grey]{Markup.Escape(DiskSelectionScreen.BuildSystemInfoLine(context))}[/]");
        context.RenderStatusBar();
    }

    private static void RenderIdentity(TuiContext context, DiskCard card)
    {
        var body = new StringBuilder();
        body.AppendLine(
            "[bold]" + Markup.Escape(TuiFormats.Ellipsis(card.ModelName, 48)) + "[/]  " +
            $"[grey]{Markup.Escape(context.L("DiskCardDetailView.Manufacturer"))}: " +
            Markup.Escape(TuiFormats.Ellipsis(card.ManufacturerDisplay, 24)) + "[/]");
        body.AppendLine(
            $"{Markup.Escape(context.L("DiskCardDetailView.SerialNumber"))}: " +
            Markup.Escape(string.IsNullOrWhiteSpace(card.SerialNumber) ? "N/A" : card.SerialNumber) + "   " +
            $"{Markup.Escape(context.L("DiskCardDetailView.Capacity"))}: " +
            Markup.Escape(TuiFormats.Capacity(card.Capacity)) + "   " +
            $"{Markup.Escape(context.L("DiskCardDetailView.DiskType"))}: " +
            Markup.Escape(string.IsNullOrWhiteSpace(card.DiskType) ? "N/A" : card.DiskType) + "   " +
            $"{Markup.Escape(context.L("DiskCardDetailView.Firmware"))}: " +
            Markup.Escape(string.IsNullOrWhiteSpace(card.FirmwareVersion) ? "N/A" : card.FirmwareVersion));
        body.AppendLine(
            $"{Markup.Escape(context.L("DiskCardDetailView.Path"))}: " +
            Markup.Escape(string.IsNullOrWhiteSpace(card.DevicePath) ? "N/A" : card.DevicePath) + "   " +
            $"{Markup.Escape(context.L("DiskCardDetailView.TestCount"))}: " +
            Markup.Escape(card.TestCount.ToString(Invariant)) + "   " +
            $"{Markup.Escape(context.L("DiskCardDetailView.LastGrade"))}: " +
            $"{TuiTheme.GradeMarkup(card.OverallGrade)} " +
            $"{Markup.Escape(context.L("DiskCardDetailView.Score"))}: {TuiTheme.ScoreMarkup(card.OverallScore)}");
        body.Append(
            BuildGradeSeal(context, card));

        context.Io.Write(new Panel(new Markup(body.ToString()))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: TuiTheme.Primary),
            Header = new PanelHeader($"[bold]{Markup.Escape(context.L("DiskCardDetailView.DiskInfo"))}[/]", Justify.Left),
            Padding = new Padding(1, 0, 1, 0),
        });
    }

    /// <summary>Grade seal line: grade, score and the resulting health classification.</summary>
    internal static string BuildGradeSeal(TuiContext context, DiskCard card)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(card);

        string health = TuiFormats.HealthStatus(card.OverallGrade);
        var builder = new StringBuilder();
        builder.Append(Markup.Escape(context.L("Grade.Final"))).Append(": ");
        builder.Append(TuiTheme.GradeMarkup(card.OverallGrade));
        builder.Append("  ").Append(Markup.Escape(context.L("DiskCardDetailView.Score"))).Append(": ");
        builder.Append(TuiTheme.ScoreMarkup(card.OverallScore));
        builder.Append("  ").Append(Markup.Escape(context.L("Disk.HealthStatus"))).Append(": ");
        builder.Append($"[{HealthColor(card.OverallGrade).ToMarkup()}]{Markup.Escape(health)}[/]");

        if (card.IsArchived)
        {
            builder.Append("  [").Append(TuiTheme.Muted.ToMarkup()).Append(']')
                .Append(Markup.Escape(context.L("Common.Archived"))).Append("[/]");
        }

        if (card.IsLocked)
        {
            builder.Append("  [").Append(TuiTheme.Danger.ToMarkup()).Append(']')
                .Append(Markup.Escape(context.L("Tui.Flag.Locked"))).Append("[/]");
        }

        return builder.ToString();
    }

    private static Color HealthColor(string? grade) => TuiFormats.HealthStatus(grade) switch
    {
        "OK" => TuiTheme.Success,
        "Warning" => TuiTheme.Warning,
        "Critical" => TuiTheme.Danger,
        _ => TuiTheme.Muted,
    };

    private static void RenderSessions(TuiContext context, DiskCardDetailState state)
    {
        ITuiIo io = context.Io;
        io.Line();
        io.Line($"[bold]{Markup.Escape(context.L("DiskCardDetailView.TestHistory"))}[/]");

        if (state.Sessions.Count == 0)
        {
            io.Line($"[grey]{Markup.Escape(context.L("History.NoTests"))}[/]");
            return;
        }

        int width = Math.Max(80, io.Width);
        int budget = Math.Max(24, width - DiskSelectionScreen.BordersAndPadding);
        bool compact = width < 104;

        // Widths are allocated from the available content width, so the table always
        // fits the terminal without wrapping a single cell.
        int[] sizes = compact
            ? DiskSelectionScreen.AllocateWidths(budget - CompactSessionFixedWidth, new[] { 11 }, new[] { 1.0 })
            : DiskSelectionScreen.AllocateWidths(budget - SessionFixedWidth, new[] { 11, 8 }, new[] { 0.4, 0.6 });

        var table = new Table
        {
            Border = TableBorder.Rounded,
            BorderStyle = new Style(foreground: TuiTheme.Muted),
        };

        table.AddColumn(new TableColumn(Header(context, "Status.Indicator")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.Date")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.TestType")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.Grade")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.Score")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.Errors")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.Temperature")) { NoWrap = true });
        if (!compact)
        {
            table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.Duration")) { NoWrap = true });
            table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.Notes")) { NoWrap = true });
        }

        int typeWidth = sizes[0];
        int notesWidth = compact ? 0 : sizes[1];
        string dateFormat = compact ? "dd.MM.yyyy" : "dd.MM.yyyy HH:mm";

        for (int index = 0; index < state.Sessions.Count; index++)
        {
            TestSession session = state.Sessions[index];
            bool selected = index == state.SelectedIndex;
            var cells = new List<IRenderable>
            {
                new Markup(selected ? $"[{TuiTheme.Primary.ToMarkup()}]▶[/]" : "[grey]·[/]"),
                new Markup(Cell(session.StartedAt.ToString(dateFormat, Invariant), 16)),
                new Markup(Cell(session.TestType.ToString(), typeWidth)),
                new Markup(TuiTheme.GradeMarkup(session.Grade)),
                new Markup(TuiTheme.ScoreMarkup(session.Score)),
                new Markup(Markup.Escape(session.ErrorCount.ToString(Invariant))),
                new Markup(TuiTheme.TemperatureMarkup(session.Temperature > 0 ? session.Temperature : null)),
            };

            if (!compact)
            {
                cells.Add(new Markup(Markup.Escape(TuiFormats.Duration(session.Duration))));
                cells.Add(new Markup(Cell(session.Notes, notesWidth)));
            }

            table.AddRow(cells);
        }

        io.Write(table);
    }

    private const int SessionFixedWidth = 1 + 16 + 6 + 5 + 5 + 7 + 8;
    private const int CompactSessionFixedWidth = 1 + 10 + 6 + 5 + 5 + 7;

    private static void RenderCertificates(TuiContext context, DiskCardDetailState state)
    {
        ITuiIo io = context.Io;
        io.Line();
        io.Line($"[bold]{Markup.Escape(context.L("DiskCardDetailView.Certificates"))}[/]");

        if (state.Certificates.Count == 0)
        {
            io.Line($"[grey]{Markup.Escape(context.L("Common.NotAvailable"))}[/]");
            return;
        }

        int width = Math.Max(80, io.Width);
        int budget = Math.Max(24, width - DiskSelectionScreen.BordersAndPadding);
        bool compact = width < 104;
        int[] sizes = compact
            ? DiskSelectionScreen.AllocateWidths(budget - CompactCertificateFixedWidth, new[] { 8 }, new[] { 1.0 })
            : DiskSelectionScreen.AllocateWidths(budget - CertificateFixedWidth, new[] { 8, 9 }, new[] { 0.6, 0.4 });

        var table = new Table
        {
            Border = TableBorder.Rounded,
            BorderStyle = new Style(foreground: TuiTheme.Muted),
        };

        table.AddColumn(new TableColumn(Header(context, "Tui.Column.CertificateNumber")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.Generated")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.Grade")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.Score")) { NoWrap = true });
        if (!compact)
        {
            table.AddColumn(new TableColumn(Header(context, "DiskCardDetailView.SmartSnapshot")) { NoWrap = true });
        }

        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Pdf")) { NoWrap = true });

        int numberWidth = sizes[0];
        int healthWidth = compact ? 0 : sizes[1];
        string dateFormat = compact ? "dd.MM.yyyy" : "dd.MM.yyyy HH:mm";

        foreach (DiskCertificate certificate in state.Certificates)
        {
            var cells = new List<IRenderable>
            {
                new Markup(Cell(certificate.CertificateNumber, numberWidth)),
                new Markup(Cell(certificate.GeneratedAt == default ? "N/A" : certificate.GeneratedAt.ToString(dateFormat, Invariant), 16)),
                new Markup(TuiTheme.GradeMarkup(certificate.Grade)),
                new Markup(TuiTheme.ScoreMarkup(certificate.Score)),
            };

            if (!compact)
            {
                cells.Add(new Markup(Cell(certificate.HealthStatus, healthWidth)));
            }

            cells.Add(new Markup(certificate.PdfGenerated ? "[green]✔[/]" : "[grey]–[/]"));
            table.AddRow(cells);
        }

        io.Write(table);
    }

    private const int CertificateFixedWidth = 16 + 6 + 5 + 3;
    private const int CompactCertificateFixedWidth = 10 + 6 + 5 + 3;

    private static string Header(TuiContext context, string key) =>
        "[grey]" + Markup.Escape(context.L(key)) + "[/]";

    private static string Cell(string? value, int width) =>
        string.IsNullOrWhiteSpace(value) ? "[grey]–[/]" : Markup.Escape(TuiFormats.Ellipsis(value, width));

    // ---------------------------------------------------------------- actions

    private static List<TuiChoice> BuildChoices(TuiContext context, DiskCardDetailState state)
    {
        var choices = new List<TuiChoice>();

        for (int index = 0; index < state.Sessions.Count; index++)
        {
            TestSession session = state.Sessions[index];
            choices.Add(new TuiChoice(
                SessionKeyPrefix + index.ToString(Invariant),
                string.Create(
                    Invariant,
                    $"{index + 1}. {session.StartedAt.ToString("dd.MM.yyyy HH:mm", Invariant)} · " +
                    $"{session.TestType} · {session.Grade} · {session.Score.ToString("F0", Invariant)}/100"),
                session.ErrorCount.ToString(Invariant) + "×"));
        }

        choices.Add(new TuiChoice(ActionSessionDetail, "🔬 " + context.L("Tui.DiskCardDetail.SessionDetail")));
        choices.Add(new TuiChoice(ActionCertificate, "🏅 " + context.L("DiskCardDetailView.GenerateCert")));
        choices.Add(new TuiChoice(ActionNotes, "📝 " + context.L("DiskCardDetailView.SaveNotes")));
        choices.Add(new TuiChoice(ActionRefresh, "🔄 " + context.L("Common.Refresh")));
        choices.Add(new TuiChoice(TuiKeys.MainMenu, "☰ " + context.L("Tui.Common.Menu")));
        choices.Add(new TuiChoice(TuiKeys.Back, context.L("Tui.DiskCardDetail.BackToCards")));
        return choices;
    }

    private static bool TryResolveSession(string choice, DiskCardDetailState state, out int index)
    {
        index = -1;
        if (!choice.StartsWith(SessionKeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(choice[SessionKeyPrefix.Length..], NumberStyles.Integer, Invariant, out int parsed))
        {
            return false;
        }

        if (parsed < 0 || parsed >= state.Sessions.Count)
        {
            return false;
        }

        index = parsed;
        return true;
    }

    private static void ShowSessionDetail(TuiContext context, DiskCardDetailState state)
    {
        if (state.SelectedSession is not { } session)
        {
            context.Dialogs.Warning(context.L("Common.NoHistory"), context.L("DiskCardDetail.Status.NoTestForCert"));
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine(context.L("DiskCardDetailView.TotalErrors") + ": " + session.ErrorCount.ToString(Invariant));
        builder.AppendLine(context.L("DiskCardDetailView.Duration") + ": " + TuiFormats.Duration(session.Duration));
        builder.AppendLine(context.L("DiskCardDetailView.Temperature") + ": " + TuiFormats.Temperature(session.Temperature));
        builder.AppendLine();

        SmartaData? smart = session.SmartBefore;
        if (smart is null)
        {
            builder.AppendLine(context.L("DiskCardDetail.Status.NoSmartSnapshot"));
        }
        else
        {
            builder.AppendLine(context.L("DiskCardDetailView.SmartSnapshot") + ":");
            builder.AppendLine("  " + context.L("DiskCardDetailView.Model") + ": " + (string.IsNullOrWhiteSpace(smart.DeviceModel) ? "N/A" : smart.DeviceModel));
            builder.AppendLine("  " + context.L("DiskCardDetailView.SerialNumber") + ": " + (string.IsNullOrWhiteSpace(smart.SerialNumber) ? "N/A" : smart.SerialNumber));
            builder.AppendLine("  " + context.L("DiskCardDetailView.Temperature") + ": " + TuiFormats.Temperature(smart.Temperature));
            builder.AppendLine("  " + context.L("DiskCardDetailView.PowerOnHours") + ": " + FormatOptional(smart.PowerOnHours));
            builder.AppendLine("  " + context.L("DiskCardDetailView.Reallocated") + ": " + FormatOptional(smart.ReallocatedSectorCount));
            builder.AppendLine("  " + context.L("DiskCardDetailView.Pending") + ": " + FormatOptional(smart.PendingSectorCount));
            builder.AppendLine("  " + context.L("DiskCardDetailView.Uncorrectable") + ": " + FormatOptional(smart.UncorrectableErrorCount));
            builder.AppendLine("  " + context.L("DiskCardDetailView.MediaErrors") + ": " + FormatOptional(smart.MediaErrors));
        }

        builder.AppendLine();
        AppendErrors(context, builder, session);

        context.Dialogs.Notice(
            context.L(
                "DiskCardDetail.Status.TestSelected",
                session.TestType.ToString(),
                session.StartedAt.ToString("dd.MM.yyyy HH:mm", Invariant)),
            builder.ToString().TrimEnd());
    }

    /// <summary>Error list of one session (desktop <c>BuildErrorSummary</c>).</summary>
    internal static void AppendErrors(TuiContext context, StringBuilder builder, TestSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(session);

        if (session.Errors.Count == 0)
        {
            builder.Append(context.L("DiskCardDetail.Status.NoErrorsForTest"));
            return;
        }

        bool first = true;
        foreach (TestError error in session.Errors.OrderBy(entry => entry.Timestamp).Take(10))
        {
            if (!first)
            {
                builder.AppendLine();
            }

            first = false;
            builder.Append('[').Append(error.Phase).Append("] ").Append(error.ErrorCode).Append(": ").Append(error.Message);
            if (!string.IsNullOrWhiteSpace(error.Details))
            {
                builder.AppendLine();
                builder.Append("  ").Append(error.Details);
            }
        }
    }

    private static async Task SaveNotesAsync(TuiContext context, DiskCardDetailState state)
    {
        if (state.Card is not { } card)
        {
            return;
        }

        string? note = context.Dialogs.Ask(context.L("DiskCardDetail.Dialog.NotePrompt"), card.Notes ?? string.Empty);
        if (note is null)
        {
            return;
        }

        try
        {
            await context.UseScopedAsync(async services =>
            {
                var repository = services.GetRequiredService<IDiskCardRepository>();
                DiskCard updated = await repository.GetByIdAsync(card.Id).ConfigureAwait(false) ?? card;
                updated.Notes = note;
                await repository.UpdateAsync(updated).ConfigureAwait(false);
            }).ConfigureAwait(false);

            card.Notes = note;
            context.StatusMessage = context.L("DiskCardDetail.Dialog.NoteSaved");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Dialogs.Error(context.L("Common.Error"), context.L("Common.CardSaveFailed") + " " + ex.Message);
        }
    }

    /// <summary>
    /// Generates (and saves) a certificate for the selected session while a live progress
    /// panel is displayed. On success the certificate screen is opened.
    /// </summary>
    private static async Task<ScreenResult?> GenerateCertificateAsync(
        TuiContext context,
        DiskCardDetailState state,
        CancellationToken cancellationToken)
    {
        if (state.SelectedSession is not { } session)
        {
            context.Dialogs.Error(context.L("Common.Error"), context.L("DiskCardDetail.Status.NoTestForCert"));
            return null;
        }

        string title = context.L("Tui.DiskCardDetail.Generating");
        CertificateExportResult? exported = null;

        using var navigationLock = context.LockNavigation(context.L("Common.OperationRunning"));
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progress = new TuiProgress(title) { ShowChart = false };

        await context.Io.RunLiveAsync(
            title,
            progress,
            async panel =>
            {
                var reporter = new CertificateProgressBridge(panel);
                exported = await context.UseScopedAsync(services =>
                    services.GetRequiredService<CertificateExportService>()
                        .ExportCertificateAsync(session.Id, reporter, operation.Token)).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        if (exported is null || !exported.IsSuccess)
        {
            context.Dialogs.Error(
                context.L("Common.Error"),
                context.L("DiskCardDetail.Error.GenerateCert", exported?.ErrorMessage ?? context.L("Common.Unknown")));
            return null;
        }

        string certificateNumber = exported.Certificate?.CertificateNumber ?? "N/A";
        context.Selection.SelectedTestSessionId = session.Id;
        context.Selection.SelectedCertificateId = exported.Certificate?.Id;
        context.StatusMessage = context.L("DiskCardDetail.Status.CertGenerated", certificateNumber);
        context.Dialogs.Success(
            context.L("DiskCardDetail.Dialog.Certificate"),
            context.L("DiskCardDetail.Status.CertGenerated", certificateNumber));
        return ScreenResult.Navigate(ScreenIds.Certificate);
    }

    private static string FormatOptional(int? value) =>
        value is null ? "N/A" : value.Value.ToString(Invariant);

    /// <summary>Bridges the exporter progress reports into the live terminal panel.</summary>
    private sealed class CertificateProgressBridge : IProgress<CertificateExportProgress>
    {
        private readonly TuiProgress _panel;

        public CertificateProgressBridge(TuiProgress panel) => _panel = panel;

        public void Report(CertificateExportProgress value)
        {
            if (value is null)
            {
                return;
            }

            _panel.Percent = value.ProgressPercent;
            _panel.Phase = value.Message;
            _panel.Detail = value.Message;
        }
    }

    /// <summary>Mutable state of one screen run.</summary>
    internal sealed class DiskCardDetailState
    {
        public DiskCard? Card { get; set; }

        public int? CardId { get; set; }

        public IReadOnlyList<TestSession> Sessions { get; set; } = Array.Empty<TestSession>();

        public IReadOnlyList<DiskCertificate> Certificates { get; set; } = Array.Empty<DiskCertificate>();

        public int SelectedIndex { get; set; } = -1;

        public string? LoadError { get; set; }

        public TestSession? SelectedSession =>
            SelectedIndex >= 0 && SelectedIndex < Sessions.Count ? Sessions[SelectedIndex] : null;
    }
}

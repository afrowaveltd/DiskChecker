using System.Globalization;
using System.Text;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Terminal report screen. Mirrors the desktop <c>ReportViewModel</c>: every test
/// session of every disk card becomes one report row (date, model, serial, test
/// type, score, grade, errors) with a free text search, KPI detail for the
/// selected row, and actions to write a full text report, export a certificate
/// PDF or delete the session.
/// </summary>
public sealed class ReportScreen : ITuiScreen
{
    private const string ActionSelect = "select";
    private const string ActionSearch = "search";
    private const string ActionRefresh = "refresh";
    private const string ActionFullReport = "fullreport";
    private const string ActionCertificate = "certificate";
    private const string ActionDelete = "delete";
    private const string RowKeyPrefix = "row:";

    private string _searchText = string.Empty;
    private int? _selectedSessionId;

    public string Id => ScreenIds.Report;

    public string TitleKey => "Nav.Report";

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (!cancellationToken.IsCancellationRequested)
        {
            List<ReportRow> rows;
            try
            {
                rows = await LoadRowsAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                context.Dialogs.Error(context.L("Common.Error"), context.L("Report.LoadFailed", ex.Message));
                rows = new List<ReportRow>();
            }

            var filtered = FilterRows(rows);
            Render(context, rows, filtered);

            string? choice = context.Io.Choose(context.L("Report.Prompt"), BuildActions(context));
            if (choice is null || choice == TuiKeys.Back)
            {
                return ScreenResult.Back();
            }

            if (choice == TuiKeys.MainMenu)
            {
                return ScreenResult.Menu();
            }

            if (choice == TuiKeys.Exit)
            {
                return ScreenResult.Exit();
            }

            if (choice == ActionSelect)
            {
                SelectRow(context, filtered);
                continue;
            }

            if (choice == ActionSearch)
            {
                string? text = context.Io.Ask(context.L("Report.Search.Prompt"), _searchText);
                _searchText = text?.Trim() ?? string.Empty;
                context.StatusMessage = DescribeFilter(context, rows.Count);
                continue;
            }

            if (choice == ActionFullReport)
            {
                await WriteFullReportAsync(context, rows, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (choice == ActionCertificate)
            {
                await ExportCertificateAsync(context, rows, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (choice == ActionDelete)
            {
                await DeleteSelectedAsync(context, rows).ConfigureAwait(false);
            }
        }

        return ScreenResult.Back();
    }

    private static async Task<List<ReportRow>> LoadRowsAsync(TuiContext context, CancellationToken cancellationToken)
    {
        return await context.UseScopedAsync(async services =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repository = services.GetRequiredService<IDiskCardRepository>();

            var rows = new List<ReportRow>();
            var cards = await repository.GetAllAsync().ConfigureAwait(false);
            foreach (var card in cards)
            {
                var sessions = await repository.GetTestSessionsAsync(card.Id).ConfigureAwait(false);
                foreach (var session in sessions)
                {
                    rows.Add(new ReportRow
                    {
                        TestSessionId = session.Id,
                        CardId = card.Id,
                        TestDate = session.StartedAt,
                        DateText = FormatDate(session.StartedAt),
                        Model = card.ModelName,
                        Serial = card.SerialNumber,
                        TestType = session.TestType.ToString(),
                        Grade = session.Grade,
                        Score = session.Score,
                        Errors = session.ErrorCount,
                        Result = session.Result,
                        Duration = session.Duration,
                        BytesWritten = session.BytesWritten,
                        BytesRead = session.BytesRead,
                        AverageWrite = session.AverageWriteSpeedMBps,
                        MinWrite = session.MinWriteSpeedMBps,
                        MaxWrite = session.MaxWriteSpeedMBps,
                        AverageRead = session.AverageReadSpeedMBps,
                        MinRead = session.MinReadSpeedMBps,
                        MaxRead = session.MaxReadSpeedMBps,
                        WriteErrors = session.WriteErrors,
                        ReadErrors = session.ReadErrors,
                        VerificationErrors = session.VerificationErrors,
                        StartTemperature = session.StartTemperature,
                        MaxTemperature = session.MaxTemperature,
                        AverageTemperature = session.AverageTemperature,
                    });
                }
            }

            return rows.OrderByDescending(row => row.TestDate).ToList();
        }).ConfigureAwait(false);
    }

    private static string FormatDate(DateTime value) =>
        value.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private List<ReportRow> FilterRows(IReadOnlyList<ReportRow> rows)
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return rows.ToList();
        }

        return rows
            .Where(row => Matches(row, _searchText))
            .ToList();
    }

    private static bool Matches(ReportRow row, string search)
    {
        return row.Model.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Serial.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.TestType.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Grade.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.DateText.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private string DescribeFilter(TuiContext context, int total) =>
        string.IsNullOrWhiteSpace(_searchText)
            ? context.L("Report.Status.Loaded", total.ToString(CultureInfo.InvariantCulture))
            : context.L("Report.Filter", _searchText);

    private void Render(TuiContext context, IReadOnlyList<ReportRow> all, IReadOnlyList<ReportRow> filtered)
    {
        var io = context.Io;
        string filter = string.IsNullOrWhiteSpace(_searchText)
            ? context.L("Report.Filter.None")
            : context.L("Report.Filter", _searchText);
        io.Line($"[grey]{Markup.Escape(context.L("Report.Filter.Label", filter))}[/]");

        if (all.Count == 0)
        {
            io.Write(new Panel(new Markup(Markup.Escape(context.L("Report.Empty"))))
            {
                Border = BoxBorder.Rounded,
                Expand = true,
                BorderStyle = new Style(foreground: TuiTheme.Muted),
                Header = new PanelHeader($"[bold]{Markup.Escape(context.L("Report.Title"))}[/]", Justify.Left),
                Padding = new Padding(1, 0, 1, 0),
            });
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Expand();

        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("Report.Column.Date"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("Report.Column.Model"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("Report.Column.Serial"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("Report.Column.Type"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("Report.Column.Score"))}[/]") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("Report.Column.Grade"))}[/]") { Alignment = Justify.Center });
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("Report.Column.Errors"))}[/]") { Alignment = Justify.Right });

        int modelWidth = Math.Max(10, (io.Width - 76) / 2);
        foreach (var row in filtered)
        {
            table.AddRow(
                Markup.Escape(row.DateText),
                Markup.Escape(TuiFormats.Ellipsis(row.Model, modelWidth)),
                Markup.Escape(TuiFormats.Ellipsis(row.Serial, 18)),
                Markup.Escape(row.TestType),
                Markup.Escape(row.Score.ToString("F0", CultureInfo.InvariantCulture)),
                TuiTheme.GradeMarkup(row.Grade),
                Markup.Escape(row.Errors.ToString(CultureInfo.InvariantCulture)));
        }

        io.Write(table);

        double average = filtered.Count == 0 ? 0 : filtered.Average(row => row.Score);
        int errors = filtered.Sum(row => row.Errors);
        io.Line($"[grey]{Markup.Escape(context.L("Report.Summary",
            filtered.Count.ToString(CultureInfo.InvariantCulture),
            average.ToString("F1", CultureInfo.InvariantCulture),
            errors.ToString(CultureInfo.InvariantCulture)))}[/]");

        var selected = FindSelected(all);
        if (selected is null)
        {
            io.Line($"[grey]{Markup.Escape(context.L("Report.NoSelection"))}[/]");
            return;
        }

        RenderKpi(context, selected);
    }

    private static void RenderKpi(TuiContext context, ReportRow row)
    {
        var builder = new List<string>
        {
            context.L("Report.Kpi.Result", row.Result.ToString(), row.Grade, row.Score.ToString("F0", CultureInfo.InvariantCulture)),
            context.L(
                "Report.Kpi.Write",
                TuiFormats.Speed(row.AverageWrite),
                TuiFormats.Speed(row.MinWrite),
                TuiFormats.Speed(row.MaxWrite)),
            context.L(
                "Report.Kpi.Read",
                TuiFormats.Speed(row.AverageRead),
                TuiFormats.Speed(row.MinRead),
                TuiFormats.Speed(row.MaxRead)),
            context.L("Report.Kpi.Data", TuiFormats.Bytes(row.BytesWritten), TuiFormats.Bytes(row.BytesRead)),
            context.L("Report.Kpi.Duration", TuiFormats.Duration(row.Duration)),
            context.L(
                "Report.Kpi.Temperature",
                TuiFormats.Temperature(row.StartTemperature),
                row.AverageTemperature is null
                    ? "—"
                    : TuiFormats.Temperature((int)Math.Round(row.AverageTemperature.Value)),
                TuiFormats.Temperature(row.MaxTemperature)),
            context.L(
                "Report.Kpi.Errors",
                row.WriteErrors.ToString(CultureInfo.InvariantCulture),
                row.ReadErrors.ToString(CultureInfo.InvariantCulture),
                row.VerificationErrors.ToString(CultureInfo.InvariantCulture)),
        };

        var panel = new Panel(new Markup(Markup.Escape(string.Join(Environment.NewLine, builder))))
        {
            Border = BoxBorder.Rounded,
            Expand = true,
            BorderStyle = new Style(foreground: TuiTheme.Primary),
            Header = new PanelHeader($"[bold]{Markup.Escape(context.L("Report.Kpi"))}[/]", Justify.Left),
            Padding = new Padding(1, 0, 1, 0),
        };

        context.Io.Write(panel);
    }

    private static List<TuiChoice> BuildActions(TuiContext context) => new()
    {
        new TuiChoice(ActionSelect, "▶ " + context.L("Report.Action.Select")),
        new TuiChoice(ActionSearch, "🔍 " + context.L("Report.Action.Search")),
        new TuiChoice(ActionRefresh, "🔄 " + context.L("Common.Refresh")),
        new TuiChoice(ActionFullReport, "📄 " + context.L("Report.Action.FullReport")),
        new TuiChoice(ActionCertificate, "🏅 " + context.L("Report.Action.Certificate")),
        new TuiChoice(ActionDelete, "🗑 " + context.L("Common.Delete")),
        new TuiChoice(TuiKeys.MainMenu, "☰ " + context.L("Common.Navigation")),
        new TuiChoice(TuiKeys.Back, context.L("Common.Back")),
    };

    private void SelectRow(TuiContext context, IReadOnlyList<ReportRow> filtered)
    {
        if (filtered.Count == 0)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("Report.Empty"));
            return;
        }

        var choices = new List<TuiChoice>(filtered.Count);
        for (int index = 0; index < filtered.Count; index++)
        {
            var row = filtered[index];
            choices.Add(new TuiChoice(
                RowKeyPrefix + index.ToString(CultureInfo.InvariantCulture),
                Describe(row)));
        }

        string? selected = context.Io.Choose(context.L("Report.Picker"), choices, pageSize: 18);
        if (selected is null || !selected.StartsWith(RowKeyPrefix, StringComparison.Ordinal))
        {
            return;
        }

        string raw = selected[RowKeyPrefix.Length..];
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int position)
            || position < 0
            || position >= filtered.Count)
        {
            return;
        }

        _selectedSessionId = filtered[position].TestSessionId;
        context.StatusMessage = context.L("Report.Selected", Describe(filtered[position]));
    }

    private ReportRow? FindSelected(IReadOnlyList<ReportRow> rows)
    {
        if (_selectedSessionId is null)
        {
            return null;
        }

        foreach (var row in rows)
        {
            if (row.TestSessionId == _selectedSessionId.Value)
            {
                return row;
            }
        }

        return null;
    }

    private static string Describe(ReportRow row) => string.Create(
        CultureInfo.InvariantCulture,
        $"{row.DateText} · {row.Model} · {row.TestType} · {row.Grade}");

    private async Task WriteFullReportAsync(TuiContext context, IReadOnlyList<ReportRow> rows, CancellationToken cancellationToken)
    {
        var row = FindSelected(rows);
        if (row is null)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("Report.NoSelection"));
            return;
        }

        string title = context.L("Report.Action.FullReport");
        var progress = new TuiProgress(title);
        progress.AddPhase(context.L("Report.Phase.Load"));
        progress.AddPhase(context.L("Report.Phase.Compose"));
        progress.AddPhase(context.L("Report.Phase.Write"));
        string? savedPath = null;
        string? failure = null;

        using (context.LockNavigation(context.L("Report.Generating")))
        {
            await context.Io.RunLiveAsync(title, progress, async live =>
            {
                live.Percent = 10;
                live.Phase = context.L("Report.Phase.Load");
                live.SetPhaseState(0, TuiPhaseState.Running);

                var report = await context.UseScopedAsync(async services =>
                {
                    var repository = services.GetRequiredService<IDiskCardRepository>();
                    var card = await repository.GetByIdAsync(row.CardId).ConfigureAwait(false);
                    var session = await repository.GetTestSessionWithoutSamplesAsync(row.TestSessionId).ConfigureAwait(false)
                        ?? await repository.GetTestSessionAsync(row.TestSessionId).ConfigureAwait(false);

                    if (card is null || session is null)
                    {
                        return (Text: (string?)null, Card: (DiskCard?)null, Session: (TestSession?)null);
                    }

                    string text = BuildFullReportText(context, services, session, card);
                    return (Text: text, Card: card, Session: session);
                }).ConfigureAwait(false);

                live.SetPhaseState(0, TuiPhaseState.Completed);
                if (report.Text is null || report.Card is null || report.Session is null)
                {
                    live.SetPhaseState(2, TuiPhaseState.Failed);
                    failure = context.L("Report.NotAvailable");
                    return;
                }

                live.Percent = 60;
                live.Phase = context.L("Report.Phase.Compose");
                live.SetPhaseState(1, TuiPhaseState.Completed);
                live.Percent = 80;
                live.Phase = context.L("Report.Phase.Write");

                savedPath = await SaveReportFileAsync(context, report.Text, report.Card.Id, report.Session.Id).ConfigureAwait(false);
                live.SetPhaseState(2, savedPath is null ? TuiPhaseState.Failed : TuiPhaseState.Completed);
                live.Percent = 100;
                live.Phase = context.L("Report.Phase.Done");
            }, cancellationToken).ConfigureAwait(false);
        }

        if (savedPath is not null)
        {
            context.StatusMessage = context.L("Report.Status.Saved", savedPath);
            context.Dialogs.Success(context.L("Report.FullReportReady"), context.L("Report.SavedTo", savedPath));
            return;
        }

        context.StatusMessage = failure ?? context.L("Common.SaveFailed") + " " + title;
        context.Dialogs.Error(context.L("Common.Error"), failure ?? context.L("Report.GenerateError", title));
    }

    private static string BuildFullReportText(TuiContext context, IServiceProvider services, TestSession session, DiskCard card)
    {
        ReportGenerationService? generation = null;
        try
        {
            generation = services.GetService<ReportGenerationService>();
        }
        catch (InvalidOperationException)
        {
            generation = null;
        }

        if (generation is not null)
        {
            try
            {
                string generated = generation.GenerateTerminalReport(session, ToDriveInfo(card));
                if (!string.IsNullOrWhiteSpace(generated))
                {
                    return generated;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException)
            {
                // The terminal report generator needs the full analysis pipeline;
                // when it is unavailable the readable text report is written instead.
            }
        }

        return BuildTextReport(context, session, card);
    }

    private static CoreDriveInfo ToDriveInfo(DiskCard card) => new()
    {
        Path = card.DevicePath,
        Name = card.ModelName,
        Model = card.ModelName,
        TotalSize = card.Capacity,
        SerialNumber = card.SerialNumber,
        FirmwareVersion = card.FirmwareVersion,
        Interface = card.InterfaceType,
    };

    private static string BuildTextReport(TuiContext context, TestSession session, DiskCard card)
    {
        var builder = new StringBuilder();
        builder.AppendLine(context.L("Report.Text.Header"));
        builder.AppendLine(new string('=', 62));
        builder.AppendLine(context.L("Report.Text.Disk", card.ModelName, card.SerialNumber));
        builder.AppendLine(context.L("Report.Text.Test", session.TestType.ToString()));
        builder.AppendLine(context.L("Report.Text.Date", FormatDate(session.StartedAt)));
        builder.AppendLine(context.L(
            "Report.Text.Rating",
            session.Grade,
            session.Score.ToString("F0", CultureInfo.InvariantCulture)));
        builder.AppendLine(context.L("Report.Text.Result", session.Result.ToString()));
        builder.AppendLine(context.L("Report.Text.Duration", TuiFormats.Duration(session.Duration)));
        builder.AppendLine(context.L(
            "Report.Text.Speeds",
            TuiFormats.Speed(session.AverageWriteSpeedMBps),
            TuiFormats.Speed(session.AverageReadSpeedMBps)));
        builder.AppendLine(context.L(
            "Report.Text.Errors",
            session.ErrorCount.ToString(CultureInfo.InvariantCulture)));
        builder.AppendLine(context.L(
            "Report.Text.Temperature",
            TuiFormats.Temperature(session.StartTemperature),
            TuiFormats.Temperature(session.MaxTemperature)));

        if (!string.IsNullOrWhiteSpace(session.Notes))
        {
            builder.AppendLine(context.L("Report.Text.Notes", session.Notes));
        }

        builder.AppendLine();
        builder.AppendLine(context.L(
            "Report.Text.Generated",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        return builder.ToString();
    }

    private static async Task<string?> SaveReportFileAsync(TuiContext context, string text, int cardId, int sessionId)
    {
        string fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"Report_{cardId}_{sessionId}_{DateTime.UtcNow:yyyyMMddHHmmss}.txt");

        foreach (string directory in await CandidateDirectoriesAsync(context).ConfigureAwait(false))
        {
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, fileName);
                await File.WriteAllTextAsync(path, text).ConfigureAwait(false);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Try the next candidate directory (default export path, then temp).
            }
        }

        return null;
    }

    private static async Task<List<string>> CandidateDirectoriesAsync(TuiContext context)
    {
        var directories = new List<string>();
        string? configured = null;
        try
        {
            var settings = context.GetRequiredService<ISettingsService>();
            configured = await settings.GetDefaultExportPathAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            configured = null;
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            directories.Add(configured);
        }

        directories.Add(Path.GetTempPath());
        return directories;
    }

    private async Task ExportCertificateAsync(TuiContext context, IReadOnlyList<ReportRow> rows, CancellationToken cancellationToken)
    {
        var row = FindSelected(rows);
        if (row is null)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("Report.NoSelection"));
            return;
        }

        string title = context.L("Report.Action.Certificate");
        var progress = new TuiProgress(title);
        progress.AddPhase(context.L("Report.Phase.Certificate"));
        progress.AddPhase(context.L("Report.Phase.Pdf"));
        CertificateExportResult? result = null;

        using (context.LockNavigation(context.L("Report.Status.Exporting")))
        {
            await context.Io.RunLiveAsync(title, progress, async live =>
            {
                live.Phase = context.L("Report.Phase.Certificate");
                live.SetPhaseState(0, TuiPhaseState.Running);
                var reporter = new ExportProgress(live);

                result = await context.UseScopedAsync(async services =>
                {
                    var export = services.GetRequiredService<CertificateExportService>();
                    return await export.ExportCertificateAsync(row.TestSessionId, reporter, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                live.SetPhaseState(0, result.IsSuccess ? TuiPhaseState.Completed : TuiPhaseState.Failed);
                live.SetPhaseState(1, result.IsSuccess ? TuiPhaseState.Completed : TuiPhaseState.Skipped);
                live.Percent = 100;
            }, cancellationToken).ConfigureAwait(false);
        }

        if (result is null)
        {
            context.Dialogs.Error(context.L("Common.Error"), context.L("Report.NotAvailable"));
            return;
        }

        if (!result.IsSuccess)
        {
            context.StatusMessage = context.L("Common.Error") + ": " + (result.ErrorMessage ?? string.Empty);
            context.Dialogs.Error(context.L("Common.Error"), result.ErrorMessage ?? context.L("Report.NotAvailable"));
            return;
        }

        string number = result.Certificate?.CertificateNumber ?? "—";
        context.StatusMessage = context.L("Report.CertificatePdfSaved", result.PdfPath ?? "—");
        context.Dialogs.Success(context.L("Report.CertificateCreated", number), context.StatusMessage);
    }

    private async Task DeleteSelectedAsync(TuiContext context, IReadOnlyList<ReportRow> rows)
    {
        var row = FindSelected(rows);
        if (row is null)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("Report.NoSelection"));
            return;
        }

        bool confirmed = context.Dialogs.Confirm(
            context.L("Common.Confirmation"),
            context.L("Report.DeleteConfirmMessage") + " " + Describe(row),
            danger: true);
        if (!confirmed)
        {
            return;
        }

        using (context.LockNavigation(context.L("Report.Status.Deleting")))
        {
            await context.UseScopedAsync(async services =>
            {
                var repository = services.GetRequiredService<IDiskCardRepository>();
                await repository.DeleteTestSessionAsync(row.TestSessionId).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        _selectedSessionId = null;
        context.StatusMessage = context.L("Report.Status.Deleted");
        context.Dialogs.Success(context.L("Common.Confirmation"), context.L("Report.Status.Deleted"));
    }

    private sealed class ExportProgress : IProgress<CertificateExportProgress>
    {
        private readonly TuiProgress _progress;

        public ExportProgress(TuiProgress progress) => _progress = progress;

        public void Report(CertificateExportProgress value)
        {
            if (value is null)
            {
                return;
            }

            _progress.Percent = value.ProgressPercent;
            _progress.Detail = value.Message;
        }
    }

    private sealed class ReportRow
    {
        public int TestSessionId { get; init; }

        public int CardId { get; init; }

        public DateTime TestDate { get; init; }

        public string DateText { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public string Serial { get; init; } = string.Empty;

        public string TestType { get; init; } = string.Empty;

        public string Grade { get; init; } = "?";

        public double Score { get; init; }

        public int Errors { get; init; }

        public TestResult Result { get; init; }

        public TimeSpan Duration { get; init; }

        public long BytesWritten { get; init; }

        public long BytesRead { get; init; }

        public double AverageWrite { get; init; }

        public double MinWrite { get; init; }

        public double MaxWrite { get; init; }

        public double AverageRead { get; init; }

        public double MinRead { get; init; }

        public double MaxRead { get; init; }

        public int WriteErrors { get; init; }

        public int ReadErrors { get; init; }

        public int VerificationErrors { get; init; }

        public int? StartTemperature { get; init; }

        public int? MaxTemperature { get; init; }

        public double? AverageTemperature { get; init; }
    }
}

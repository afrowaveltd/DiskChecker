using System.Globalization;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Terminal history screen. Mirrors the desktop <c>HistoryViewModel</c>: it lists
/// every finished test report with its date, disk, test type, grade, score and
/// error count, filters the rows by test type, and offers delete / clear / detail
/// actions. Detail opens the certificate screen when the serial number resolves a
/// disk card, otherwise it renders a text detail panel.
/// </summary>
public sealed class HistoryScreen : ITuiScreen
{
    private const string ActionSelect = "select";
    private const string ActionFilter = "filter";
    private const string ActionRefresh = "refresh";
    private const string ActionDelete = "delete";
    private const string ActionClear = "clear";
    private const string ActionDetail = "detail";
    private const string RowKeyPrefix = "row:";

    private int _filterIndex;
    private Guid? _selectedReportId;

    public string Id => ScreenIds.History;

    public string TitleKey => "Nav.History";

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (!cancellationToken.IsCancellationRequested)
        {
            List<HistoryRow> rows;
            try
            {
                rows = await LoadRowsAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                context.Dialogs.Error(context.L("Common.Error"), context.L("History.LoadTestsError", ex.Message));
                rows = new List<HistoryRow>();
            }

            var filtered = FilterRows(rows);
            Render(context, rows, filtered);

            string? choice = context.Io.Choose(context.L("History.Prompt"), BuildActions(context));
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

            if (choice == ActionFilter)
            {
                ChooseFilter(context);
                continue;
            }

            if (choice == ActionDelete)
            {
                await DeleteSelectedAsync(context, rows).ConfigureAwait(false);
                continue;
            }

            if (choice == ActionClear)
            {
                await ClearAllAsync(context).ConfigureAwait(false);
                continue;
            }

            if (choice == ActionDetail)
            {
                ScreenResult? navigation = await OpenDetailAsync(context, rows, cancellationToken).ConfigureAwait(false);
                if (navigation is not null)
                {
                    return navigation.Value;
                }
            }
        }

        return ScreenResult.Back();
    }

    private static async Task<List<HistoryRow>> LoadRowsAsync(TuiContext context, CancellationToken cancellationToken)
    {
        return await context.UseScopedAsync(async services =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var testHistory = services.GetRequiredService<TestHistoryService>();
            var legacyHistory = services.GetRequiredService<HistoryService>();
            var repository = services.GetRequiredService<IDiskCardRepository>();

            var reports = await testHistory.GetAllTestReportsAsync().ConfigureAwait(false);
            if (reports.Count == 0)
            {
                var historical = await legacyHistory.GetHistoryAsync(cancellationToken).ConfigureAwait(false);
                return historical
                    .Select(BuildLegacyRow)
                    .OrderByDescending(row => row.TestDate)
                    .ToList();
            }

            var sessions = await LoadSessionIndexAsync(repository).ConfigureAwait(false);
            var rows = new List<HistoryRow>(reports.Count);
            foreach (var report in reports)
            {
                sessions.TryGetValue(report.ReportId, out SessionInfo? session);
                rows.Add(new HistoryRow
                {
                    ReportId = report.ReportId,
                    TestDate = report.TestDate,
                    DateText = FormatDate(report.TestDate),
                    Disk = report.DriveModel,
                    TestType = report.TestType,
                    Grade = report.Grade,
                    Score = report.Score,
                    Errors = report.Errors,
                    SerialNumber = report.SerialNumber,
                    TestSessionId = session?.SessionId,
                    CertificateId = session?.CertificateId,
                    Health = TuiFormats.HealthStatus(report.Grade),
                    DurationText = "—",
                    Notes = null,
                });
            }

            return rows;
        }).ConfigureAwait(false);
    }

    private static async Task<Dictionary<Guid, SessionInfo>> LoadSessionIndexAsync(IDiskCardRepository repository)
    {
        var index = new Dictionary<Guid, SessionInfo>();
        var cards = await repository.GetAllAsync().ConfigureAwait(false);
        foreach (var card in cards)
        {
            var sessions = await repository.GetTestSessionsAsync(card.Id).ConfigureAwait(false);
            foreach (var session in sessions)
            {
                index[session.SessionId] = new SessionInfo(session.Id, card.Id, session.CertificateId);
            }
        }

        return index;
    }

    private static HistoryRow BuildLegacyRow(HistoricalTest test)
    {
        return new HistoryRow
        {
            ReportId = test.Id,
            TestDate = test.TestDate,
            DateText = FormatDate(test.TestDate),
            Disk = test.DiskName,
            TestType = test.TestType,
            Grade = string.IsNullOrWhiteSpace(test.Grade) ? "?" : test.Grade,
            Score = test.Score,
            Errors = test.ErrorCount,
            SerialNumber = test.SerialNumber,
            TestSessionId = null,
            CertificateId = null,
            Health = string.IsNullOrWhiteSpace(test.HealthAssessment) ? "Unknown" : test.HealthAssessment,
            DurationText = TuiFormats.Duration(TimeSpan.FromSeconds(test.Duration)),
            Notes = test.Notes,
        };
    }

    private static string FormatDate(DateTime value) =>
        value.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private List<HistoryRow> FilterRows(IReadOnlyList<HistoryRow> rows)
    {
        IEnumerable<HistoryRow> filtered = _filterIndex switch
        {
            1 => rows.Where(row => IsSurfaceType(row.TestType)),
            2 => rows.Where(row => IsSmartType(row.TestType)),
            3 => rows.Where(row => IsSanitizationType(row.TestType)),
            _ => rows,
        };

        return filtered.ToList();
    }

    private static bool IsSurfaceType(string? testType)
    {
        if (string.IsNullOrWhiteSpace(testType))
        {
            return false;
        }

        foreach (string candidate in SurfaceTestTypes)
        {
            if (testType.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] SurfaceTestTypes =
    {
        "QuickRead", "QuickWrite", "FullRead", "FullWrite", "FullReadWrite", "SurfaceScan",
    };

    private static readonly string[] SmartTestTypes = { "SmartShort", "SmartExtended", "SmartConveyance" };

    private static bool IsSmartType(string? testType)
    {
        if (string.IsNullOrWhiteSpace(testType))
        {
            return false;
        }

        foreach (string candidate in SmartTestTypes)
        {
            if (testType.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSanitizationType(string? testType) =>
        !string.IsNullOrWhiteSpace(testType) && testType.Equals("Sanitization", StringComparison.OrdinalIgnoreCase);

    private void Render(TuiContext context, IReadOnlyList<HistoryRow> all, IReadOnlyList<HistoryRow> filtered)
    {
        var io = context.Io;
        string filterLabel = FilterLabel(context);
        io.Line($"[grey]{Markup.Escape(context.L("History.Filter.Label", filterLabel))}[/]");

        if (all.Count == 0)
        {
            io.Write(new Panel(new Markup(Markup.Escape(context.L("History.Empty"))))
            {
                Border = BoxBorder.Rounded,
                Expand = true,
                BorderStyle = new Style(foreground: TuiTheme.Muted),
                Header = new PanelHeader($"[bold]{Markup.Escape(context.L("History.Title"))}[/]", Justify.Left),
                Padding = new Padding(1, 0, 1, 0),
            });
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Expand();

        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("History.Column.Date"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("History.Column.Disk"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("History.Column.Type"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("History.Column.Grade"))}[/]") { Alignment = Justify.Center });
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("History.Column.Score"))}[/]") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("History.Column.Errors"))}[/]") { Alignment = Justify.Right });

        int diskWidth = Math.Max(12, io.Width - 58);
        foreach (var row in filtered)
        {
            table.AddRow(
                Markup.Escape(row.DateText),
                Markup.Escape(TuiFormats.Ellipsis(row.Disk, diskWidth)),
                Markup.Escape(row.TestType),
                TuiTheme.GradeMarkup(row.Grade),
                Markup.Escape(row.Score.ToString(CultureInfo.InvariantCulture)),
                Markup.Escape(row.Errors.ToString(CultureInfo.InvariantCulture)));
        }

        io.Write(table);

        double average = filtered.Count == 0 ? 0 : filtered.Average(row => (double)row.Score);
        int errors = filtered.Sum(row => row.Errors);
        io.Line($"[grey]{Markup.Escape(context.L("History.Summary",
            filtered.Count.ToString(CultureInfo.InvariantCulture),
            average.ToString("F1", CultureInfo.InvariantCulture),
            errors.ToString(CultureInfo.InvariantCulture)))}[/]");

        var selected = FindSelected(all);
        if (selected is not null)
        {
            io.Line($"[{TuiTheme.Primary.ToMarkup()}]{Markup.Escape(context.L("History.Selected", Describe(selected)))}[/]");
        }
    }

    private string FilterLabel(TuiContext context) => _filterIndex switch
    {
        1 => context.L("History.Filter.Surface"),
        2 => context.L("History.Filter.Smart"),
        3 => context.L("History.Filter.Sanitize"),
        _ => context.L("History.Filter.All"),
    };

    private static string Describe(HistoryRow row) => string.Create(
        CultureInfo.InvariantCulture,
        $"{row.DateText} · {row.Disk} · {row.TestType} · {row.Grade}");

    private static List<TuiChoice> BuildActions(TuiContext context) => new()
    {
        new TuiChoice(ActionSelect, "▶ " + context.L("History.Action.Select")),
        new TuiChoice(ActionFilter, "🎚 " + context.L("History.Action.Filter")),
        new TuiChoice(ActionRefresh, "🔄 " + context.L("Common.Refresh")),
        new TuiChoice(ActionDelete, "🗑 " + context.L("Common.Delete")),
        new TuiChoice(ActionClear, "🧹 " + context.L("History.Action.ClearAll")),
        new TuiChoice(ActionDetail, "🔎 " + context.L("History.Action.Detail")),
        new TuiChoice(TuiKeys.MainMenu, "☰ " + context.L("Common.Navigation")),
        new TuiChoice(TuiKeys.Back, context.L("Common.Back")),
    };

    private void ChooseFilter(TuiContext context)
    {
        var choices = new List<TuiChoice>
        {
            new("all", context.L("History.Filter.All")),
            new("surface", context.L("History.Filter.Surface")),
            new("smart", context.L("History.Filter.Smart")),
            new("sanitize", context.L("History.Filter.Sanitize")),
        };

        string? selected = context.Io.Choose(context.L("History.Filter.Title"), choices);
        _filterIndex = selected switch
        {
            "surface" => 1,
            "smart" => 2,
            "sanitize" => 3,
            _ => 0,
        };
    }

    private void SelectRow(TuiContext context, IReadOnlyList<HistoryRow> filtered)
    {
        if (filtered.Count == 0)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("History.Empty"));
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

        string? selected = context.Io.Choose(context.L("History.Picker"), choices, pageSize: 18);
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

        _selectedReportId = filtered[position].ReportId;
    }

    private HistoryRow? FindSelected(IReadOnlyList<HistoryRow> rows)
    {
        if (_selectedReportId is null)
        {
            return null;
        }

        foreach (var row in rows)
        {
            if (row.ReportId == _selectedReportId.Value)
            {
                return row;
            }
        }

        return null;
    }

    private async Task DeleteSelectedAsync(TuiContext context, IReadOnlyList<HistoryRow> rows)
    {
        var row = FindSelected(rows);
        if (row is null)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("History.NoSelection"));
            return;
        }

        bool confirmed = context.Dialogs.Confirm(
            context.L("Common.Confirmation"),
            context.L("History.DeleteConfirmMessage") + " " + Describe(row),
            danger: true);
        if (!confirmed)
        {
            return;
        }

        bool deleted;
        using (context.LockNavigation(context.L("History.Status.Deleting")))
        {
            deleted = await context.UseScopedAsync(async services =>
            {
                if (row.TestSessionId is int sessionId)
                {
                    var history = services.GetRequiredService<TestHistoryService>();
                    return await history.DeleteTestReportAsync(sessionId).ConfigureAwait(false);
                }

                var legacy = services.GetRequiredService<HistoryService>();
                await legacy.DeleteHistoryAsync(row.ReportId).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        if (deleted)
        {
            _selectedReportId = null;
            context.StatusMessage = context.L("History.Status.Deleted");
            context.Dialogs.Success(context.L("Common.Confirmation"), context.L("History.Status.Deleted"));
            return;
        }

        context.Dialogs.Error(context.L("Common.Error"), context.L("History.Status.DeleteError", "-"));
    }

    private async Task ClearAllAsync(TuiContext context)
    {
        bool confirmed = context.Dialogs.Confirm(
            context.L("History.ClearTitle"),
            context.L("History.ClearConfirmMessage"),
            danger: true);
        if (!confirmed)
        {
            return;
        }

        int removed;
        using (context.LockNavigation(context.L("History.Status.Clearing")))
        {
            removed = await context.UseScopedAsync(async services =>
            {
                var legacy = services.GetRequiredService<HistoryService>();
                var before = await legacy.GetHistoryAsync().ConfigureAwait(false);
                int count = before.Count();
                await legacy.ClearHistoryAsync().ConfigureAwait(false);
                return count;
            }).ConfigureAwait(false);
        }

        _selectedReportId = null;
        context.StatusMessage = context.L("History.Cleared", removed.ToString(CultureInfo.InvariantCulture));
        context.Dialogs.Success(context.L("History.ClearTitle"), context.StatusMessage);
    }

    private async Task<ScreenResult?> OpenDetailAsync(
        TuiContext context,
        IReadOnlyList<HistoryRow> rows,
        CancellationToken cancellationToken)
    {
        var row = FindSelected(rows);
        if (row is null)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("History.NoSelection"));
            return null;
        }

        var resolved = await context.UseScopedAsync(async services =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repository = services.GetRequiredService<IDiskCardRepository>();
            DiskCard? card = null;
            if (!string.IsNullOrWhiteSpace(row.SerialNumber))
            {
                card = await repository.GetBySerialNumberAsync(row.SerialNumber).ConfigureAwait(false);
            }

            if (card is null)
            {
                return new ResolvedTarget(null, null, null);
            }

            var sessions = await repository.GetTestSessionsAsync(card.Id).ConfigureAwait(false);
            var closest = sessions
                .OrderBy(session => Math.Abs((session.StartedAt - row.TestDate).Ticks))
                .FirstOrDefault();

            return new ResolvedTarget(card, closest?.Id, closest?.CertificateId);
        }).ConfigureAwait(false);

        if (resolved.Card is not null)
        {
            context.Selection.SelectedDiskCardId = resolved.Card.Id;
            context.Selection.SelectedTestSessionId = resolved.SessionId;
            context.Selection.SelectedCertificateId = resolved.CertificateId;
            context.StatusMessage = context.L("History.Status.DetailShown");
            return ScreenResult.Navigate(ScreenIds.Certificate);
        }

        RenderDetailPanel(context, row);
        return null;
    }

    private static void RenderDetailPanel(TuiContext context, HistoryRow row)
    {
        var builder = new List<string>
        {
            context.L("History.Detail.Disk", row.Disk),
            context.L("History.Detail.Type", row.TestType),
            context.L("History.Detail.Date", row.DateText),
            context.L("History.Detail.Rating", row.Grade, row.Score.ToString(CultureInfo.InvariantCulture)),
            context.L("History.Detail.Health", row.Health),
            context.L("History.Detail.Duration", row.DurationText),
            context.L("History.Detail.Errors", row.Errors.ToString(CultureInfo.InvariantCulture)),
            context.L("History.Detail.Notes", string.IsNullOrWhiteSpace(row.Notes) ? context.L("History.Detail.NoNotes") : row.Notes),
        };

        var panel = new Panel(new Markup(Markup.Escape(string.Join(Environment.NewLine, builder))))
        {
            Border = BoxBorder.Rounded,
            Expand = true,
            BorderStyle = new Style(foreground: TuiTheme.Primary),
            Header = new PanelHeader($"[bold]{Markup.Escape(context.L("History.DetailTitle"))}[/]", Justify.Left),
            Padding = new Padding(1, 0, 1, 0),
        };

        context.Io.Write(panel);
    }

    private sealed record SessionInfo(int SessionId, int CardId, int? CertificateId);

    private sealed record ResolvedTarget(DiskCard? Card, int? SessionId, int? CertificateId);

    private sealed class HistoryRow
    {
        public Guid ReportId { get; init; }

        public DateTime TestDate { get; init; }

        public string DateText { get; init; } = string.Empty;

        public string Disk { get; init; } = string.Empty;

        public string TestType { get; init; } = string.Empty;

        public string Grade { get; init; } = "?";

        public int Score { get; init; }

        public int Errors { get; init; }

        public string? SerialNumber { get; init; }

        public int? TestSessionId { get; init; }

        public int? CertificateId { get; init; }

        public string Health { get; init; } = string.Empty;

        public string DurationText { get; init; } = "—";

        public string? Notes { get; init; }
    }
}

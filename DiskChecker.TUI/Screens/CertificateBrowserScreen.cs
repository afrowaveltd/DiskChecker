using System.Globalization;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Terminal certificate browser. Mirrors the desktop
/// <c>CertificateBrowserViewModel</c>: every certificate of every disk card is
/// listed with its number, model, serial number, grade, score, test type,
/// generation date and recommendation flag. Free text and grade filters narrow
/// the list; selecting a certificate renders the complete document.
/// </summary>
public sealed class CertificateBrowserScreen : ITuiScreen
{
    private const string ActionSelect = "select";
    private const string ActionSearch = "search";
    private const string ActionGrade = "grade";
    private const string ActionRefresh = "refresh";
    private const string ActionExportPdf = "pdf";
    private const string ActionTechnician = "technician";
    private const string ActionOpenCard = "card";
    private const string RowKeyPrefix = "row:";

    private string _searchText = string.Empty;
    private string _gradeFilter = string.Empty;
    private int? _selectedCertificateId;

    public string Id => ScreenIds.Certificates;

    public string TitleKey => "Nav.Certificates";

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (!cancellationToken.IsCancellationRequested)
        {
            List<CertificateRow> rows;
            DiskCertificate? selectedCertificate = null;
            try
            {
                (rows, selectedCertificate) = await LoadAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                context.Dialogs.Error(context.L("Common.Error"), context.L("CertificateBrowser.Error.Load", ex.Message));
                rows = new List<CertificateRow>();
            }

            var filtered = FilterRows(rows);
            Render(context, rows, filtered, selectedCertificate);

            string? choice = context.Io.Choose(context.L("CertificateBrowser.Prompt"), BuildActions(context));
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
                string? text = context.Io.Ask(context.L("CertificateBrowser.SearchWatermark"), _searchText);
                _searchText = text?.Trim() ?? string.Empty;
                continue;
            }

            if (choice == ActionGrade)
            {
                ChooseGrade(context);
                continue;
            }

            if (choice == ActionExportPdf)
            {
                await ExportPdfAsync(context, selectedCertificate).ConfigureAwait(false);
                continue;
            }

            if (choice == ActionTechnician)
            {
                await SaveTechnicianAssessmentAsync(context, selectedCertificate).ConfigureAwait(false);
                continue;
            }

            if (choice == ActionOpenCard)
            {
                if (selectedCertificate is null)
                {
                    context.Dialogs.Warning(context.L("Common.Warning"), context.L("CertificateBrowser.NoSelection"));
                    continue;
                }

                context.Selection.SelectedDiskCardId = selectedCertificate.DiskCardId;
                context.StatusMessage = context.L("CertificateBrowser.Status.RedirectedToCards", selectedCertificate.DiskModel);
                return ScreenResult.Navigate(ScreenIds.DiskCardDetail);
            }
        }

        return ScreenResult.Back();
    }

    private async Task<(List<CertificateRow> Rows, DiskCertificate? Selected)> LoadAsync(
        TuiContext context,
        CancellationToken cancellationToken)
    {
        return await context.UseScopedAsync(async services =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repository = services.GetRequiredService<IDiskCardRepository>();

            var rows = new List<CertificateRow>();
            var cards = await repository.GetAllAsync().ConfigureAwait(false);
            foreach (var card in cards)
            {
                var certificates = await repository.GetCertificatesAsync(card.Id).ConfigureAwait(false);
                foreach (var certificate in certificates)
                {
                    rows.Add(new CertificateRow
                    {
                        Id = certificate.Id,
                        Number = certificate.CertificateNumber,
                        Model = certificate.DiskModel,
                        Serial = certificate.SerialNumber,
                        Grade = certificate.Grade,
                        Score = certificate.Score,
                        TestType = certificate.TestType,
                        GeneratedAt = certificate.GeneratedAt,
                        GeneratedText = FormatDate(certificate.GeneratedAt),
                        Recommended = certificate.Recommended,
                        DiskCardId = certificate.DiskCardId,
                    });
                }
            }

            DiskCertificate? selected = null;
            if (_selectedCertificateId is int certificateId)
            {
                selected = await repository.GetCertificateAsync(certificateId).ConfigureAwait(false);
                if (selected is null)
                {
                    _selectedCertificateId = null;
                }
            }

            return (rows.OrderByDescending(row => row.GeneratedAt).ToList(), selected);
        }).ConfigureAwait(false);
    }

    private static string FormatDate(DateTime value) =>
        value.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private List<CertificateRow> FilterRows(IReadOnlyList<CertificateRow> rows)
    {
        IEnumerable<CertificateRow> filtered = rows;
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            filtered = filtered.Where(row =>
                row.Model.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || row.Serial.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || row.Number.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_gradeFilter))
        {
            filtered = filtered.Where(row => row.Grade.Equals(_gradeFilter, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToList();
    }

    private void Render(
        TuiContext context,
        IReadOnlyList<CertificateRow> all,
        IReadOnlyList<CertificateRow> filtered,
        DiskCertificate? selected)
    {
        var io = context.Io;
        string filterText = string.IsNullOrWhiteSpace(_searchText)
            ? context.L("CertificateBrowser.Filter.None")
            : context.L("CertificateBrowser.Filter.Text", _searchText);
        string gradeText = string.IsNullOrWhiteSpace(_gradeFilter)
            ? context.L("CertificateBrowser.Status.All")
            : _gradeFilter;
        io.Line($"[grey]{Markup.Escape(context.L(
            "CertificateBrowser.Filter.Label",
            filterText,
            gradeText))}[/]");

        if (all.Count == 0)
        {
            io.Write(new Panel(new Markup(Markup.Escape(context.L("CertificateBrowser.NoCertificates"))))
            {
                Border = BoxBorder.Rounded,
                Expand = true,
                BorderStyle = new Style(foreground: TuiTheme.Muted),
                Header = new PanelHeader($"[bold]{Markup.Escape(context.L("CertificateBrowser.Title"))}[/]", Justify.Left),
                Padding = new Padding(1, 0, 1, 0),
            });
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Expand();

        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateBrowser.Column.Number"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateBrowser.Column.Model"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateBrowser.Column.Serial"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateBrowser.Column.Grade"))}[/]") { Alignment = Justify.Center });
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateBrowser.Column.Score"))}[/]") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateBrowser.Column.TestType"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateBrowser.Column.Generated"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateBrowser.Column.Recommended"))}[/]") { Alignment = Justify.Center });

        int modelWidth = Math.Max(10, (io.Width - 96) / 2);
        foreach (var row in filtered)
        {
            table.AddRow(
                Markup.Escape(TuiFormats.Ellipsis(row.Number, 22)),
                Markup.Escape(TuiFormats.Ellipsis(row.Model, modelWidth)),
                Markup.Escape(TuiFormats.Ellipsis(row.Serial, 18)),
                TuiTheme.GradeMarkup(row.Grade),
                Markup.Escape(row.Score.ToString("F0", CultureInfo.InvariantCulture)),
                Markup.Escape(row.TestType),
                Markup.Escape(row.GeneratedText),
                row.Recommended
                    ? $"[{(TuiTheme.Success).ToMarkup()}]✔[/]"
                    : "[grey]—[/]");
        }

        io.Write(table);

        double average = filtered.Count == 0 ? 0 : filtered.Average(row => row.Score);
        io.Line($"[grey]{Markup.Escape(context.L("CertificateBrowser.Summary",
            filtered.Count.ToString(CultureInfo.InvariantCulture),
            all.Count.ToString(CultureInfo.InvariantCulture),
            average.ToString("F1", CultureInfo.InvariantCulture)))}[/]");

        if (selected is null)
        {
            io.Line($"[grey]{Markup.Escape(context.L("CertificateBrowser.SelectPrompt"))}[/]");
            return;
        }

        RenderDocument(context, selected);
    }

    private static void RenderDocument(TuiContext context, DiskCertificate certificate)
    {
        WritePanel(context, "CertificateBrowser.CertTitle", TuiTheme.Primary, new List<string>
        {
            context.L("CertificateView.CertNumber") + " " + certificate.CertificateNumber,
            context.L("CertificateView.Model") + " " + certificate.DiskModel,
            context.L("CertificateView.SerialNumber") + " " + certificate.SerialNumber,
            context.L("CertificateView.Capacity") + " " + certificate.Capacity,
            context.L("CertificateView.DiskTypeLabel") + " " + certificate.DiskType,
            context.L("CertificateView.Firmware") + " " + certificate.Firmware,
            context.L("CertificateView.Interface") + " " + certificate.Interface,
            context.L("CertificateView.GeneratedLabel") + " " + FormatDate(certificate.GeneratedAt),
            context.L("CertificateView.GradeLabel") + " " + certificate.Grade
                + " · " + context.L("CertificateView.Score") + ": "
                + certificate.Score.ToString("F0", CultureInfo.InvariantCulture) + "/100",
            context.L("CertificateView.HealthStatusFormat", certificate.HealthStatus),
        });

        WritePanel(context, "CertificateBrowser.TestResults", TuiTheme.Primary, new List<string>
        {
            context.L("CertificateView.TestTypeFormat", certificate.TestType),
            context.L("CertificateView.TestDurationFormat", TuiFormats.Duration(certificate.TestDuration)),
            context.L("CertificateView.AvgWriteSpeedFormat", TuiFormats.Speed(certificate.AvgWriteSpeed)),
            context.L("CertificateView.AvgReadSpeedFormat", TuiFormats.Speed(certificate.AvgReadSpeed)),
            context.L("CertificateView.MaxSpeedsFormat", TuiFormats.Speed(certificate.MaxWriteSpeed), TuiFormats.Speed(certificate.MaxReadSpeed)),
            context.L("CertificateView.TemperatureFormat", certificate.TemperatureRange),
            context.L("CertificateView.ErrorsFormat", certificate.ErrorCount.ToString(CultureInfo.InvariantCulture)),
        });

        var smart = new List<string>
        {
            context.L("CertificateView.SmartPassedFormat", certificate.SmartPassed ? context.L("CertificateView.Yes") : context.L("CertificateView.No")),
            context.L("CertificateView.PowerOnHoursFormat", Number(certificate.PowerOnHours)),
            context.L("CertificateView.PowerCyclesFormat", Number(certificate.PowerCycles)),
            context.L("CertificateView.ReallocatedSectorsFormat", certificate.ReallocatedSectors.ToString(CultureInfo.InvariantCulture)),
            context.L("CertificateView.PendingSectorsFormat", certificate.PendingSectors.ToString(CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrWhiteSpace(certificate.SmartDeltaSummary))
        {
            smart.Add(context.L("CertificateView.SmartDelta") + ": " + certificate.SmartDeltaSummary);
        }

        WritePanel(context, "CertificateView.SmartSummary", TuiTheme.Accent, smart);

        if (certificate.SeekAvgLatencyMs.HasValue)
        {
            var seek = new List<string>
            {
                context.L("CertificateView.Average") + " " + TuiFormats.Latency(certificate.SeekAvgLatencyMs.Value),
                context.L("CertificateView.SeekMinLabel") + " " + TuiFormats.Latency(certificate.SeekMinLatencyMs ?? double.NaN),
                context.L("CertificateView.SeekMaxLabel") + " " + TuiFormats.Latency(certificate.SeekMaxLatencyMs ?? double.NaN),
                context.L("CertificateView.SeekP95Label") + " " + TuiFormats.Latency(certificate.SeekP95LatencyMs ?? double.NaN),
            };

            if (!string.IsNullOrWhiteSpace(certificate.SeekTestSummary))
            {
                seek.Add(context.L("CertificateView.SeekSummaryFormat", certificate.SeekTestSummary));
            }

            WritePanel(context, "CertificateView.SeekMetrics", TuiTheme.Accent, seek);
        }

        if (certificate.Sanitize1AvgWriteMBps.HasValue || certificate.Sanitize2AvgWriteMBps.HasValue)
        {
            var sanitization = new List<string>
            {
                context.L("CertificateView.SanitizePass1Format", SpeedAndErrors(certificate.Sanitize1AvgWriteMBps, certificate.Sanitize1AvgReadMBps, certificate.Sanitize1Errors)),
                context.L("CertificateView.SanitizePass2Format", SpeedAndErrors(certificate.Sanitize2AvgWriteMBps, certificate.Sanitize2AvgReadMBps, certificate.Sanitize2Errors)),
                context.L("CertificateView.ChangeFormat", FormatChange(certificate.WriteSpeedChangePercent, certificate.ReadSpeedChangePercent)),
                context.L("CertificateView.SanitizationMethodLabel") + " " + (certificate.SanitizationMethod ?? "—"),
                context.L("CertificateView.DataVerifiedLabel") + " " + (certificate.DataVerified ? context.L("CertificateView.Yes") : context.L("CertificateView.No")),
                context.L("CertificateView.PartitionSchemeLabel") + " " + (certificate.PartitionScheme ?? "—"),
                context.L("CertificateView.FileSystemLabel") + " " + (certificate.FileSystem ?? "—"),
                context.L("CertificateView.VolumeLabelLabel") + " " + (certificate.VolumeLabel ?? "—"),
            };

            WritePanel(context, "CertificateView.SanitizeComparison", TuiTheme.Accent, sanitization);
        }

        WritePanel(context, "CertificateView.Recommendations", RecommendationColor(certificate.Notes), new List<string>
        {
            context.L("CertificateView.RecommendedFormat", certificate.Recommended ? context.L("CertificateView.Yes") : context.L("CertificateView.No")),
            context.L("CertificateView.DiagnosticBadgeFormat", DiagnosticBadge(context, certificate.Notes)),
            string.IsNullOrWhiteSpace(certificate.RecommendationNotes)
                ? context.L("CertificateView.NoSignals")
                : certificate.RecommendationNotes,
        });

        WritePanel(context, "CertificateView.TechnicianAssessment.Title", TuiTheme.Muted, new List<string>
        {
            string.IsNullOrWhiteSpace(certificate.TechnicianAssessment)
                ? context.L("CertificateView.TechnicianAssessment.None")
                : certificate.TechnicianAssessment,
        });
    }

    private static Color RecommendationColor(string? notes) =>
        HasCriticalSignals(notes) ? TuiTheme.Danger : HasWarningSignals(notes) ? TuiTheme.Warning : TuiTheme.Success;

    private static string DiagnosticBadge(TuiContext context, string? notes) =>
        HasCriticalSignals(notes)
            ? context.L("CertificateView.Badge.Critical")
            : HasWarningSignals(notes)
                ? context.L("CertificateView.Badge.Warning")
                : context.L("CertificateView.Badge.Stable");

    private static bool HasCriticalSignals(string? notes) =>
        !string.IsNullOrWhiteSpace(notes)
        && (notes.Contains("kritické", StringComparison.OrdinalIgnoreCase)
            || notes.Contains("kritický", StringComparison.OrdinalIgnoreCase));

    private static bool HasWarningSignals(string? notes) =>
        !string.IsNullOrWhiteSpace(notes)
        && (notes.Contains("propad", StringComparison.OrdinalIgnoreCase)
            || notes.Contains("nestabil", StringComparison.OrdinalIgnoreCase)
            || notes.Contains("histor", StringComparison.OrdinalIgnoreCase));

    private static string SpeedAndErrors(double? write, double? read, int? errors) => string.Create(
        CultureInfo.InvariantCulture,
        $"{TuiFormats.Speed(write ?? double.NaN)} / {TuiFormats.Speed(read ?? double.NaN)} · {errors ?? 0}");

    private static string FormatChange(double? write, double? read) => string.Create(
        CultureInfo.InvariantCulture,
        $"{FormatPercent(write)} / {FormatPercent(read)}");

    private static string FormatPercent(double? value) =>
        value.HasValue ? value.Value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " %" : "—";

    private static string Number(int value) => value > 0 ? value.ToString("N0", CultureInfo.InvariantCulture) : "N/A";

    private static void WritePanel(TuiContext context, string titleKey, Color color, IReadOnlyList<string> lines)
    {
        var panel = new Panel(new Markup(Markup.Escape(string.Join(Environment.NewLine, lines))))
        {
            Border = BoxBorder.Rounded,
            Expand = true,
            BorderStyle = new Style(foreground: color),
            Header = new PanelHeader($"[bold]{Markup.Escape(context.L(titleKey))}[/]", Justify.Left),
            Padding = new Padding(1, 0, 1, 0),
        };

        context.Io.Write(panel);
    }

    private static List<TuiChoice> BuildActions(TuiContext context) => new()
    {
        new TuiChoice(ActionSelect, "▶ " + context.L("CertificateBrowser.Action.Select")),
        new TuiChoice(ActionSearch, "🔍 " + context.L("CertificateBrowser.Action.Search")),
        new TuiChoice(ActionGrade, "🎚 " + context.L("CertificateBrowser.Action.Grade")),
        new TuiChoice(ActionRefresh, "🔄 " + context.L("Common.Refresh")),
        new TuiChoice(ActionExportPdf, "📥 " + context.L("CertificateBrowser.ExportPdf")),
        new TuiChoice(ActionTechnician, "📝 " + context.L("CertificateView.TechnicianAssessment.Save")),
        new TuiChoice(ActionOpenCard, "💿 " + context.L("CertificateBrowser.Action.OpenCard")),
        new TuiChoice(TuiKeys.MainMenu, "☰ " + context.L("Common.Navigation")),
        new TuiChoice(TuiKeys.Back, context.L("Common.Back")),
    };

    private void SelectRow(TuiContext context, IReadOnlyList<CertificateRow> filtered)
    {
        if (filtered.Count == 0)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("CertificateBrowser.NoCertificates"));
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

        string? selected = context.Io.Choose(context.L("CertificateBrowser.Picker"), choices, pageSize: 18);
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

        _selectedCertificateId = filtered[position].Id;
        context.StatusMessage = context.L("CertificateBrowser.Status.CertLoaded", filtered[position].Number, filtered[position].Model);
    }

    private void ChooseGrade(TuiContext context)
    {
        var choices = new List<TuiChoice>
        {
            new(string.Empty, context.L("CertificateBrowser.Status.All")),
            new("A", "A"),
            new("B", "B"),
            new("C", "C"),
            new("D", "D"),
            new("E", "E"),
            new("F", "F"),
        };

        string? selected = context.Io.Choose(context.L("CertificateBrowser.Action.Grade"), choices);
        _gradeFilter = selected ?? string.Empty;
    }

    private static string Describe(CertificateRow row) => string.Create(
        CultureInfo.InvariantCulture,
        $"{row.Number} · {row.Model} · {row.Grade} · {row.GeneratedText}");

    private async Task ExportPdfAsync(TuiContext context, DiskCertificate? certificate)
    {
        if (certificate is null)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("CertificateBrowser.Status.SelectFirst"));
            return;
        }

        string? pdfPath = null;
        string? failure = null;

        using (context.LockNavigation(context.L("CertificateView.Status.ExportingPdf")))
        {
            await context.UseScopedAsync(async services =>
            {
                var generator = services.GetRequiredService<ICertificateGenerator>();
                var repository = services.GetRequiredService<IDiskCardRepository>();
                try
                {
                    pdfPath = await generator.GeneratePdfAsync(certificate).ConfigureAwait(false);
                    certificate.PdfPath = pdfPath;
                    certificate.PdfGenerated = true;
                    await repository.UpdateCertificateAsync(certificate).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
                {
                    failure = ex.Message;
                }
            }).ConfigureAwait(false);
        }

        if (pdfPath is null)
        {
            context.StatusMessage = context.L("Common.Error") + ": " + (failure ?? string.Empty);
            context.Dialogs.Error(context.L("Common.Error"), failure ?? context.L("CertificateBrowser.Status.SelectFirst"));
            return;
        }

        context.StatusMessage = context.L("CertificateBrowser.Status.PdfExported", pdfPath);
        context.Dialogs.Success(context.L("CertificateView.Dialog.PdfExport"), context.StatusMessage);
    }

    private async Task SaveTechnicianAssessmentAsync(TuiContext context, DiskCertificate? certificate)
    {
        if (certificate is null)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("CertificateBrowser.Status.SelectFirst"));
            return;
        }

        if (certificate.Id <= 0)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("CertificateView.TechnicianAssessment.SaveFirst"));
            return;
        }

        string? answer = context.Io.Ask(
            context.L("CertificateView.TechnicianAssessment.Watermark"),
            certificate.TechnicianAssessment);
        string text = answer?.Trim() ?? string.Empty;

        if (text.Length > 2000)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("CertificateView.TechnicianAssessment.TooLong"));
            return;
        }

        bool saved = false;
        using (context.LockNavigation(context.L("CertificateView.TechnicianAssessment.Save")))
        {
            saved = await context.UseScopedAsync(async services =>
            {
                var repository = services.GetRequiredService<IDiskCardRepository>();
                bool updated = await repository
                    .UpdateCertificateTechnicianAssessmentAsync(certificate.Id, text.Length == 0 ? null : text)
                    .ConfigureAwait(false);
                if (!updated)
                {
                    return false;
                }

                certificate.TechnicianAssessment = text.Length == 0 ? null : text;
                certificate.TechnicianAssessmentUpdatedAtUtc = DateTime.UtcNow;
                certificate.PdfGenerated = false;
                return true;
            }).ConfigureAwait(false);
        }

        if (!saved)
        {
            context.Dialogs.Error(context.L("Common.Error"), context.L("CertificateView.TechnicianAssessment.Missing"));
            return;
        }

        context.StatusMessage = context.L("CertificateView.TechnicianAssessment.Saved");
        context.Dialogs.Success(context.L("CertificateView.TechnicianAssessment.Title"), context.StatusMessage);
    }

    private sealed class CertificateRow
    {
        public int Id { get; init; }

        public string Number { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public string Serial { get; init; } = string.Empty;

        public string Grade { get; init; } = "?";

        public double Score { get; init; }

        public string TestType { get; init; } = string.Empty;

        public DateTime GeneratedAt { get; init; }

        public string GeneratedText { get; init; } = string.Empty;

        public bool Recommended { get; init; }

        public int DiskCardId { get; init; }
    }
}

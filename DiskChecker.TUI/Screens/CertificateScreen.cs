using System.Globalization;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Terminal certificate detail. Mirrors the desktop <c>CertificateViewModel</c> in
/// a terminal shape: it loads the certificate from the shared selection
/// (certificate, then test session, then selected disk), renders the complete
/// document with a grade seal, lists the card's test sessions and regenerates the
/// certificate in memory when another session is chosen.
/// </summary>
public sealed class CertificateScreen : ITuiScreen
{
    private const string ActionSavePdf = "pdf";
    private const string ActionTechnician = "note";
    private const string ActionSwitchSession = "session";
    private const string SessionKeyPrefix = "session:";

    private DiskCertificate? _certificate;
    private DiskCard? _card;
    private List<TestSession> _sessions = new();
    private int? _sessionId;
    private bool _loaded;

    public string Id => ScreenIds.Certificate;

    public string TitleKey => "Nav.Certificates";

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_loaded)
        {
            _loaded = true;
            try
            {
                await LoadAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                context.Dialogs.Error(context.L("Common.Error"), context.L("CertificateView.Error.Load", ex.Message));
            }
        }

        if (_certificate is null)
        {
            context.Dialogs.Notice(context.L("CertificateView.Status.Title"), context.L("CertificateView.Status.NoCertificate"));
            return ScreenResult.Navigate(ScreenIds.DiskCardDetail);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            Render(context);

            string? choice = context.Io.Choose(context.L("CertificateView.Prompt"), BuildActions(context));
            if (choice is null || choice == TuiKeys.Back)
            {
                return ScreenResult.Navigate(ScreenIds.DiskCardDetail);
            }

            if (choice == TuiKeys.MainMenu)
            {
                return ScreenResult.Menu();
            }

            if (choice == TuiKeys.Exit)
            {
                return ScreenResult.Exit();
            }

            if (choice == ActionSavePdf)
            {
                await SavePdfAsync(context).ConfigureAwait(false);
                continue;
            }

            if (choice == ActionTechnician)
            {
                await SaveTechnicianNoteAsync(context).ConfigureAwait(false);
                continue;
            }

            if (choice == ActionSwitchSession)
            {
                await SwitchSessionAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }

        return ScreenResult.Navigate(ScreenIds.DiskCardDetail);
    }

    private async Task LoadAsync(TuiContext context, CancellationToken cancellationToken)
    {
        await context.UseScopedAsync(async services =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repository = services.GetRequiredService<IDiskCardRepository>();
            var generator = services.GetRequiredService<ICertificateGenerator>();

            DiskCertificate? certificate = null;
            TestSession? session = null;
            DiskCard? card = null;

            if (context.Selection.SelectedCertificateId is int certificateId)
            {
                certificate = await repository.GetCertificateAsync(certificateId).ConfigureAwait(false);
                if (certificate is not null && certificate.TestSessionId > 0)
                {
                    session = await repository.GetTestSessionWithoutSamplesAsync(certificate.TestSessionId).ConfigureAwait(false);
                    if (session is not null)
                    {
                        card = await repository.GetByIdAsync(session.DiskCardId).ConfigureAwait(false);
                    }
                }

                if (card is null && certificate is not null && certificate.DiskCardId > 0)
                {
                    card = await repository.GetByIdAsync(certificate.DiskCardId).ConfigureAwait(false);
                }
            }

            if (session is null && context.Selection.SelectedTestSessionId is int sessionId)
            {
                session = await repository.GetTestSessionWithoutSamplesAsync(sessionId).ConfigureAwait(false);
                if (session is not null)
                {
                    card = await repository.GetByIdAsync(session.DiskCardId).ConfigureAwait(false) ?? card;
                }
            }

            if (card is null && context.Selection.SelectedDisk is CoreDriveInfo disk)
            {
                card = await repository.GetByDevicePathAsync(disk.Path).ConfigureAwait(false);
            }

            if (card is null)
            {
                return;
            }

            if (certificate is null && session is not null)
            {
                certificate = await generator.GenerateCertificateAsync(session, card).ConfigureAwait(false);
            }

            if (certificate is null)
            {
                certificate = await repository.GetLatestCertificateAsync(card.Id).ConfigureAwait(false);
            }

            if (certificate is not null && session is null && certificate.TestSessionId > 0)
            {
                session = await repository.GetTestSessionWithoutSamplesAsync(certificate.TestSessionId).ConfigureAwait(false);
            }

            var sessions = await repository.GetTestSessionsAsync(card.Id).ConfigureAwait(false);

            _card = card;
            _certificate = certificate;
            _sessions = sessions.OrderByDescending(candidate => candidate.StartedAt).ToList();
            _sessionId = session?.Id ?? _sessions.FirstOrDefault()?.Id;

            if (_certificate is not null)
            {
                context.Selection.SelectedDiskCardId = card.Id;
                context.Selection.SelectedTestSessionId = _sessionId;
                context.Selection.SelectedCertificateId = _certificate.Id > 0 ? _certificate.Id : null;
                context.StatusMessage = context.L("CertificateView.Status.Loaded", _certificate.CertificateNumber);
            }
        }).ConfigureAwait(false);
    }

    private void Render(TuiContext context)
    {
        var certificate = _certificate;
        if (certificate is null)
        {
            return;
        }

        WritePanel(context, "CertificateView.DiskInfo", TuiTheme.Primary, new List<string>
        {
            context.L("CertificateView.CertNumber") + " " + certificate.CertificateNumber,
            context.L("CertificateView.Model") + " " + certificate.DiskModel,
            context.L("CertificateView.SerialNumber") + " " + certificate.SerialNumber,
            context.L("CertificateView.Capacity") + " " + certificate.Capacity,
            context.L("CertificateView.DiskTypeLabel") + " " + certificate.DiskType,
            context.L("CertificateView.Firmware") + " " + certificate.Firmware,
            context.L("CertificateView.Interface") + " " + certificate.Interface,
            context.L("CertificateView.GeneratedLabel") + " " + FormatDate(certificate.GeneratedAt),
        });

        RenderSeal(context, certificate);

        WritePanel(context, "CertificateView.TestResults", TuiTheme.Primary, new List<string>
        {
            context.L("CertificateView.TestTypeFormat", certificate.TestType),
            context.L("CertificateView.HealthStatusFormat", certificate.HealthStatus),
            context.L("CertificateView.TestDurationFormat", TuiFormats.Duration(certificate.TestDuration)),
            context.L("CertificateView.ErrorsFormat", certificate.ErrorCount.ToString(CultureInfo.InvariantCulture)),
        });

        WritePanel(context, "CertificateView.Performance", TuiTheme.Primary, new List<string>
        {
            context.L("CertificateView.AvgWriteSpeedFormat", TuiFormats.Speed(certificate.AvgWriteSpeed)),
            context.L("CertificateView.AvgReadSpeedFormat", TuiFormats.Speed(certificate.AvgReadSpeed)),
            context.L("CertificateView.MaxSpeedsFormat", TuiFormats.Speed(certificate.MaxWriteSpeed), TuiFormats.Speed(certificate.MaxReadSpeed)),
            context.L("CertificateView.TemperatureFormat", certificate.TemperatureRange),
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
            WritePanel(context, "CertificateView.SeekMetrics", TuiTheme.Accent, new List<string>
            {
                context.L("CertificateView.Average") + " " + TuiFormats.Latency(certificate.SeekAvgLatencyMs.Value),
                context.L("CertificateView.SeekMinLabel") + " " + TuiFormats.Latency(certificate.SeekMinLatencyMs ?? double.NaN),
                context.L("CertificateView.SeekMaxLabel") + " " + TuiFormats.Latency(certificate.SeekMaxLatencyMs ?? double.NaN),
                context.L("CertificateView.SeekP95Label") + " " + TuiFormats.Latency(certificate.SeekP95LatencyMs ?? double.NaN),
            });
        }

        if (certificate.SanitizationPerformed)
        {
            WritePanel(context, "CertificateView.SanitizeComparison", TuiTheme.Accent, new List<string>
            {
                context.L("CertificateView.SanitizationMethodLabel") + " " + (certificate.SanitizationMethod ?? "—"),
                context.L("CertificateView.SanitizePass1Format", SpeedValue(certificate.Sanitize1AvgWriteMBps, certificate.Sanitize1AvgReadMBps)),
                context.L("CertificateView.SanitizePass2Format", SpeedValue(certificate.Sanitize2AvgWriteMBps, certificate.Sanitize2AvgReadMBps)),
                context.L("CertificateView.ChangeFormat", FormatChange(certificate.WriteSpeedChangePercent, certificate.ReadSpeedChangePercent)),
                context.L("CertificateView.DataVerifiedLabel") + " " + (certificate.DataVerified ? context.L("CertificateView.Yes") : context.L("CertificateView.No")),
            });
        }

        WritePanel(context, "CertificateView.DiagnosticSignals", RecommendationColor(certificate.Notes), new List<string>
        {
            context.L("CertificateView.DiagnosticBadgeFormat", DiagnosticBadge(context, certificate.Notes)),
            string.IsNullOrWhiteSpace(certificate.Notes) ? context.L("CertificateView.NoSignals") : certificate.Notes,
        });

        WritePanel(context, "CertificateView.Recommendations", TuiTheme.Success, new List<string>
        {
            context.L("CertificateView.RecommendedFormat", certificate.Recommended ? context.L("CertificateView.Yes") : context.L("CertificateView.No")),
            string.IsNullOrWhiteSpace(certificate.RecommendationNotes) ? context.L("CertificateView.NoSignals") : certificate.RecommendationNotes,
        });

        WritePanel(context, "CertificateView.TechnicianAssessment.Title", TuiTheme.Muted, new List<string>
        {
            string.IsNullOrWhiteSpace(certificate.TechnicianAssessment)
                ? context.L("CertificateView.TechnicianAssessment.None")
                : certificate.TechnicianAssessment,
        });

        RenderSessions(context);
    }

    private static void RenderSeal(TuiContext context, DiskCertificate certificate)
    {
        string markup =
            $"[bold]{Markup.Escape(context.L("CertificateView.FinalGrade"))}[/]  " +
            $"[bold]{TuiTheme.GradeMarkup(certificate.Grade)}[/]  " +
            $"[bold]{certificate.Score.ToString("F0", CultureInfo.InvariantCulture)}/100[/]  " +
            $"[grey]{Markup.Escape(certificate.HealthStatus)}[/]";

        var panel = new Panel(new Markup(markup))
        {
            Border = BoxBorder.Double,
            Expand = true,
            BorderStyle = new Style(foreground: TuiTheme.GradeColor(certificate.Grade)),
            Header = new PanelHeader($"[bold]{Markup.Escape(context.L("CertificateView.OverallRating"))}[/]", Justify.Center),
            Padding = new Padding(1, 0, 1, 0),
        };

        context.Io.Write(panel);
    }

    private void RenderSessions(TuiContext context)
    {
        var io = context.Io;
        if (_sessions.Count == 0)
        {
            io.Line($"[grey]{Markup.Escape(context.L("CertificateView.Session.SelectForDetail"))}[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted);

        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateView.Session.Indicator"))}[/]") { Alignment = Justify.Center });
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateView.Session.Date"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateView.Session.Type"))}[/]"));
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateView.Session.Grade"))}[/]") { Alignment = Justify.Center });
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(context.L("CertificateView.Session.Score"))}[/]") { Alignment = Justify.Right });

        foreach (var session in _sessions)
        {
            bool current = session.Id == _sessionId;
            table.AddRow(
                current ? $"[{TuiTheme.Success.ToMarkup()}]●[/]" : "[grey]○[/]",
                Markup.Escape(FormatDate(session.StartedAt)),
                Markup.Escape(session.TestType.ToString()),
                TuiTheme.GradeMarkup(session.Grade),
                Markup.Escape(session.Score.ToString("F0", CultureInfo.InvariantCulture)));
        }

        context.Io.Write(table);
    }

    private static List<TuiChoice> BuildActions(TuiContext context) => new()
    {
        new TuiChoice(ActionSavePdf, "💾 " + context.L("CertificateView.SavePdf")),
        new TuiChoice(ActionTechnician, "📝 " + context.L("CertificateView.TechnicianAssessment.Save")),
        new TuiChoice(ActionSwitchSession, "🔁 " + context.L("CertificateView.Session.Switch")),
        new TuiChoice(TuiKeys.MainMenu, "☰ " + context.L("Common.Navigation")),
        new TuiChoice(TuiKeys.Back, context.L("Common.Back")),
    };

    private async Task SavePdfAsync(TuiContext context)
    {
        var certificate = _certificate;
        if (certificate is null)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("CertificateView.Status.GenerateFirst"));
            return;
        }

        string? pdfPath = null;
        string? failure = null;

        using (context.LockNavigation(context.L("CertificateView.Status.ExportingPdf")))
        {
            await context.UseScopedAsync(async services =>
            {
                var generator = services.GetRequiredService<ICertificateGenerator>();
                try
                {
                    pdfPath = await generator.GeneratePdfAsync(certificate).ConfigureAwait(false);
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
            context.Dialogs.Error(context.L("Common.Error"), failure ?? context.L("CertificateView.Error.ExportPdf", "—"));
            return;
        }

        context.StatusMessage = context.L("CertificateView.Status.PdfSaved", pdfPath);
        context.Dialogs.Success(context.L("CertificateView.Dialog.PdfExport"), context.StatusMessage);
    }

    private async Task SaveTechnicianNoteAsync(TuiContext context)
    {
        var certificate = _certificate;
        if (certificate is null)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("CertificateView.Status.GenerateFirst"));
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

    private async Task SwitchSessionAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_sessions.Count == 0)
        {
            context.Dialogs.Warning(context.L("Common.Warning"), context.L("CertificateView.Status.NoTest"));
            return;
        }

        var choices = _sessions
            .Select(session => new TuiChoice(
                SessionKeyPrefix + session.Id.ToString(CultureInfo.InvariantCulture),
                DescribeSession(session)))
            .ToList();

        string? selected = context.Io.Choose(context.L("CertificateView.Session.Switch"), choices);
        if (selected is null || !selected.StartsWith(SessionKeyPrefix, StringComparison.Ordinal))
        {
            return;
        }

        string raw = selected[SessionKeyPrefix.Length..];
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sessionId))
        {
            return;
        }

        var card = _card;
        if (card is null)
        {
            return;
        }

        DiskCertificate? regenerated = null;
        using (context.LockNavigation(context.L("CertificateView.Status.Generating")))
        {
            await context.UseScopedAsync(async services =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var repository = services.GetRequiredService<IDiskCardRepository>();
                var generator = services.GetRequiredService<ICertificateGenerator>();

                var session = await repository.GetTestSessionWithoutSamplesAsync(sessionId).ConfigureAwait(false)
                    ?? _sessions.FirstOrDefault(candidate => candidate.Id == sessionId);
                if (session is null)
                {
                    return;
                }

                regenerated = await generator.GenerateCertificateAsync(session, card).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        if (regenerated is null)
        {
            context.Dialogs.Error(context.L("Common.Error"), context.L("CertificateView.Error.GenerateFailed", "—"));
            return;
        }

        _certificate = regenerated;
        _sessionId = sessionId;
        context.Selection.SelectedTestSessionId = sessionId;
        context.Selection.SelectedCertificateId = regenerated.Id > 0 ? regenerated.Id : null;
        context.StatusMessage = context.L("CertificateView.Status.Generated", regenerated.CertificateNumber);
    }

    private static string DescribeSession(TestSession session) => string.Create(
        CultureInfo.InvariantCulture,
        $"{FormatDate(session.StartedAt)} · {session.TestType} · {session.Grade} · {session.Score:F0}");

    private static string FormatDate(DateTime value) =>
        value.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Number(int value) => value > 0 ? value.ToString("N0", CultureInfo.InvariantCulture) : "N/A";

    private static string SpeedValue(double? write, double? read) => string.Create(
        CultureInfo.InvariantCulture,
        $"{TuiFormats.Speed(write ?? double.NaN)} / {TuiFormats.Speed(read ?? double.NaN)}");

    private static string FormatChange(double? write, double? read) => string.Create(
        CultureInfo.InvariantCulture,
        $"{FormatPercent(write)} / {FormatPercent(read)}");

    private static string FormatPercent(double? value) =>
        value.HasValue ? value.Value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " %" : "—";

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
}

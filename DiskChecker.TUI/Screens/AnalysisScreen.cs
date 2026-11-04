using System.Globalization;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Analysis workspace: stored measurements, throughput / seek / temperature charts,
/// zoom into a progress window, the top anomalies and stalls and SMART trends.
/// Terminal counterpart of the desktop <c>AnalysisViewModel</c>.
/// </summary>
public sealed class AnalysisScreen : ITuiScreen
{
    private const int EventLimit = 8;
    private const int FullLayoutWidth = 150;
    private const double ZoomPadding = 0.5d;

    private readonly List<TestAnalysisSummary> _summaries = new();

    private TestAnalysisSummary? _selected;
    private TestAnalysisData? _data;
    private SmartTrendReport? _trends;
    private List<TestAnomalyEvent> _anomalies = new();
    private List<TestStallEvent> _stalls = new();
    private int _selectedAnomaly = -1;
    private int _selectedStall = -1;
    private string _status = string.Empty;
    private double? _zoomStart;
    private double? _zoomEnd;
    private double _manualZoomStart;
    private double _manualZoomEnd = 10d;
    private bool _loaded;

    /// <inheritdoc />
    public string Id => ScreenIds.Analysis;

    /// <inheritdoc />
    public string TitleKey => "Nav.Analysis";

    /// <inheritdoc />
    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_loaded)
        {
            await RefreshWorkspaceAsync(context, cancellationToken).ConfigureAwait(false);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Render(context);

            string? action = context.Io.Choose(context.L("Tui.Analysis.Actions"), BuildActions(context), pageSize: 14);
            switch (action)
            {
                case "refresh":
                    await RefreshWorkspaceAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "select":
                    await SelectMeasurementAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "zoom":
                    await ApplyManualZoomAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "zoom-reset":
                    await ResetZoomAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "anomaly":
                    await ShowAnomalyDetailAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "stall":
                    await ShowStallDetailAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case TuiKeys.Exit:
                    return ScreenResult.Exit();
                case TuiKeys.MainMenu:
                    return ScreenResult.Menu();
                default:
                    return ScreenResult.Back();
            }
        }
    }

    // ========== Data ==========

    private async Task RefreshWorkspaceAsync(TuiContext context, CancellationToken cancellationToken)
    {
        _status = context.L("Analysis.Status.LoadingData");

        List<TestAnalysisSummary> summaries;
        try
        {
            summaries = await context.UseScopedAsync(async provider =>
            {
                var repository = provider.GetRequiredService<IDiskCardRepository>();
                var analysis = provider.GetRequiredService<ITestAnalysisDataService>();
                var cards = await repository.GetAllAsync().ConfigureAwait(false);
                var list = new List<TestAnalysisSummary>();
                foreach (var card in cards.OrderBy(item => item.ModelName, StringComparer.OrdinalIgnoreCase))
                {
                    var cardSummaries = await analysis.GetDiskAnalysisSummariesAsync(card.Id, cancellationToken).ConfigureAwait(false);
                    list.AddRange(cardSummaries);
                }

                return list;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            _status = context.L("Analysis.Status.LoadError", ex.Message);
            _loaded = true;
            return;
        }

        int? previousSessionId = _selected?.TestSessionId;
        _summaries.Clear();
        _summaries.AddRange(summaries);
        _loaded = true;

        _selected = _summaries.FirstOrDefault(item => item.TestSessionId == previousSessionId) ?? _summaries.FirstOrDefault();
        _status = _summaries.Count == 0
            ? context.L("Tui.Analysis.Empty.Body")
            : context.L("Analysis.Status.LoadedCount", _summaries.Count);

        await LoadSelectedAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadSelectedAsync(TuiContext context, CancellationToken cancellationToken)
    {
        _zoomStart = null;
        _zoomEnd = null;

        if (_selected is null)
        {
            _data = null;
            _trends = null;
            RebuildEvents();
            return;
        }

        try
        {
            int sessionId = _selected.TestSessionId;
            _data = await context.UseScopedAsync(provider =>
                provider.GetRequiredService<ITestAnalysisDataService>().GetAnalysisDataAsync(sessionId, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            _data = null;
            _trends = null;
            RebuildEvents();
            _status = context.L("Analysis.Status.DetailError", ex.Message);
            return;
        }

        RebuildEvents();
        if (_data is null)
        {
            _trends = null;
            _status = context.L("Analysis.Status.MeasurementNotFound");
            return;
        }

        _status = context.L("Analysis.Status.MeasurementLoaded", _data.Session.Id);
        _trends = await LoadTrendsAsync(context, _selected.DiskCardId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SmartTrendReport?> LoadTrendsAsync(TuiContext context, int diskCardId, CancellationToken cancellationToken)
    {
        try
        {
            var trends = context.GetRequiredService<SmartTrendService>();
            return await trends.BuildTrendReportAsync(diskCardId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            // SMART trends are supplementary; the workspace keeps working without them.
            return null;
        }
    }

    private void RebuildEvents()
    {
        _anomalies = _data?.AnomalyEvents.OrderByDescending(item => item.SeverityScore).Take(EventLimit).ToList() ?? new List<TestAnomalyEvent>();
        _stalls = _data?.StallEvents.OrderByDescending(item => item.DurationMs).Take(EventLimit).ToList() ?? new List<TestStallEvent>();
        _selectedAnomaly = _anomalies.Count > 0 ? 0 : -1;
        _selectedStall = _stalls.Count > 0 ? 0 : -1;
    }

    // ========== Actions ==========

    private IReadOnlyList<TuiChoice> BuildActions(TuiContext context)
    {
        var actions = new List<TuiChoice>
        {
            new("refresh", "🔄 " + context.L("Common.Refresh")),
            new("select", "📈 " + context.L("Tui.Analysis.Action.Select"), Enabled: _summaries.Count > 0),
            new("zoom", "🔍 " + context.L("Tui.Analysis.Action.Zoom"), Enabled: _data is not null),
            new("zoom-reset", "🔎 " + context.L("Tui.Analysis.Action.ZoomReset"), Enabled: _data is not null),
            new("anomaly", "⚠ " + context.L("Tui.Analysis.Action.Anomaly"), Enabled: _anomalies.Count > 0),
            new("stall", "⏱ " + context.L("Tui.Analysis.Action.Stall"), Enabled: _stalls.Count > 0),
            new(TuiKeys.Back, context.L("Common.Back")),
            new(TuiKeys.MainMenu, "☰ " + context.L("Tui.Common.Menu")),
        };

        return actions;
    }

    private async Task SelectMeasurementAsync(TuiContext context, CancellationToken cancellationToken)
    {
        var choices = _summaries
            .Select((summary, index) => new TuiChoice(
                "measurement-" + index.ToString(CultureInfo.InvariantCulture),
                TuiFormats.Ellipsis(summary.DiskModel, 34),
                SummaryLine(context, summary)))
            .ToList();

        string? choice = context.Io.Choose(context.L("Tui.Analysis.Action.Select"), choices, pageSize: 12);
        if (choice is null || choice == TuiKeys.Back || !choice.StartsWith("measurement-", StringComparison.Ordinal))
        {
            return;
        }

        if (!int.TryParse(choice["measurement-".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            || index < 0 || index >= _summaries.Count)
        {
            return;
        }

        _selected = _summaries[index];
        await LoadSelectedAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyManualZoomAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_data is null)
        {
            return;
        }

        string? startText = context.Io.Ask(context.L("Tui.Analysis.Zoom.Start"), PercentText(_manualZoomStart));
        string? endText = context.Io.Ask(context.L("Tui.Analysis.Zoom.End"), PercentText(_manualZoomEnd));

        if (!TryParsePercent(startText, out double start) || !TryParsePercent(endText, out double end) || Math.Abs(end - start) < 0.01d)
        {
            _status = context.L("Tui.Analysis.Zoom.Invalid");
            return;
        }

        _manualZoomStart = Math.Min(start, end);
        _manualZoomEnd = Math.Max(start, end);
        await ApplyZoomAsync(context, _manualZoomStart, _manualZoomEnd, cancellationToken, padding: false).ConfigureAwait(false);
    }

    private async Task ResetZoomAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_data is null)
        {
            return;
        }

        _zoomStart = null;
        _zoomEnd = null;
        await ReloadTelemetryAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyZoomAsync(
        TuiContext context,
        double startPercent,
        double endPercent,
        CancellationToken cancellationToken,
        bool padding = true)
    {
        double pad = padding ? ZoomPadding : 0d;
        _zoomStart = Math.Clamp(Math.Min(startPercent, endPercent) - pad, 0d, 100d);
        _zoomEnd = Math.Clamp(Math.Max(startPercent, endPercent) + pad, 0d, 100d);
        await ReloadTelemetryAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReloadTelemetryAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_data is null)
        {
            return;
        }

        int sessionId = _data.Session.Id;
        double start = _zoomStart ?? 0d;
        double end = _zoomEnd ?? 100d;

        try
        {
            var samples = await context.UseScopedAsync(provider =>
                provider.GetRequiredService<ITestAnalysisDataService>().GetTelemetryRangeAsync(sessionId, start, end, cancellationToken)).ConfigureAwait(false);

            _data.TelemetrySamples = samples;
            RebuildEvents();
            _status = _zoomStart is null
                ? context.L("Tui.Analysis.Zoom.Reset")
                : context.L("Tui.Analysis.Zoom.Applied", start, end, samples.Count);
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            _status = context.L("Tui.Analysis.Zoom.Failed", ex.Message);
        }
    }

    private async Task ShowAnomalyDetailAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_anomalies.Count == 0)
        {
            return;
        }

        string? choice = context.Io.Choose(
            context.L("AnalysisView.TopAnomalies"),
            _anomalies.Select((anomaly, index) => new TuiChoice(
                "anomaly-" + index.ToString(CultureInfo.InvariantCulture),
                AnomalyDisplayText(anomaly))).ToList(),
            pageSize: EventLimit);

        if (choice is null || choice == TuiKeys.Back || !choice.StartsWith("anomaly-", StringComparison.Ordinal))
        {
            return;
        }

        if (!int.TryParse(choice["anomaly-".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            || index < 0 || index >= _anomalies.Count)
        {
            return;
        }

        _selectedAnomaly = index;
        var anomaly = _anomalies[index];
        context.Dialogs.Notice(context.L("AnalysisView.AnomaliesLabel"), AnomalyDetailText(context, anomaly));
        await ApplyZoomAsync(context, anomaly.StartProgressPercent, anomaly.EndProgressPercent, cancellationToken).ConfigureAwait(false);
        _status = context.L("Analysis.Status.AnomalySelected", AnomalyDisplayText(anomaly));
    }

    private async Task ShowStallDetailAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_stalls.Count == 0)
        {
            return;
        }

        string? choice = context.Io.Choose(
            context.L("AnalysisView.TopStalls"),
            _stalls.Select((stall, index) => new TuiChoice(
                "stall-" + index.ToString(CultureInfo.InvariantCulture),
                StallDisplayText(stall))).ToList(),
            pageSize: EventLimit);

        if (choice is null || choice == TuiKeys.Back || !choice.StartsWith("stall-", StringComparison.Ordinal))
        {
            return;
        }

        if (!int.TryParse(choice["stall-".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            || index < 0 || index >= _stalls.Count)
        {
            return;
        }

        _selectedStall = index;
        var stall = _stalls[index];
        context.Dialogs.Notice(context.L("AnalysisView.StallsLabel"), StallDetailText(context, stall));
        await ApplyZoomAsync(context, stall.StartProgressPercent, stall.EndProgressPercent, cancellationToken).ConfigureAwait(false);
        _status = context.L("Analysis.Status.StallSelected", StallDisplayText(stall));
    }

    // ========== Rendering ==========

    private void Render(TuiContext context)
    {
        context.Io.Clear();
        context.Io.Write(new Rule($"[bold]{Markup.Escape(context.L("AnalysisView.Title"))}[/] " +
                                 $"[grey]{Markup.Escape(context.L("AnalysisView.Subtitle"))}[/]")
        {
            Justification = Justify.Left,
            Style = new Style(foreground: TuiTheme.Primary),
        });

        if (!string.IsNullOrWhiteSpace(_status))
        {
            context.Io.Line($"[grey]{Markup.Escape(_status)}[/]");
        }

        RenderMeasurements(context);

        if (_selected is not null)
        {
            context.Io.Line($"[bold]{Markup.Escape(SummaryLine(context, _selected))}[/]");
        }

        if (_data is null)
        {
            context.Io.Write(new Panel(new Markup(Markup.Escape(context.L("Analysis.Status.SelectMeasurement"))))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: TuiTheme.Warning),
                Padding = new Padding(1, 0, 1, 0),
            });
            return;
        }

        bool full = context.Io.Width >= FullLayoutWidth;
        context.Io.Line($"[grey]{Markup.Escape(context.L("Analysis.Status.Mode", context.L(full ? "Tui.Analysis.Layout.Full" : "Tui.Analysis.Layout.Compact")))}[/]");
        context.Io.Line($"[grey]{Markup.Escape(context.L("Analysis.Status.TelemetryLine", _data.TelemetrySamples.Count, _data.TemperatureSamples.Count, _data.SeekSamples.Count))}[/]");
        context.Io.Line($"[grey]{Markup.Escape(context.L("Analysis.Status.EventsLine", _data.AnomalyEvents.Count, _data.StallEvents.Count))}[/]");
        if (!string.IsNullOrWhiteSpace(_data.SmartReport.Summary))
        {
            context.Io.Line($"[grey]{Markup.Escape(_data.SmartReport.Summary)}[/]");
        }
        context.Io.Line($"[grey]{Markup.Escape(ZoomText(context))}[/]");

        int chartWidth = Math.Clamp(context.Io.Width - 4, 32, 160);
        RenderThroughputChart(context, chartWidth, full ? 12 : 8);
        RenderSeekChart(context, chartWidth, full ? 10 : 6);

        if (full)
        {
            RenderTemperatureChart(context, chartWidth, 8);
        }

        RenderEvents(context);
        RenderSmartSection(context, chartWidth, full);
    }

    private void RenderMeasurements(TuiContext context)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Title($"[bold]{Markup.Escape(context.L("AnalysisView.Measurements"))}[/]")
            .AddColumn(new TableColumn("#") { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Model")))
            .AddColumn(new TableColumn(context.L("Tui.Analysis.Column.Type")))
            .AddColumn(new TableColumn(context.L("Tui.Analysis.Column.Started")))
            .AddColumn(new TableColumn(context.L("Grade")) { Alignment = Justify.Center })
            .AddColumn(new TableColumn(context.L("Tui.Analysis.Column.Telemetry")) { Alignment = Justify.Right });

        if (_summaries.Count == 0)
        {
            table.AddRow("-", Markup.Escape(context.L("Tui.Analysis.Empty.Row")), "-", "-", "[grey]?[/]", "-");
        }
        else
        {
            for (int index = 0; index < _summaries.Count; index++)
            {
                var summary = _summaries[index];
                bool selected = _selected is not null && summary.TestSessionId == _selected.TestSessionId;
                string marker = selected ? "▶" : (index + 1).ToString(CultureInfo.InvariantCulture);
                table.AddRow(
                    marker,
                    Markup.Escape(TuiFormats.Ellipsis(summary.DiskModel, 32)),
                    Markup.Escape(summary.TestType.ToString()),
                    Markup.Escape(summary.StartedAt.ToString("g", CultureInfo.CurrentCulture)),
                    TuiTheme.GradeMarkup(summary.Grade),
                    summary.TelemetrySampleCount.ToString("N0", CultureInfo.InvariantCulture));
            }
        }

        context.Io.Write(table);
    }

    private void RenderThroughputChart(TuiContext context, int width, int height)
    {
        if (_data is null)
        {
            return;
        }

        var (minX, maxX) = ResolveZoomBounds();
        var chart = new TuiChart
        {
            YFormat = "F0",
            XFormat = "F0",
            XSuffix = " %",
            MinX = minX,
            MaxX = maxX,
            MinY = 0,
        };

        var write = chart.AddSeries(context.L("Tui.Analysis.Series.Write"), TuiTheme.WriteSeries);
        var read = chart.AddSeries(context.L("Tui.Analysis.Series.Read"), TuiTheme.ReadSeries);
        double maxSpeed = 1d;

        foreach (var sample in _data.TelemetrySamples.OrderBy(item => item.SequenceIndex))
        {
            double speed = sample.IsStalled ? 0d : Math.Max(0d, sample.SpeedMBps);
            if (IsWritePhase(sample.Phase))
            {
                write.Add(sample.ProgressPercent, speed);
                maxSpeed = Math.Max(maxSpeed, speed);
            }
            else if (IsReadPhase(sample.Phase))
            {
                read.Add(sample.ProgressPercent, speed);
                maxSpeed = Math.Max(maxSpeed, speed);
            }
        }

        chart.MaxY = maxSpeed * 1.1d;
        context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.Analysis.Chart.Throughput"))}[/]");
        context.Io.Write(chart.Render(width, height));
    }

    private void RenderSeekChart(TuiContext context, int width, int height)
    {
        if (_data is null || _data.SeekSamples.Count == 0)
        {
            return;
        }

        var points = _data.SeekSamples
            .Where(sample => sample.LatencyMs > 0d && !sample.HasError)
            .OrderBy(sample => sample.Index)
            .Select(sample => new TuiPoint(sample.Index, sample.LatencyMs));

        var chart = TuiChartFactory.Line(points, context.L("Tui.Analysis.Series.SeekLatency"), TuiTheme.Accent, "F2");
        chart.MinY = 0;

        context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.Analysis.Chart.Seek"))}[/]");
        context.Io.Write(chart.Render(width, height));
    }

    private void RenderTemperatureChart(TuiContext context, int width, int height)
    {
        if (_data is null || _data.TemperatureSamples.Count == 0)
        {
            return;
        }

        var points = _data.TemperatureSamples
            .OrderBy(sample => sample.Timestamp)
            .Select(sample => new TuiPoint(sample.ProgressPercent, sample.TemperatureCelsius));

        var chart = TuiChartFactory.Line(points, context.L("Tui.Analysis.Series.Temperature"), TuiTheme.Warning, "F0");
        chart.MinY = 0;

        context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.Analysis.Chart.Temperature"))}[/]");
        context.Io.Write(chart.Render(width, height));
    }

    private void RenderEvents(TuiContext context)
    {
        var anomalies = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Title($"[bold]{Markup.Escape(context.L("AnalysisView.TopAnomalies"))}[/]")
            .AddColumn(new TableColumn("#") { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Tui.Analysis.Column.Event")));

        if (_anomalies.Count == 0)
        {
            anomalies.AddRow("-", "[grey]" + Markup.Escape(context.L("Tui.Analysis.Events.None")) + "[/]");
        }
        else
        {
            for (int index = 0; index < _anomalies.Count; index++)
            {
                string prefix = index == _selectedAnomaly ? "▶ " : string.Empty;
                anomalies.AddRow(
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    Markup.Escape(prefix + AnomalyDisplayText(_anomalies[index])));
            }
        }

        context.Io.Write(anomalies);

        var stalls = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Title($"[bold]{Markup.Escape(context.L("AnalysisView.TopStalls"))}[/]")
            .AddColumn(new TableColumn("#") { Alignment = Justify.Right })
            .AddColumn(new TableColumn(context.L("Tui.Analysis.Column.Event")));

        if (_stalls.Count == 0)
        {
            stalls.AddRow("-", "[grey]" + Markup.Escape(context.L("Tui.Analysis.Events.None")) + "[/]");
        }
        else
        {
            for (int index = 0; index < _stalls.Count; index++)
            {
                string prefix = index == _selectedStall ? "▶ " : string.Empty;
                stalls.AddRow(
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    Markup.Escape(prefix + StallDisplayText(_stalls[index])));
            }
        }

        context.Io.Write(stalls);

        if (_selectedAnomaly >= 0 && _selectedAnomaly < _anomalies.Count)
        {
            context.Io.Line($"[grey]{Markup.Escape(context.L("AnalysisView.AnomaliesLabel"))}: " +
                            $"{Markup.Escape(FirstLine(AnomalyDetailText(context, _anomalies[_selectedAnomaly])))}[/]");
        }

        if (_selectedStall >= 0 && _selectedStall < _stalls.Count)
        {
            context.Io.Line($"[grey]{Markup.Escape(context.L("AnalysisView.StallsLabel"))}: " +
                            $"{Markup.Escape(FirstLine(StallDetailText(context, _stalls[_selectedStall])))}[/]");
        }
    }

    private void RenderSmartSection(TuiContext context, int width, bool full)
    {
        context.Io.Line($"[bold]{Markup.Escape(context.L("AnalysisView.SmartTrends"))}[/]");

        if (_trends is null)
        {
            context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.Analysis.Smart.Unavailable"))}[/]");
            return;
        }

        context.Io.Line($"[grey]{Markup.Escape(_trends.Summary)}[/]");
        string dateRange = _trends.FirstSnapshot is not null && _trends.LastSnapshot is not null
            ? context.L("Tui.Analysis.Smart.DateRange", _trends.FirstSnapshot.Value, _trends.LastSnapshot.Value)
            : context.L("Common.NA");
        context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.Analysis.Smart.Snapshots", _trends.SnapshotCount))} " +
                        $"{Markup.Escape(dateRange)}[/]");

        if (_trends.WearAssessment is not null)
        {
            context.Io.Line($"[{TuiTheme.Warning.ToMarkup()}]{Markup.Escape(_trends.WearAssessment.Description)}[/]");
        }

        if (_trends.SnapshotCount < 2)
        {
            context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.Analysis.Smart.NeedTwo"))}[/]");
            return;
        }

        if (!full)
        {
            return;
        }

        RenderTrendChart(context, width, "Teplota", "AnalysisView.Temperature", TuiTheme.Warning);
        RenderTrendChart(context, width, "Reallocated Sectors", "AnalysisView.ReallocatedSectors", TuiTheme.Danger);
        RenderTrendChart(context, width, "Wear Leveling", "AnalysisView.WearLeveling", TuiTheme.Success);
        RenderTrendChart(context, width, "Percentage Used", "AnalysisView.PercentUsed", TuiTheme.Accent);
    }

    private void RenderTrendChart(TuiContext context, int width, string metricName, string captionKey, Color color)
    {
        var trend = _trends?.Trends.FirstOrDefault(item => string.Equals(item.MetricName, metricName, StringComparison.Ordinal));
        if (trend is null)
        {
            return;
        }

        var valid = trend.Points.Where(point => point.Value.HasValue).OrderBy(point => point.Timestamp).ToList();
        if (valid.Count < 2)
        {
            return;
        }

        var points = valid.Select((point, index) => new TuiPoint(index, point.Value!.Value));
        var chart = TuiChartFactory.Line(points, context.L(captionKey), color, "F1");
        chart.MinY = 0;

        context.Io.Line($"[grey]{Markup.Escape(context.L(captionKey))} · {Markup.Escape(trend.Description)}[/]");
        context.Io.Write(chart.Render(width, 6));
    }

    // ========== Text helpers ==========

    private (double MinX, double MaxX) ResolveZoomBounds() =>
        _zoomStart is not null && _zoomEnd is not null ? (_zoomStart.Value, _zoomEnd.Value) : (0d, 100d);

    private string ZoomText(TuiContext context) => _zoomStart is not null && _zoomEnd is not null
        ? context.L("Tui.Analysis.Zoom.Text", _zoomStart.Value, _zoomEnd.Value)
        : context.L("Analysis.Status.ZoomFull");

    private static string SummaryLine(TuiContext context, TestAnalysisSummary summary) =>
        context.L(
            "Tui.Analysis.Summary.Line",
            summary.DiskModel,
            summary.TestType.ToString(),
            summary.StartedAt.ToString("g", CultureInfo.CurrentCulture),
            summary.Grade,
            summary.Score);

    private static string AnomalyDisplayText(TestAnomalyEvent anomaly) => string.Create(
        CultureInfo.InvariantCulture,
        $"Severity {anomaly.SeverityScore:F0} | @{anomaly.StartProgressPercent:F1}-{anomaly.EndProgressPercent:F1}% | {anomaly.MinSpeedMBps:F1}-{anomaly.MaxSpeedMBps:F1} MB/s");

    private static string AnomalyDetailText(TuiContext context, TestAnomalyEvent anomaly) => context.L(
        "Analysis.Detail.Anomaly",
        anomaly.Phase.ToString(),
        anomaly.StartProgressPercent,
        anomaly.EndProgressPercent,
        anomaly.DurationMs / 1000d,
        anomaly.MinSpeedMBps,
        anomaly.AvgSpeedMBps,
        anomaly.MaxSpeedMBps,
        anomaly.MaxDeviationPercent,
        anomaly.SeverityScore,
        anomaly.StartLba512,
        anomaly.EndLba512,
        anomaly.StartBytesProcessed,
        anomaly.EndBytesProcessed,
        anomaly.DefectType ?? context.L("Common.NA"));

    private static string StallDisplayText(TestStallEvent stall) => string.Create(
        CultureInfo.InvariantCulture,
        $"{stall.DurationMs / 1000d:F2}s | @{stall.StartProgressPercent:F1}-{stall.EndProgressPercent:F1}% | {FormatSpeed(stall.LastSpeedBeforeStallMBps)} -> {FormatSpeed(stall.FirstSpeedAfterStallMBps)}");

    private static string StallDetailText(TuiContext context, TestStallEvent stall) => context.L(
        "Analysis.Detail.Stall",
        stall.Phase.ToString(),
        stall.StartedAtUtc,
        stall.EndedAtUtc,
        stall.DurationMs / 1000d,
        stall.StartProgressPercent,
        stall.EndProgressPercent,
        stall.BytesProcessed,
        FormatSpeed(stall.LastSpeedBeforeStallMBps),
        FormatSpeed(stall.FirstSpeedAfterStallMBps));

    private static string FormatSpeed(double? value) =>
        value.HasValue ? value.Value.ToString("F1", CultureInfo.InvariantCulture) + " MB/s" : "n/a";

    private static string PercentText(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static string FirstLine(string text)
    {
        int separator = text.IndexOf('\n', StringComparison.Ordinal);
        return separator < 0 ? text : text[..separator];
    }

    private static bool TryParsePercent(string? text, out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string cleaned = text.Trim().TrimEnd('%').Replace(',', '.').Trim();
        if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return false;
        }

        if (parsed < 0d || parsed > 100d)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool IsWritePhase(TelemetrySamplePhase phase) =>
        phase is TelemetrySamplePhase.Write or TelemetrySamplePhase.Sanitize1Write or TelemetrySamplePhase.Sanitize2Write;

    private static bool IsReadPhase(TelemetrySamplePhase phase) =>
        phase is TelemetrySamplePhase.Read or TelemetrySamplePhase.Sanitize1Read or TelemetrySamplePhase.Sanitize2Read;

    private static bool IsServiceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or ArgumentException or FormatException or TimeoutException;
}
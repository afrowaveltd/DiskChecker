using System.Globalization;
using DiskChecker.Core.Interfaces;
using DiskChecker.TUI.Services;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Seek test screen: SMART driven recommendation, configurable seek patterns, live
/// latency measurement and persistence of the result, seek samples and a certificate.
/// </summary>
public sealed class SeekTestScreen : ITuiScreen
{
    private CoreDriveInfo? _disk;
    private SmartaData? _smart;
    private SeekTestRecommendation? _recommendation;
    private SeekTestResult? _lastResult;
    private int? _lastSessionId;
    private string _status = string.Empty;

    public string Id => ScreenIds.SeekTest;

    public string TitleKey => "Nav.SeekTest";

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        _disk = context.Selection.SelectedDisk;
        if (_disk is null)
        {
            context.Dialogs.Warning("Seek test", "Nejprve vyberte disk na obrazovce výběru disků.");
            return ScreenResult.Back();
        }

        await LoadRecommendationAsync(context, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            Render(context);
            var choice = context.Io.Choose("Seek test", BuildActions(context), pageSize: 12);
            switch (choice)
            {
                case "recommended":
                    await RunSeekAsync(context, _recommendation?.RecommendedType ?? SeekTestType.Random,
                        _recommendation?.RecommendedSeekCount ?? 1000, cancellationToken).ConfigureAwait(false);
                    break;
                case "full-stroke":
                    await RunSeekAsync(context, SeekTestType.FullStroke, 1000, cancellationToken).ConfigureAwait(false);
                    break;
                case "random":
                    await RunSeekAsync(context, SeekTestType.Random, 1500, cancellationToken).ConfigureAwait(false);
                    break;
                case "skip":
                    await RunSeekAsync(context, SeekTestType.Skip, 1000, cancellationToken).ConfigureAwait(false);
                    break;
                case "custom":
                    await RunCustomAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "refresh":
                    await LoadRecommendationAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "details":
                    ShowDetails(context);
                    context.Io.Pause();
                    break;
                case "certificate":
                    return NavigateToCertificate(context);
                case "back":
                    return ScreenResult.Back();
                case TuiKeys.MainMenu:
                    return ScreenResult.Menu();
                default:
                    return ScreenResult.Back();
            }
        }
    }

    private async Task LoadRecommendationAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        _status = "Načítám doporučení podle SMART…";
        try
        {
            var seekService = context.GetRequiredService<SeekTestService>();
            _recommendation = await seekService.GetRecommendationAsync(_disk, cancellationToken).ConfigureAwait(false);

            using var scope = context.CreateScope();
            var smartCheck = scope.ServiceProvider.GetRequiredService<SmartCheckService>();
            if (_disk.SupportsSmart)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                _smart = await smartCheck.GetSmartaDataSnapshotAsync(_disk, timeout.Token).ConfigureAwait(false);
            }

            _status = _recommendation.IsTooFragile
                ? "Disk je příliš opotřebený pro seek test – test nelze spustit."
                : $"Doporučení načteno: {_recommendation.RecommendedSeekCount} seeků ({_recommendation.RecommendedType}).";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _recommendation = null;
            _status = "Doporučení se nepodařilo načíst: " + ex.Message;
        }
    }

    private IReadOnlyList<TuiChoice> BuildActions(TuiContext context)
    {
        var actions = new List<TuiChoice>();

        if (_recommendation is { IsTooFragile: false })
        {
            actions.Add(new TuiChoice(
                "recommended",
                $"▶ Doporučený test ({_recommendation.RecommendedType}, {_recommendation.RecommendedSeekCount} seeků)"));
        }

        actions.Add(new TuiChoice("full-stroke", "↔ Full stroke (1000 seeků)"));
        actions.Add(new TuiChoice("random", "🎲 Náhodný (1500 seeků)"));
        actions.Add(new TuiChoice("skip", "⏭ Přeskakování (1000 seeků)"));
        actions.Add(new TuiChoice("custom", "⚙ Vlastní počet seeků"));
        actions.Add(new TuiChoice("refresh", "🔄 " + context.L("Common.Refresh")));

        if (_lastResult is not null)
        {
            actions.Add(new TuiChoice("details", "📊 Detail posledního měření"));
        }

        if (_lastSessionId is not null)
        {
            actions.Add(new TuiChoice("certificate", "🏅 Certifikát"));
        }

        actions.Add(new TuiChoice("back", "↩ Zpět"));
        actions.Add(new TuiChoice(TuiKeys.MainMenu, "☰ Navigace"));
        return actions;
    }

    private void Render(TuiContext context)
    {
        var io = context.Io;
        io.Clear();
        io.Rule("Seek test");

        if (_disk is null)
        {
            io.Line("[red]Není vybrán žádný disk.[/]");
            return;
        }

        io.Write(ScreenWidgets.KeyValue("Disk", new[]
        {
            ("Model", _disk.Name),
            ("Cesta", _disk.Path),
            ("Kapacita", TuiFormats.Capacity(_disk.TotalSize)),
            ("SMART", _smart is null
                ? "nedostupné"
                : $"teplota {TuiFormats.Temperature(_smart.Temperature)}, POH {(_smart.PowerOnHours?.ToString(CultureInfo.InvariantCulture) ?? "-")} h"),
        }));

        if (_recommendation is not null)
        {
            string badge = _recommendation.IsTooFragile
                ? "[red]❌ Test NEDOPORUČEN[/]"
                : _recommendation.IsConservative
                    ? "[yellow]⚠️ Konzervativní režim[/]"
                    : "[green]✅ Plný režim[/]";

            io.Write(ScreenWidgets.KeyValue("Doporučení", new[]
            {
                ("Stav", Markup.Remove(badge)),
                ("Typ", _recommendation.RecommendedType.ToString()),
                ("Počet seeků", _recommendation.RecommendedSeekCount.ToString()),
                ("Max. bezpečně", _recommendation.MaxSafeSeekCount.ToString()),
                ("Odůvodnění", TuiFormats.Ellipsis(_recommendation.Rationale, Math.Max(20, io.Width - 30))),
            }));
        }
        else
        {
            io.Line("[grey]Doporučení není k dispozici.[/]");
        }

        if (_lastResult is not null)
        {
            io.Write(ScreenWidgets.Metrics(new (string, string)[]
            {
                ("Průměr", TuiFormats.Latency(_lastResult.AverageLatencyMs)),
                ("Medián", TuiFormats.Latency(_lastResult.MedianLatencyMs)),
                ("Minimum", TuiFormats.Latency(_lastResult.MinLatencyMs)),
                ("Maximum", TuiFormats.Latency(_lastResult.MaxLatencyMs)),
                ("P95", TuiFormats.Latency(_lastResult.P95LatencyMs)),
                ("P99", TuiFormats.Latency(_lastResult.P99LatencyMs)),
                ("Chyby", _lastResult.ErrorCount.ToString()),
                ("Vzorků", _lastResult.Samples.Count.ToString()),
            }));
        }

        if (!string.IsNullOrWhiteSpace(_status))
        {
            io.Line($"[grey]{Markup.Escape(_status)}[/]");
        }

        context.RenderStatusBar();
    }

    private void ShowDetails(TuiContext context)
    {
        context.Io.Clear();
        context.Io.Rule("Detail seek testu");
        if (_lastResult is null)
        {
            context.Io.Line("[grey]Zatím nebylo provedeno žádné měření.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("#")
            .AddColumn(new TableColumn("Latence").RightAligned())
            .AddColumn(new TableColumn("Zdroj LBA").RightAligned())
            .AddColumn(new TableColumn("Cíl LBA").RightAligned())
            .AddColumn(new TableColumn("Vzdálenost").RightAligned())
            .AddColumn("Chyba");

        foreach (var sample in _lastResult.Samples.Take(40))
        {
            table.AddRow(
                sample.Index.ToString(),
                TuiFormats.Latency(sample.LatencyMs),
                sample.SourceLba.ToString(),
                sample.DestinationLba.ToString(),
                sample.SeekDistance.ToString(),
                sample.HasError ? "[red]" + Markup.Escape(sample.ErrorMessage ?? "chyba") + "[/]" : string.Empty);
        }

        context.Io.Write(table);
    }

    private async Task RunCustomAsync(TuiContext context, CancellationToken cancellationToken)
    {
        string? input = context.Io.Ask("Zadejte počet seeků (100–5000):", "1000");
        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0)
        {
            context.Dialogs.Warning("Seek test", "Neplatný počet seeků.");
            return;
        }

        count = Math.Clamp(count, 100, 5000);
        await RunSeekAsync(context, SeekTestType.Random, count, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunSeekAsync(
        TuiContext context,
        SeekTestType type,
        int seekCount,
        CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        if (_recommendation is { IsTooFragile: true })
        {
            if (!context.Dialogs.Confirm(
                    "Disk je příliš opotřebený",
                    "Doporučení varuje před poškozením disku. Chcete test přesto spustit?",
                    danger: true))
            {
                return;
            }
        }

        var seekService = context.GetRequiredService<SeekTestService>();
        var request = new SeekTestRequest
        {
            Drive = _disk,
            TestType = type,
            SeekCount = seekCount,
            SkipSegments = 1000,
            BlockSizeBytes = 4096,
            TimeoutSeconds = Math.Max(60, seekCount / 10 + 30),
        };

        var progress = new TuiProgress();
        var chart = new TuiChart { MinX = 0, MaxX = seekCount, YFormat = "F1" };
        var series = chart.AddSeries("Latence", TuiTheme.Accent);
        progress.Chart = chart;
        progress.Phase = "Seek test";

        SeekTestResult? result = null;
        using var operationScope = context.GetRequiredService<TuiCancellation>().BeginOperation();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationScope.Token, cancellationToken);
        using var navigationLock = context.LockNavigation("Probíhá seek test – navigace je zablokovaná.");

        await context.Io.RunLiveAsync("Seek test", progress, async state =>
        {
            var startedAt = DateTime.UtcNow;
            try
            {
                result = await seekService.RunAsync(request, update =>
                {
                    state.Percent = update.PercentComplete;
                    state.SetMetric("Seeky", $"{update.SeeksCompleted} / {update.TotalSeeks}");
                    state.SpeedText = TuiFormats.Latency(update.CurrentAverageLatencyMs);
                    if (update.LatestSample is { HasError: false } sample)
                    {
                        series.Add(update.SeeksCompleted, sample.LatencyMs);
                    }
                }, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                state.AddLog("Test přerušen uživatelem.");
                return;
            }

            state.Percent = result.IsCompleted ? 100 : state.Percent;
            state.SpeedText = TuiFormats.Latency(result.AverageLatencyMs);
            state.ElapsedText = TuiFormats.Duration(DateTime.UtcNow - startedAt);
            state.AddLog($"Průměr {TuiFormats.Latency(result.AverageLatencyMs)}, P95 {TuiFormats.Latency(result.P95LatencyMs)}, chyby {result.ErrorCount}");
        }, cancellationToken).ConfigureAwait(false);

        _lastResult = result;
        if (result is null)
        {
            _status = "Seek test nevrátil výsledek.";
            return;
        }

        _status = result.IsCompleted
            ? $"Test dokončen – průměrná latence {TuiFormats.Latency(result.AverageLatencyMs)}."
            : result.WasAborted
                ? "Test byl přerušen: " + (result.Notes ?? "bez dokončení")
                : "Test nedokončen.";

        if (result.IsCompleted)
        {
            await PersistAsync(context, result, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistAsync(TuiContext context, SeekTestResult result, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        try
        {
            using var scope = context.CreateScope();
            var cardService = scope.ServiceProvider.GetRequiredService<DiskCardTestService>();
            var repository = scope.ServiceProvider.GetRequiredService<IDiskCardRepository>();
            var certificateGenerator = scope.ServiceProvider.GetRequiredService<ICertificateGenerator>();

            var card = await cardService.GetOrCreateCardAsync(_disk, _smart, cancellationToken).ConfigureAwait(false);
            var grade = SeekGrade.Calculate(result);
            var session = new TestSession
            {
                DiskCardId = card.Id,
                SessionId = Guid.NewGuid(),
                TestType = TestType.Seek,
                StartedAt = result.StartedAtUtc,
                CompletedAt = DateTime.UtcNow,
                Duration = result.Duration,
                Status = TestStatus.Completed,
                IsDestructive = false,
                Result = TestResult.Pass,
                Grade = grade.Grade,
                Score = (int)Math.Round(grade.Score),
                Notes = $"Seek test: {result.TestType}, {result.SeekCount} seeků, avg {result.AverageLatencyMs:F2} ms",
                SmartBefore = _smart,
                AverageReadSpeedMBps = 0,
            };

            session = await repository.CreateTestSessionAsync(session).ConfigureAwait(false);
            await repository.CreateSeekSamplesAsync(session.Id, result.TestType, result.Samples).ConfigureAwait(false);

            var certificate = await certificateGenerator.GenerateCertificateAsync(session, card).ConfigureAwait(false);
            certificate.CertificateNumber = $"SEEK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24];
            certificate.TestType = "Seek test (" + result.TestType + ")";
            certificate.SeekAvgLatencyMs = result.AverageLatencyMs;
            certificate.SeekMinLatencyMs = result.MinLatencyMs;
            certificate.SeekMaxLatencyMs = result.MaxLatencyMs;
            certificate.SeekStdDevLatencyMs = result.LatencyStdDevMs;
            certificate.SeekMedianLatencyMs = result.MedianLatencyMs;
            certificate.SeekP95LatencyMs = result.P95LatencyMs;
            certificate.SeekP99LatencyMs = result.P99LatencyMs;
            certificate.SeekTotalCount = result.Samples.Count;
            certificate.SeekErrorCount = result.ErrorCount;
            certificate.SeekTestSummary =
                $"{result.TestType}: {result.SeekCount} seeků, avg {result.AverageLatencyMs:F2} ms, p95 {result.P95LatencyMs:F2} ms";
            await repository.CreateCertificateAsync(certificate).ConfigureAwait(false);

            _lastSessionId = session.Id;
            context.Selection.SelectedDiskCardId = card.Id;
            context.Selection.SelectedTestSessionId = session.Id;
            context.Selection.SelectedCertificateId = certificate.Id;
            _status += $" Výsledek uložen (známka {grade.Grade}).";

            if (!string.IsNullOrWhiteSpace(certificate.PdfPath))
            {
                _status += " PDF: " + certificate.PdfPath;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            _status += " Uložení selhalo: " + ex.Message;
        }
    }

    private ScreenResult NavigateToCertificate(TuiContext context)
    {
        if (_lastSessionId is null)
        {
            context.Dialogs.Warning("Certifikát", "Nejprve dokončete seek test.");
            return ScreenResult.Stay();
        }

        return ScreenResult.Navigate(ScreenIds.Certificate);
    }
}

/// <summary>Seek grading rules shared with the desktop implementation.</summary>
public static class SeekGrade
{
    public static (string Grade, double Score) Calculate(SeekTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var samples = result.Samples.Where(sample => !sample.HasError).Select(sample => sample.LatencyMs).ToList();
        if (!result.IsCompleted || result.WasAborted || samples.Count == 0)
        {
            return ("F", 0);
        }

        double average = samples.Average();
        if (result.TestType == SeekTestType.FullStroke)
        {
            return average switch
            {
                < 15 => ("A", 90 + (15 - average) * 0.6),
                < 20 => ("B", 75 + (20 - average)),
                < 25 => ("C", 60 + (25 - average) * 2),
                < 30 => ("D", 45 + (30 - average) * 2),
                _ => ("E", Math.Max(1, 25 - (average - 30) * 0.5)),
            };
        }

        double score = 100;
        double errorRate = result.Samples.Count == 0 ? 0 : result.ErrorCount / (double)result.Samples.Count;
        if (errorRate > 0.10) score -= 40;
        else if (errorRate > 0.05) score -= 25;
        else if (errorRate > 0.02) score -= 15;
        else if (errorRate > 0) score -= 5;

        if (average > 100) score -= 15;
        else if (average > 60) score -= 5;

        score = Math.Clamp(score, 0, 100);
        string grade = score switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 55 => "D",
            >= 40 => "E",
            _ => "F",
        };
        return (grade, score);
    }
}

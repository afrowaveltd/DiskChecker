using System.Globalization;
using System.Text.Json;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Services;
using DiskChecker.TUI.Services;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Absolute destructive test: two sanitization passes, three seek patterns, SMART
/// tracking before/after, scoring, session persistence and a certificate.
/// Terminal counterpart of the desktop eight phase workflow.
/// </summary>
public sealed class AbsoluteDestructiveScreen : ITuiScreen
{
    private const int PhaseCount = 8;

    private CoreDriveInfo? _disk;
    private SmartaData? _baseline;
    private SmartaData? _final;
    private SanitizationResult? _sanitize1;
    private SanitizationResult? _sanitize2;
    private readonly Dictionary<SeekTestType, SeekTestResult> _seeks = new();
    private DiskCertificate? _certificate;
    private int? _sessionId;
    private string _status = "Připraven ke spuštění.";
    private string _results = string.Empty;
    private string _grade = "-";
    private double _score;
    private int _minTemperature;
    private int _maxTemperature;

    public string Id => ScreenIds.AbsoluteDestructive;

    public string TitleKey => "Nav.DestructiveTest";

    /// <summary>Cooldown between destructive phases; tests set this to zero.</summary>
    internal TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Seek counts per phase; tests lower them to keep runtime short.</summary>
    internal int FullStrokeSeeks { get; set; } = 800;

    internal int RandomSeeks { get; set; } = 1500;

    internal int SkipSeeks { get; set; } = 1000;

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        _disk = context.Selection.SelectedDisk;
        if (_disk is null)
        {
            context.Dialogs.Warning("Destruktivní test", "Nejprve vyberte disk na obrazovce výběru disků.");
            return ScreenResult.Back();
        }

        await LoadBaselineAsync(context, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            Render(context);
            var choice = context.Io.Choose("Absolutní destruktivní test", BuildActions(context), pageSize: 10);
            switch (choice)
            {
                case "start":
                    await RunAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "results":
                    ShowResults(context);
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

    private async Task LoadBaselineAsync(TuiContext context, CancellationToken cancellationToken)
    {
        _baseline = null;
        if (_disk is null || !_disk.SupportsSmart)
        {
            return;
        }

        try
        {
            using var scope = context.CreateScope();
            var smartCheck = scope.ServiceProvider.GetRequiredService<SmartCheckService>();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            _baseline = await smartCheck.GetSmartaDataSnapshotAsync(_disk, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
            _baseline = null;
        }
    }

    private IReadOnlyList<TuiChoice> BuildActions(TuiContext context)
    {
        var actions = new List<TuiChoice>
        {
            new("start", "⚠ SPUSTIT absolutní destruktivní test (8 fází)"),
        };

        if (!string.IsNullOrEmpty(_results))
        {
            actions.Add(new TuiChoice("results", "📊 Zobrazit výsledky"));
        }

        if (_certificate is not null)
        {
            actions.Add(new TuiChoice("certificate", "🏅 Zobrazit certifikát"));
        }

        actions.Add(new TuiChoice("back", "↩ Zpět"));
        actions.Add(new TuiChoice(TuiKeys.MainMenu, "☰ Navigace"));
        return actions;
    }

    private void Render(TuiContext context)
    {
        var io = context.Io;
        io.Clear();
        io.Rule("Absolutní destruktivní test");

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
            ("Systémový disk", _disk.IsSystemDisk ? "ANO" : "ne"),
            ("SMART před testem", FormatSmartSummary(_baseline)),
            ("SMART po testu", FormatSmartSummary(_final)),
            ("Teploty", _minTemperature > 0 ? $"{_minTemperature}–{_maxTemperature} °C" : "-"),
        }));

        io.Write(ScreenWidgets.Notice(
            "Pozor",
            "Test opakovaně přepíše celý disk nulami, změří seek latence a na konci vytvoří oddíl. " +
            "Všechna data budou nevratně zničena a test může trvat několik hodin.",
            TuiTheme.Danger,
            "⛔"));

        if (!string.IsNullOrEmpty(_results))
        {
            io.Line($"Známka: {TuiTheme.GradeMarkup(_grade)}   Skóre: {TuiTheme.ScoreMarkup(_score)}");
        }

        io.Line($"[grey]{Markup.Escape(_status)}[/]");
        context.RenderStatusBar();
    }

    private void ShowResults(TuiContext context)
    {
        context.Io.Clear();
        context.Io.Rule("Výsledky absolutního destruktivního testu");
        foreach (string line in _results.Split('\n'))
        {
            context.Io.Line(Markup.Escape(line));
        }
    }

    private ScreenResult NavigateToCertificate(TuiContext context)
    {
        if (_certificate is null)
        {
            context.Dialogs.Warning("Certifikát", "Certifikát zatím není k dispozici.");
            return ScreenResult.Stay();
        }

        context.Selection.SelectedDiskCardId = _certificate.DiskCardId;
        context.Selection.SelectedTestSessionId = _sessionId;
        context.Selection.SelectedCertificateId = _certificate.Id;
        return ScreenResult.Navigate(ScreenIds.Certificate);
    }

    private async Task RunAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        if (_disk.IsSystemDisk)
        {
            context.Dialogs.Error("Destruktivní test", "Systémový disk nelze testovat destruktivním testem.");
            return;
        }

        if (!context.Dialogs.Confirm(
                "Potvrzení destruktivního testu",
                $"Test nevratně přepíše disk:\n\n{_disk.Name}\n{_disk.Path}\n{TuiFormats.Capacity(_disk.TotalSize)}\n\n" +
                "Spustit osmifázový destruktivní test?",
                danger: true))
        {
            return;
        }

        var sanitization = context.GetRequiredService<IDiskSanitizationService>();
        var seekService = context.GetRequiredService<SeekTestService>();
        var startedAt = DateTime.UtcNow;

        _sanitize1 = null;
        _sanitize2 = null;
        _seeks.Clear();
        _final = null;
        _certificate = null;
        _results = string.Empty;
        _minTemperature = int.MaxValue;
        _maxTemperature = 0;

        var progress = new TuiProgress();
        foreach (string phase in new[]
                 {
                     "Příprava", "Sanitizace 1", "Seek: Full Stroke", "Seek: Random",
                     "Seek: Skip", "Sanitizace 2", "Finalizace", "Vytvoření oddílu",
                 })
        {
            progress.AddPhase(phase);
        }

        var chart = new TuiChart { MinX = 0, MaxX = 100, XSuffix = " %", YFormat = "F0" };
        var writeSeries = chart.AddSeries("Zápis", TuiTheme.WriteSeries);
        var readSeries = chart.AddSeries("Čtení", TuiTheme.ReadSeries);
        var seekSeries = chart.AddSeries("Seek (ms)", TuiTheme.Accent);
        progress.Chart = chart;
        progress.ShowChart = context.Io.Width >= 90;

        using var operationScope = context.GetRequiredService<TuiCancellation>().BeginOperation();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationScope.Token, cancellationToken);
        using var navigationLock = context.LockNavigation("Probíhá absolutní destruktivní test – navigace je zablokovaná.");

        bool aborted = false;
        await context.Io.RunLiveAsync("Absolutní destruktivní test", progress, async state =>
        {
            try
            {
                // Phase 0 – preparation
                state.SetPhaseState(0, TuiPhaseState.Running, "čtu SMART a teplotu");
                await LoadBaselineAsync(context, linked.Token).ConfigureAwait(false);
                UpdateTemperature(state, _baseline?.Temperature);
                state.SetPhaseState(0, TuiPhaseState.Completed, "připraveno");
                state.Percent = 100d / PhaseCount;

                // Phase 1 – sanitization #1
                await RunSanitizationPhaseAsync(context, sanitization, state, 1, writeSeries, readSeries, linked.Token)
                    .ConfigureAwait(false);
                await CooldownAsync(state, linked.Token).ConfigureAwait(false);

                // Phases 2-4 – seek patterns
                await RunSeekPhaseAsync(context, seekService, state, SeekTestType.FullStroke, 2, FullStrokeSeeks, seekSeries, linked.Token)
                    .ConfigureAwait(false);
                await CooldownAsync(state, linked.Token).ConfigureAwait(false);
                await RunSeekPhaseAsync(context, seekService, state, SeekTestType.Random, 3, RandomSeeks, seekSeries, linked.Token)
                    .ConfigureAwait(false);
                await CooldownAsync(state, linked.Token).ConfigureAwait(false);
                await RunSeekPhaseAsync(context, seekService, state, SeekTestType.Skip, 4, SkipSeeks, seekSeries, linked.Token)
                    .ConfigureAwait(false);
                await CooldownAsync(state, linked.Token).ConfigureAwait(false);

                // Phase 5 – sanitization #2
                await RunSanitizationPhaseAsync(context, sanitization, state, 5, writeSeries, readSeries, linked.Token)
                    .ConfigureAwait(false);

                // Phase 6 – finalisation
                state.SetPhaseState(6, TuiPhaseState.Running, "sestavuji výsledky");
                await CaptureFinalSmartAsync(context, linked.Token).ConfigureAwait(false);
                state.SetPhaseState(6, TuiPhaseState.Completed, "výsledky připraveny");

                // Phase 7 – partition
                state.SetPhaseState(7, TuiPhaseState.Running, "vytvářím oddíl");
                try
                {
                    var partition = await Task.Run(
                        () => sanitization.CreatePartitionAsync(_disk.Path, "Tested", format: true, progress: null, linked.Token),
                        linked.Token).ConfigureAwait(false);
                    state.SetPhaseState(7, TuiPhaseState.Completed,
                        partition.Success ? partition.FileSystem ?? "vytvořen" : partition.ErrorMessage ?? "selhalo");
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    state.SetPhaseState(7, TuiPhaseState.Failed, ex.Message);
                }

                state.Percent = 100;
            }
            catch (OperationCanceledException)
            {
                aborted = true;
                state.AddLog("Test přerušen uživatelem.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                aborted = true;
                state.AddLog("Chyba: " + ex.Message);
            }
        }, cancellationToken).ConfigureAwait(false);

        if (!aborted)
        {
            await FinalizeAsync(context, startedAt, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _status = "Test byl přerušen nebo selhal. Část fází nemusí být dokončena.";
        }
    }

    private async Task RunSanitizationPhaseAsync(
        TuiContext context,
        IDiskSanitizationService sanitization,
        TuiProgress state,
        int phaseIndex,
        TuiSeries writeSeries,
        TuiSeries readSeries,
        CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        state.SetPhaseState(phaseIndex, TuiPhaseState.Running, "zápis nul + ověření");
        double phaseBase = phaseIndex * 100d / PhaseCount;
        double phaseSpan = 100d / PhaseCount;

        bool finished = false;
        var reporter = new Progress<SanitizationProgress>(update =>
        {
            if (Volatile.Read(ref finished))
            {
                return;
            }

            state.Percent = Math.Min(99, phaseBase + (update.ProgressPercent / 100d * phaseSpan));
            state.Phase = update.Phase;
            state.SpeedText = TuiFormats.Speed(update.CurrentSpeedMBps);
            state.Errors = update.Errors;
            if (update.EstimatedTimeRemaining is { } remaining)
            {
                state.EtaText = TuiFormats.Eta(remaining);
            }

            if (update.TotalElapsed is { } elapsed)
            {
                state.ElapsedText = TuiFormats.Duration(elapsed);
            }

            if (update.CurrentSpeedMBps > 0)
            {
                var series = update.IsReadVerifyPhase ? readSeries : writeSeries;
                series.Add(update.ProgressPercent, Math.Min(update.CurrentSpeedMBps, 2000));
            }
        });

        try
        {
            var result = await Task.Run(
                () => sanitization.SanitizeDiskAsync(
                    _disk.Path,
                    _disk.TotalSize,
                    createPartition: false,
                    format: false,
                    volumeLabel: string.Empty,
                    progress: reporter,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            Volatile.Write(ref finished, true);
            if (phaseIndex == 1)
            {
                _sanitize1 = result;
            }
            else
            {
                _sanitize2 = result;
            }

            state.SetPhaseState(phaseIndex,
                result.Success ? TuiPhaseState.Completed : TuiPhaseState.Failed,
                $"Write {TuiFormats.SpeedNoUnit(result.WriteSpeedMBps)} MB/s | Read {TuiFormats.SpeedNoUnit(result.ReadSpeedMBps)} MB/s | chyby {result.ErrorsDetected}");
        }
        catch (OperationCanceledException)
        {
            state.SetPhaseState(phaseIndex, TuiPhaseState.Failed, "přerušeno");
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            state.SetPhaseState(phaseIndex, TuiPhaseState.Failed, ex.Message);
            throw;
        }
    }

    private async Task RunSeekPhaseAsync(
        TuiContext context,
        SeekTestService seekService,
        TuiProgress state,
        SeekTestType type,
        int phaseIndex,
        int seekCount,
        TuiSeries series,
        CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        state.SetPhaseState(phaseIndex, TuiPhaseState.Running, $"spouštím {type}");
        double phaseBase = phaseIndex * 100d / PhaseCount;
        double phaseSpan = 100d / PhaseCount;

        var request = new SeekTestRequest
        {
            Drive = _disk,
            TestType = type,
            SeekCount = seekCount,
            SkipSegments = 1000,
            BlockSizeBytes = 4096,
            TimeoutSeconds = Math.Max(60, seekCount / 10 + 30),
        };

        try
        {
            var result = await seekService.RunAsync(request, update =>
            {
                state.Percent = Math.Min(99, phaseBase + (update.PercentComplete / 100d * phaseSpan));
                state.SpeedText = TuiFormats.Latency(update.CurrentAverageLatencyMs);
                state.Detail = $"{update.SeeksCompleted} / {update.TotalSeeks} seeků";
                if (update.LatestSample is { HasError: false } sample)
                {
                    series.Add(update.SeeksCompleted, sample.LatencyMs);
                }
            }, cancellationToken).ConfigureAwait(false);

            _seeks[type] = result;
            UpdateTemperature(state, result.SmartaData?.Temperature);
            state.SetPhaseState(phaseIndex, TuiPhaseState.Completed,
                $"avg {result.AverageLatencyMs:F2} ms | P95 {result.P95LatencyMs:F2} ms | vzorků {result.Samples.Count}");
        }
        catch (OperationCanceledException)
        {
            state.SetPhaseState(phaseIndex, TuiPhaseState.Failed, "přerušeno");
            throw;
        }
    }

    private async Task CooldownAsync(TuiProgress state, CancellationToken cancellationToken)
    {
        if (Cooldown <= TimeSpan.Zero)
        {
            return;
        }

        var remaining = Cooldown;
        while (remaining > TimeSpan.Zero)
        {
            state.Detail = "Teplotní stabilizace: " + ((int)remaining.TotalSeconds) + " s";
            var step = TimeSpan.FromMilliseconds(500);
            await Task.Delay(step < remaining ? step : remaining, cancellationToken).ConfigureAwait(false);
            remaining -= step;
        }

        state.Detail = string.Empty;
    }

    private void UpdateTemperature(TuiProgress state, int? temperature)
    {
        if (temperature is not > 0)
        {
            return;
        }

        _minTemperature = Math.Min(_minTemperature == int.MaxValue ? temperature.Value : _minTemperature, temperature.Value);
        _maxTemperature = Math.Max(_maxTemperature, temperature.Value);
        state.TemperatureText = $"{temperature.Value}°C (min {_minTemperature}, max {_maxTemperature})";
    }

    private async Task CaptureFinalSmartAsync(TuiContext context, CancellationToken cancellationToken)
    {
        _final = null;
        if (_disk is null || !_disk.SupportsSmart)
        {
            return;
        }

        try
        {
            using var scope = context.CreateScope();
            var smartCheck = scope.ServiceProvider.GetRequiredService<SmartCheckService>();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            _final = await smartCheck.GetSmartaDataSnapshotAsync(_disk, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
            _final = null;
        }
    }

    private async Task FinalizeAsync(TuiContext context, DateTime startedAt, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        var smart = _final ?? _baseline;
        double score = ComputeScore(smart);
        _score = score;
        _grade = TuiFormats.GradeFromScore(score);
        _results = BuildResults(startedAt, smart);

        try
        {
            using var scope = context.CreateScope();
            var cardService = scope.ServiceProvider.GetRequiredService<DiskCardTestService>();
            var repository = scope.ServiceProvider.GetRequiredService<IDiskCardRepository>();
            var certificateGenerator = scope.ServiceProvider.GetRequiredService<ICertificateGenerator>();

            var card = await cardService.GetOrCreateCardAsync(_disk, smart, cancellationToken).ConfigureAwait(false);
            var session = new TestSession
            {
                DiskCardId = card.Id,
                SessionId = Guid.NewGuid(),
                TestType = TestType.AbsoluteDestructive,
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startedAt,
                Status = TestStatus.Completed,
                IsDestructive = true,
                WasLocked = _disk.IsSystemDisk,
                Result = TestResult.Pass,
                Grade = _grade,
                Score = (int)Math.Round(score),
                HealthAssessment = MapHealthAssessment(smart),
                Notes = BuildNotes(smart),
                SmartBefore = _baseline,
                SmartAfter = _final,
                AverageWriteSpeedMBps = _sanitize2?.WriteSpeedMBps ?? _sanitize1?.WriteSpeedMBps ?? 0,
                AverageReadSpeedMBps = _sanitize2?.ReadSpeedMBps ?? _sanitize1?.ReadSpeedMBps ?? 0,
                SeekResultsJson = JsonSerializer.Serialize(_seeks),
                Sanitize1ResultJson = _sanitize1 is null ? null : JsonSerializer.Serialize(_sanitize1),
                Sanitize2ResultJson = _sanitize2 is null ? null : JsonSerializer.Serialize(_sanitize2),
                StartTemperature = _baseline?.Temperature,
                MaxTemperature = _maxTemperature > 0 ? _maxTemperature : null,
                AverageTemperature = _minTemperature > 0 && _maxTemperature > 0 ? (_minTemperature + _maxTemperature) / 2.0 : null,
            };

            session = await repository.CreateTestSessionAsync(session).ConfigureAwait(false);

            var certificate = await certificateGenerator.GenerateCertificateAsync(session, card).ConfigureAwait(false);
            certificate.CertificateNumber = $"DEST-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24];
            certificate.Grade = _grade;
            certificate.Score = score;
            certificate.TestType = "Absolutní destruktivní test";
            certificate.TestDuration = session.Duration;
            certificate.ErrorCount = (_sanitize1?.ErrorsDetected ?? 0) + (_sanitize2?.ErrorsDetected ?? 0);
            certificate.SanitizationPerformed = true;
            certificate.SanitizationMethod = "Zero-fill + verify (2×)";
            certificate.DataVerified = _sanitize2?.Success ?? false;
            certificate.TemperatureRange = _maxTemperature > 0 ? $"{_minTemperature}-{_maxTemperature}°C" : "-";
            certificate.Recommended = score >= 70;
            certificate.Notes = BuildNotes(smart);
            certificate.SeekAvgLatencyMs = _seeks.Values.SelectMany(result => result.Samples).Select(sample => sample.LatencyMs).DefaultIfEmpty(0).Average();
            certificate.SeekTestSummary = string.Join(" | ", _seeks.Select(pair => $"{pair.Key}: {pair.Value.AverageLatencyMs:F2} ms"));

            certificate = await repository.CreateCertificateAsync(certificate).ConfigureAwait(false);

            _certificate = certificate;
            _sessionId = session.Id;
            context.Selection.SelectedDiskCardId = card.Id;
            context.Selection.SelectedTestSessionId = session.Id;
            context.Selection.SelectedCertificateId = certificate.Id;

            _status = $"Test dokončen – známka {_grade}, skóre {score:F0}/100, certifikát {certificate.CertificateNumber} uložen.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            _status = $"Test dokončen, ale uložení selhalo: {ex.Message}";
        }
    }

    private double ComputeScore(SmartaData? smart)
    {
        double score = 100;
        score -= ((_sanitize1?.ErrorsDetected ?? 0) + (_sanitize2?.ErrorsDetected ?? 0)) * 5;

        double write1 = _sanitize1?.WriteSpeedMBps ?? 0;
        double write2 = _sanitize2?.WriteSpeedMBps ?? 0;
        if (write1 > 0 && write2 > 0)
        {
            double degradation = (1 - (write2 / write1)) * 100;
            if (degradation > 5)
            {
                score -= (degradation - 5) * 2;
            }
        }

        var latencies = _seeks.Values.SelectMany(result => result.Samples)
            .Where(sample => !sample.HasError)
            .Select(sample => sample.LatencyMs)
            .ToList();
        if (latencies.Count > 0)
        {
            double average = latencies.Average();
            double variance = latencies.Average(value => Math.Pow(value - average, 2));
            double stdDev = Math.Sqrt(variance);
            if (average > 0 && stdDev > 0)
            {
                double cv = stdDev / average;
                if (cv > 0.50) score -= 25;
                else if (cv > 0.30) score -= 15;
                else if (cv > 0.20) score -= 8;
                else if (cv > 0.10) score -= 2;
            }

            if (average > 100) score -= 10;
            else if (average > 60) score -= 3;
        }

        if (smart is not null)
        {
            score -= Math.Min(40, (smart.ReallocatedSectorCount ?? 0) * 3.0);
            score -= Math.Min(55, (smart.PendingSectorCount ?? 0) * 18.0);
            score -= Math.Min(55, (smart.UncorrectableErrorCount ?? 0) * 15.0);
            score -= Math.Min(55, (smart.MediaErrors ?? 0) * 15.0);

            if ((smart.PendingSectorCount ?? 0) > 0 || (smart.UncorrectableErrorCount ?? 0) > 0 || smart.IsFailing)
            {
                score = Math.Min(score, 34);
            }
            else if ((smart.ReallocatedSectorCount ?? 0) > 50)
            {
                score = Math.Min(score, 49);
            }
            else if ((smart.ReallocatedSectorCount ?? 0) > 0)
            {
                score = Math.Min(score, 79);
            }
        }

        return Math.Clamp(score, 0, 100);
    }

    private string BuildResults(DateTime startedAt, SmartaData? smart)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("=== ABSOLUTNÍ DESTRUKTIVNÍ TEST – VÝSLEDKY ===");
        builder.AppendLine();
        if (_sanitize1 is not null && _sanitize2 is not null)
        {
            builder.AppendLine($"Sanitizace 1: Write {_sanitize1.WriteSpeedMBps:F1} / Read {_sanitize1.ReadSpeedMBps:F1} MB/s | chyby {_sanitize1.ErrorsDetected}");
            builder.AppendLine($"Sanitizace 2: Write {_sanitize2.WriteSpeedMBps:F1} / Read {_sanitize2.ReadSpeedMBps:F1} MB/s | chyby {_sanitize2.ErrorsDetected}");
            builder.AppendLine();
        }

        foreach (var pair in _seeks)
        {
            builder.AppendLine($"{pair.Key}: avg {pair.Value.AverageLatencyMs:F2} ms, P95 {pair.Value.P95LatencyMs:F2} ms, chyby {pair.Value.ErrorCount}");
        }

        builder.AppendLine();
        builder.AppendLine(_minTemperature > 0 ? $"Teplota: {_minTemperature}–{_maxTemperature} °C" : "Teplota: N/A");
        builder.AppendLine("SMART změny: " + BuildSmartDelta());
        builder.AppendLine($"Celková doba: {TuiFormats.Duration(DateTime.UtcNow - startedAt)}");
        builder.AppendLine($"Známka: {_grade} ({_score:F0}/100)");
        _ = smart;
        return builder.ToString();
    }

    private string BuildSmartDelta()
    {
        if (_baseline is null && _final is null)
        {
            return "SMART nedostupný – změny nelze porovnat.";
        }

        var parts = new List<string>();
        Add("Realokované", _baseline?.ReallocatedSectorCount, _final?.ReallocatedSectorCount, parts);
        Add("Čekající", _baseline?.PendingSectorCount, _final?.PendingSectorCount, parts);
        Add("Neopravitelné", _baseline?.UncorrectableErrorCount, _final?.UncorrectableErrorCount, parts);
        Add("Teplota", _baseline?.Temperature, _final?.Temperature, parts);
        return parts.Count == 0 ? "beze změn" : string.Join(" | ", parts);

        static void Add(string label, int? before, int? after, List<string> parts)
        {
            if (before is null && after is null)
            {
                return;
            }

            parts.Add($"{label}: {before?.ToString(CultureInfo.InvariantCulture) ?? "?"}→{after?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
        }
    }

    private static string BuildNotes(SmartaData? smart)
    {
        var notes = new List<string> { "Absolutní destruktivní test: 2× sanitizace, 3× seek, SMART před/po." };
        if (smart is { IsFailing: true })
        {
            notes.Add("SMART hlásí selhání disku.");
        }

        if (smart?.ReallocatedSectorCount is > 0)
        {
            notes.Add("Realokované sektory: " + smart.ReallocatedSectorCount.Value);
        }

        if (smart?.PendingSectorCount is > 0)
        {
            notes.Add("Čekající sektory: " + smart.PendingSectorCount.Value);
        }

        return string.Join("; ", notes);
    }

    private static HealthAssessment MapHealthAssessment(SmartaData? smart)
    {
        if (smart is null)
        {
            return HealthAssessment.Unknown;
        }

        if ((smart.ReallocatedSectorCount ?? 0) > 50 || (smart.PendingSectorCount ?? 0) > 10 || smart.IsFailing)
        {
            return HealthAssessment.Critical;
        }

        if ((smart.ReallocatedSectorCount ?? 0) > 10 || (smart.PendingSectorCount ?? 0) > 0)
        {
            return HealthAssessment.Fair;
        }

        return (smart.ReallocatedSectorCount ?? 0) > 0 ? HealthAssessment.Good : HealthAssessment.Excellent;
    }

    private static string FormatSmartSummary(SmartaData? smart) =>
        smart is null
            ? "nedostupný"
            : $"POH={smart.PowerOnHours?.ToString(CultureInfo.InvariantCulture) ?? "-"}h | " +
              $"Realloc={smart.ReallocatedSectorCount?.ToString(CultureInfo.InvariantCulture) ?? "-"} | " +
              $"Pending={smart.PendingSectorCount?.ToString(CultureInfo.InvariantCulture) ?? "-"} | " +
              $"Temp={smart.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "-"}°C";
}

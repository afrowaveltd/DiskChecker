using DiskChecker.Core.Interfaces;
using DiskChecker.TUI.Services;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Surface test screen: real sequential read / write+verify passes and full
/// sanitization with live telemetry, persistence into the shared history and an
/// optional certificate. Terminal counterpart of the desktop surface test.
/// </summary>
public sealed class SurfaceTestScreen : ITuiScreen
{
    private const long QuickSampleBytes = 512L * 1024 * 1024;
    private const long Megabyte = 1024L * 1024L;

    private CoreDriveInfo? _disk;
    private SmartaData? _smart;
    private TuiSurfaceTestReport? _report;
    private SanitizationResult? _sanitization;
    private int? _lastSessionId;
    private string _status = string.Empty;

    public string Id => ScreenIds.SurfaceTest;

    public string TitleKey => "Nav.SurfaceTest";

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        _disk = await ResolveDiskAsync(context, cancellationToken).ConfigureAwait(false);
        if (_disk is null)
        {
            return ScreenResult.Back();
        }

        await TryLoadSmartAsync(context, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            Render(context);
            string? choice = context.Io.Choose("Test povrchu", BuildActions(context), pageSize: 12);
            switch (choice)
            {
                case "read":
                    await RunSurfaceAsync(context, TuiSurfaceOperation.ReadScan, null, cancellationToken).ConfigureAwait(false);
                    break;
                case "quick":
                    await RunSurfaceAsync(context, TuiSurfaceOperation.ReadScan, QuickSampleBytes, cancellationToken).ConfigureAwait(false);
                    break;
                case "write":
                    await RunSurfaceAsync(context, TuiSurfaceOperation.WriteVerify, null, cancellationToken).ConfigureAwait(false);
                    break;
                case "sanitize":
                    await RunSanitizationAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "certificate":
                    return NavigateToCertificate(context);
                case "refresh":
                    await TryLoadSmartAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "back":
                    return ScreenResult.Back();
                case TuiKeys.MainMenu:
                    return ScreenResult.Menu();
                default:
                    return ScreenResult.Back();
            }
        }
    }

    private static async Task<CoreDriveInfo?> ResolveDiskAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (context.Selection.SelectedDisk is { } selected)
        {
            return selected;
        }

        IReadOnlyList<CoreDriveInfo> drives;
        try
        {
            drives = await context.DiskCache.GetDrivesAsync(forceRefresh: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            context.Dialogs.Error("Disky", "Detekce disků selhala: " + ex.Message);
            return null;
        }

        if (drives.Count == 0)
        {
            context.Dialogs.Warning("Disky", "Nebyly nalezeny žádné disky.");
            return null;
        }

        var choice = context.Io.Choose(
            "Vyberte disk pro test povrchu",
            drives.Select(drive => new TuiChoice(drive.Path, drive.Name, TuiFormats.Capacity(drive.TotalSize))).ToList());

        var disk = drives.FirstOrDefault(drive => string.Equals(drive.Path, choice, StringComparison.OrdinalIgnoreCase));
        if (disk is not null)
        {
            context.Selection.SelectedDisk = disk;
            context.Selection.SelectedDiskDisplayName = disk.Name;
        }

        return disk;
    }

    private async Task TryLoadSmartAsync(TuiContext context, CancellationToken cancellationToken)
    {
        _smart = null;
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
            _smart = await smartCheck.GetSmartaDataSnapshotAsync(_disk, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
            _smart = null;
        }
    }

    private IReadOnlyList<TuiChoice> BuildActions(TuiContext context)
    {
        var actions = new List<TuiChoice>
        {
            new("read", "📖 Čtení celého disku (nedestruktivní)", "změří propustnost všech sektorů"),
            new("quick", "⚡ Rychlý test (512 MB)", "vzorek ze začátku disku"),
            new("write", "✍ Zápis + ověření (DESTRUKTIVNÍ)", "přepíše celý disk"),
            new("sanitize", "🧹 Sanitizace (DESTRUKTIVNÍ)", "nuly, ověření, volitelně oddíl"),
        };

        if (_lastSessionId is not null)
        {
            actions.Add(new TuiChoice("certificate", "🏅 Certifikát z posledního testu"));
        }

        actions.Add(new TuiChoice("refresh", "🔄 " + context.L("Common.Refresh")));
        actions.Add(new TuiChoice("back", "↩ Zpět"));
        actions.Add(new TuiChoice(TuiKeys.MainMenu, "☰ Navigace"));
        return actions;
    }

    private void Render(TuiContext context)
    {
        var io = context.Io;
        io.Clear();
        io.Rule("Test povrchu");

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
            ("Sériové číslo", _disk.SerialNumber ?? "-"),
            ("Rozhraní", TuiFormats.BusTypeDisplay(_disk)),
            ("SMART", _smart is null
                ? "nedostupné"
                : $"teplota {TuiFormats.Temperature(_smart.Temperature)}, " +
                  $"POH {(_smart.PowerOnHours is > 0 ? TuiFormats.Number(_smart.PowerOnHours.Value) + " h" : "-")}"),
        }));

        if (_report is not null)
        {
            var metrics = new List<(string, string)>
            {
                ("Přečteno/zapsáno", TuiFormats.Bytes(_report.ProcessedBytes)),
                ("Průměr", TuiFormats.Speed(_report.AverageSpeedMbps)),
                ("Minimum", TuiFormats.Speed(_report.MinSpeedMbps)),
                ("Maximum", TuiFormats.Speed(_report.MaxSpeedMbps)),
                ("Chyby", _report.ErrorCount.ToString()),
                ("Trvání", TuiFormats.Duration(_report.Duration)),
            };
            io.Write(ScreenWidgets.Metrics(metrics));
        }

        if (_sanitization is not null)
        {
            var metrics = new List<(string, string)>
            {
                ("Sanitizace", _sanitization.Success ? "úspěšná" : "selhala"),
                ("Zápis", TuiFormats.Speed(_sanitization.WriteSpeedMBps)),
                ("Čtení", TuiFormats.Speed(_sanitization.ReadSpeedMBps)),
                ("Chyby", _sanitization.ErrorsDetected.ToString()),
                ("Oddíl", _sanitization.PartitionCreated ? (_sanitization.FileSystem ?? "vytvořen") : "nevytvořen"),
            };
            io.Write(ScreenWidgets.Metrics(metrics));
        }

        if (!string.IsNullOrWhiteSpace(_status))
        {
            io.Line($"[grey]{Markup.Escape(_status)}[/]");
        }

        context.RenderStatusBar();
    }

    private async Task RunSurfaceAsync(
        TuiContext context,
        TuiSurfaceOperation operation,
        long? maxBytes,
        CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        bool destructive = operation == TuiSurfaceOperation.WriteVerify;
        if (destructive)
        {
            if (!context.Dialogs.Confirm(
                    "Destruktivní test povrchu",
                    $"Test ZAPÍŠE data na celý disk a přepíše veškerý jeho obsah.\n\n" +
                    $"{_disk.Name}\n{_disk.Path}\n{TuiFormats.Capacity(_disk.TotalSize)}\n\nPokračovat?",
                    danger: true))
            {
                return;
            }
        }

        var runner = context.GetRequiredService<ITuiSurfaceTestRunner>();
        var options = new TuiSurfaceTestOptions(operation, Megabyte, maxBytes);
        var progress = new TuiProgress();
        var chart = new TuiChart { MinX = 0, MaxX = 100, XSuffix = " %", YFormat = "F0" };
        var series = chart.AddSeries(operation == TuiSurfaceOperation.ReadScan ? "Čtení" : "Zápis", TuiTheme.ReadSeries);
        progress.Chart = chart;

        TuiSurfaceTestReport? report = null;
        bool finished = false;
        using var operationScope = context.GetRequiredService<TuiCancellation>().BeginOperation();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationScope.Token, cancellationToken);
        using var navigationLock = context.LockNavigation("Probíhá test povrchu – navigace je zablokovaná.");

        await context.Io.RunLiveAsync("Test povrchu", progress, async state =>
        {
            var reporter = new Progress<SurfaceTestProgress>(update =>
            {
                if (Volatile.Read(ref finished))
                {
                    return;
                }

                state.Percent = update.PercentComplete;
                state.SpeedText = TuiFormats.Speed(update.CurrentThroughputMbps);
                state.Phase = update.Phase ?? "Test";
                state.SetMetric("Přeneseno", TuiFormats.Bytes(update.BytesProcessed));
                if (update.TotalElapsed is { } elapsed)
                {
                    state.ElapsedText = TuiFormats.Duration(elapsed);
                }

                if (update.PercentComplete > 0)
                {
                    double total = (update.TotalElapsed?.TotalSeconds ?? 0) / (update.PercentComplete / 100d);
                    if (total > 0)
                    {
                        state.EtaText = TuiFormats.Eta(TimeSpan.FromSeconds(Math.Max(0, total - (update.TotalElapsed?.TotalSeconds ?? 0))));
                    }
                }

                if (update.CurrentThroughputMbps > 0)
                {
                    series.Add(update.PercentComplete, Math.Min(update.CurrentThroughputMbps, chart.MaxY ?? 2000));
                }
            });

            report = await runner.RunAsync(_disk.Path, _disk.TotalSize, options, reporter, linked.Token).ConfigureAwait(false);
            Volatile.Write(ref finished, true);
            state.Percent = report.Completed ? 100 : report.TotalBytes > 0
                ? report.ProcessedBytes * 100d / report.TotalBytes : 0;
            state.SpeedText = TuiFormats.Speed(report.AverageSpeedMbps);
            state.AddLog(report.Completed
                ? "Test dokončen: " + TuiFormats.Speed(report.AverageSpeedMbps)
                : report.Cancelled ? "Test přerušen." : "Test selhal: " + (report.FailureMessage ?? "neznámá chyba"));
        }, cancellationToken).ConfigureAwait(false);

        _report = report;
        if (report is null)
        {
            return;
        }

        _status = report.Completed
            ? $"Test dokončen – průměr {TuiFormats.Speed(report.AverageSpeedMbps)}, chyby {report.ErrorCount}."
            : report.Cancelled ? "Test byl přerušen." : "Test selhal: " + (report.FailureMessage ?? "neznámá chyba");

        await PersistAsync(context, report, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistAsync(TuiContext context, TuiSurfaceTestReport report, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        try
        {
            var result = new SurfaceTestResult
            {
                StartedAtUtc = DateTime.UtcNow - report.Duration,
                CompletedAtUtc = DateTime.UtcNow,
                DriveModel = _disk.Name,
                DriveSerialNumber = _disk.SerialNumber,
                DriveTotalBytes = _disk.TotalSize,
                DriveInterface = TuiFormats.BusTypeDisplay(_disk),
                Operation = report.Operation == TuiSurfaceOperation.WriteVerify
                    ? SurfaceTestOperation.WritePattern : SurfaceTestOperation.ReadOnly,
                FullDiskRead = report.Operation == TuiSurfaceOperation.ReadScan
                    && report.TotalBytes == _disk.TotalSize && report.Completed,
                Technology = _smart?.DeviceType?.Contains("nvme", StringComparison.OrdinalIgnoreCase) == true
                    ? DriveTechnology.Nvme
                    : _smart?.DeviceType?.Contains("ssd", StringComparison.OrdinalIgnoreCase) == true
                        ? DriveTechnology.Ssd
                        : DriveTechnology.Unknown,
                TotalBytesTested = report.ProcessedBytes,
                AverageSpeedMbps = report.AverageSpeedMbps,
                PeakSpeedMbps = report.MaxSpeedMbps,
                MinSpeedMbps = report.MinSpeedMbps,
                ErrorCount = report.ErrorCount,
                CurrentTemperatureCelsius = _smart?.Temperature,
                PowerOnHours = _smart?.PowerOnHours,
                ReallocatedSectors = _smart?.ReallocatedSectorCount,
                Samples = report.Samples,
            };

            using var scope = context.CreateScope();
            var cardService = scope.ServiceProvider.GetRequiredService<DiskCardTestService>();
            var card = await cardService.GetOrCreateCardAsync(_disk, _smart, cancellationToken).ConfigureAwait(false);
            var session = await cardService.SaveSurfaceTestAsync(card, result, _smart, cancellationToken).ConfigureAwait(false);
            _lastSessionId = session.Id;
            context.Selection.SelectedDiskCardId = card.Id;
            context.Selection.SelectedTestSessionId = session.Id;
            _status += " Výsledek uložen do historie.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            _status += " Uložení do historie selhalo: " + ex.Message;
        }
    }

    private async Task RunSanitizationAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        if (!context.Dialogs.Confirm(
                "Sanitizace disku",
                $"Sanitizace přepíše celý disk nulami a poté jej ověří čtením.\n\n" +
                $"{_disk.Name}\n{_disk.Path}\n{TuiFormats.Capacity(_disk.TotalSize)}\n\n" +
                "Všechna data budou nevratně zničena. Pokračovat?",
                danger: true))
        {
            return;
        }

        string? pauseInput = context.Dialogs.Ask(
            "Pauza po každých 10 GiB zápisu, v sekundách (0 = bez pauzy, 1–60):", "0");
        if (pauseInput is null)
        {
            _status = "Sanitizace zrušena.";
            return;
        }
        if (!int.TryParse(pauseInput, out int pauseSeconds) || pauseSeconds is < 0 or > 60)
        {
            context.Dialogs.Warning("Neplatná prodleva", "Zadejte celé číslo od 0 do 60 sekund.");
            return;
        }

        bool createPartition = context.Dialogs.Confirm(
            "Oddíl po sanitizaci",
            "Má se po dokončení vytvořit oddíl a formátovat jej?",
            danger: false);
        var sanitization = context.GetRequiredService<IDiskSanitizationService>();
        var progress = new TuiProgress();
        var chart = new TuiChart { MinX = 0, MaxX = 100, XSuffix = " %", YFormat = "F0" };
        var writeSeries = chart.AddSeries("Zápis", TuiTheme.WriteSeries);
        var readSeries = chart.AddSeries("Čtení", TuiTheme.ReadSeries);
        progress.Chart = chart;
        progress.AddPhase("Sanitizace");
        progress.SetPhaseState(0, TuiPhaseState.Running, "spuštěno");

        using var operationScope = context.GetRequiredService<TuiCancellation>().BeginOperation();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationScope.Token, cancellationToken);
        using var navigationLock = context.LockNavigation("Probíhá sanitizace – navigace je zablokovaná.");

        SanitizationResult? result = null;
        bool sanitizationFinished = false;
        await context.Io.RunLiveAsync("Sanitizace disku", progress, async state =>
        {
            var reporter = new Progress<SanitizationProgress>(update =>
            {
                if (Volatile.Read(ref sanitizationFinished))
                {
                    return;
                }

                state.Percent = update.ProgressPercent;
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

                var target = update.IsReadVerifyPhase ? readSeries : writeSeries;
                if (update.CurrentSpeedMBps > 0)
                {
                    target.Add(update.ProgressPercent, Math.Min(update.CurrentSpeedMBps, 2000));
                }

                if (!string.IsNullOrWhiteSpace(update.StatusDetail))
                {
                    state.Detail = update.StatusDetail;
                }
            });

            try
            {
                var pacing = SanitizationWritePacing.FromSeconds(pauseSeconds);
                result = await Task.Run(
                    () => pacing is null
                        ? sanitization.SanitizeDiskAsync(
                            _disk.Path, _disk.TotalSize, createPartition, createPartition,
                            "DiskChecker", reporter, linked.Token)
                        : sanitization.SanitizeDiskPacedAsync(
                            _disk.Path, _disk.TotalSize, createPartition, createPartition,
                            "DiskChecker", pacing, reporter, linked.Token),
                    linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                state.SetPhaseState(0, TuiPhaseState.Skipped, "přerušeno");
                return;
            }

            Volatile.Write(ref sanitizationFinished, true);
            state.Percent = 100;
            state.SetPhaseState(0, result.Success ? TuiPhaseState.Completed : TuiPhaseState.Failed,
                result.Success ? "dokončeno" : result.ErrorMessage ?? "selhalo");
        }, cancellationToken).ConfigureAwait(false);

        _sanitization = result;
        if (result is null)
        {
            _status = "Sanitizace byla přerušena.";
            return;
        }

        _status = result.Success
            ? $"Sanitizace dokončena – zápis {TuiFormats.Speed(result.WriteSpeedMBps)}, čtení {TuiFormats.Speed(result.ReadSpeedMBps)}."
            : "Sanitizace selhala: " + (result.ErrorMessage ?? "neznámá chyba");

        await PersistSanitizationAsync(context, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistSanitizationAsync(TuiContext context, SanitizationResult result, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        try
        {
            using var scope = context.CreateScope();
            var cardService = scope.ServiceProvider.GetRequiredService<DiskCardTestService>();
            var card = await cardService.GetOrCreateCardAsync(_disk, _smart, cancellationToken).ConfigureAwait(false);
            var session = await cardService.SaveSanitizationAsync(
                card,
                result,
                writeSamples: null,
                readSamples: null,
                smartaData: _smart,
                smartAfter: result.SmartAfter,
                cancellationToken).ConfigureAwait(false);

            _lastSessionId = session.Id;
            context.Selection.SelectedDiskCardId = card.Id;
            context.Selection.SelectedTestSessionId = session.Id;
            _status += " Výsledek uložen do historie.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            _status += " Uložení do historie selhalo: " + ex.Message;
        }
    }

    private ScreenResult NavigateToCertificate(TuiContext context)
    {
        if (_lastSessionId is null)
        {
            context.Dialogs.Warning("Certifikát", "Nejprve dokončete test.");
            return ScreenResult.Stay();
        }

        context.Selection.SelectedCertificateId = null;
        return ScreenResult.Navigate(ScreenIds.Certificate);
    }
}

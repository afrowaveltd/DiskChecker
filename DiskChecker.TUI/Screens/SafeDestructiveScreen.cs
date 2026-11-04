using System.Globalization;
using System.Text;
using System.Text.Json;
using DiskChecker.Core.Interfaces;
using DiskChecker.TUI.Services;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Safe destructive test: a verified raw sector image is created first, then the
/// disk is overwritten by the destructive test and finally restored from the image
/// and verified again. Terminal counterpart of the desktop
/// <c>SafeDestructiveTestViewModel</c>.
/// <para>
/// The workflow exists in two real modes:
/// <list type="bullet">
/// <item><c>RawBackupRestore</c> – backup, verification, destructive test, restore, verification.</item>
/// <item><c>BackupOnly</c> – verified rescue copy, the source disk is never written to.</item>
/// </list>
/// When the destructive phase already started and the run is cancelled or throws, the
/// verified image is restored automatically before the failure is reported.
/// </para>
/// </summary>
public sealed class SafeDestructiveScreen : ITuiScreen
{
    private const int PhasePrepare = 0;
    private const int PhaseBackup = 1;
    private const int PhaseVerifyBackup = 2;
    private const int PhaseTest = 3;
    private const int PhaseRestore = 4;
    private const int PhaseVerifyRestore = 5;
    private const int PhaseFinalise = 6;
    private const int PhaseCount = 7;

    private static readonly SeekTestType[] SeekPhases =
    {
        SeekTestType.FullStroke,
        SeekTestType.Random,
        SeekTestType.Skip,
    };

    private readonly Dictionary<SeekTestType, SeekTestResult> _seeks = new();

    private CoreDriveInfo? _disk;
    private TuiSafeMode _mode = TuiSafeMode.RawBackupRestore;
    private IReadOnlyList<TuiSafeBackupTarget> _targets = Array.Empty<TuiSafeBackupTarget>();
    private TuiSafeBackupTarget? _selectedTarget;
    private TuiSafeBackupResult? _backup;
    private SmartaData? _smartBefore;
    private SmartaData? _smartAfter;
    private SanitizationResult? _sanitize;
    private DiskCertificate? _certificate;
    private int? _sessionId;
    private string _status = string.Empty;
    private string _results = string.Empty;
    private string _grade = "-";
    private double _score;
    private double _backupSpeedMBps;
    private int _minTemperature;
    private int _maxTemperature;
    private bool _destructivePhaseStarted;
    private bool _restoreCompleted;

    public string Id => ScreenIds.SafeDestructive;

    public string TitleKey => "Nav.SafeDestructiveTest";

    /// <summary>Streaming workflow engine; tests replace it to control the I/O phase.</summary>
    internal TuiSafeWorkflowService Workflow { get; set; } = new();

    /// <summary>Backup volumes offered by the screen; tests inject a temporary folder.</summary>
    internal IReadOnlyList<TuiSafeBackupTarget>? BackupTargetsOverride { get; set; }

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        _disk = context.Selection.SelectedDisk;
        if (_disk is null)
        {
            context.Dialogs.Warning(
                context.L("SafeDestructive.Title"),
                context.L("Tui.SafeDestructive.NoDisk"));
            return ScreenResult.Back();
        }

        LoadTargets(context);

        while (true)
        {
            Render(context);
            string? choice = context.Io.Choose(context.L("SafeDestructive.Title"), BuildActions(context), pageSize: 10);
            switch (choice)
            {
                case "start":
                    await RunWorkflowAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "mode":
                    ShowModeSelector(context);
                    break;
                case "target":
                    ShowTargetSelector(context);
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

    // ──────────────────────────────────────────────
    //  Selection
    // ──────────────────────────────────────────────

    private void LoadTargets(TuiContext context)
    {
        long required = _disk?.TotalSize ?? 0;
        _targets = BackupTargetsOverride ?? TuiSafeWorkflowService.DiscoverTargets(_disk?.Path);

        _selectedTarget = _targets
            .Where(target => target.HasRoomFor(required))
            .OrderByDescending(target => target.FreeBytes)
            .FirstOrDefault();

        if (_selectedTarget is not null)
        {
            _status = context.L("Tui.SafeDestructive.Status.Ready");
            return;
        }

        _status = _targets.Count == 0
            ? context.L("Tui.SafeDestructive.Targets.None")
            : context.L("Tui.SafeDestructive.Targets.NotEnough", TuiFormats.Bytes(required));
    }

    private void ShowModeSelector(TuiContext context)
    {
        var choices = new List<TuiChoice>
        {
            new(
                "raw",
                context.L("SafeDestructive.Mode.BackupRestore"),
                context.L("SafeDestructive.Mode.BackupRestore.Description")),
            new(
                "backup-only",
                context.L("Tui.SafeDestructive.Mode.BackupOnly"),
                context.L("Tui.SafeDestructive.Mode.BackupOnly.Description")),
        };

        switch (context.Io.Choose(context.L("SafeDestructive.ModeTitle"), choices))
        {
            case "raw":
                _mode = TuiSafeMode.RawBackupRestore;
                break;
            case "backup-only":
                _mode = TuiSafeMode.BackupOnly;
                break;
            default:
                break;
        }
    }

    private void ShowTargetSelector(TuiContext context)
    {
        if (_targets.Count == 0)
        {
            context.Dialogs.Warning(context.L("SafeDestructive.Title"), context.L("Tui.SafeDestructive.Targets.None"));
            return;
        }

        var choices = _targets
            .Select((target, index) => new TuiChoice(
                "target-" + index.ToString(CultureInfo.InvariantCulture),
                target.Label,
                TuiFormats.Bytes(target.FreeBytes)))
            .ToList();

        string? choice = context.Io.Choose(context.L("Tui.SafeDestructive.TargetTitle"), choices);
        if (choice is null || !choice.StartsWith("target-", StringComparison.Ordinal))
        {
            return;
        }

        if (int.TryParse(choice["target-".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int selected)
            && selected >= 0 && selected < _targets.Count)
        {
            _selectedTarget = _targets[selected];
            _status = context.L("Tui.SafeDestructive.Status.Ready");
        }
    }

    // ──────────────────────────────────────────────
    //  Rendering
    // ──────────────────────────────────────────────

    private void Render(TuiContext context)
    {
        var io = context.Io;
        io.Clear();
        io.Rule(context.L("SafeDestructive.Title"));

        if (_disk is null)
        {
            io.Line($"[red]{Markup.Escape(context.L("Common.NoDiskSelected"))}[/]");
            return;
        }

        int valueWidth = Math.Max(24, io.Width - 28);
        io.Write(ScreenWidgets.KeyValue(context.L("Tui.SafeDestructive.DiskInfo"), new[]
        {
            (context.L("Tui.SafeDestructive.Label.Model"),
                TuiFormats.Ellipsis(string.IsNullOrWhiteSpace(_disk.Name) ? _disk.Path : _disk.Name, valueWidth)),
            (context.L("Tui.SafeDestructive.Label.Path"), TuiFormats.Ellipsis(_disk.Path, valueWidth)),
            (context.L("Tui.SafeDestructive.Label.Size"), TuiFormats.Bytes(_disk.TotalSize)),
            (context.L("Tui.SafeDestructive.Label.Mode"), ModeLabel(context)),
            (context.L("Tui.SafeDestructive.Label.Target"),
                _selectedTarget is null
                    ? context.L("Tui.SafeDestructive.Target.None")
                    : TuiFormats.Ellipsis(_selectedTarget.Label, valueWidth)),
            (context.L("Tui.SafeDestructive.Label.Free"), _selectedTarget is null
                ? context.L("Tui.SafeDestructive.Target.None")
                : TuiFormats.Bytes(_selectedTarget.FreeBytes)),
            (context.L("Tui.SafeDestructive.Label.SmartBefore"), FormatSmart(context, _smartBefore)),
            (context.L("Tui.SafeDestructive.Label.SmartAfter"), FormatSmart(context, _smartAfter)),
            (context.L("Tui.SafeDestructive.Label.Temperature"), _maxTemperature > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{_minTemperature}–{_maxTemperature} °C")
                : "—"),
            (context.L("Tui.SafeDestructive.Label.Result"),
                string.IsNullOrEmpty(_results)
                    ? "—"
                    : $"{context.L("Tui.SafeDestructive.Label.Grade")}: {_grade} ({_score.ToString("F0", CultureInfo.InvariantCulture)}/100)"),
        }));

        io.Write(ScreenWidgets.Notice(
            context.L("Tui.SafeDestructive.Notice.Title"),
            ModeDescription(context),
            _mode == TuiSafeMode.RawBackupRestore ? TuiTheme.Warning : TuiTheme.Success,
            "🛡"));

        if (!string.IsNullOrWhiteSpace(_status))
        {
            io.Line($"[grey]{Markup.Escape(_status)}[/]");
        }

        context.RenderStatusBar();
    }

    private string ModeLabel(TuiContext context) => _mode == TuiSafeMode.BackupOnly
        ? context.L("Tui.SafeDestructive.Mode.BackupOnly")
        : context.L("SafeDestructive.Mode.BackupRestore");

    private string ModeDescription(TuiContext context) => _mode == TuiSafeMode.BackupOnly
        ? context.L("Tui.SafeDestructive.Notice.BackupOnly")
        : context.L("Tui.SafeDestructive.Notice.BackupRestore");

    private string FormatSmart(TuiContext context, SmartaData? smart) => smart is null
        ? context.L("Tui.SafeDestructive.NoSmart")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"POH={smart.PowerOnHours?.ToString(CultureInfo.InvariantCulture) ?? "-"}h | " +
            $"Realloc={smart.ReallocatedSectorCount?.ToString(CultureInfo.InvariantCulture) ?? "-"} | " +
            $"Pending={smart.PendingSectorCount?.ToString(CultureInfo.InvariantCulture) ?? "-"} | " +
            $"Temp={smart.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "-"}°C");

    private IReadOnlyList<TuiChoice> BuildActions(TuiContext context)
    {
        bool canStart = _disk is not null && _selectedTarget is not null;

        var actions = new List<TuiChoice>
        {
            new("start", context.L("Tui.SafeDestructive.Action.Start"), Enabled: canStart),
            new("mode", context.L("Tui.SafeDestructive.Action.Mode")),
            new("target", context.L("Tui.SafeDestructive.Action.Target")),
        };

        if (!string.IsNullOrEmpty(_results))
        {
            actions.Add(new TuiChoice("results", context.L("Tui.SafeDestructive.Action.Results")));
        }

        if (_certificate is not null)
        {
            actions.Add(new TuiChoice("certificate", context.L("Tui.SafeDestructive.Action.Certificate")));
        }

        actions.Add(new TuiChoice("back", context.L("Common.Back")));
        actions.Add(new TuiChoice(TuiKeys.MainMenu, context.L("Tui.Common.Menu")));
        return actions;
    }

    private void ShowResults(TuiContext context)
    {
        context.Io.Clear();
        context.Io.Rule(context.L("Tui.SafeDestructive.Results.Title"));
        foreach (string line in _results.Split('\n'))
        {
            context.Io.Line(Markup.Escape(line.TrimEnd('\r')));
        }
    }

    private ScreenResult NavigateToCertificate(TuiContext context)
    {
        if (_certificate is null)
        {
            context.Dialogs.Warning(context.L("Tui.SafeDestructive.Action.Certificate"), context.L("Common.CertNotReady"));
            return ScreenResult.Stay();
        }

        context.Selection.SelectedDiskCardId = _certificate.DiskCardId;
        context.Selection.SelectedTestSessionId = _sessionId;
        context.Selection.SelectedCertificateId = _certificate.Id;
        return ScreenResult.Navigate(ScreenIds.Certificate);
    }

    // ──────────────────────────────────────────────
    //  Workflow
    // ──────────────────────────────────────────────

    private async Task RunWorkflowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_disk is null)
        {
            return;
        }

        if (_selectedTarget is null)
        {
            context.Dialogs.Error(context.L("SafeDestructive.Title"), context.L("Tui.SafeDestructive.Error.NoTarget"));
            return;
        }

        if (_disk.IsSystemDisk)
        {
            context.Dialogs.Error(context.L("SafeDestructive.Title"), context.L("Tui.SafeDestructive.Error.SystemDisk"));
            return;
        }

        if (_disk.TotalSize <= 0)
        {
            context.Dialogs.Error(context.L("SafeDestructive.Title"), context.L("Tui.SafeDestructive.Error.NoSize"));
            return;
        }

        string backupFolder = Path.Combine(
            _selectedTarget.Root,
            TuiSafeWorkflowService.BackupFolderPrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

        if (!context.Dialogs.Confirm(
                context.L("Tui.SafeDestructive.Confirm.Title"),
                context.L(
                    "Tui.SafeDestructive.Confirm.Body",
                    _disk.Name,
                    _disk.Path,
                    TuiFormats.Bytes(_disk.TotalSize),
                    backupFolder),
                danger: true))
        {
            _status = context.L("Tui.SafeDestructive.Status.Declined");
            return;
        }

        await ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(TuiContext context, CancellationToken cancellationToken)
    {
        if (_disk is null || _selectedTarget is null)
        {
            return;
        }

        _smartBefore = null;
        _smartAfter = null;
        _sanitize = null;
        _backup = null;
        _certificate = null;
        _sessionId = null;
        _results = string.Empty;
        _grade = "-";
        _score = 0;
        _backupSpeedMBps = 0;
        _minTemperature = 0;
        _maxTemperature = 0;
        _destructivePhaseStarted = false;
        _restoreCompleted = false;
        _seeks.Clear();

        var target = _selectedTarget;
        var startedAt = DateTime.UtcNow;
        double[] weights = BuildWeights(_mode);
        double[] bases = BuildBases(weights);

        var progress = new TuiProgress(context.L("SafeDestructive.Title"));
        foreach (string phase in new[]
                 {
                     context.L("Tui.SafeDestructive.Phase.Prepare"),
                     context.L("Tui.SafeDestructive.Phase.Backup"),
                     context.L("Tui.SafeDestructive.Phase.VerifyBackup"),
                     context.L("Tui.SafeDestructive.Phase.Test"),
                     context.L("Tui.SafeDestructive.Phase.Restore"),
                     context.L("Tui.SafeDestructive.Phase.VerifyRestore"),
                     context.L("Tui.SafeDestructive.Phase.Finalise"),
                 })
        {
            progress.AddPhase(phase);
        }

        var chart = new TuiChart { MinX = 0, MaxX = 100, XSuffix = " %", YFormat = "F0" };
        TuiSeries backupSeries = chart.AddSeries(context.L("Tui.SafeDestructive.Chart.Backup"), TuiTheme.Accent);
        TuiSeries writeSeries = chart.AddSeries(context.L("Tui.SafeDestructive.Chart.Write"), TuiTheme.WriteSeries);
        TuiSeries readSeries = chart.AddSeries(context.L("Tui.SafeDestructive.Chart.Read"), TuiTheme.ReadSeries);
        progress.Chart = chart;
        progress.ShowChart = context.Io.Width >= 90;

        using var operationScope = context.GetRequiredService<TuiCancellation>().BeginOperation();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationScope.Token, cancellationToken);
        using var navigationLock = context.LockNavigation(context.L("Tui.SafeDestructive.NavLock"));

        _status = context.L("Tui.SafeDestructive.Status.Running");
        bool aborted = false;
        string? failure = null;

        await context.Io.RunLiveAsync(context.L("SafeDestructive.Title"), progress, async state =>
        {
            try
            {
                await RunPrepareAsync(context, state, weights, bases, linked.Token).ConfigureAwait(false);
                await RunBackupAsync(context, state, weights, bases, target, backupSeries, linked.Token).ConfigureAwait(false);
                await RunVerifyBackupAsync(context, state, weights, bases, linked.Token).ConfigureAwait(false);

                if (_mode == TuiSafeMode.BackupOnly)
                {
                    string skipped = context.L("Tui.SafeDestructive.Phase.Skipped");
                    state.SetPhaseState(PhaseTest, TuiPhaseState.Skipped, skipped);
                    state.SetPhaseState(PhaseRestore, TuiPhaseState.Skipped, skipped);
                    state.SetPhaseState(PhaseVerifyRestore, TuiPhaseState.Skipped, skipped);
                }
                else
                {
                    _destructivePhaseStarted = true;
                    await RunTestAsync(context, state, weights, bases, writeSeries, readSeries, linked.Token).ConfigureAwait(false);
                    await RunRestoreAsync(context, state, weights, bases, writeSeries, linked.Token).ConfigureAwait(false);
                    await RunVerifyRestoreAsync(context, state, weights, bases, linked.Token).ConfigureAwait(false);
                }

                await RunFinaliseAsync(context, state, weights, bases, startedAt, linked.Token).ConfigureAwait(false);
                state.Percent = 100;
            }
            catch (OperationCanceledException)
            {
                aborted = true;
                if (_destructivePhaseStarted && !_restoreCompleted)
                {
                    await EmergencyRestoreAsync(context, state, weights, bases,
                        context.L("SafeDestructive.Emergency.CancelledAfterDestructive")).ConfigureAwait(false);
                }

                state.AddLog(context.L("SafeDestructive.Log.OperationCancelled"));
            }
            catch (Exception ex)
            {
                aborted = true;
                failure = ex.Message;
                if (_destructivePhaseStarted && !_restoreCompleted)
                {
                    await EmergencyRestoreAsync(context, state, weights, bases,
                        context.L("SafeDestructive.Emergency.ErrorAfterDestructive", ex.Message)).ConfigureAwait(false);
                }

                state.AddLog(context.L("Tui.SafeDestructive.Log.Failed", ex.Message));
            }
        }, cancellationToken).ConfigureAwait(false);

        if (!aborted)
        {
            return;
        }

        _status = failure is null
            ? context.L("Tui.SafeDestructive.Status.Cancelled")
            : context.L("Tui.SafeDestructive.Status.Error", failure);
    }

    private async Task RunPrepareAsync(
        TuiContext context,
        TuiProgress state,
        double[] weights,
        double[] bases,
        CancellationToken cancellationToken)
    {
        state.SetPhaseState(PhasePrepare, TuiPhaseState.Running, context.L("Tui.SafeDestructive.Detail.Prepare"));
        state.Phase = context.L("Tui.SafeDestructive.Phase.Prepare");
        state.Percent = bases[PhasePrepare];

        _smartBefore = await CaptureSmartAsync(context, cancellationToken).ConfigureAwait(false);
        UpdateTemperature(state);
        state.SetPhaseState(
            PhasePrepare,
            TuiPhaseState.Completed,
            context.L("Tui.SafeDestructive.Detail.PrepareDone", FormatSmart(context, _smartBefore)));
        state.Percent = bases[PhasePrepare] + weights[PhasePrepare];
    }

    private async Task RunBackupAsync(
        TuiContext context,
        TuiProgress state,
        double[] weights,
        double[] bases,
        TuiSafeBackupTarget target,
        TuiSeries series,
        CancellationToken cancellationToken)
    {
        var disk = _disk;
        if (disk is null)
        {
            throw new InvalidOperationException(context.L("Tui.SafeDestructive.Error.NoDisk"));
        }

        state.SetPhaseState(PhaseBackup, TuiPhaseState.Running, context.L("Tui.SafeDestructive.Detail.Backup"));
        state.Phase = context.L("Tui.SafeDestructive.Phase.Backup");

        var reporter = CreateTransferReporter(state, weights, bases, PhaseBackup, series, update => _backupSpeedMBps = update.SpeedMbps);
        _backup = await Workflow.CreateBackupAsync(
                disk.Path,
                target.Root,
                disk.TotalSize,
                disk.Name,
                reporter,
                cancellationToken)
            .ConfigureAwait(false);

        state.SetPhaseState(
            PhaseBackup,
            TuiPhaseState.Completed,
            context.L("Tui.SafeDestructive.Detail.BackupDone", TuiFormats.Bytes(_backup.TotalBytes)));
        state.SetMetric(
            context.L("Tui.SafeDestructive.Metric.Blocks"),
            (disk.TotalSize / TuiSafeWorkflowService.BlockSize).ToString(CultureInfo.InvariantCulture));
        state.AddLog(context.L("Tui.SafeDestructive.Log.BackupPath", _backup.ImagePath));
        state.Percent = bases[PhaseBackup] + weights[PhaseBackup];
    }

    private async Task RunVerifyBackupAsync(
        TuiContext context,
        TuiProgress state,
        double[] weights,
        double[] bases,
        CancellationToken cancellationToken)
    {
        var backup = _backup;
        if (backup is null)
        {
            throw new InvalidOperationException(context.L("SafeDestructive.Error.BackupNotVerified"));
        }

        state.SetPhaseState(PhaseVerifyBackup, TuiPhaseState.Running, context.L("Tui.SafeDestructive.Detail.VerifyBackup"));
        state.Phase = context.L("Tui.SafeDestructive.Phase.VerifyBackup");

        var reporter = CreateTransferReporter(state, weights, bases, PhaseVerifyBackup, null, null);
        bool verified = await Workflow.VerifyImageAsync(
                backup.ImagePath,
                backup.Sha256,
                backup.TotalBytes,
                reporter,
                cancellationToken)
            .ConfigureAwait(false);

        if (!verified)
        {
            state.SetPhaseState(PhaseVerifyBackup, TuiPhaseState.Failed, context.L("SafeDestructive.Error.BackupNotVerified"));
            throw new InvalidOperationException(context.L("SafeDestructive.Error.BackupNotVerified"));
        }

        _backup = backup with { Verified = true };

        if (backup.UnreadableBytes > 0)
        {
            state.SetPhaseState(
                PhaseVerifyBackup,
                TuiPhaseState.Failed,
                context.L("Tui.SafeDestructive.Log.Unreadable", TuiFormats.Bytes(backup.UnreadableBytes)));
            state.AddLog(context.L("Tui.SafeDestructive.Log.Unreadable", TuiFormats.Bytes(backup.UnreadableBytes)));

            if (_mode == TuiSafeMode.RawBackupRestore)
            {
                throw new InvalidOperationException(
                    context.L("Tui.SafeDestructive.Error.Unreadable", TuiFormats.Bytes(backup.UnreadableBytes)));
            }

            return;
        }

        state.SetMetric(context.L("Tui.SafeDestructive.Metric.Checksum"), backup.Sha256[..16] + "…");
        state.SetPhaseState(PhaseVerifyBackup, TuiPhaseState.Completed, context.L("Tui.SafeDestructive.Detail.Verified"));
        state.Percent = bases[PhaseVerifyBackup] + weights[PhaseVerifyBackup];
    }

    private async Task RunTestAsync(
        TuiContext context,
        TuiProgress state,
        double[] weights,
        double[] bases,
        TuiSeries writeSeries,
        TuiSeries readSeries,
        CancellationToken cancellationToken)
    {
        var disk = _disk;
        if (disk is null)
        {
            throw new InvalidOperationException(context.L("Tui.SafeDestructive.Error.NoDisk"));
        }

        state.SetPhaseState(PhaseTest, TuiPhaseState.Running, context.L("Tui.SafeDestructive.Detail.Test"));
        state.Phase = context.L("Tui.SafeDestructive.Phase.Test");

        double phaseBase = bases[PhaseTest];
        double phaseSpan = weights[PhaseTest];
        const double sanitizeShare = 0.6;
        double lastPoint = -1;

        var sanitization = context.GetRequiredService<IDiskSanitizationService>();
        var reporter = new InlineProgress<SanitizationProgress>(update =>
        {
            double fraction = Math.Clamp(update.ProgressPercent / 100d, 0, 1) * sanitizeShare;
            state.Percent = Math.Min(99, phaseBase + fraction * phaseSpan);
            state.Phase = string.IsNullOrWhiteSpace(update.Phase)
                ? context.L("Tui.SafeDestructive.Phase.Test")
                : update.Phase;
            state.SpeedText = TuiFormats.Speed(update.CurrentSpeedMBps);
            state.Errors = update.Errors;
            if (update.TotalElapsed is { } elapsed)
            {
                state.ElapsedText = TuiFormats.Duration(elapsed);
            }

            if (update.EstimatedTimeRemaining is { } remaining)
            {
                state.EtaText = TuiFormats.Eta(remaining);
            }

            state.Detail = context.L("Tui.SafeDestructive.Detail.Sanitize",
                update.ProgressPercent.ToString("F0", CultureInfo.InvariantCulture));

            if (update.CurrentSpeedMBps > 0 && update.ProgressPercent - lastPoint >= 0.5)
            {
                lastPoint = update.ProgressPercent;
                TuiSeries series = update.IsReadVerifyPhase ? readSeries : writeSeries;
                series.Add(state.Percent, Math.Min(update.CurrentSpeedMBps, 8000));
            }
        });

        _sanitize = await Task.Run(
                () => sanitization.SanitizeDiskAsync(
                    disk.Path,
                    disk.TotalSize,
                    createPartition: false,
                    format: false,
                    volumeLabel: string.Empty,
                    progress: reporter,
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        if (_sanitize is not null && !_sanitize.Success)
        {
            state.AddLog(context.L("Tui.SafeDestructive.Log.SanitizeFailed", _sanitize.ErrorMessage ?? string.Empty));
        }

        var seekService = context.GetRequiredService<SeekTestService>();
        double seekSpan = phaseSpan * (1 - sanitizeShare);
        foreach (SeekTestType type in SeekPhases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Detail = context.L("Tui.SafeDestructive.Detail.Seek", type.ToString());

            SeekTestType current = type;
            double phaseStart = phaseBase + phaseSpan * sanitizeShare;
            var result = await seekService.RunWithRecommendationAsync(
                    disk,
                    preferredType: current,
                    progressCallback: update =>
                    {
                        state.Percent = Math.Min(99, phaseStart + Math.Clamp(update.PercentComplete / 100d, 0, 1) * seekSpan);
                        state.SpeedText = TuiFormats.Latency(update.CurrentAverageLatencyMs);
                        state.Detail = string.Create(
                            CultureInfo.InvariantCulture,
                            $"{current}: {update.SeeksCompleted}/{update.TotalSeeks} seeks, {update.CurrentAverageLatencyMs:F2} ms");
                        if (update.LatestSample is { HasError: false } sample)
                        {
                            readSeries.Add(state.Percent, sample.LatencyMs);
                        }
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _seeks[current] = result;
        }

        _smartAfter = await CaptureSmartAsync(context, cancellationToken).ConfigureAwait(false);
        UpdateTemperature(state);

        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Write {_sanitize?.WriteSpeedMBps ?? 0:F1} / Read {_sanitize?.ReadSpeedMBps ?? 0:F1} MB/s | chyby {_sanitize?.ErrorsDetected ?? 0}");
        state.SetPhaseState(PhaseTest, TuiPhaseState.Completed, summary);
        state.Percent = bases[PhaseTest] + weights[PhaseTest];
    }

    private async Task RunRestoreAsync(
        TuiContext context,
        TuiProgress state,
        double[] weights,
        double[] bases,
        TuiSeries series,
        CancellationToken cancellationToken)
    {
        var disk = _disk;
        var backup = _backup;
        if (disk is null || backup is null)
        {
            throw new InvalidOperationException(context.L("Tui.SafeDestructive.Error.NoBackup"));
        }

        state.SetPhaseState(PhaseRestore, TuiPhaseState.Running, context.L("Tui.SafeDestructive.Detail.Restore"));
        state.Phase = context.L("Tui.SafeDestructive.Phase.Restore");

        var reporter = CreateTransferReporter(state, weights, bases, PhaseRestore, series, null);
        var restored = await Workflow.RestoreAsync(
                backup.ImagePath,
                disk.Path,
                backup.TotalBytes,
                reporter,
                cancellationToken)
            .ConfigureAwait(false);

        _restoreCompleted = true;
        state.SetPhaseState(
            PhaseRestore,
            TuiPhaseState.Completed,
            context.L("Tui.SafeDestructive.Detail.Restored", TuiFormats.Bytes(restored.BytesWritten)));
        state.Percent = bases[PhaseRestore] + weights[PhaseRestore];
    }

    private async Task RunVerifyRestoreAsync(
        TuiContext context,
        TuiProgress state,
        double[] weights,
        double[] bases,
        CancellationToken cancellationToken)
    {
        var disk = _disk;
        var backup = _backup;
        if (disk is null || backup is null)
        {
            throw new InvalidOperationException(context.L("Tui.SafeDestructive.Error.NoBackup"));
        }

        state.SetPhaseState(PhaseVerifyRestore, TuiPhaseState.Running, context.L("Tui.SafeDestructive.Detail.VerifyRestore"));
        state.Phase = context.L("Tui.SafeDestructive.Phase.VerifyRestore");

        var reporter = CreateTransferReporter(state, weights, bases, PhaseVerifyRestore, null, null);
        bool verified = await Workflow.VerifyRestoreAsync(
                disk.Path,
                backup.TotalBytes,
                backup.Sha256,
                reporter,
                cancellationToken)
            .ConfigureAwait(false);

        if (!verified)
        {
            state.SetPhaseState(PhaseVerifyRestore, TuiPhaseState.Failed, context.L("Tui.SafeDestructive.Error.RestoreVerify"));
            throw new IOException(context.L("Tui.SafeDestructive.Error.RestoreVerify"));
        }

        state.SetPhaseState(PhaseVerifyRestore, TuiPhaseState.Completed, context.L("Tui.SafeDestructive.Detail.Verified"));
        state.Percent = bases[PhaseVerifyRestore] + weights[PhaseVerifyRestore];
    }

    private async Task RunFinaliseAsync(
        TuiContext context,
        TuiProgress state,
        double[] weights,
        double[] bases,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        state.SetPhaseState(PhaseFinalise, TuiPhaseState.Running, context.L("Tui.SafeDestructive.Detail.Finalise"));
        state.Phase = context.L("Tui.SafeDestructive.Phase.Finalise");

        _score = CalculateSafeScore();
        _grade = TuiFormats.GradeFromScore(_score);
        _results = BuildResults(context, startedAt);

        bool persistFailed = false;
        try
        {
            await PersistAsync(context, startedAt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            persistFailed = true;
            _status = context.L("Tui.SafeDestructive.Error.Persist", ex.Message);
            state.AddLog(_status);
        }

        if (_certificate is not null)
        {
            state.AddLog(context.L(
                "Tui.SafeDestructive.Status.Saved",
                _certificate.CertificateNumber,
                _grade,
                _score));
        }

        state.SetPhaseState(PhaseFinalise, TuiPhaseState.Completed, context.L("Tui.SafeDestructive.Detail.FinaliseDone", _grade));
        state.Percent = 100;

        if (!persistFailed && _certificate is null)
        {
            _status = _mode == TuiSafeMode.BackupOnly
                ? context.L("Tui.SafeDestructive.Status.DoneBackupOnly")
                : context.L("SafeDestructive.Status.Done");
        }
    }

    private async Task EmergencyRestoreAsync(
        TuiContext context,
        TuiProgress state,
        double[] weights,
        double[] bases,
        string reason)
    {
        var disk = _disk;
        var backup = _backup;

        if (disk is null || backup is null || !backup.Verified)
        {
            state.AddLog(context.L("Tui.SafeDestructive.Emergency.NotPossible", reason));
            return;
        }

        state.SetPhaseState(PhaseRestore, TuiPhaseState.Running, context.L("Tui.SafeDestructive.Phase.Restore"));
        state.Phase = context.L("Tui.SafeDestructive.Phase.Restore");
        state.AddLog(context.L("Tui.SafeDestructive.Emergency.Started", reason));

        try
        {
            var reporter = CreateTransferReporter(state, weights, bases, PhaseRestore, null, null);
            await Workflow.RestoreAsync(backup.ImagePath, disk.Path, backup.TotalBytes, reporter, CancellationToken.None)
                .ConfigureAwait(false);

            bool verified = await Workflow.VerifyRestoreAsync(
                    disk.Path,
                    backup.TotalBytes,
                    backup.Sha256,
                    reporter,
                    CancellationToken.None)
                .ConfigureAwait(false);

            _restoreCompleted = true;
            state.SetPhaseState(
                PhaseRestore,
                verified ? TuiPhaseState.Completed : TuiPhaseState.Failed,
                verified
                    ? context.L("Tui.SafeDestructive.Detail.Restored", TuiFormats.Bytes(backup.TotalBytes))
                    : context.L("Tui.SafeDestructive.Error.RestoreVerify"));
            state.AddLog(verified
                ? context.L("Tui.SafeDestructive.Emergency.Done")
                : context.L("Tui.SafeDestructive.Emergency.VerifyFailed"));
        }
        catch (Exception ex)
        {
            state.SetPhaseState(PhaseRestore, TuiPhaseState.Failed, ex.Message);
            state.AddLog(context.L("Tui.SafeDestructive.Emergency.Failed", ex.Message));
        }
    }

    // ──────────────────────────────────────────────
    //  Persistence
    // ──────────────────────────────────────────────

    private async Task PersistAsync(TuiContext context, DateTime startedAt, CancellationToken cancellationToken)
    {
        var disk = _disk;
        if (disk is null)
        {
            return;
        }

        var smart = _smartAfter ?? _smartBefore;
        using var scope = context.CreateScope();
        var cardService = scope.ServiceProvider.GetRequiredService<DiskCardTestService>();
        var repository = scope.ServiceProvider.GetRequiredService<IDiskCardRepository>();
        var certificateGenerator = scope.ServiceProvider.GetRequiredService<ICertificateGenerator>();

        var card = await cardService.GetOrCreateCardAsync(disk, smart, cancellationToken).ConfigureAwait(false);

        var session = new TestSession
        {
            DiskCardId = card.Id,
            SessionId = Guid.NewGuid(),
            TestType = TestType.AbsoluteDestructive,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Duration = DateTime.UtcNow - startedAt,
            Status = TestStatus.Completed,
            IsDestructive = _mode == TuiSafeMode.RawBackupRestore,
            WasLocked = true,
            Result = TestResult.Pass,
            Grade = _grade,
            Score = _score,
            HealthAssessment = MapHealthAssessment(smart),
            Notes = BuildNotes(context, smart),
            SmartBefore = _smartBefore,
            SmartAfter = _smartAfter,
            SmartChanges = BuildSmartChanges(),
            AverageWriteSpeedMBps = _sanitize?.WriteSpeedMBps ?? _backupSpeedMBps,
            AverageReadSpeedMBps = _sanitize?.ReadSpeedMBps ?? 0,
            BytesWritten = _sanitize?.BytesWritten ?? 0,
            BytesRead = _sanitize?.BytesRead ?? 0,
            WriteErrors = _sanitize?.ErrorsDetected ?? 0,
            VerificationErrors = _sanitize?.ErrorsDetected ?? 0,
            SeekResultsJson = _seeks.Count == 0 ? null : JsonSerializer.Serialize(_seeks),
            Sanitize1ResultJson = _sanitize is null ? null : JsonSerializer.Serialize(_sanitize),
            StartTemperature = _smartBefore?.Temperature,
            MaxTemperature = _maxTemperature > 0 ? _maxTemperature : _smartAfter?.Temperature,
            AverageTemperature = _minTemperature > 0
                ? (_minTemperature + _maxTemperature) / 2d
                : _smartAfter?.Temperature,
        };

        session = await repository.CreateTestSessionAsync(session).ConfigureAwait(false);

        var certificate = await certificateGenerator.GenerateCertificateAsync(session, card).ConfigureAwait(false);
        certificate.CertificateNumber = BuildCertificateNumber();
        certificate.TestType = context.L("Tui.SafeDestructive.Certificate.TestType");
        certificate.Grade = _grade;
        certificate.Score = _score;
        certificate.TestDuration = session.Duration;
        certificate.SanitizationPerformed = _sanitize is not null;
        certificate.SanitizationMethod = _sanitize is null ? null : context.L("Tui.SafeDestructive.Certificate.SanitizeMethod");
        certificate.DataVerified = _backup?.Verified ?? false;
        certificate.Recommended = _score >= 70;
        certificate.Notes = BuildNotes(context, smart);
        certificate.TemperatureRange = _maxTemperature > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{_minTemperature}-{_maxTemperature}°C")
            : "N/A";
        certificate.ErrorCount = _sanitize?.ErrorsDetected ?? 0;
        certificate.SeekAvgLatencyMs = _seeks.Count == 0
            ? null
            : _seeks.Values.SelectMany(result => result.Samples).Select(sample => sample.LatencyMs).DefaultIfEmpty(0).Average();
        certificate.SeekTestSummary = _seeks.Count == 0
            ? null
            : string.Join(" | ", _seeks.Select(pair => string.Create(
                CultureInfo.InvariantCulture,
                $"{pair.Key}: {pair.Value.AverageLatencyMs:F2} ms")));
        certificate.RecommendationNotes = _results;

        certificate = await repository.CreateCertificateAsync(certificate).ConfigureAwait(false);

        _certificate = certificate;
        _sessionId = session.Id;
        context.Selection.SelectedDiskCardId = card.Id;
        context.Selection.SelectedTestSessionId = session.Id;
        context.Selection.SelectedCertificateId = certificate.Id;
        _status = context.L("Tui.SafeDestructive.Status.Saved", certificate.CertificateNumber, _grade, _score);
    }

    private static string BuildCertificateNumber() =>
        string.Concat(
            "SAFE-",
            DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            "-",
            Guid.NewGuid().ToString("N"))[..24];

    // ──────────────────────────────────────────────
    //  Scoring and results
    // ──────────────────────────────────────────────

    private int CalculateSafeScore()
    {
        double averageWrite = _sanitize?.WriteSpeedMBps ?? _backupSpeedMBps;
        double score = averageWrite switch
        {
            >= 250 => 95,
            >= 200 => 85,
            >= 150 => 70,
            >= 100 => 55,
            >= 60 => 40,
            >= 30 => 25,
            _ => 10,
        };

        var smart = _smartAfter ?? _smartBefore;
        if (smart is not null)
        {
            int reallocated = smart.ReallocatedSectorCount ?? 0;
            int pending = smart.PendingSectorCount ?? 0;
            int uncorrectable = smart.UncorrectableErrorCount ?? 0;
            int media = smart.MediaErrors ?? 0;

            score -= Math.Min(40, reallocated * 3.0);
            score -= Math.Min(55, pending * 18.0);
            score -= Math.Min(55, uncorrectable * 15.0);
            score -= Math.Min(55, media * 15.0);

            if (pending > 0 || uncorrectable > 0 || media > 0 || smart.IsFailing)
            {
                score = Math.Min(score, 34);
            }
            else if (reallocated > 50)
            {
                score = Math.Min(score, 49);
            }
            else if (reallocated > 0)
            {
                score = Math.Min(score, 79);
            }
        }

        return (int)Math.Clamp(score, 0, 100);
    }

    private string BuildResults(TuiContext context, DateTime startedAt)
    {
        var builder = new StringBuilder();
        builder.Append(context.L("Tui.SafeDestructive.Results.Header")).Append('\n');
        builder.Append(ResultLine(context, context.L("Tui.SafeDestructive.Label.Model"), _disk?.Name ?? "—")).Append('\n');
        builder.Append(ResultLine(context, context.L("Tui.SafeDestructive.Label.Path"), _disk?.Path ?? "—")).Append('\n');
        builder.Append(ResultLine(context, context.L("Tui.SafeDestructive.Label.Size"), TuiFormats.Bytes(_disk?.TotalSize ?? 0))).Append('\n');
        builder.Append(ResultLine(context, context.L("Tui.SafeDestructive.Label.Mode"), ModeLabel(context))).Append('\n');

        if (_backup is not null)
        {
            builder.Append(ResultLine(context, context.L("Tui.SafeDestructive.Results.Backup"),
                string.Create(CultureInfo.InvariantCulture, $"{_backup.ImagePath} ({_backup.UnreadableBytes} B nečitelných)"))).Append('\n');
            builder.Append(ResultLine(context, context.L("Tui.SafeDestructive.Results.Checksum"), _backup.Sha256)).Append('\n');
            builder.Append(ResultLine(context, context.L("Tui.SafeDestructive.Results.Verify"),
                _backup.Verified
                    ? context.L("Tui.SafeDestructive.Detail.Verified")
                    : context.L("Tui.SafeDestructive.Results.NotVerified"))).Append('\n');
        }

        if (_sanitize is not null)
        {
            builder.Append(ResultLine(context, context.L("Tui.SafeDestructive.Results.Sanitize"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Write {_sanitize.WriteSpeedMBps:F1} MB/s | Read {_sanitize.ReadSpeedMBps:F1} MB/s | chyby {_sanitize.ErrorsDetected}"))).Append('\n');
        }

        foreach (var pair in _seeks)
        {
            builder.Append(ResultLine(
                context,
                context.L("Tui.SafeDestructive.Detail.Seek", pair.Key.ToString()),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"avg {pair.Value.AverageLatencyMs:F2} ms | P95 {pair.Value.P95LatencyMs:F2} ms | chyby {pair.Value.ErrorCount}"))).Append('\n');
        }

        builder.Append(ResultLine(context, context.L("Tui.SafeDestructive.Results.Restore"),
            _restoreCompleted
                ? context.L("Tui.SafeDestructive.Detail.Verified")
                : context.L("Tui.SafeDestructive.Results.Skipped"))).Append('\n');

        builder.Append(ResultLine(
            context,
            context.L("Tui.SafeDestructive.Label.Temperature"),
            _maxTemperature > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{_minTemperature}–{_maxTemperature} °C")
                : "—")).Append('\n');

        builder.Append(ResultLine(context, context.L("Tui.SafeDestructive.Results.Duration"),
            TuiFormats.Duration(DateTime.UtcNow - startedAt))).Append('\n');
        builder.Append(ResultLine(
            context,
            context.L("Tui.SafeDestructive.Results.Score"),
            string.Create(CultureInfo.InvariantCulture, $"{_grade} ({_score:F0}/100)")));
        return builder.ToString();
    }

    private static string ResultLine(TuiContext context, string label, string value) =>
        context.L("Tui.SafeDestructive.Results.Line", label, value);

    private string BuildNotes(TuiContext context, SmartaData? smart)
    {
        var notes = new List<string>
        {
            _mode == TuiSafeMode.BackupOnly
                ? context.L("Tui.SafeDestructive.Notes.BackupOnly")
                : context.L("Tui.SafeDestructive.Notes.BackupRestore"),
        };

        if (_backup is not null)
        {
            notes.Add(context.L("Tui.SafeDestructive.Notes.Checksum", _backup.Sha256));
        }

        if (smart is { IsFailing: true })
        {
            notes.Add(context.L("Tui.SafeDestructive.Notes.SmartFailing"));
        }

        if (smart?.ReallocatedSectorCount is > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"Realokované sektory: {smart.ReallocatedSectorCount.Value}"));
        }

        if (smart?.PendingSectorCount is > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"Čekající sektory: {smart.PendingSectorCount.Value}"));
        }

        return string.Join("; ", notes);
    }

    private List<SmartAttributeChange> BuildSmartChanges()
    {
        var changes = new List<SmartAttributeChange>();
        if (_smartBefore is null || _smartAfter is null)
        {
            return changes;
        }

        Add("Teplota", _smartBefore.Temperature, _smartAfter.Temperature);
        Add("Realokované sektory", _smartBefore.ReallocatedSectorCount, _smartAfter.ReallocatedSectorCount);
        Add("Čekající sektory", _smartBefore.PendingSectorCount, _smartAfter.PendingSectorCount);
        Add("Neopravitelné chyby", _smartBefore.UncorrectableErrorCount, _smartAfter.UncorrectableErrorCount);
        return changes;

        void Add(string name, long? before, long? after)
        {
            long beforeValue = before ?? 0;
            long afterValue = after ?? 0;
            if (beforeValue == afterValue)
            {
                return;
            }

            changes.Add(new SmartAttributeChange
            {
                AttributeName = name,
                ValueBefore = beforeValue,
                ValueAfter = afterValue,
                Change = afterValue - beforeValue,
            });
        }
    }

    private static HealthAssessment MapHealthAssessment(SmartaData? smart)
    {
        if (smart is null)
        {
            return HealthAssessment.Unknown;
        }

        int reallocated = smart.ReallocatedSectorCount ?? 0;
        int pending = smart.PendingSectorCount ?? 0;
        if (smart.IsFailing || pending > 0 || (smart.UncorrectableErrorCount ?? 0) > 0 || (smart.MediaErrors ?? 0) > 0 || reallocated > 50)
        {
            return HealthAssessment.Critical;
        }

        if (reallocated > 10)
        {
            return HealthAssessment.Fair;
        }

        return reallocated > 0 ? HealthAssessment.Good : HealthAssessment.Excellent;
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private static double[] BuildWeights(TuiSafeMode mode) => mode == TuiSafeMode.BackupOnly
        ? new[] { 2d, 60d, 30d, 0d, 0d, 0d, 8d }
        : new[] { 2d, 28d, 5d, 35d, 25d, 3d, 2d };

    private static double[] BuildBases(double[] weights)
    {
        var bases = new double[weights.Length];
        double running = 0;
        for (int index = 0; index < weights.Length; index++)
        {
            bases[index] = running;
            running += weights[index];
        }

        return bases;
    }

    private IProgress<TuiSafeTransferProgress> CreateTransferReporter(
        TuiProgress state,
        double[] weights,
        double[] bases,
        int phaseIndex,
        TuiSeries? series,
        Action<TuiSafeTransferProgress>? observer)
    {
        double lastPoint = -1;
        return new InlineProgress<TuiSafeTransferProgress>(update =>
        {
            state.Percent = Math.Min(99, bases[phaseIndex] + Math.Clamp(update.Percent / 100d, 0, 1) * weights[phaseIndex]);
            state.SpeedText = TuiFormats.Speed(update.SpeedMbps);
            state.ElapsedText = TuiFormats.Duration(update.Elapsed);
            state.EtaText = TuiFormats.Eta(update.Eta);
            state.Detail = string.Create(
                CultureInfo.InvariantCulture,
                $"{TuiFormats.Bytes(update.BytesProcessed)} / {TuiFormats.Bytes(update.TotalBytes)}");

            if (series is not null && update.SpeedMbps > 0 && update.Percent - lastPoint >= 0.5)
            {
                lastPoint = update.Percent;
                series.Add(state.Percent, Math.Min(update.SpeedMbps, 8000));
            }

            observer?.Invoke(update);
        });
    }

    private async Task<SmartaData?> CaptureSmartAsync(TuiContext context, CancellationToken cancellationToken)
    {
        var disk = _disk;
        if (disk is null || !disk.SupportsSmart)
        {
            return null;
        }

        try
        {
            return await context.UseScopedAsync(provider =>
                    provider.GetRequiredService<SmartCheckService>().GetSmartaDataSnapshotAsync(disk, cancellationToken))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private void UpdateTemperature(TuiProgress state)
    {
        int? temperature = _smartAfter?.Temperature ?? _smartBefore?.Temperature;
        if (temperature is not > 0)
        {
            return;
        }

        _minTemperature = _minTemperature > 0 ? Math.Min(_minTemperature, temperature.Value) : temperature.Value;
        _maxTemperature = Math.Max(_maxTemperature, temperature.Value);
        state.TemperatureText = string.Create(
            CultureInfo.InvariantCulture,
            $"{temperature.Value}°C (min {_minTemperature}, max {_maxTemperature})");
    }

    /// <summary>Progress implementation that reports synchronously on the worker thread.</summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler) =>
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));

        public void Report(T value) => _handler(value);
    }
}

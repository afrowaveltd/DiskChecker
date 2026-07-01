using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Manual test profile for when SMART data is unavailable.
/// </summary>
public enum ManualTestProfile
{
    /// <summary>Conservative — aged/fragile disk, reduced seek counts.</summary>
    Conservative = 0,
    /// <summary>Standard — medium-wear disk, balanced test (default).</summary>
    Standard = 1,
    /// <summary>Aggressive — young/healthy disk, full test intensity.</summary>
    Aggressive = 2
}

/// <summary>
/// Represents one phase in the Absolute Destructive Test timeline.
/// </summary>
public partial class TestPhaseViewModel : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public int PhaseIndex { get; init; }

    [ObservableProperty] private TestPhaseStatus _status = TestPhaseStatus.Pending;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private double _progressPercent;
}

public enum TestPhaseStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// Orchestrates the Absolute Destructive Test: Sanitize₁ → Seek×3 → Sanitize₂ → Certificate.
/// Captures SMART snapshots and temperature at every checkpoint.
/// </summary>
public partial class AbsoluteDestructiveTestViewModel : ViewModelBase, INavigableViewModel, IDisposable
{
    // ──────────────────────────────────────────────
    //  Dependencies
    // ──────────────────────────────────────────────

    private readonly INavigationService _navigationService;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly IDialogService _dialogService;
    private readonly IDiskSanitizationService _sanitizationService;
    private readonly SeekTestService _seekTestService;
    private readonly ISmartaProvider _smartaProvider;
    private readonly SmartCheckService _smartCheckService;
    private readonly IDiskCardRepository _diskCardRepository;
    private readonly ICertificateGenerator _certificateGenerator;
    private readonly TestCompletionNotificationService _notificationService;
    private readonly IPowerManagementService _powerManagementService;

    // ──────────────────────────────────────────────
    //  State
    // ──────────────────────────────────────────────

    private CancellationTokenSource? _testCancellation;
    private bool _disposed;
    private DateTime _testStartTime;
    private DateTime _phaseStartTime;
    private DateTime _lastUiUpdate = DateTime.MinValue;
    private const int UiUpdateThrottleMs = 100; // Update UI max every 100ms
    private IPowerManagementSession? _powerSession;

    // SMART snapshots at each checkpoint
    private SmartaData? _smartBaseline;
    private SmartaData? _smartAfterSanitize1;
    private SmartaData? _smartPreSeekFs;
    private SmartaData? _smartPostSeekFs;
    private SmartaData? _smartPreSeekRandom;
    private SmartaData? _smartPostSeekRandom;
    private SmartaData? _smartPreSeekSkip;
    private SmartaData? _smartPostSeekSkip;
    private SmartaData? _smartFinal;

    // Sanitization results
    private SanitizationResult? _sanitize1Result;
    private SanitizationResult? _sanitize2Result;

    // Adaptive speed samplers for anomaly detection
    private AdaptiveSpeedSampler? _samplerPass1;
    private AdaptiveSpeedSampler? _samplerPass2;
    private string? _anomalyReport;

    // Seek results
    private SeekTestResult? _seekFullStrokeResult;
    private SeekTestResult? _seekRandomResult;
    private SeekTestResult? _seekSkipResult;

    // Chart data for current phase
    private readonly ObservableCollection<ObservablePoint> _currentPhasePoints = new();
    private readonly ObservableCollection<ObservablePoint> _readPhasePoints = new();

    // ── Sanitization chart (persists across both sanitization passes) ──
    private readonly ObservableCollection<ObservablePoint> _sanitizePass1WritePoints = new();
    private readonly ObservableCollection<ObservablePoint> _sanitizePass1ReadPoints = new();
    private readonly ObservableCollection<ObservablePoint> _sanitizePass2WritePoints = new();
    private readonly ObservableCollection<ObservablePoint> _sanitizePass2ReadPoints = new();

    // ── Seek result charts (one per seek type, switchable) ──
    private readonly ObservableCollection<ObservablePoint> _seekFullStrokePoints = new();
    private readonly ObservableCollection<ObservablePoint> _seekRandomPoints = new();
    private readonly ObservableCollection<ObservablePoint> _seekSkipPoints = new();

    // ──────────────────────────────────────────────
    //  Observable properties
    // ──────────────────────────────────────────────

    [ObservableProperty] private CoreDriveInfo? _selectedDrive;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _wasAborted;
    [ObservableProperty] private string _statusMessage = "Připraven k absolutnímu destruktivnímu testu";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private double _currentPhaseProgress;
    [ObservableProperty] private string _currentPhaseName = string.Empty;
    [ObservableProperty] private string _elapsedTime = "00:00";
    [ObservableProperty] private string _estimatedRemaining = "--:--";
    [ObservableProperty] private int _currentTemperature;
    [ObservableProperty] private int _minTemperature = int.MaxValue;
    [ObservableProperty] private int _maxTemperature;
    [ObservableProperty] private bool _isSmartAvailable;
    [ObservableProperty] private bool _showManualProfileSelector;
    [ObservableProperty] private ManualTestProfile _selectedManualProfile = ManualTestProfile.Standard;
    [ObservableProperty] private string _smartBaselineSummary = "Čekám...";
    [ObservableProperty] private string _smartCurrentSummary = "Čekám...";
    [ObservableProperty] private string _smartDeltaSummary = "—";
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _resultsSummary = string.Empty;
    [ObservableProperty] private string _sanitizeComparisonSummary = string.Empty;
    [ObservableProperty] private string _seekResultsSummary = string.Empty;
    [ObservableProperty] private bool _isCertificateReady;
    [ObservableProperty] private DiskCertificate? _certificate;

    // ── Sanitization chart properties ──
    [ObservableProperty] private string _sanitizeChartTitle = "Sanitizace";
    [ObservableProperty] private string _sanitizeDataWritten = "Zapsáno: 0 / 0 GB";
    [ObservableProperty] private string _sanitizeDataRead = "Přečteno: 0 / 0 GB";
    [ObservableProperty] private double _sanitizeProgressPercent;
    [ObservableProperty] private bool _isSanitizePass2; // true when second pass is running

    // ── Sanitization chart series toggles ──
    [ObservableProperty] private bool _showSanitizePass1Write = true;
    [ObservableProperty] private bool _showSanitizePass1Read = true;
    [ObservableProperty] private bool _showSanitizePass2Write = true;
    [ObservableProperty] private bool _showSanitizePass2Read = true;

    // ── Seek chart properties (switchable) ──
    [ObservableProperty] private string _seekChartTitle = "Seek: Full Stroke";
    [ObservableProperty] private int _selectedSeekChartIndex; // 0=FullStroke, 1=Random, 2=Skip
    [ObservableProperty] private bool _hasSeekCharts;

    // ── Post-test statistics ──
    [ObservableProperty] private string _postTestStatistics = string.Empty;
    [ObservableProperty] private string _smartChangeDetails = string.Empty;

    // ── Disk recovery ──
    [ObservableProperty] private bool _isDiskRecoveryActive;
    [ObservableProperty] private string _diskRecoveryStatus = string.Empty;
    [ObservableProperty] private int _diskRecoverySecondsRemaining;
    [ObservableProperty] private string _diskRecoveryCountdown = string.Empty;
    [ObservableProperty] private int _diskDisappearanceCount;
    [ObservableProperty] private string _diskDisappearanceLog = string.Empty;

    // Phase timeline
    public ObservableCollection<TestPhaseViewModel> Phases { get; } = new();

    // Manual profile options
    public ObservableCollection<ManualProfileOption> ManualProfiles { get; } = new()
    {
        new() { Profile = ManualTestProfile.Conservative, Name = "🛡️ Konzervativní", Description = "Pro staré/opotřebené disky — 800 seeků, nižší zátěž", SeekCount = 800 },
        new() { Profile = ManualTestProfile.Standard, Name = "⚖️ Standardní (výchozí)", Description = "Pro disky středního stáří — 1500 seeků, vyvážený test", SeekCount = 1500 },
        new() { Profile = ManualTestProfile.Aggressive, Name = "🚀 Agresivní", Description = "Pro mladé/zdravé disky — 3000 seeků, plná zátěž", SeekCount = 3000 }
    };

    // LiveCharts for current phase
    public ObservableCollection<ObservablePoint> CurrentPhasePoints => _currentPhasePoints;
    public ObservableCollection<ObservablePoint> ReadPhasePoints => _readPhasePoints;

    public ISeries[] CurrentPhaseSeries { get; }

    public Axis[] CurrentPhaseXAxes { get; }

    public Axis[] CurrentPhaseYAxes { get; }

    // ── Sanitization chart series (two passes, different colors) ──
    // MUST initialize with dummy series + axes so LiveCharts2 SkiaSharp initializes
    // its render surface immediately. Array.Empty causes a permanently black chart.
    [ObservableProperty] private ISeries[] _sanitizeChartSeries = new ISeries[]
    {
        new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>(),
            Fill = null, Stroke = null, GeometrySize = 0, LineSmoothness = 0
        }
    };
    [ObservableProperty] private Axis[] _sanitizeChartXAxes = new Axis[]
    {
        new Axis { Name = "Progres (%)", NameTextSize = 10, TextSize = 9, MinLimit = 0, MaxLimit = 100, Labeler = v => v.ToString("F0") }
    };
    [ObservableProperty] private Axis[] _sanitizeChartYAxes = new Axis[]
    {
        new Axis { Name = "MB/s", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => $"{v:F0}" }
    };

    // ── Seek chart series (switchable) ──
    // MUST initialize with dummy series + axes so LiveCharts2 SkiaSharp initializes
    // its render surface immediately. Array.Empty causes a permanently black chart.
    [ObservableProperty] private ISeries[] _seekChartSeries = new ISeries[]
    {
        new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>(),
            Fill = null, Stroke = null, GeometrySize = 0, LineSmoothness = 0
        }
    };
    [ObservableProperty] private Axis[] _seekChartXAxes = new Axis[]
    {
        new Axis { Name = "Seek #", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => v.ToString("F0") }
    };
    [ObservableProperty] private Axis[] _seekChartYAxes = new Axis[]
    {
        new Axis { Name = "Latence (ms)", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => $"{v:F1}" }
    };

    // ──────────────────────────────────────────────
    //  Constructor
    // ──────────────────────────────────────────────

    public AbsoluteDestructiveTestViewModel(
        INavigationService navigationService,
        ISelectedDiskService selectedDiskService,
        IDialogService dialogService,
        IDiskSanitizationService sanitizationService,
        SeekTestService seekTestService,
        ISmartaProvider smartaProvider,
        SmartCheckService smartCheckService,
        IDiskCardRepository diskCardRepository,
        ICertificateGenerator certificateGenerator,
        TestCompletionNotificationService notificationService,
        IPowerManagementService powerManagementService)
    {
        _navigationService = navigationService;
        _selectedDiskService = selectedDiskService;
        _dialogService = dialogService;
        _sanitizationService = sanitizationService;
        _seekTestService = seekTestService;
        _smartaProvider = smartaProvider;
        _smartCheckService = smartCheckService;
        _diskCardRepository = diskCardRepository;
        _certificateGenerator = certificateGenerator;
        _notificationService = notificationService;
        _powerManagementService = powerManagementService;

        // Build phase timeline
        Phases.Add(new TestPhaseViewModel { Name = "Příprava", Icon = "🔍", PhaseIndex = 0 });
        Phases.Add(new TestPhaseViewModel { Name = "1. Sanitizace", Icon = "🧹", PhaseIndex = 1 });
        Phases.Add(new TestPhaseViewModel { Name = "Seek: Full Stroke", Icon = "↔️", PhaseIndex = 2 });
        Phases.Add(new TestPhaseViewModel { Name = "Seek: Náhodný", Icon = "🎲", PhaseIndex = 3 });
        Phases.Add(new TestPhaseViewModel { Name = "Seek: Přeskakování", Icon = "⏭️", PhaseIndex = 4 });
        Phases.Add(new TestPhaseViewModel { Name = "2. Sanitizace", Icon = "🧹", PhaseIndex = 5 });
        Phases.Add(new TestPhaseViewModel { Name = "Finalizace", Icon = "📄", PhaseIndex = 6 });

        // LiveCharts setup
        CurrentPhaseSeries = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Name = "Zápis",
                Values = _currentPhasePoints,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(239, 68, 68), 2),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            },
            new LineSeries<ObservablePoint>
            {
                Name = "Čtení",
                Values = _readPhasePoints,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(34, 197, 94), 2),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            }
        };

        CurrentPhaseXAxes = new Axis[]
        {
            new Axis
            {
                Name = "Vzorek",
                MinLimit = null,
                MaxLimit = null,
                LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                TextSize = 10
            }
        };

        CurrentPhaseYAxes = new Axis[]
        {
            new Axis
            {
                Name = "ms / MB/s",
                MinLimit = 0,
                LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                TextSize = 10
            }
        };

        // Sanitization chart – two passes with distinct colors
        // Pass 1: Write=red, Read=green (solid)
        // Pass 2: Write=dark red, Read=dark green (dashed feel via different shade)
        SanitizeChartSeries = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Name = "1. Zápis",
                Values = _sanitizePass1WritePoints,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(239, 68, 68), 2),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            },
            new LineSeries<ObservablePoint>
            {
                Name = "1. Čtení",
                Values = _sanitizePass1ReadPoints,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(34, 197, 94), 2),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            },
            new LineSeries<ObservablePoint>
            {
                Name = "2. Zápis",
                Values = _sanitizePass2WritePoints,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(185, 28, 28), 2),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            },
            new LineSeries<ObservablePoint>
            {
                Name = "2. Čtení",
                Values = _sanitizePass2ReadPoints,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(21, 128, 61), 2),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            }
        };

        SanitizeChartXAxes = new Axis[]
        {
            new Axis
            {
                Name = "Progres (%)",
                MinLimit = 0,
                MaxLimit = 100,
                LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                TextSize = 10
            }
        };

        SanitizeChartYAxes = new Axis[]
        {
            new Axis
            {
                Name = "MB/s",
                MinLimit = 0,
                LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                TextSize = 10
            }
        };

        // Seek chart – single chart, switchable data
        SeekChartSeries = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Name = "Latence",
                Values = _seekFullStrokePoints,
                Fill = null,
                GeometrySize = 3,
                GeometryFill = new SolidColorPaint(SKColors.DodgerBlue),
                GeometryStroke = new SolidColorPaint(SKColors.DodgerBlue, 1),
                Stroke = new SolidColorPaint(SKColors.DodgerBlue, 1.5f),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            }
        };

        SeekChartXAxes = new Axis[]
        {
            new Axis
            {
                Name = "Seek #",
                MinLimit = 0,
                LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                TextSize = 10
            }
        };

        SeekChartYAxes = new Axis[]
        {
            new Axis
            {
                Name = "Latence (ms)",
                MinLimit = 0,
                LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                TextSize = 10
            }
        };
    }

    // ──────────────────────────────────────────────
    //  Navigation
    // ──────────────────────────────────────────────

    public void OnNavigatedTo()
    {
        SelectedDrive = _selectedDiskService.SelectedDisk;
        if (SelectedDrive == null)
        {
            StatusMessage = "Není vybrán žádný disk. Vyberte disk v přehledu.";
            return;
        }

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            StatusMessage = "Zjišťuji SMART dostupnost...";

            // Use pre-detected SMART support flag to avoid device contention
            if (SelectedDrive!.SupportsSmart)
            {
                var smarta = await _smartaProvider.GetSmartaDataAsync(
                    SelectedDrive.Path, CancellationToken.None);

                if (smarta != null)
                {
                    IsSmartAvailable = true;
                    ShowManualProfileSelector = false;
                    _smartBaseline = smarta;
                    SmartBaselineSummary = FormatSmartSummary(smarta, "Výchozí");
                    StatusMessage = $"SMART dostupný — {smarta.DeviceModel}";
                }
                else
                {
                    IsSmartAvailable = false;
                    ShowManualProfileSelector = true;
                    SmartBaselineSummary = "SMART nedostupný — vyberte manuální profil";
                    StatusMessage = "SMART nedostupný. Vyberte manuální testovací profil.";
                }
            }
            else
            {
                IsSmartAvailable = false;
                ShowManualProfileSelector = true;
                SmartBaselineSummary = "SMART nedostupný (disk nepodporuje SMART)";
                StatusMessage = "SMART nedostupný. Vyberte manuální testovací profil.";
            }
        }
        catch
        {
            IsSmartAvailable = false;
            ShowManualProfileSelector = true;
            SmartBaselineSummary = "SMART nedostupný (chyba čtení)";
            StatusMessage = "SMART nedostupný. Vyberte manuální testovací profil.";
        }
    }

    // ──────────────────────────────────────────────
    //  Commands
    // ──────────────────────────────────────────────

    [RelayCommand]
    private async Task StartTestAsync()
    {
        if (SelectedDrive == null)
        {
            await _dialogService.ShowErrorAsync("Chyba", "Není vybrán žádný disk.");
            return;
        }

        if (!IsSmartAvailable && ShowManualProfileSelector)
        {
            ShowManualProfileSelector = false;
        }

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "⚠️ Absolutní Destruktivní Test",
            $"Tento test KOMPLETNĚ ZNIČÍ všechna data na disku:\n\n" +
            $"{SelectedDrive.Name} ({SelectedDrive.Path})\n" +
            $"Kapacita: {FormatBytes(SelectedDrive.TotalSize)}\n\n" +
            $"Průběh testu:\n" +
            $"1. První sanitizace (zápis nul + ověření)\n" +
            $"2. Trojice seek testů (FullStroke, Random, Skip) po 1500 vzorcích\n" +
            $"3. Druhá sanitizace pro porovnání\n" +
            $"4. Generování certifikátu\n\n" +
            $"Doba trvání: až několik hodin dle velikosti disku.\n\n" +
            $"OPRAVDU CHCETE POKRAČOVAT?");

        if (!confirmed) return;

        _testCancellation = new CancellationTokenSource();
        _testStartTime = DateTime.UtcNow;
        IsTesting = true;
        IsCompleted = false;
        WasAborted = false;
        HasResults = false;
        IsCertificateReady = false;
        Certificate = null;
        OverallProgress = 0;

        // Reset all phases
        foreach (var p in Phases) { p.Status = TestPhaseStatus.Pending; p.Detail = string.Empty; p.ProgressPercent = 0; }

        try
        {
            // Start power management session
            try
            {
                _powerSession = await _powerManagementService.BeginTestSessionAsync(_testCancellation.Token);
                StatusMessage = "🔋 Aktivováno řízení napájení — zabráněno uspání systému";
            }
            catch (Exception ex)
            {
                // Non-critical — test can continue without power management
                await _dialogService.ShowWarningAsync("Upozornění", 
                    $"Nepodařilo se aktivovat řízení napájení: {ex.Message}\n\n" +
                    "Test bude pokračovat, ale systém se může uspat během dlouhého testu.");
            }

            await RunAllPhasesAsync(_testCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            WasAborted = true;
            StatusMessage = "Test přerušen uživatelem.";
        }
        catch (Exception ex)
        {
            WasAborted = true;
            StatusMessage = $"Test selhal: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba testu", $"Test selhal s chybou:\n{ex.Message}");
        }
        finally
        {
            // Restore power settings
            if (_powerSession != null)
            {
                try
                {
                    await _powerSession.RestoreAsync();
                    _powerSession.Dispose();
                    _powerSession = null;
                    StatusMessage = "🔋 Obnoveno původní nastavení napájení";
                }
                catch (Exception ex)
                {
                    // Log but don't interrupt cleanup
                    StatusMessage = $"⚠️ Chyba při obnovení nastavení napájení: {ex.Message}";
                }
            }

            IsTesting = false;
            _testCancellation?.Dispose();
            _testCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelTest()
    {
        _testCancellation?.Cancel();
        StatusMessage = "Přerušuji test...";
    }

    [RelayCommand]
    private async Task GenerateCertificateAsync()
    {
        if (!IsCertificateReady || Certificate == null)
        {
            await _dialogService.ShowErrorAsync("Chyba", "Certifikát ještě není připraven.");
            return;
        }

        try
        {
            StatusMessage = "Generuji certifikát...";
            var card = await _diskCardRepository.GetByDevicePathAsync(SelectedDrive!.Path);
            if (card == null)
            {
                await _dialogService.ShowErrorAsync("Chyba", "Karta disku nenalezena.");
                return;
            }

            await _diskCardRepository.CreateCertificateAsync(Certificate);
            StatusMessage = $"Certifikát uložen: {Certificate.CertificateNumber}";

            _selectedDiskService.SelectedCertificateId = Certificate.Id;
            _navigationService.NavigateTo<CertificateViewModel>();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se uložit certifikát: {ex.Message}");
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        if (IsTesting)
        {
            _ = _dialogService.ShowErrorAsync("Test běží", "Nelze opustit test během jeho průběhu. Nejprve test přerušte.");
            return;
        }
        _navigationService.NavigateTo<DiskSelectionViewModel>();
    }

    [RelayCommand]
    private void SwitchSeekChart(string param)
    {
        if (!int.TryParse(param, out var index)) return;
        SelectedSeekChartIndex = index;

        var (title, points) = index switch
        {
            0 => ("Seek: Full Stroke", _seekFullStrokePoints),
            1 => ("Seek: Náhodný", _seekRandomPoints),
            2 => ("Seek: Přeskakování", _seekSkipPoints),
            _ => ("Seek: Full Stroke", _seekFullStrokePoints)
        };

        SeekChartTitle = title;
        ApplySeekChartSeries(points);
    }

    /// <summary>
    /// Refreshes the seek chart to show the currently selected seek type.
    /// Called after a seek phase completes so the chart renders the collected data.
    /// </summary>
    private void RefreshSeekChart()
    {
        var points = SelectedSeekChartIndex switch
        {
            0 => _seekFullStrokePoints,
            1 => _seekRandomPoints,
            2 => _seekSkipPoints,
            _ => _seekFullStrokePoints
        };
        ApplySeekChartSeries(points);
    }

    private void ApplySeekChartSeries(ObservableCollection<ObservablePoint> points)
    {
        var newSeries = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Name = "Latence",
                Values = points,
                Fill = null,
                GeometrySize = 3,
                GeometryFill = new SolidColorPaint(SKColors.DodgerBlue),
                GeometryStroke = new SolidColorPaint(SKColors.DodgerBlue, 1),
                Stroke = new SolidColorPaint(SKColors.DodgerBlue, 1.5f),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            }
        };
        var newXAxes = new Axis[]
        {
            new Axis
            {
                Name = "Seek #",
                MinLimit = 0,
                LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                TextSize = 10
            }
        };
        var newYAxes = new Axis[]
        {
            new Axis
            {
                Name = "Latence (ms)",
                MinLimit = 0,
                LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                TextSize = 10
            }
        };

        // Two-step assignment forces LiveCharts2 SkiaSharp to detect the change and redraw
        SeekChartSeries = Array.Empty<ISeries>();
        SeekChartXAxes = Array.Empty<Axis>();
        SeekChartYAxes = Array.Empty<Axis>();
        SeekChartSeries = newSeries;
        SeekChartXAxes = newXAxes;
        SeekChartYAxes = newYAxes;
    }

    [RelayCommand]
    private void ToggleSanitizeSeries(string param)
    {
        switch (param)
        {
            case "pass1write": ShowSanitizePass1Write = !ShowSanitizePass1Write; break;
            case "pass1read": ShowSanitizePass1Read = !ShowSanitizePass1Read; break;
            case "pass2write": ShowSanitizePass2Write = !ShowSanitizePass2Write; break;
            case "pass2read": ShowSanitizePass2Read = !ShowSanitizePass2Read; break;
        }
        RebuildSanitizeChart();
    }

    private void RebuildSanitizeChart()
    {
        var seriesList = new List<ISeries>();

        if (ShowSanitizePass1Write)
        {
            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Name = "1. Zápis",
                Values = _sanitizePass1WritePoints,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(239, 68, 68), 2),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            });
        }

        if (ShowSanitizePass1Read)
        {
            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Name = "1. Čtení",
                Values = _sanitizePass1ReadPoints,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(34, 197, 94), 2),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            });
        }

        if (ShowSanitizePass2Write)
        {
            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Name = "2. Zápis",
                Values = _sanitizePass2WritePoints,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(185, 28, 28), 2),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            });
        }

        if (ShowSanitizePass2Read)
        {
            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Name = "2. Čtení",
                Values = _sanitizePass2ReadPoints,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(21, 128, 61), 2),
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            });
        }

        // Two-step assignment forces LiveCharts2 SkiaSharp to detect the change and redraw
        SanitizeChartSeries = Array.Empty<ISeries>();
        SanitizeChartSeries = seriesList.ToArray();
    }

    // ──────────────────────────────────────────────
    //  Phase orchestration
    // ──────────────────────────────────────────────

    private async Task RunAllPhasesAsync(CancellationToken ct)
    {
        // Phase 0: Preparation
        await RunPhaseAsync(0, "Příprava", async () =>
        {
            ct.ThrowIfCancellationRequested();
            SetPhase(0, TestPhaseStatus.Running, "Čtení SMART a teploty...");

            // Capture baseline SMART if not already done (only if drive supports SMART)
            if (_smartBaseline == null && SelectedDrive!.SupportsSmart)
            {
                try
                {
                    _smartBaseline = await _smartaProvider.GetSmartaDataAsync(SelectedDrive.Path, ct);
                }
                catch { /* SMART unavailable — already handled */ }
            }

            SmartBaselineSummary = _smartBaseline != null
                ? FormatSmartSummary(_smartBaseline, "Výchozí před testem")
                : "SMART nedostupný";

            // Capture initial temperature
            var temp = _smartBaseline?.Temperature ?? await ReadTemperatureAsync(ct);
            UpdateTemperature(temp);

            // Validate disk is not SSD (seek tests are for HDDs)
            if (_smartBaseline?.DeviceType?.Contains("nvme", StringComparison.OrdinalIgnoreCase) == true ||
                _smartBaseline?.DeviceType?.Contains("ssd", StringComparison.OrdinalIgnoreCase) == true)
            {
                var cont = await _dialogService.ShowConfirmationAsync(
                    "SSD detekován",
                    "Disk byl detekován jako SSD. Seek testy na SSD nemají diagnostickou hodnotu " +
                    "(absence mechanických hlav).\n\nChcete přesto pokračovat?");
                if (!cont) throw new OperationCanceledException();
            }

            SetPhase(0, TestPhaseStatus.Completed, "Připraveno");
        }, ct);

        // Phase 1: First sanitization
        await RunPhaseAsync(1, "1. Sanitizace", async () =>
        {
            ct.ThrowIfCancellationRequested();
            SetPhase(1, TestPhaseStatus.Running, "Zápis nul + ověření...");

            IsSanitizePass2 = false;
            SanitizeChartTitle = "🧹 1. Sanitizace – Zápis nul + Ověření";
            _sanitizePass1WritePoints.Clear();
            _sanitizePass1ReadPoints.Clear();
            _sanitizePass2WritePoints.Clear();
            _sanitizePass2ReadPoints.Clear();

            // Initialize adaptive sampler for pass 1
            _samplerPass1 = new AdaptiveSpeedSampler();
            _samplerPass1.Initialize(SelectedDrive!.TotalSize);

            _sanitize1Result = await _sanitizationService.SanitizeDiskAsync(
                SelectedDrive!.Path,
                SelectedDrive.TotalSize,
                createPartition: false,
                format: false,
                volumeLabel: "",
                new Progress<SanitizationProgress>(p =>
                {
                    // Feed adaptive sampler
                    _samplerPass1?.AddSample(p.CurrentSpeedMBps, (long)(SelectedDrive!.TotalSize * p.ProgressPercent / 100.0), DateTime.UtcNow);

                    // Throttle UI updates to max every 100ms
                    var now = DateTime.UtcNow;
                    if ((now - _lastUiUpdate).TotalMilliseconds < UiUpdateThrottleMs && p.ProgressPercent < 100)
                        return;

                    _lastUiUpdate = now;

                    Dispatcher.UIThread.Post(() =>
                    {
                        CurrentPhaseProgress = p.ProgressPercent;
                        Phases[1].ProgressPercent = p.ProgressPercent;
                        Phases[1].Detail = $"{p.Phase} — {p.CurrentSpeedMBps:F1} MB/s";
                        UpdateTemperature(_smartBaseline?.Temperature ?? 30);
                        UpdateOverallProgress();

                        // Feed into sanitization chart (pass 1)
                        if (p.IsReadVerifyPhase)
                            _sanitizePass1ReadPoints.Add(new ObservablePoint(p.ProgressPercent, p.CurrentSpeedMBps));
                        else
                            _sanitizePass1WritePoints.Add(new ObservablePoint(p.ProgressPercent, p.CurrentSpeedMBps));

                        // Also feed the current phase chart (backward compat)
                        if (p.IsReadVerifyPhase)
                            _readPhasePoints.Add(new ObservablePoint(p.ProgressPercent, p.CurrentSpeedMBps));
                        else
                            _currentPhasePoints.Add(new ObservablePoint(p.ProgressPercent, p.CurrentSpeedMBps));

                        // Update data counters
                        var totalGB = SelectedDrive!.TotalSize / (1024.0 * 1024.0 * 1024.0);
                        var writtenGB = totalGB * p.ProgressPercent / 100.0;
                        SanitizeDataWritten = $"Zapsáno: {writtenGB:F1} / {totalGB:F0} GB ({p.ProgressPercent:F0}%)";
                        if (p.IsReadVerifyPhase)
                        {
                            var readGB = totalGB * p.ProgressPercent / 100.0;
                            SanitizeDataRead = $"Přečteno: {readGB:F1} / {totalGB:F0} GB ({p.ProgressPercent:F0}%)";
                        }
                        SanitizeProgressPercent = p.ProgressPercent;
                    });
                }),
                ct);

            // Capture SMART after first sanitization
            _smartAfterSanitize1 = await CaptureSmartAsync(ct);

            // Finalize adaptive sampler for pass 1
            _samplerPass1?.FinalizePhase();

            SetPhase(1, TestPhaseStatus.Completed,
                $"Write: {_sanitize1Result.WriteSpeedMBps:F1} MB/s | Read: {_sanitize1Result.ReadSpeedMBps:F1} MB/s | Chyby: {_sanitize1Result.ErrorsDetected}");
        }, ct);

        // Cooldown 1
        await CooldownAsync(TimeSpan.FromMinutes(1), ct);

        // Phase 2: Seek FullStroke
        await RunSeekPhaseAsync(2, "Seek: Full Stroke", SeekTestType.FullStroke, ct);

        // Cooldown 2
        await CooldownAsync(TimeSpan.FromMinutes(1), ct);

        // Phase 3: Seek Random
        await RunSeekPhaseAsync(3, "Seek: Náhodný", SeekTestType.Random, ct);

        // Cooldown 3
        await CooldownAsync(TimeSpan.FromMinutes(1), ct);

        // Phase 4: Seek Skip
        await RunSeekPhaseAsync(4, "Seek: Přeskakování", SeekTestType.Skip, ct);

        // Cooldown 4
        await CooldownAsync(TimeSpan.FromMinutes(1), ct);

        // Phase 5: Second sanitization
        await RunPhaseAsync(5, "2. Sanitizace", async () =>
        {
            ct.ThrowIfCancellationRequested();
            SetPhase(5, TestPhaseStatus.Running, "Zápis nul + ověření (finální)...");

            IsSanitizePass2 = true;
            SanitizeChartTitle = "🧹 2. Sanitizace – Zápis nul + Ověření (finální)";

            // Initialize adaptive sampler for pass 2
            _samplerPass2 = new AdaptiveSpeedSampler();
            _samplerPass2.Initialize(SelectedDrive!.TotalSize);

            _sanitize2Result = await _sanitizationService.SanitizeDiskAsync(
                SelectedDrive!.Path,
                SelectedDrive.TotalSize,
                createPartition: false,
                format: false,
                volumeLabel: "",
                new Progress<SanitizationProgress>(p =>
                {
                    // Feed adaptive sampler
                    _samplerPass2?.AddSample(p.CurrentSpeedMBps, (long)(SelectedDrive!.TotalSize * p.ProgressPercent / 100.0), DateTime.UtcNow);

                    // Throttle UI updates to max every 100ms
                    var now = DateTime.UtcNow;
                    if ((now - _lastUiUpdate).TotalMilliseconds < UiUpdateThrottleMs && p.ProgressPercent < 100)
                        return;

                    _lastUiUpdate = now;

                    Dispatcher.UIThread.Post(() =>
                    {
                        CurrentPhaseProgress = p.ProgressPercent;
                        Phases[5].ProgressPercent = p.ProgressPercent;
                        Phases[5].Detail = $"{p.Phase} — {p.CurrentSpeedMBps:F1} MB/s";
                        UpdateOverallProgress();

                        // Feed into sanitization chart (pass 2)
                        if (p.IsReadVerifyPhase)
                            _sanitizePass2ReadPoints.Add(new ObservablePoint(p.ProgressPercent, p.CurrentSpeedMBps));
                        else
                            _sanitizePass2WritePoints.Add(new ObservablePoint(p.ProgressPercent, p.CurrentSpeedMBps));

                        // Also feed the current phase chart (backward compat)
                        if (p.IsReadVerifyPhase)
                            _readPhasePoints.Add(new ObservablePoint(p.ProgressPercent, p.CurrentSpeedMBps));
                        else
                            _currentPhasePoints.Add(new ObservablePoint(p.ProgressPercent, p.CurrentSpeedMBps));

                        // Update data counters
                        var totalGB = SelectedDrive!.TotalSize / (1024.0 * 1024.0 * 1024.0);
                        var writtenGB = totalGB * p.ProgressPercent / 100.0;
                        SanitizeDataWritten = $"Zapsáno: {writtenGB:F1} / {totalGB:F0} GB ({p.ProgressPercent:F0}%)";
                        if (p.IsReadVerifyPhase)
                        {
                            var readGB = totalGB * p.ProgressPercent / 100.0;
                            SanitizeDataRead = $"Přečteno: {readGB:F1} / {totalGB:F0} GB ({p.ProgressPercent:F0}%)";
                        }
                        SanitizeProgressPercent = p.ProgressPercent;
                    });
                }),
                ct);

            // Finalize adaptive sampler for pass 2
            _samplerPass2?.FinalizePhase();

            SetPhase(5, TestPhaseStatus.Completed,
                $"Write: {_sanitize2Result.WriteSpeedMBps:F1} MB/s | Read: {_sanitize2Result.ReadSpeedMBps:F1} MB/s | Chyby: {_sanitize2Result.ErrorsDetected}");
        }, ct);

        // Phase 6: Finalization
        await RunPhaseAsync(6, "Finalizace", async () =>
        {
            ct.ThrowIfCancellationRequested();
            SetPhase(6, TestPhaseStatus.Running, "Shromažďování výsledků...");

            // Final SMART snapshot
            _smartFinal = await CaptureSmartAsync(ct);

            // Build SMART delta
            SmartDeltaSummary = BuildSmartDeltaSummary();

            // Build results
            BuildResultsSummary();

            // Generate certificate
            await BuildCertificateAsync();

            // Save test session
            await SaveTestSessionAsync();

            SetPhase(6, TestPhaseStatus.Completed, "Hotovo — certifikát připraven");
            IsCompleted = true;
            HasResults = true;
            IsCertificateReady = true;
            StatusMessage = "✅ Absolutní destruktivní test dokončen!";
        }, ct);
    }

    // ──────────────────────────────────────────────
    //  Phase helpers
    // ──────────────────────────────────────────────

    private async Task RunPhaseAsync(int index, string name, Func<Task> action, CancellationToken ct)
    {
        CurrentPhaseName = name;
        _phaseStartTime = DateTime.UtcNow;
        _currentPhasePoints.Clear();
        _readPhasePoints.Clear();

        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            SetPhase(index, TestPhaseStatus.Failed, "Přerušeno");
            throw;
        }
        catch (Exception ex)
        {
            SetPhase(index, TestPhaseStatus.Failed, ex.Message);
            throw;
        }

        UpdateOverallProgress();
    }

    private async Task RunSeekPhaseAsync(int index, string name, SeekTestType seekType, CancellationToken ct)
    {
        await RunPhaseAsync(index, name, async () =>
        {
            ct.ThrowIfCancellationRequested();

            // Capture pre-seek SMART
            var preSmart = await CaptureSmartAsync(ct);
            StorePreSeekSmart(seekType, preSmart);

            SetPhase(index, TestPhaseStatus.Running, $"Spouštím {name}...");

            var seekCount = GetSeekCount();
            var request = new SeekTestRequest
            {
                Drive = SelectedDrive!,
                TestType = seekType,
                SeekCount = seekCount,
                SkipSegments = 1000,
                BlockSizeBytes = 4096,
                TimeoutSeconds = 30
            };

            SeekTestResult? result = null;
            var progressSamples = new List<double>();

            // Determine which seek chart collection to populate
            var targetPoints = seekType switch
            {
                SeekTestType.FullStroke => _seekFullStrokePoints,
                SeekTestType.Random => _seekRandomPoints,
                SeekTestType.Skip => _seekSkipPoints,
                _ => _seekFullStrokePoints
            };
            targetPoints.Clear();

            result = await _seekTestService.RunAsync(
                request,
                progress =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        CurrentPhaseProgress = progress.PercentComplete;
                        Phases[index].ProgressPercent = progress.PercentComplete;
                        Phases[index].Detail = $"{progress.SeeksCompleted}/{progress.TotalSeeks} seeků";

                        if (progress.LatestSample != null && !progress.LatestSample.HasError)
                        {
                            _currentPhasePoints.Add(new ObservablePoint(
                                progress.SeeksCompleted,
                                progress.LatestSample.LatencyMs));
                            progressSamples.Add(progress.LatestSample.LatencyMs);

                            // Also populate the persistent seek chart
                            targetPoints.Add(new ObservablePoint(
                                progress.SeeksCompleted,
                                progress.LatestSample.LatencyMs));
                        }

                        UpdateOverallProgress();
                    });
                },
                ct);

            // Store result
            StoreSeekResult(seekType, result);

            // Capture post-seek SMART
            var postSmart = await CaptureSmartAsync(ct);
            StorePostSeekSmart(seekType, postSmart);

            // Update temperature
            var temp = postSmart?.Temperature ?? _smartBaseline?.Temperature ?? 30;
            UpdateTemperature(temp);

            var avgLatency = result.Samples.Count > 0
                ? result.Samples.Average(s => s.LatencyMs)
                : 0;

            SetPhase(index, TestPhaseStatus.Completed,
                $"avg: {avgLatency:F2} ms | P95: {result.P95LatencyMs:F2} ms | vzorků: {result.SeekCount}");

            // Mark seek charts as available and force chart refresh.
            // LiveCharts2 SkiaSharp needs series reassignment to detect ObservableCollection changes.
            HasSeekCharts = true;
            RefreshSeekChart();
        }, ct);
    }

    private async Task CooldownAsync(TimeSpan duration, CancellationToken ct)
    {
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(500, ct);
            Dispatcher.UIThread.Post(() =>
            {
                var remaining = end - DateTime.UtcNow;
                StatusMessage = $"Teplotní stabilizace... {remaining.TotalSeconds:F0}s";
            });
        }
    }

    private void SetPhase(int index, TestPhaseStatus status, string detail)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Phases[index].Status = status;
            Phases[index].Detail = detail;
            if (status == TestPhaseStatus.Completed)
                Phases[index].ProgressPercent = 100;
        });
    }

    private void UpdateOverallProgress()
    {
        // 7 phases, each contributes ~14.3%
        var completed = Phases.Count(p => p.Status == TestPhaseStatus.Completed);
        var running = Phases.FirstOrDefault(p => p.Status == TestPhaseStatus.Running);
        var runningContribution = running != null ? running.ProgressPercent / 100.0 * (1.0 / Phases.Count) : 0;
        OverallProgress = (completed / (double)Phases.Count + runningContribution) * 100;

        var elapsed = DateTime.UtcNow - _testStartTime;
        ElapsedTime = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

        if (OverallProgress > 0 && OverallProgress < 100)
        {
            var totalEstimated = elapsed.TotalSeconds / (OverallProgress / 100.0);
            var remaining = totalEstimated - elapsed.TotalSeconds;
            if (remaining > 0)
                EstimatedRemaining = $"{(int)remaining / 3600:D2}:{(int)(remaining % 3600) / 60:D2}:{(int)remaining % 60:D2}";
        }
        else if (OverallProgress >= 100)
        {
            EstimatedRemaining = "00:00:00";
        }
    }

    // ──────────────────────────────────────────────
    //  SMART helpers
    // ──────────────────────────────────────────────

    private async Task<SmartaData?> CaptureSmartAsync(CancellationToken ct)
    {
        // Skip SMART query if the drive doesn't support SMART
        if (SelectedDrive == null || !SelectedDrive.SupportsSmart)
            return null;

        try
        {
            return await _smartaProvider.GetSmartaDataAsync(SelectedDrive.Path, ct);
        }
        catch
        {
            return null;
        }
    }

    private async Task<int> ReadTemperatureAsync(CancellationToken ct)
    {
        // Skip SMART query if the drive doesn't support SMART
        if (SelectedDrive == null || !SelectedDrive.SupportsSmart)
            return 30;

        try
        {
            var smarta = await _smartaProvider.GetSmartaDataAsync(SelectedDrive.Path, ct);
            return smarta?.Temperature ?? 30;
        }
        catch
        {
            return 30;
        }
    }

    private void UpdateTemperature(int temp)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CurrentTemperature = temp;
            if (temp < MinTemperature) MinTemperature = temp;
            if (temp > MaxTemperature) MaxTemperature = temp;
        });
    }

    private void StorePreSeekSmart(SeekTestType type, SmartaData? smart)
    {
        switch (type)
        {
            case SeekTestType.FullStroke: _smartPreSeekFs = smart; break;
            case SeekTestType.Random: _smartPreSeekRandom = smart; break;
            case SeekTestType.Skip: _smartPreSeekSkip = smart; break;
        }
    }

    private void StorePostSeekSmart(SeekTestType type, SmartaData? smart)
    {
        switch (type)
        {
            case SeekTestType.FullStroke: _smartPostSeekFs = smart; break;
            case SeekTestType.Random: _smartPostSeekRandom = smart; break;
            case SeekTestType.Skip: _smartPostSeekSkip = smart; break;
        }
    }

    private void StoreSeekResult(SeekTestType type, SeekTestResult result)
    {
        switch (type)
        {
            case SeekTestType.FullStroke: _seekFullStrokeResult = result; break;
            case SeekTestType.Random: _seekRandomResult = result; break;
            case SeekTestType.Skip: _seekSkipResult = result; break;
        }
    }

    private int GetSeekCount() => SelectedManualProfile switch
    {
        ManualTestProfile.Conservative => 800,
        ManualTestProfile.Aggressive => 3000,
        _ => 1500
    };

    // ──────────────────────────────────────────────
    //  Results & Certificate
    // ──────────────────────────────────────────────

    private void BuildResultsSummary()
    {
        var sb = new System.Text.StringBuilder();

        // Sanitization comparison
        if (_sanitize1Result != null && _sanitize2Result != null)
        {
            var writeDelta = _sanitize2Result.WriteSpeedMBps - _sanitize1Result.WriteSpeedMBps;
            var readDelta = _sanitize2Result.ReadSpeedMBps - _sanitize1Result.ReadSpeedMBps;
            var writeDeltaPct = _sanitize1Result.WriteSpeedMBps > 0
                ? (writeDelta / _sanitize1Result.WriteSpeedMBps) * 100 : 0;
            var readDeltaPct = _sanitize1Result.ReadSpeedMBps > 0
                ? (readDelta / _sanitize1Result.ReadSpeedMBps) * 100 : 0;

            SanitizeComparisonSummary =
                $"Sanitizace 1: Write {_sanitize1Result.WriteSpeedMBps:F1} / Read {_sanitize1Result.ReadSpeedMBps:F1} MB/s | Chyby: {_sanitize1Result.ErrorsDetected}\n" +
                $"Sanitizace 2: Write {_sanitize2Result.WriteSpeedMBps:F1} / Read {_sanitize2Result.ReadSpeedMBps:F1} MB/s | Chyby: {_sanitize2Result.ErrorsDetected}\n" +
                $"Δ Write: {writeDelta:+0.0;-0.0} MB/s ({writeDeltaPct:+0.0;-0.0}%) | Δ Read: {readDelta:+0.0;-0.0} MB/s ({readDeltaPct:+0.0;-0.0}%)";
        }

        // Seek results
        var fsAvg = _seekFullStrokeResult?.AverageLatencyMs ?? 0;
        var rndAvg = _seekRandomResult?.AverageLatencyMs ?? 0;
        var skipAvg = _seekSkipResult?.AverageLatencyMs ?? 0;

        SeekResultsSummary =
            $"Full Stroke: avg {fsAvg:F2} ms, P95 {_seekFullStrokeResult?.P95LatencyMs ?? 0:F2} ms\n" +
            $"Náhodný:    avg {rndAvg:F2} ms, P95 {_seekRandomResult?.P95LatencyMs ?? 0:F2} ms\n" +
            $"Skip:       avg {skipAvg:F2} ms, P95 {_seekSkipResult?.P95LatencyMs ?? 0:F2} ms";

        // Overall
        sb.AppendLine("=== ABSOLUTNÍ DESTRUKTIVNÍ TEST — VÝSLEDKY ===");
        sb.AppendLine();
        sb.AppendLine(SanitizeComparisonSummary);
        sb.AppendLine();
        sb.AppendLine(SeekResultsSummary);
        sb.AppendLine();
        sb.AppendLine($"Teplota: {MinTemperature}–{MaxTemperature}°C");
        sb.AppendLine($"SMART delta: {SmartDeltaSummary}");
        sb.AppendLine($"Celková doba: {ElapsedTime}");

        ResultsSummary = sb.ToString();

        // ── Build post-test statistics (compact, for the stats panel) ──
        var statsSb = new System.Text.StringBuilder();
        statsSb.AppendLine("═══ STATISTIKA ═══");
        statsSb.AppendLine();

        // Sanitization averages
        if (_sanitize1Result != null)
        {
            statsSb.AppendLine($"🧹 Sanitizace 1:");
            statsSb.AppendLine($"   Zápis:  {_sanitize1Result.WriteSpeedMBps:F1} MB/s");
            statsSb.AppendLine($"   Čtení:  {_sanitize1Result.ReadSpeedMBps:F1} MB/s");
            statsSb.AppendLine($"   Chyby:  {_sanitize1Result.ErrorsDetected}");
        }
        if (_sanitize2Result != null)
        {
            statsSb.AppendLine($"🧹 Sanitizace 2:");
            statsSb.AppendLine($"   Zápis:  {_sanitize2Result.WriteSpeedMBps:F1} MB/s");
            statsSb.AppendLine($"   Čtení:  {_sanitize2Result.ReadSpeedMBps:F1} MB/s");
            statsSb.AppendLine($"   Chyby:  {_sanitize2Result.ErrorsDetected}");
        }

        // Seek averages
        statsSb.AppendLine();
        statsSb.AppendLine($"🎯 Seek testy:");
        statsSb.AppendLine($"   Full Stroke: avg {fsAvg:F2} ms, P95 {_seekFullStrokeResult?.P95LatencyMs ?? 0:F2} ms, P99 {_seekFullStrokeResult?.P99LatencyMs ?? 0:F2} ms");
        statsSb.AppendLine($"   Náhodný:    avg {rndAvg:F2} ms, P95 {_seekRandomResult?.P95LatencyMs ?? 0:F2} ms, P99 {_seekRandomResult?.P99LatencyMs ?? 0:F2} ms");
        statsSb.AppendLine($"   Skip:       avg {skipAvg:F2} ms, P95 {_seekSkipResult?.P95LatencyMs ?? 0:F2} ms, P99 {_seekSkipResult?.P99LatencyMs ?? 0:F2} ms");

        // Temperature
        statsSb.AppendLine();
        statsSb.AppendLine($"🌡 Teplota: {MinTemperature}–{MaxTemperature}°C (průměr {(MinTemperature + MaxTemperature) / 2:F0}°C)");

        // Disk disappearances
        if (DiskDisappearanceCount > 0)
        {
            statsSb.AppendLine();
            statsSb.AppendLine($"⚠️ Disk zmizel {DiskDisappearanceCount}x během testu");
        }

        PostTestStatistics = statsSb.ToString();

        // ── Build SMART change details ──
        SmartChangeDetails = BuildSmartChangeDetails();
    }

    private string BuildSmartChangeDetails()
    {
        if (_smartBaseline == null) return "SMART nedostupný – změny nelze sledovat.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ SMART ZMĚNY ═══");
        sb.AppendLine();

        // Temperature change
        var baselineTemp = _smartBaseline.Temperature;
        var finalTemp = _smartFinal?.Temperature ?? baselineTemp;
        var tempDelta = finalTemp - baselineTemp;
        sb.AppendLine($"🌡 Teplota: {baselineTemp}°C → {finalTemp}°C (Δ {tempDelta:+0;-0}°C)");

        // Reallocated sectors
        var baselineRealloc = _smartBaseline.ReallocatedSectorCount ?? 0;
        var finalRealloc = _smartFinal?.ReallocatedSectorCount ?? baselineRealloc;
        if (finalRealloc != baselineRealloc)
            sb.AppendLine($"⚠️ Reallocated sectors: {baselineRealloc} → {finalRealloc} (Δ {finalRealloc - baselineRealloc:+0;-0})");

        // Pending sectors
        var baselinePending = _smartBaseline.PendingSectorCount ?? 0;
        var finalPending = _smartFinal?.PendingSectorCount ?? baselinePending;
        if (finalPending != baselinePending)
            sb.AppendLine($"⚠️ Pending sectors: {baselinePending} → {finalPending} (Δ {finalPending - baselinePending:+0;-0})");

        // Uncorrectable sectors
        var baselineUncorr = _smartBaseline.UncorrectableErrorCount ?? 0;
        var finalUncorr = _smartFinal?.UncorrectableErrorCount ?? baselineUncorr;
        if (finalUncorr != baselineUncorr)
            sb.AppendLine($"⚠️ Uncorrectable: {baselineUncorr} → {finalUncorr} (Δ {finalUncorr - baselineUncorr:+0;-0})");

        // Power-on hours
        var baselineHours = _smartBaseline.PowerOnHours ?? 0;
        var finalHours = _smartFinal?.PowerOnHours ?? baselineHours;
        sb.AppendLine($"⏱ Power-on hours: {baselineHours} → {finalHours} (Δ {finalHours - baselineHours:+0;-0}h)");

        // Wear leveling (SSD)
        var baselineWear = _smartBaseline.WearLevelingCount ?? 0;
        var finalWear = _smartFinal?.WearLevelingCount ?? baselineWear;
        if (finalWear != baselineWear)
            sb.AppendLine($"🔋 Wear leveling: {baselineWear} → {finalWear} (Δ {finalWear - baselineWear:+0;-0})");

        if (sb.Length <= "═══ SMART ZMĚNY ═══\n\n🌡 Teplota:".Length + 20)
        {
            sb.AppendLine("✅ Žádné významné SMART změny detekovány.");
        }

        return sb.ToString();
    }

    private string BuildSmartDeltaSummary()
    {
        if (_smartBaseline == null && _smartFinal == null)
            return "SMART nedostupný — nelze porovnat";

        var before = _smartBaseline;
        var after = _smartFinal;

        var parts = new List<string>();

        if (before?.ReallocatedSectorCount != null || after?.ReallocatedSectorCount != null)
        {
            var b = before?.ReallocatedSectorCount ?? 0;
            var a = after?.ReallocatedSectorCount ?? 0;
            parts.Add($"Reallocated: {b}→{a} ({(a - b):+0;-0})");
        }

        if (before?.PendingSectorCount != null || after?.PendingSectorCount != null)
        {
            var b = before?.PendingSectorCount ?? 0;
            var a = after?.PendingSectorCount ?? 0;
            parts.Add($"Pending: {b}→{a} ({(a - b):+0;-0})");
        }

        if (before?.PowerOnHours != null || after?.PowerOnHours != null)
        {
            var b = before?.PowerOnHours ?? 0;
            var a = after?.PowerOnHours ?? 0;
            parts.Add($"POH: {b}→{a} (+{a - b}h)");
        }

        if (before?.Temperature != null || after?.Temperature != null)
        {
            var b = before?.Temperature ?? 0;
            var a = after?.Temperature ?? 0;
            parts.Add($"Teplota: {b}→{a}°C ({(a - b):+0;-0}°C)");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : "Beze změn";
    }

    private async Task BuildCertificateAsync()
    {
        var card = await _diskCardRepository.GetByDevicePathAsync(SelectedDrive!.Path);
        if (card == null)
        {
            card = new DiskCard
            {
                DevicePath = SelectedDrive!.Path,
                ModelName = SelectedDrive.Name,
                SerialNumber = SelectedDrive.SerialNumber ?? "",
                Capacity = SelectedDrive.TotalSize,
                DiskType = _smartBaseline?.DeviceType ?? "Unknown",
                CreatedAt = DateTime.UtcNow,
                LastTestedAt = DateTime.UtcNow
            };
            card = await _diskCardRepository.CreateAsync(card);
        }

        var cert = new DiskCertificate
        {
            CertificateNumber = $"DEST-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24],
            DiskCardId = card.Id,
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = Environment.UserName,
            DiskModel = SelectedDrive!.Name,
            SerialNumber = SelectedDrive.SerialNumber ?? "-",
            Capacity = FormatBytes(SelectedDrive.TotalSize),
            DiskType = _smartBaseline?.DeviceType ?? "HDD",
            TestType = "Absolutní destruktivní test",
            TestDuration = DateTime.UtcNow - _testStartTime,
            Grade = CalculateGrade(),
            Score = CalculateScore(),
            HealthStatus = DetermineHealthStatus(),
            Status = CertificateStatus.Active,
            Recommended = DetermineRecommended(),
            RecommendationNotes = BuildRecommendationNotes(),
            Notes = BuildCertificateNotes(),
            SmartPassed = _smartFinal?.ReallocatedSectorCount == 0 && _smartFinal?.PendingSectorCount == 0,
            PowerOnHours = _smartFinal?.PowerOnHours ?? _smartBaseline?.PowerOnHours ?? 0,
            PowerCycles = _smartFinal?.PowerCycleCount ?? _smartBaseline?.PowerCycleCount ?? 0,
            ReallocatedSectors = _smartFinal?.ReallocatedSectorCount ?? _smartBaseline?.ReallocatedSectorCount ?? 0,
            PendingSectors = _smartFinal?.PendingSectorCount ?? _smartBaseline?.PendingSectorCount ?? 0,
            SanitizationPerformed = true,
            SanitizationMethod = "Zero-fill + verify (2×)",
            DataVerified = _sanitize2Result?.Success ?? false,
            ErrorCount = (_sanitize1Result?.ErrorsDetected ?? 0) + (_sanitize2Result?.ErrorsDetected ?? 0),
            TemperatureRange = $"{MinTemperature}–{MaxTemperature}°C"
        };

        // Seek metrics
        var allSeekSamples = new List<SeekLatencySample>();
        if (_seekFullStrokeResult?.Samples != null) allSeekSamples.AddRange(_seekFullStrokeResult.Samples);
        if (_seekRandomResult?.Samples != null) allSeekSamples.AddRange(_seekRandomResult.Samples);
        if (_seekSkipResult?.Samples != null) allSeekSamples.AddRange(_seekSkipResult.Samples);

        if (allSeekSamples.Count > 0)
        {
            var latencies = allSeekSamples.Select(s => s.LatencyMs).OrderBy(l => l).ToList();
            cert.SeekAvgLatencyMs = latencies.Average();
            cert.SeekMinLatencyMs = latencies.Min();
            cert.SeekMaxLatencyMs = latencies.Max();
            cert.SeekStdDevLatencyMs = Math.Sqrt(latencies.Average(l => Math.Pow(l - cert.SeekAvgLatencyMs.Value, 2)));
            cert.SeekP95LatencyMs = Percentile(latencies, 0.95);
            cert.SeekTestSummary =
                $"FS: {_seekFullStrokeResult?.AverageLatencyMs:F2}ms | " +
                $"RND: {_seekRandomResult?.AverageLatencyMs:F2}ms | " +
                $"SKIP: {_seekSkipResult?.AverageLatencyMs:F2}ms";
        }

        // Before/after comparison
        if (_sanitize1Result != null)
        {
            cert.Sanitize1AvgWriteMBps = _sanitize1Result.WriteSpeedMBps;
            cert.Sanitize1AvgReadMBps = _sanitize1Result.ReadSpeedMBps;
            cert.Sanitize1Errors = _sanitize1Result.ErrorsDetected;
        }

        if (_sanitize2Result != null)
        {
            cert.Sanitize2AvgWriteMBps = _sanitize2Result.WriteSpeedMBps;
            cert.Sanitize2AvgReadMBps = _sanitize2Result.ReadSpeedMBps;
            cert.Sanitize2Errors = _sanitize2Result.ErrorsDetected;
        }

        // Populate generic speed fields used by CertificateView (AvgWriteSpeed, AvgReadSpeed, etc.)
        // Use the second sanitization as the "final" speed, or first if second unavailable.
        var finalSanitize = _sanitize2Result ?? _sanitize1Result;
        if (finalSanitize != null)
        {
            cert.AvgWriteSpeed = finalSanitize.WriteSpeedMBps;
            cert.MaxWriteSpeed = finalSanitize.WriteSpeedMBps; // sanitization reports average, use as max
            cert.AvgReadSpeed = finalSanitize.ReadSpeedMBps;
            cert.MaxReadSpeed = finalSanitize.ReadSpeedMBps;
        }

        // Populate chart profile points for the certificate view
        cert.WriteProfilePoints = DownsamplePoints(
            _sanitizePass2WritePoints.Count > 0 ? _sanitizePass2WritePoints : _sanitizePass1WritePoints, 32);
        cert.ReadProfilePoints = DownsamplePoints(
            _sanitizePass2ReadPoints.Count > 0 ? _sanitizePass2ReadPoints : _sanitizePass1ReadPoints, 32);

        if (cert.Sanitize1AvgWriteMBps > 0 && cert.Sanitize2AvgWriteMBps > 0)
        {
            cert.WriteSpeedChangePercent = ((cert.Sanitize2AvgWriteMBps.Value - cert.Sanitize1AvgWriteMBps.Value)
                / cert.Sanitize1AvgWriteMBps.Value) * 100;
        }

        if (cert.Sanitize1AvgReadMBps > 0 && cert.Sanitize2AvgReadMBps > 0)
        {
            cert.ReadSpeedChangePercent = ((cert.Sanitize2AvgReadMBps.Value - cert.Sanitize1AvgReadMBps.Value)
                / cert.Sanitize1AvgReadMBps.Value) * 100;
        }

        cert.SmartDeltaSummary = SmartDeltaSummary;

        Certificate = cert;
    }

    private async Task SaveTestSessionAsync()
    {
        try
        {
            var card = await _diskCardRepository.GetByDevicePathAsync(SelectedDrive!.Path);
            if (card == null)
            {
                // Card must exist before we can attach a test session to it.
                // Create it now so the foreign key is valid.
                card = new DiskCard
                {
                    DevicePath = SelectedDrive!.Path,
                    ModelName = SelectedDrive.Name ?? "Unknown",
                    SerialNumber = SelectedDrive.SerialNumber ?? "",
                    Capacity = SelectedDrive.TotalSize,
                    DiskType = _smartBaseline?.DeviceType ?? "Unknown",
                    CreatedAt = DateTime.UtcNow,
                    LastTestedAt = DateTime.UtcNow
                };
                card = await _diskCardRepository.CreateAsync(card);
            }

            var session = new TestSession
            {
                DiskCardId = card.Id,
                SessionId = Guid.NewGuid(),
                TestType = TestType.AbsoluteDestructive,
                StartedAt = _testStartTime,
                CompletedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - _testStartTime,
                Status = TestStatus.Completed,
                IsDestructive = true,
                WasLocked = true,
                SmartBefore = _smartBaseline,
                SmartAfter = _smartFinal,
                StartTemperature = _smartBaseline?.Temperature,
                MaxTemperature = MaxTemperature,
                AverageTemperature = (MinTemperature + MaxTemperature) / 2.0,
                Result = TestResult.Pass,
                Grade = Certificate?.Grade ?? "?",
                Score = Certificate?.Score ?? 0,
                HealthAssessment = MapHealthAssessment(Certificate?.HealthStatus),
                Notes = Certificate?.Notes,
                SeekResultsJson = JsonSerializer.Serialize(new
                {
                    FullStroke = _seekFullStrokeResult,
                    Random = _seekRandomResult,
                    Skip = _seekSkipResult
                }),
                Sanitize1ResultJson = JsonSerializer.Serialize(_sanitize1Result),
                Sanitize2ResultJson = JsonSerializer.Serialize(_sanitize2Result),
                AnomaliesJson = JsonSerializer.Serialize(
                    (_samplerPass1?.GetAnomalies() ?? new List<SpeedAnomaly>())
                    .Concat(_samplerPass2?.GetAnomalies() ?? new List<SpeedAnomaly>())
                    .ToList())
            };

            // Build SMART changes
            if (_smartBaseline != null && _smartFinal != null)
            {
                session.SmartChanges = BuildSmartChanges(_smartBaseline, _smartFinal);
            }

            await _diskCardRepository.CreateTestSessionAsync(session);

            if (Certificate != null)
            {
                Certificate.TestSessionId = session.Id;
                Certificate.DiskCardId = card.Id;
                await _diskCardRepository.CreateCertificateAsync(Certificate);
                StatusMessage = $"✅ Test dokončen – certifikát {Certificate.CertificateNumber} uložen";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Uložení session selhalo: {ex.Message}";
        }
    }

    // ──────────────────────────────────────────────
    //  Scoring & grading
    // ──────────────────────────────────────────────

    private string CalculateGrade()
    {
        var score = CalculateScore();
        return score switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 55 => "D",
            >= 40 => "E",
            _ => "F"
        };
    }

    private double CalculateScore()
    {
        double score = 100;

        // Errors
        var totalErrors = (_sanitize1Result?.ErrorsDetected ?? 0) + (_sanitize2Result?.ErrorsDetected ?? 0);
        score -= totalErrors * 5;

        // Write speed degradation
        if (_sanitize1Result != null && _sanitize2Result != null &&
            _sanitize1Result.WriteSpeedMBps > 0 && _sanitize2Result.WriteSpeedMBps > 0)
        {
            var writeDegradation = (1 - _sanitize2Result.WriteSpeedMBps / _sanitize1Result.WriteSpeedMBps) * 100;
            if (writeDegradation > 5) score -= (writeDegradation - 5) * 2;
        }

        // Seek latency (high = bad)
        var allLatencies = new List<double>();
        if (_seekFullStrokeResult?.Samples != null) allLatencies.AddRange(_seekFullStrokeResult.Samples.Select(s => s.LatencyMs));
        if (_seekRandomResult?.Samples != null) allLatencies.AddRange(_seekRandomResult.Samples.Select(s => s.LatencyMs));
        if (_seekSkipResult?.Samples != null) allLatencies.AddRange(_seekSkipResult.Samples.Select(s => s.LatencyMs));

        if (allLatencies.Count > 0)
        {
            var avgLatency = allLatencies.Average();
            if (avgLatency > 20) score -= (avgLatency - 20) * 0.5;
            var p95 = Percentile(allLatencies.OrderBy(l => l).ToList(), 0.95);
            if (p95 > 40) score -= (p95 - 40) * 0.3;
        }

        // SMART issues
        if (_smartFinal != null)
        {
            if (_smartFinal.ReallocatedSectorCount > 0) score -= (double)(_smartFinal.ReallocatedSectorCount * 3);
            if (_smartFinal.PendingSectorCount > 0) score -= (double)(_smartFinal.PendingSectorCount * 5);
        }

        // Anomaly-based penalty (adaptive sampling)
        var allAnomalies = (_samplerPass1?.GetAnomalies() ?? new List<SpeedAnomaly>())
            .Concat(_samplerPass2?.GetAnomalies() ?? new List<SpeedAnomaly>())
            .ToList();
        if (allAnomalies.Count > 0)
        {
            var anomalyService = new AnomalyAnalysisService();
            var anomalyPenalty = anomalyService.ComputeAnomalyPenalty(allAnomalies);
            score -= anomalyPenalty;

            // Add anomaly report to notes
            var anomalyReport = anomalyService.GenerateAnomalyReport(allAnomalies);
            if (!string.IsNullOrWhiteSpace(anomalyReport))
            {
                _anomalyReport = anomalyReport;
            }
        }

        return Math.Clamp(score, 0, 100);
    }

    private string DetermineHealthStatus()
    {
        var realloc = _smartFinal?.ReallocatedSectorCount ?? _smartBaseline?.ReallocatedSectorCount ?? 0;
        var pending = _smartFinal?.PendingSectorCount ?? _smartBaseline?.PendingSectorCount ?? 0;
        var errors = (_sanitize1Result?.ErrorsDetected ?? 0) + (_sanitize2Result?.ErrorsDetected ?? 0);

        if (realloc > 50 || pending > 10 || errors > 10) return "Kritický";
        if (realloc > 10 || pending > 0 || errors > 3) return "Varovný";
        if (realloc > 0 || errors > 0) return "Dobrý";
        return "Výborný";
    }

    private bool DetermineRecommended()
    {
        var score = CalculateScore();
        var realloc = _smartFinal?.ReallocatedSectorCount ?? _smartBaseline?.ReallocatedSectorCount ?? 0;
        var pending = _smartFinal?.PendingSectorCount ?? _smartBaseline?.PendingSectorCount ?? 0;
        return score >= 70 && realloc == 0 && pending == 0;
    }

    private string BuildRecommendationNotes()
    {
        if (DetermineRecommended())
            return "Disk prošel absolutním destruktivním testem bez významných problémů. Doporučen k použití.";
        return "Disk vykazuje známky opotřebení. Doporučena zvýšená opatrnost nebo výměna.";
    }

    private string BuildCertificateNotes()
    {
        var notes = new List<string>();
        var errors = (_sanitize1Result?.ErrorsDetected ?? 0) + (_sanitize2Result?.ErrorsDetected ?? 0);
        if (errors > 0) notes.Add($"Detekovány chyby během sanitizace: {errors}");
        if (_smartFinal?.ReallocatedSectorCount > 0) notes.Add($"Realokované sektory: {_smartFinal.ReallocatedSectorCount}");
        if (_smartFinal?.PendingSectorCount > 0) notes.Add($"Pending sektory: {_smartFinal.PendingSectorCount}");

        // Include anomaly report if available
        if (!string.IsNullOrWhiteSpace(_anomalyReport))
            notes.Add(_anomalyReport);

        return string.Join("; ", notes);
    }

    private static HealthAssessment MapHealthAssessment(string? status) => status switch
    {
        "Výborný" => HealthAssessment.Excellent,
        "Dobrý" => HealthAssessment.Good,
        "Varovný" => HealthAssessment.Fair,
        "Kritický" => HealthAssessment.Critical,
        _ => HealthAssessment.Unknown
    };

    // ──────────────────────────────────────────────
    //  Disk recovery
    // ──────────────────────────────────────────────

    /// <summary>
    /// Waits for a disappeared disk to reappear, with countdown and re-initialization attempts.
    /// Called when I/O operations fail with device-not-found errors.
    /// </summary>
    private async Task<bool> RecoverDiskAsync(CancellationToken ct)
    {
        DiskDisappearanceCount++;
        var disappearanceTime = DateTime.UtcNow;
        var logEntry = $"[{disappearanceTime:HH:mm:ss}] Disk zmizel (výskyt #{DiskDisappearanceCount})";

        DiskDisappearanceLog = string.IsNullOrEmpty(DiskDisappearanceLog)
            ? logEntry
            : DiskDisappearanceLog + "\n" + logEntry;

        IsDiskRecoveryActive = true;
        DiskRecoveryStatus = "🔍 Disk přestal odpovídat – čekám na obnovení...";

        const int recoveryTimeoutSeconds = 600; // 10 minutes
        const int checkIntervalSeconds = 5;

        for (int remaining = recoveryTimeoutSeconds; remaining > 0; remaining -= checkIntervalSeconds)
        {
            ct.ThrowIfCancellationRequested();

            DiskRecoverySecondsRemaining = remaining;
            DiskRecoveryCountdown = $"⏳ Čekám {remaining / 60:D2}:{remaining % 60:D2}";

            // Check if disk reappeared
            try
            {
                var drives = System.IO.DriveInfo.GetDrives();
                var found = drives.Any(d =>
                    d.Name.StartsWith(SelectedDrive!.Path, StringComparison.OrdinalIgnoreCase) ||
                    d.Name.Equals(SelectedDrive!.Path, StringComparison.OrdinalIgnoreCase));

                if (!found)
                {
                    // Try raw device check
                    try
                    {
                        using var fs = System.IO.File.OpenRead(SelectedDrive!.Path);
                        found = true;
                    }
                    catch (System.IO.FileNotFoundException) { found = false; }
                    catch (System.IO.IOException) { found = false; }
                }

                if (found)
                {
                    DiskRecoveryStatus = "✅ Disk nalezen – pokouším se o re-inicializaci...";
                    DiskRecoveryCountdown = "Re-inicializace...";

                    // Wait a moment for the OS to stabilize
                    await Task.Delay(2000, ct);

                    // Try to re-initialize
                    try
                    {
                        // Re-read SMART (only if drive supports SMART)
                        if (SelectedDrive!.SupportsSmart)
                        {
                            var smarta = await _smartaProvider.GetSmartaDataAsync(SelectedDrive.Path, ct);
                            if (smarta != null)
                            {
                                _smartBaseline = smarta;
                                SmartBaselineSummary = FormatSmartSummary(smarta, "Po recovery");
                            }
                        }

                        DiskRecoveryStatus = "✅ Disk obnoven – pokračuji v testu";
                        DiskDisappearanceLog += $"\n[{DateTime.UtcNow:HH:mm:ss}] Disk obnoven po {DiskDisappearanceCount}. výskytu (čekalo se {(recoveryTimeoutSeconds - remaining)}s)";
                        IsDiskRecoveryActive = false;
                        return true;
                    }
                    catch
                    {
                        DiskRecoveryStatus = "⚠️ Disk nalezen, ale re-inicializace selhala – zkouším dál...";
                    }
                }
            }
            catch
            {
                // Detection itself failed – disk or controller is gone
            }

            await Task.Delay(checkIntervalSeconds * 1000, ct);
        }

        // Timeout – disk never came back
        DiskRecoveryStatus = "❌ Disk se nevrátil do 10 minut – test končí";
        DiskRecoveryCountdown = "Timeout";
        DiskDisappearanceLog += $"\n[{DateTime.UtcNow:HH:mm:ss}] Recovery timeout – disk se nevrátil";
        IsDiskRecoveryActive = false;
        return false;
    }

    // ──────────────────────────────────────────────
    //  Utility
    // ──────────────────────────────────────────────

    private static string FormatSmartSummary(SmartaData smart, string label)
    {
        return $"{label}: POH={smart.PowerOnHours}h | Realloc={smart.ReallocatedSectorCount} | " +
               $"Pending={smart.PendingSectorCount} | Temp={smart.Temperature}°C | {smart.DeviceModel}";
    }

    private static string FormatBytes(long bytes)
    {
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        return gb >= 1024 ? $"{gb / 1024:F2} TB" : $"{gb:F0} GB";
    }

    /// <summary>
    /// Downsamples a collection of ObservablePoint (X=progress%, Y=speed) to a list of
    /// Y-values suitable for certificate chart rendering.
    /// </summary>
    private static List<double> DownsamplePoints(
        ObservableCollection<ObservablePoint> points, int targetCount)
    {
        if (points.Count == 0) return new List<double>();
        if (points.Count <= targetCount) return points.Select(p => p.Y ?? 0).ToList();

        var result = new List<double>(targetCount);
        var step = (double)points.Count / targetCount;
        for (int i = 0; i < targetCount; i++)
        {
            var idx = (int)(i * step);
            if (idx >= points.Count) idx = points.Count - 1;
            result.Add(points[idx].Y ?? 0);
        }
        return result;
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];

        var index = percentile * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];

        var fraction = index - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static List<SmartAttributeChange> BuildSmartChanges(SmartaData before, SmartaData after)
    {
        var changes = new List<SmartAttributeChange>();

        AddChange("Reallocated Sector Count", before.ReallocatedSectorCount, after.ReallocatedSectorCount);
        AddChange("Pending Sector Count", before.PendingSectorCount, after.PendingSectorCount);
        AddChange("Power-On Hours", before.PowerOnHours, after.PowerOnHours);
        AddChange("Power Cycle Count", before.PowerCycleCount, after.PowerCycleCount);
        AddChange("Temperature", before.Temperature, after.Temperature);

        return changes;

        void AddChange(string name, long? valBefore, long? valAfter)
        {
            var b = valBefore ?? 0;
            var a = valAfter ?? 0;
            if (b != a)
            {
                changes.Add(new SmartAttributeChange
                {
                    AttributeName = name,
                    ValueBefore = b,
                    ValueAfter = a,
                    Change = a - b
                });
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Dispose
    // ──────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _testCancellation?.Cancel();
        _testCancellation?.Dispose();
        _powerSession?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Manual profile option for UI selection.
/// </summary>
public class ManualProfileOption
{
    public ManualTestProfile Profile { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int SeekCount { get; init; }
}

using System;
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
using DiskChecker.UI.Avalonia.Services.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Seek Test feature – measures disk head positioning latency
/// using SMART-informed recommendations (full-stroke, random, or skip seeks).
/// </summary>
public partial class SeekTestViewModel : ViewModelBase, INavigableViewModel, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly IDiskCacheService _diskCacheService;
    private readonly SeekTestService _seekTestService;
    private readonly ICertificateGenerator _certificateGenerator;
    private readonly IDiskCardRepository _diskCardRepository;
    private readonly DiskCardTestService _cardTestService;

    private CoreDriveInfo? _selectedDrive;
    private bool _isTesting;
    private bool _isLoadingRecommendation;
    private string _statusMessage = "Připraven k seek testu";
    private string _recommendationText = "Načítám doporučení...";
    private string _recommendationRationale = "";
    private bool _isRecommendationConservative;
    private bool _isDiskTooFragile;
    private int _recommendedSeekCount;
    private int _maxSafeSeekCount;
    private SeekTestType _recommendedTestType = SeekTestType.Random;
    private SeekTestType _selectedTestType = SeekTestType.Random;
    private int _selectedSeekCount = 3000;
    private int _selectedSkipSegments = 1000;
    private int _blockSizeBytes = 4096;
    private int _timeoutSeconds = 300;

    // Progress
    private double _progressPercent;
    private int _completedSeeks;
    private int _totalSeeks;
    private string _elapsedTime = "00:00";
    private string _estimatedRemaining = "00:00";

    // Results
    private bool _hasResults;
    private double _averageLatencyMs;
    private double _minLatencyMs;
    private double _maxLatencyMs;
    private double _stdDevLatencyMs;
    private double _medianLatencyMs;
    private double _p95LatencyMs;
    private double _p99LatencyMs;
    private int _errorCount;
    private string _resultSummary = "";
    private ObservableCollection<SeekLatencySample> _samples = new();

    // Real-time chart data
    private ObservableCollection<ObservablePoint> _latencyChartValues = new();
    // Real-time chart MUST be initialized with dummy data so LiveCharts2 SkiaSharp
    // initializes its render surface when IsTesting becomes true. The dummy points
    // use negative X values (-9..0) which fall outside the visible area (MinLimit=0),
    // so they are invisible but keep the render surface alive.
    private ISeries[] _latencySeries = new ISeries[]
    {
        new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>(
                Enumerable.Range(-9, 10).Select(i => new ObservablePoint(i, 0))),
            Fill = null,
            Stroke = null,
            GeometrySize = 0,
            LineSmoothness = 0
        }
    };
    private Axis[] _latencyXAxes = new Axis[]
    {
        new Axis { Name = "Seek #", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => v.ToString("F0") }
    };
    private Axis[] _latencyYAxes = new Axis[]
    {
        new Axis { Name = "Latence (ms)", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => $"{v:F1}" }
    };
    private int _chartPointCount;
    private SeekLatencySample? _latestSample;
    private bool _isPrePositioned;

    // Final chart data (separate from real-time to avoid LiveCharts visibility issues).
    // MUST be initialized with dummy data so LiveCharts2 SkiaSharp initializes
    // its render surface immediately. The dummy points use negative X values (-9..0)
    // which fall outside the visible area (MinLimit=0), so they are invisible.
    private ISeries[] _finalLatencySeries = new ISeries[]
    {
        new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>(
                Enumerable.Range(-9, 10).Select(i => new ObservablePoint(i, 0))),
            Fill = null,
            Stroke = null,
            GeometrySize = 0,
            LineSmoothness = 0
        }
    };
    private Axis[] _finalLatencyXAxes = new Axis[]
    {
        new Axis { Name = "Seek #", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => v.ToString("F0") }
    };
    private Axis[] _finalLatencyYAxes = new Axis[]
    {
        new Axis { Name = "Latence (ms)", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => $"{v:F1}" }
    };
    private int _finalChartPointCount;

    // Test type options for the UI – MUST match SeekTestType enum order (FullStroke=0, Random=1, Skip=2)
    // because EnumToIndexConverter uses (int)enumValue as the SelectedIndex.
    public ObservableCollection<SeekTestTypeOption> TestTypeOptions { get; } = new();

    private CancellationTokenSource? _testCancellation;
    private bool _disposed;
    private bool _isFinalChartBuilt; // guards against progress callbacks corrupting final chart

    public SeekTestViewModel(
        INavigationService navigationService,
        ISelectedDiskService selectedDiskService,
        IDialogService dialogService,
        ISettingsService settingsService,
        IDiskCacheService diskCacheService,
        SeekTestService seekTestService,
        ICertificateGenerator certificateGenerator,
        IDiskCardRepository diskCardRepository,
        DiskCardTestService cardTestService)
    {
        _navigationService = navigationService;
        _selectedDiskService = selectedDiskService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _diskCacheService = diskCacheService;
        _seekTestService = seekTestService;
        _certificateGenerator = certificateGenerator;
        _diskCardRepository = diskCardRepository;
        _cardTestService = cardTestService;

        // Build test type options
        TestTypeOptions.Add(new() { Type = SeekTestType.FullStroke, Key = "FullStroke", Name = "↔️ " + L.Get("SeekTest.FullStroke") + " (Full Stroke)", Description = L.Get("SeekTest.FullStrokeDescFull") });
        TestTypeOptions.Add(new() { Type = SeekTestType.Random, Key = "Random", Name = "🎲 " + L.Get("SeekTest.Random") + " (Random)", Description = L.Get("SeekTest.RandomDescFull") });
        TestTypeOptions.Add(new() { Type = SeekTestType.Skip, Key = "Skip", Name = "⏭️ " + L.Get("SeekTest.Skip") + " (Skip)", Description = L.Get("SeekTest.SkipDescFull") });

        GoBackCommand = new RelayCommand(NavigateBack);
        StartTestCommand = new AsyncRelayCommand(StartTestAsync, () => !IsTesting && !IsDiskTooFragile && SelectedDrive != null);
        AbortTestCommand = new AsyncRelayCommand(AbortTestAsync, () => IsTesting);
        LoadRecommendationCommand = new AsyncRelayCommand(LoadRecommendationAsync);
    }

    #region Properties

    public CoreDriveInfo? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (SetProperty(ref _selectedDrive, value))
            {
                OnPropertyChanged(nameof(HasSelectedDrive));
                OnPropertyChanged(nameof(DriveDisplayName));
                OnPropertyChanged(nameof(DriveCapacityText));
                StartTestCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedDrive => SelectedDrive != null;
    public string DriveDisplayName => SelectedDrive?.Name ?? "—";
    public string DriveCapacityText => SelectedDrive != null
        ? FormatBytes(SelectedDrive.TotalSize)
        : "—";

    public bool IsTesting
    {
        get => _isTesting;
        set
        {
            if (SetProperty(ref _isTesting, value))
            {
                StartTestCommand.NotifyCanExecuteChanged();
                AbortTestCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsNotTesting));
            }
        }
    }

    public bool IsNotTesting => !IsTesting;

    public bool IsLoadingRecommendation
    {
        get => _isLoadingRecommendation;
        set => SetProperty(ref _isLoadingRecommendation, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string RecommendationText
    {
        get => _recommendationText;
        set => SetProperty(ref _recommendationText, value);
    }

    public string RecommendationRationale
    {
        get => _recommendationRationale;
        set => SetProperty(ref _recommendationRationale, value);
    }

    public bool IsRecommendationConservative
    {
        get => _isRecommendationConservative;
        set => SetProperty(ref _isRecommendationConservative, value);
    }

    public bool IsDiskTooFragile
    {
        get => _isDiskTooFragile;
        set
        {
            if (SetProperty(ref _isDiskTooFragile, value))
            {
                StartTestCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanRunTest));
            }
        }
    }

    public bool CanRunTest => !IsDiskTooFragile && SelectedDrive != null;

    public int RecommendedSeekCount
    {
        get => _recommendedSeekCount;
        set => SetProperty(ref _recommendedSeekCount, value);
    }

    public int MaxSafeSeekCount
    {
        get => _maxSafeSeekCount;
        set => SetProperty(ref _maxSafeSeekCount, value);
    }

    public SeekTestType RecommendedTestType
    {
        get => _recommendedTestType;
        set => SetProperty(ref _recommendedTestType, value);
    }

    public SeekTestType SelectedTestType
    {
        get => _selectedTestType;
        set
        {
            if (SetProperty(ref _selectedTestType, value))
            {
                OnPropertyChanged(nameof(IsRandomSelected));
                OnPropertyChanged(nameof(IsFullStrokeSelected));
                OnPropertyChanged(nameof(IsSkipSelected));
            }
        }
    }

    public bool IsRandomSelected => SelectedTestType == SeekTestType.Random;
    public bool IsFullStrokeSelected => SelectedTestType == SeekTestType.FullStroke;
    public bool IsSkipSelected => SelectedTestType == SeekTestType.Skip;

    public int SelectedSeekCount
    {
        get => _selectedSeekCount;
        set
        {
            if (SetProperty(ref _selectedSeekCount, value))
            {
                OnPropertyChanged(nameof(IsSeekCountExceedsSafe));
            }
        }
    }

    public bool IsSeekCountExceedsSafe => SelectedSeekCount > MaxSafeSeekCount;

    public int SelectedSkipSegments
    {
        get => _selectedSkipSegments;
        set => SetProperty(ref _selectedSkipSegments, value);
    }

    public int BlockSizeBytes
    {
        get => _blockSizeBytes;
        set => SetProperty(ref _blockSizeBytes, value);
    }

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => SetProperty(ref _timeoutSeconds, value);
    }

    // Progress
    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    public int CompletedSeeks
    {
        get => _completedSeeks;
        set => SetProperty(ref _completedSeeks, value);
    }

    public int TotalSeeks
    {
        get => _totalSeeks;
        set => SetProperty(ref _totalSeeks, value);
    }

    public string ElapsedTime
    {
        get => _elapsedTime;
        set => SetProperty(ref _elapsedTime, value);
    }

    public string EstimatedRemaining
    {
        get => _estimatedRemaining;
        set => SetProperty(ref _estimatedRemaining, value);
    }

    // Results
    public bool HasResults
    {
        get => _hasResults;
        set => SetProperty(ref _hasResults, value);
    }

    public double AverageLatencyMs
    {
        get => _averageLatencyMs;
        set => SetProperty(ref _averageLatencyMs, value);
    }

    public double MinLatencyMs
    {
        get => _minLatencyMs;
        set => SetProperty(ref _minLatencyMs, value);
    }

    public double MaxLatencyMs
    {
        get => _maxLatencyMs;
        set => SetProperty(ref _maxLatencyMs, value);
    }

    public double StdDevLatencyMs
    {
        get => _stdDevLatencyMs;
        set => SetProperty(ref _stdDevLatencyMs, value);
    }

    public double MedianLatencyMs
    {
        get => _medianLatencyMs;
        set => SetProperty(ref _medianLatencyMs, value);
    }

    public double P95LatencyMs
    {
        get => _p95LatencyMs;
        set => SetProperty(ref _p95LatencyMs, value);
    }

    public double P99LatencyMs
    {
        get => _p99LatencyMs;
        set => SetProperty(ref _p99LatencyMs, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        set => SetProperty(ref _errorCount, value);
    }

    public string ResultSummary
    {
        get => _resultSummary;
        set => SetProperty(ref _resultSummary, value);
    }

    public ObservableCollection<SeekLatencySample> Samples
    {
        get => _samples;
        set => SetProperty(ref _samples, value);
    }

    // Real-time chart
    public ObservableCollection<ObservablePoint> LatencyChartValues
    {
        get => _latencyChartValues;
        set => SetProperty(ref _latencyChartValues, value);
    }

    public ISeries[] LatencySeries
    {
        get => _latencySeries;
        set => SetProperty(ref _latencySeries, value);
    }

    public Axis[] LatencyXAxes
    {
        get => _latencyXAxes;
        set => SetProperty(ref _latencyXAxes, value);
    }

    public Axis[] LatencyYAxes
    {
        get => _latencyYAxes;
        set => SetProperty(ref _latencyYAxes, value);
    }

    public int ChartPointCount
    {
        get => _chartPointCount;
        set => SetProperty(ref _chartPointCount, value);
    }

    // Final chart properties (separate from real-time chart)
    public ISeries[] FinalLatencySeries
    {
        get => _finalLatencySeries;
        set => SetProperty(ref _finalLatencySeries, value);
    }

    public Axis[] FinalLatencyXAxes
    {
        get => _finalLatencyXAxes;
        set => SetProperty(ref _finalLatencyXAxes, value);
    }

    public Axis[] FinalLatencyYAxes
    {
        get => _finalLatencyYAxes;
        set => SetProperty(ref _finalLatencyYAxes, value);
    }

    public int FinalChartPointCount
    {
        get => _finalChartPointCount;
        set => SetProperty(ref _finalChartPointCount, value);
    }

    public SeekLatencySample? LatestSample
    {
        get => _latestSample;
        set
        {
            if (SetProperty(ref _latestSample, value))
            {
                OnPropertyChanged(nameof(HasLatestSample));
                OnPropertyChanged(nameof(LatestSampleText));
            }
        }
    }

    public bool HasLatestSample => LatestSample != null;

    public string LatestSampleText => LatestSample != null
        ? $"#{LatestSample.Index}: {LatestSample.LatencyMs:F2} ms (Δ {LatestSample.SeekDistance} LBA)"
        : "—";

    public bool IsPrePositioned
    {
        get => _isPrePositioned;
        set => SetProperty(ref _isPrePositioned, value);
    }

    #endregion

    #region Commands

    public IRelayCommand GoBackCommand { get; }
    public IAsyncRelayCommand StartTestCommand { get; }
    public IAsyncRelayCommand AbortTestCommand { get; }
    public IAsyncRelayCommand LoadRecommendationCommand { get; }

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        var drive = _selectedDiskService.SelectedDisk;
        if (drive != null)
        {
            SelectedDrive = drive;
            _ = LoadRecommendationAsync();
        }
    }

    private void NavigateBack()
    {
        _navigationService.NavigateTo<DiskSelectionViewModel>();
    }

    #endregion

    #region Recommendation

    private async Task LoadRecommendationAsync()
    {
        if (SelectedDrive == null) return;

        IsLoadingRecommendation = true;
        StatusMessage = "Analyzuji SMART data pro doporučení...";

        try
        {
            var rec = await _seekTestService.GetRecommendationAsync(SelectedDrive);

            var typeName = rec.RecommendedType switch
            {
                SeekTestType.FullStroke => L.Get("SeekTest.FullStroke"),
                SeekTestType.Random => L.Get("SeekTest.Random"),
                SeekTestType.Skip => L.Get("SeekTest.Skip"),
                _ => "?"
            };
            RecommendationText = rec.IsTooFragile
                ? "❌ " + L.Get("SeekTest.NotRecommended")
                : rec.IsConservative
                    ? $"⚠️ {L.Get("SeekTest.ConservativeMode")}: {typeName} – {rec.RecommendedSeekCount} {L.Get("SeekTestSeeks")}"
                    : $"✅ {L.Get("SeekTest.FullMode")}: {typeName} – {rec.RecommendedSeekCount} {L.Get("SeekTestSeeks")}";

            RecommendationRationale = rec.Rationale;
            IsRecommendationConservative = rec.IsConservative;
            IsDiskTooFragile = rec.IsTooFragile;
            RecommendedSeekCount = rec.RecommendedSeekCount;
            MaxSafeSeekCount = rec.MaxSafeSeekCount;
            RecommendedTestType = rec.RecommendedType;

            // Auto-apply recommendation
            SelectedTestType = rec.RecommendedType;
            SelectedSeekCount = rec.RecommendedSeekCount;
            SelectedSkipSegments = rec.RecommendedSkipSegments;

            if (rec.IsTooFragile)
            {
                StatusMessage = "Disk je příliš opotřebený pro seek test – test nelze spustit";
            }
            else
            {
                StatusMessage = $"Doporučení načteno: {rec.RecommendedSeekCount} seeků ({rec.RecommendedType})";
            }
        }
        catch (Exception ex)
        {
            RecommendationText = "⚠️ Nepodařilo se načíst doporučení";
            RecommendationRationale = $"Chyba: {ex.Message}";
            StatusMessage = "Chyba při načítání doporučení";
        }
        finally
        {
            IsLoadingRecommendation = false;
        }
    }

    #endregion

    #region Test Execution

    private async Task StartTestAsync()
    {
        if (SelectedDrive == null || IsDiskTooFragile) return;

        // Confirm if user selected more than recommended
        if (SelectedSeekCount > MaxSafeSeekCount)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Překročení bezpečného limitu",
                $"Požadovaný počet seeků ({SelectedSeekCount}) překračuje bezpečný limit ({MaxSafeSeekCount}) " +
                $"doporučený na základě SMART dat.\n\nChcete přesto pokračovat?");
            if (!confirmed) return;
        }

        IsTesting = true;
        HasResults = false;
        ProgressPercent = 0;
        CompletedSeeks = 0;
        TotalSeeks = SelectedSeekCount;
        ElapsedTime = "00:00";
        EstimatedRemaining = "—";
        Samples.Clear();
        ErrorCount = 0;
        LatestSample = null;
        IsPrePositioned = false;
        StatusMessage = "Spouštím seek test...";

        // Clear final chart from previous test so only the real-time chart shows during testing.
        // We intentionally use new T[0] instead of Array.Empty<T>() because LiveCharts2
        // SkiaSharp needs a fresh reference to detect the change; Array.Empty returns a singleton.
#pragma warning disable CA1825
        FinalLatencySeries = new ISeries[0];
        FinalChartPointCount = 0;
        FinalLatencyXAxes = new Axis[0];
        FinalLatencyYAxes = new Axis[0];
#pragma warning restore CA1825

        // Initialize real-time chart
        LatencyChartValues.Clear();
        LatencySeries = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Values = LatencyChartValues,
                Fill = null,
                GeometrySize = 4,
                GeometryFill = new SolidColorPaint(SKColors.DodgerBlue),
                GeometryStroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                Stroke = new SolidColorPaint(SKColors.DodgerBlue, 1.5f),
                LineSmoothness = 0.3
            }
        };
        LatencyXAxes = new Axis[]
        {
            new Axis
            {
                Name = "Seek #",
                NameTextSize = 10,
                TextSize = 9,
                MinLimit = 0,
                Labeler = v => v.ToString("F0")
            }
        };
        LatencyYAxes = new Axis[]
        {
            new Axis
            {
                Name = "Latence (ms)",
                NameTextSize = 10,
                TextSize = 9,
                MinLimit = 0,
                Labeler = v => $"{v:F1}"
            }
        };
        ChartPointCount = 0;

        _testCancellation = new CancellationTokenSource();
        _isFinalChartBuilt = false;
        var startTime = DateTime.UtcNow;

        try
        {
            var request = new SeekTestRequest
            {
                Drive = SelectedDrive,
                TestType = SelectedTestType,
                SeekCount = SelectedSeekCount,
                SkipSegments = SelectedSkipSegments,
                BlockSizeBytes = BlockSizeBytes,
                TimeoutSeconds = TimeoutSeconds
            };

            var result = await _seekTestService.RunAsync(
                request,
                progress =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        // Guard: don't touch chart data after final chart is built
                        if (_isFinalChartBuilt) return;

                        ProgressPercent = progress.PercentComplete;
                        CompletedSeeks = progress.SeeksCompleted;
                        TotalSeeks = progress.TotalSeeks;
                        var elapsed = DateTime.UtcNow - startTime;
                        ElapsedTime = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";

                        if (progress.SeeksCompleted > 0 && progress.PercentComplete > 0 && progress.PercentComplete < 100)
                        {
                            var totalEstimated = elapsed.TotalSeconds / (progress.PercentComplete / 100.0);
                            var remaining = totalEstimated - elapsed.TotalSeconds;
                            if (remaining > 0)
                            {
                                EstimatedRemaining = $"{(int)remaining / 60:D2}:{(int)remaining % 60:D2}";
                            }
                        }
                        else if (progress.PercentComplete >= 100)
                        {
                            EstimatedRemaining = "00:00";
                        }

                        // Real-time sample update
                        if (progress.LatestSample != null && !progress.LatestSample.HasError)
                        {
                            LatestSample = progress.LatestSample;
                            LatencyChartValues.Add(new ObservablePoint(
                                progress.SeeksCompleted,
                                progress.LatestSample.LatencyMs));
                            ChartPointCount = LatencyChartValues.Count;
                        }

                        // Pre-positioning indicator (first sample received = pre-positioning done)
                        if (!IsPrePositioned && progress.SeeksCompleted > 0)
                        {
                            IsPrePositioned = true;
                        }

                        StatusMessage = $"Testování... {progress.SeeksCompleted}/{progress.TotalSeeks} seeků ({progress.PercentComplete:F0}%)";
                    });
                },
                _testCancellation.Token);

            // Populate results
            HasResults = true;
            AverageLatencyMs = result.AverageLatencyMs;
            MinLatencyMs = result.MinLatencyMs;
            MaxLatencyMs = result.MaxLatencyMs;
            StdDevLatencyMs = result.LatencyStdDevMs;
            MedianLatencyMs = result.MedianLatencyMs;
            P95LatencyMs = result.P95LatencyMs;
            P99LatencyMs = result.P99LatencyMs;
            ErrorCount = result.ErrorCount;
            Samples = new ObservableCollection<SeekLatencySample>(result.Samples);

            // Build final chart with all successful samples
            BuildFinalChart(result.Samples);
            _isFinalChartBuilt = true;

            var elapsed = DateTime.UtcNow - startTime;
            ElapsedTime = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
            EstimatedRemaining = "00:00";
            ProgressPercent = 100;

            ResultSummary = result.IsCompleted
                ? $"✅ Test dokončen: {result.SeekCount} seeků, průměrná latence {result.AverageLatencyMs:F2} ms"
                : $"⚠️ Test přerušen: {result.SeekCount} seeků";

            StatusMessage = result.IsCompleted
                ? $"Test dokončen – průměrná latence {result.AverageLatencyMs:F2} ms"
                : "Test přerušen";

            // Generate and save certificate
            if (result.IsCompleted)
            {
                await SaveTestSessionAndCertificateAsync(result);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Test zrušen uživatelem";
            HasResults = true;
            ResultSummary = "⚠️ Test zrušen uživatelem";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba testu: {ex.Message}";
            HasResults = true;
            ResultSummary = $"❌ Chyba: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
            _testCancellation?.Dispose();
            _testCancellation = null;
        }
    }

    /// <summary>
    /// Saves the test session and generates a certificate for the completed seek test.
    /// </summary>
    private async Task SaveTestSessionAndCertificateAsync(SeekTestResult result)
    {
        try
        {
            if (SelectedDrive == null) return;

            // Use GetOrCreateCardAsync for proper identity resolution (SMART serial,
            // legacy key, path fallback with model/capacity matching).
            var card = await _cardTestService.GetOrCreateCardAsync(SelectedDrive);

            var session = new TestSession
            {
                DiskCardId = card.Id,
                SessionId = Guid.NewGuid(),
                TestType = TestType.Seek,
                StartedAt = DateTime.UtcNow - result.Duration,
                CompletedAt = DateTime.UtcNow,
                Duration = result.Duration,
                Status = TestStatus.Completed,
                IsDestructive = false,
                WasLocked = false,
                Result = TestResult.Pass,
                Grade = CalculateSeekGrade(result),
                Score = CalculateSeekScore(result),
                Notes = $"Seek test: {SelectedTestType}, {result.SeekCount} seeků, avg {result.AverageLatencyMs:F2} ms",
                SeekResultsJson = JsonSerializer.Serialize(result)
            };

            session = await _diskCardRepository.CreateTestSessionAsync(session);
            await _diskCardRepository.CreateSeekSamplesAsync(session.Id, result.TestType, result.Samples);

            var cert = await _certificateGenerator.GenerateCertificateAsync(session, card);
            cert.CertificateNumber = $"SEEK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24];
            cert.TestType = $"Seek test ({SelectedTestType})";
            cert.SeekAvgLatencyMs = result.AverageLatencyMs;
            cert.SeekMinLatencyMs = result.MinLatencyMs;
            cert.SeekMaxLatencyMs = result.MaxLatencyMs;
            cert.SeekStdDevLatencyMs = result.LatencyStdDevMs;
            cert.SeekMedianLatencyMs = result.MedianLatencyMs;
            cert.SeekP95LatencyMs = result.P95LatencyMs;
            cert.SeekP99LatencyMs = result.P99LatencyMs;
            cert.SeekTotalCount = result.SeekCount;
            cert.SeekErrorCount = result.ErrorCount;
            cert.SeekTestSummary = $"{SelectedTestType}: {result.SeekCount} seekĹŻ, avg {result.AverageLatencyMs:F2} ms, p95 {result.P95LatencyMs:F2} ms";cert.Notes = session.Notes;
            await _diskCardRepository.CreateCertificateAsync(cert);

            StatusMessage = $"✅ Test dokončen – certifikát {cert.CertificateNumber} uložen";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Test dokončen, ale certifikát se nepodařilo uložit: {ex.Message}";
        }
    }

    /// <summary>
    /// Calculates seek test grade based on consistency and reliability,
    /// NOT absolute latency. A 5400 RPM disk with stable 35ms seeks
    /// deserves an A just as much as a 7200 RPM disk with stable 12ms seeks.
    /// </summary>
    private static string CalculateSeekGrade(SeekTestResult result)
    {
        var score = CalculateSeekScore(result);
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

    /// <summary>
    /// Calculates seek test score (0-100) based on consistency metrics:
    /// CV (coefficient of variation), tail ratio (P99/median),
    /// outlier rate, and error rate. Absolute latency only penalizes
    /// extreme cases (>100ms avg).
    /// </summary>
    private static int CalculateSeekScore(SeekTestResult result)
    {
        var successful = result.Samples.Where(s => !s.HasError && s.LatencyMs > 0).ToList();
        if (successful.Count == 0)
            return result.ErrorCount > 0 ? 0 : 50; // no data = neutral

        var latencies = successful.Select(s => s.LatencyMs).ToList();
        var avg = latencies.Average();
        var stdDev = result.LatencyStdDevMs;
        if (stdDev <= 0 && latencies.Count > 1)
            stdDev = Math.Sqrt(latencies.Sum(l => (l - avg) * (l - avg)) / latencies.Count);

        double score = 100;

        // 1. Error rate penalty (most important for reliability)
        var errorRate = (double)result.ErrorCount / Math.Max(1, result.SeekCount);
        if (errorRate > 0.10) score -= 40;       // >10% errors = critical
        else if (errorRate > 0.05) score -= 25;   // >5% errors = serious
        else if (errorRate > 0.02) score -= 15;   // >2% errors = concerning
        else if (errorRate > 0) score -= 5;        // any errors = minor

        // 2. Coefficient of Variation (CV = stdDev/avg) - measures consistency
        if (avg > 0 && stdDev > 0)
        {
            var cv = stdDev / avg;
            if (cv > 0.50) score -= 30;       // very inconsistent
            else if (cv > 0.30) score -= 20;  // inconsistent
            else if (cv > 0.20) score -= 10;  // somewhat inconsistent
            else if (cv > 0.10) score -= 3;   // slightly inconsistent
            // cv <= 0.10 = excellent consistency, no penalty
        }

        // 3. Tail ratio (P99/Median) - measures outlier severity
        var median = result.MedianLatencyMs > 0 ? result.MedianLatencyMs
            : (latencies.Count > 0 ? SortedPercentile(latencies.OrderBy(l => l).ToList(), 0.50) : avg);
        var p99 = result.P99LatencyMs > 0 ? result.P99LatencyMs
            : (latencies.Count > 0 ? SortedPercentile(latencies.OrderBy(l => l).ToList(), 0.99) : avg);
        if (median > 0 && p99 > 0)
        {
            var tailRatio = p99 / median;
            if (tailRatio > 5.0) score -= 25;     // extreme outliers
            else if (tailRatio > 3.0) score -= 15; // significant outliers
            else if (tailRatio > 2.0) score -= 8;  // moderate outliers
            else if (tailRatio > 1.5) score -= 3;  // minor outliers
            // tailRatio <= 1.5 = tight distribution, no penalty
        }

        // 4. Outlier rate (samples > avg + 2*stdDev)
        if (stdDev > 0)
        {
            var outlierThreshold = avg + 2 * stdDev;
            var outlierCount = latencies.Count(l => l > outlierThreshold);
            var outlierRate = (double)outlierCount / latencies.Count;
            if (outlierRate > 0.10) score -= 20;
            else if (outlierRate > 0.05) score -= 10;
            else if (outlierRate > 0.02) score -= 5;
        }

        // 5. Absolute latency sanity check (only penalize extreme cases)
        // A 5400 RPM disk with 35ms avg is NORMAL, not a problem.
        if (avg > 100) score -= 15;    // very slow, possibly failing
        else if (avg > 60) score -= 5; // slow but could be old 4200 RPM

        return Math.Max(0, (int)Math.Clamp(score, 0, 100));
    }

    /// <summary>
    /// Computes a percentile from a sorted list using linear interpolation.
    /// (Local copy to avoid dependency on SeekTestExecutor internals.)
    /// </summary>
    private static double SortedPercentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        double index = percentile * (sorted.Count - 1);
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        double fraction = index - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }

    /// <summary>
    /// Builds the final latency distribution chart from all successful samples.
    /// Shows each sample as a scatter point with outlier coloring and reference lines.
    /// </summary>
    private void BuildFinalChart(List<SeekLatencySample> allSamples)
    {
        var successful = allSamples.Where(s => !s.HasError).ToList();
        if (successful.Count == 0) return;

        var latencies = successful.Select(s => s.LatencyMs).ToList();
        var avg = latencies.Average();
        var min = latencies.Min();
        var max = latencies.Max();
        var stdDev = StdDevLatencyMs;

        // Outlier thresholds
        var outlierThreshold = avg + 2 * stdDev;   // orange
        var extremeThreshold = avg + 3 * stdDev;   // red

        // Split points into three color groups
        var normalPoints = new ObservableCollection<ObservablePoint>();
        var outlierPoints = new ObservableCollection<ObservablePoint>();
        var extremePoints = new ObservableCollection<ObservablePoint>();

        foreach (var s in successful)
        {
            var point = new ObservablePoint(s.Index, s.LatencyMs);
            if (s.LatencyMs > extremeThreshold && stdDev > 0)
                extremePoints.Add(point);
            else if (s.LatencyMs > outlierThreshold && stdDev > 0)
                outlierPoints.Add(point);
            else
                normalPoints.Add(point);
        }

        // Build series list
        var seriesList = new List<ISeries>();

        // Normal points (blue)
        if (normalPoints.Count > 0)
        {
            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Values = normalPoints,
                Fill = null,
                Stroke = null,
                GeometrySize = 5,
                GeometryFill = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(180)),
                GeometryStroke = new SolidColorPaint(SKColors.DodgerBlue, 1),
                LineSmoothness = 0
            });
        }

        // Outlier points (orange, avg + 2σ)
        if (outlierPoints.Count > 0)
        {
            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Values = outlierPoints,
                Fill = null,
                Stroke = null,
                GeometrySize = 7,
                GeometryFill = new SolidColorPaint(SKColors.Orange.WithAlpha(200)),
                GeometryStroke = new SolidColorPaint(SKColors.Orange, 1.5f),
                LineSmoothness = 0
            });
        }

        // Extreme outlier points (red, avg + 3σ)
        if (extremePoints.Count > 0)
        {
            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Values = extremePoints,
                Fill = null,
                Stroke = null,
                GeometrySize = 9,
                GeometryFill = new SolidColorPaint(SKColors.Red.WithAlpha(220)),
                GeometryStroke = new SolidColorPaint(SKColors.Red, 2),
                LineSmoothness = 0
            });
        }

        // Average reference line (orange-red, dashed)
        seriesList.Add(new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>
            {
                new(1, avg),
                new(successful.Count, avg)
            },
            Stroke = new SolidColorPaint(SKColors.OrangeRed, 2),
            GeometrySize = 0,
            Fill = null,
            LineSmoothness = 0
        });

        // Median reference line (purple, dashed)
        var median = MedianLatencyMs;
        seriesList.Add(new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>
            {
                new(1, median),
                new(successful.Count, median)
            },
            Stroke = new SolidColorPaint(SKColors.MediumPurple, 2),
            GeometrySize = 0,
            Fill = null,
            LineSmoothness = 0
        });

        // P95 reference line (gold, dotted)
        var p95 = P95LatencyMs;
        if (p95 > 0)
        {
            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Values = new ObservableCollection<ObservablePoint>
                {
                    new(1, p95),
                    new(successful.Count, p95)
                },
                Stroke = new SolidColorPaint(SKColors.Gold, 1.5f),
                GeometrySize = 0,
                Fill = null,
                LineSmoothness = 0
            });
        }

        // P99 reference line (dark orange, dotted)
        var p99 = P99LatencyMs;
        if (p99 > 0)
        {
            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Values = new ObservableCollection<ObservablePoint>
                {
                    new(1, p99),
                    new(successful.Count, p99)
                },
                Stroke = new SolidColorPaint(SKColors.DarkOrange, 1.5f),
                GeometrySize = 0,
                Fill = null,
                LineSmoothness = 0
            });
        }

        // Min reference line (green)
        seriesList.Add(new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>
            {
                new(1, min),
                new(successful.Count, min)
            },
            Stroke = new SolidColorPaint(SKColors.LimeGreen, 1.5f),
            GeometrySize = 0,
            Fill = null,
            LineSmoothness = 0
        });

        // Max reference line (red)
        seriesList.Add(new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>
            {
                new(1, max),
                new(successful.Count, max)
            },
            Stroke = new SolidColorPaint(SKColors.Tomato, 1.5f),
            GeometrySize = 0,
            Fill = null,
            LineSmoothness = 0
        });

        LatencySeries = seriesList.ToArray();

        // Store all points for chart values (for count display)
        var allPoints = successful.Select(s => new ObservablePoint(s.Index, s.LatencyMs)).ToList();
        LatencyChartValues = new ObservableCollection<ObservablePoint>(allPoints);
        ChartPointCount = allPoints.Count;

        LatencyXAxes = new Axis[]
        {
            new Axis
            {
                Name = "Seek #",
                NameTextSize = 10,
                TextSize = 9,
                MinLimit = 0,
                MaxLimit = successful.Count + 1,
                Labeler = v => v.ToString("F0")
            }
        };

        var yMax = max * 1.15; // 15% headroom
        LatencyYAxes = new Axis[]
        {
            new Axis
            {
                Name = "Latence (ms)",
                NameTextSize = 10,
                TextSize = 9,
                MinLimit = 0,
                MaxLimit = yMax,
                Labeler = v => $"{v:F1}"
            }
        };

        // Also populate final chart properties (separate bindings for the results section).
        // LiveCharts2 SkiaSharp sometimes fails to pick up series/axes changes when set
        // directly. A two-step assignment via Dispatcher forces a redraw.
        var finalSeries = seriesList.ToArray();
        var finalXAxes = new Axis[]
        {
            new Axis
            {
                Name = "Seek #",
                NameTextSize = 10,
                TextSize = 9,
                MinLimit = 0,
                MaxLimit = successful.Count + 1,
                Labeler = v => v.ToString("F0")
            }
        };
        var finalYAxes = new Axis[]
        {
            new Axis
            {
                Name = "Latence (ms)",
                NameTextSize = 10,
                TextSize = 9,
                MinLimit = 0,
                MaxLimit = yMax,
                Labeler = v => $"{v:F1}"
            }
        };

        // BuildFinalChart runs on a background thread (async command). SkiaSharp
        // requires changes from the UI thread to trigger a redraw. We use
        // Dispatcher.UIThread.Post with a two-step assignment:
        //   1. Clear to new T[0] (fresh reference, NOT Array.Empty singleton)
        //   2. Set real data
        // This forces LiveCharts2 to detect the delta and render.
        Dispatcher.UIThread.Post(() =>
        {
            // Step 1: clear with fresh references
#pragma warning disable CA1825
            FinalLatencySeries = new ISeries[0];
            FinalLatencyXAxes = new Axis[0];
            FinalLatencyYAxes = new Axis[0];
#pragma warning restore CA1825

            // Step 2: set real data – chart detects the delta and redraws
            FinalLatencySeries = finalSeries;
            FinalLatencyXAxes = finalXAxes;
            FinalLatencyYAxes = finalYAxes;
            FinalChartPointCount = allPoints.Count;
        });
    }

    private async Task AbortTestAsync()
    {
        _testCancellation?.Cancel();
        StatusMessage = "Ruším test...";
        await Task.CompletedTask;
    }

    #endregion

    #region Helpers

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {suffixes[order]}";
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var cts = Interlocked.Exchange(ref _testCancellation, null);
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        cts?.Dispose();

        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// UI model for seek test type selection.
/// </summary>
public class SeekTestTypeOption
{
    public SeekTestType Type { get; init; }
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
}
using System;
using System.Collections.ObjectModel;
using System.Linq;
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
    private ISeries[] _latencySeries = Array.Empty<ISeries>();
    private Axis[] _latencyXAxes = Array.Empty<Axis>();
    private Axis[] _latencyYAxes = Array.Empty<Axis>();
    private int _chartPointCount;
    private SeekLatencySample? _latestSample;
    private bool _isPrePositioned;

    // Final chart data (separate from real-time to avoid LiveCharts visibility issues)
    private ISeries[] _finalLatencySeries = Array.Empty<ISeries>();
    private Axis[] _finalLatencyXAxes = Array.Empty<Axis>();
    private Axis[] _finalLatencyYAxes = Array.Empty<Axis>();
    private int _finalChartPointCount;

    // Test type options for the UI – MUST match SeekTestType enum order (FullStroke=0, Random=1, Skip=2)
    // because EnumToIndexConverter uses (int)enumValue as the SelectedIndex.
    public ObservableCollection<SeekTestTypeOption> TestTypeOptions { get; } = new()
    {
        new() { Type = SeekTestType.FullStroke, Name = "↔️ Plný rozsah (Full Stroke)", Description = "Zametá celý LBA rozsah od začátku do konce a zpět" },
        new() { Type = SeekTestType.Random, Name = "🎲 Náhodný (Random)", Description = "Náhodné pozice napříč celým diskem – referenční test" },
        new() { Type = SeekTestType.Skip, Name = "⏭️ Přeskakování (Skip)", Description = "Skáče o fixní segmenty (1000 segmentů) s variabilní velikostí skoku" }
    };

    private CancellationTokenSource? _testCancellation;
    private bool _disposed;
    private bool _isFinalChartBuilt; // guards against progress callbacks corrupting final chart

    public SeekTestViewModel(
        INavigationService navigationService,
        ISelectedDiskService selectedDiskService,
        IDialogService dialogService,
        ISettingsService settingsService,
        IDiskCacheService diskCacheService,
        SeekTestService seekTestService)
    {
        _navigationService = navigationService;
        _selectedDiskService = selectedDiskService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _diskCacheService = diskCacheService;
        _seekTestService = seekTestService;

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

            RecommendationText = rec.IsTooFragile
                ? "❌ Test NEDOPORUČEN"
                : rec.IsConservative
                    ? $"⚠️ Konzervativní režim: {rec.RecommendedType switch
                    {
                        SeekTestType.FullStroke => "Plný rozsah (Full Stroke)",
                        SeekTestType.Random => "Náhodný (Random)",
                        SeekTestType.Skip => "Přeskakování (Skip)",
                        _ => "Neznámý"
                    }} – {rec.RecommendedSeekCount} seeků"
                    : $"✅ Plný režim: {rec.RecommendedType switch
                    {
                        SeekTestType.FullStroke => "Plný rozsah (Full Stroke)",
                        SeekTestType.Random => "Náhodný (Random)",
                        SeekTestType.Skip => "Přeskakování (Skip)",
                        _ => "Neznámý"
                    }} – {rec.RecommendedSeekCount} seeků";

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

        // Also populate final chart properties (separate bindings for the results section)
        FinalLatencySeries = seriesList.ToArray();
        FinalChartPointCount = allPoints.Count;
        FinalLatencyXAxes = new Axis[]
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
        FinalLatencyYAxes = new Axis[]
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
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
}

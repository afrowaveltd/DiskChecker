using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Hardware.Sanitization;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.UI.Avalonia.ViewModels;

public partial class SurfaceTestViewModel : ViewModelBase, INavigableViewModel, IDisposable
{
    private const int MaxGraphPoints = 10000;
    
    // Downsampling - track last X and Y values
    private double _lastWriteX = double.MinValue;
    private double _lastReadX = double.MinValue;
    private double _lastWriteSpeed;
    private double _lastReadSpeed;
    private const double MinPointDistance = 0.1; // 0.1% distance between points = ~1000 points max
    private const double SpeedChangeThreshold = 0.05; // 5% speed change triggers new point
    private const double GbInBytes = 1024d * 1024d * 1024d;

    private readonly INavigationService _navigationService;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly IDiskCacheService _diskCacheService;
    private readonly IDiskSanitizationService _sanitizationService;
    private readonly DiskCardTestService _cardTestService;
    private readonly TestCompletionNotificationService _notificationService;
    private readonly ICertificateGenerator _certificateGenerator;
    
    private double _writeProgress;
    private double _verifyProgress;
    
    // Combined stats (for backward compatibility / total view)
    private double _currentSpeed;
    private double _minSpeed = double.MaxValue;
    private double _maxSpeed;
    private double _avgSpeed;
    private double _combinedSpeedSum;
    private int _combinedSpeedSamples;
    
    // Write phase statistics
    private double _writeCurrentSpeed;
    private double _writeMinSpeed = double.MaxValue;
    private double _writeMaxSpeed;
    private double _writeAvgSpeed;
    private double _writeSpeedSum;
    private int _writeSpeedSamples;
    
    // Read phase statistics  
    private double _readCurrentSpeed;
    private double _readMinSpeed = double.MaxValue;
    private double _readMaxSpeed;
    private double _readAvgSpeed;
    private double _readSpeedSum;
    private int _readSpeedSamples;
    private int _currentTemperature = 35;
    private int _minTemperature = int.MaxValue;
    private int _maxTemperature;
    private int _errorCount;
    private string _timeRemaining = "00:00:00";
    private string _statusMessage = "Připraven k testu";
    private bool _isTesting;
    private CoreDriveInfo? _selectedDrive;
    private bool _isLocked;
    private bool _isLoadingDrives;
    
    // Graph data
    private DateTime _testStartTime;
    private DateTime _phaseStartedAtUtc;
    private double _writePhaseMaxElapsedSeconds;
    private double _readPhaseMaxElapsedSeconds;
    private int _selectedZoomIndex = 1; // Default 5 min
    private int _currentPhase; // 0 = Write, 1 = Read (default is 0)
    private CancellationTokenSource? _testCancellation;
    private double _writeBucketStart = -1;
    private double _writeBucketSum;
    private int _writeBucketCount;
    private double _readBucketStart = -1;
    private double _readBucketSum;
    private int _readBucketCount;
    private bool _isDataWindowZoomEnabled;
    private int _zoomWindowModeIndex; // 0 = GB, 1 = %
    private double _zoomWindowGb = 10;
    private double _zoomWindowPercent = 10;
    private double _selectedZoomPresetGb = 10;
    private double _selectedZoomPresetPercent = 10;
    private double _currentDataPercent;
    private long _currentPhaseTotalBytes = 1;

    // Sample pulse indicators
    private long _samplePulseSequence;
    private bool _isWriteSamplePulseVisible;
    private bool _isReadSamplePulseVisible;

    public SurfaceTestViewModel(
        INavigationService navigationService,
        ISelectedDiskService selectedDiskService,
        IDialogService dialogService,
        ISettingsService settingsService,
        IDiskCacheService diskCacheService,
        IDiskSanitizationService sanitizationService,
        DiskCardTestService cardTestService,
        TestCompletionNotificationService notificationService,
        ICertificateGenerator certificateGenerator)
    {
        _navigationService = navigationService;
        _selectedDiskService = selectedDiskService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _diskCacheService = diskCacheService;
        _sanitizationService = sanitizationService;
        _cardTestService = cardTestService;
        _notificationService = notificationService;
        _certificateGenerator = certificateGenerator;
        
        AvailableDrives = new ObservableCollection<CoreDriveInfo>();
        SpeedHistory = new ObservableCollection<SpeedDataPoint>();
        WriteSpeedHistory = new ObservableCollection<SurfaceTestDataPoint>();
        ReadSpeedHistory = new ObservableCollection<SurfaceTestDataPoint>();
        TemperatureHistory = new ObservableCollection<TemperatureDataPoint>();
        ZoomLevels = new ObservableCollection<GraphZoomLevel>(GraphZoomLevel.DefaultZoomLevels);
        WriteSeriesValues = new ObservableCollection<ObservablePoint>();
        ReadSeriesValues = new ObservableCollection<ObservablePoint>();

        SpeedSeries =
        [
            new LineSeries<ObservablePoint>
            {
                Name = "Zápis",
                Values = WriteSeriesValues,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(239, 68, 68), 2), // Red
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            },
            new LineSeries<ObservablePoint>
            {
                Name = "Čtení",
                Values = ReadSeriesValues,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(34, 197, 94), 2), // Green
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            }
        ];

        SpeedXAxes =
        [
            new Axis
            {
                Name = "zaznamenaná data (%)",
                MinLimit = 0,
                MaxLimit = 100,
                LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                TextSize = 10,
                Labeler = value => FormatPercentWithCapacityLabel(value)
            }
        ];

        SpeedYAxes =
        [
            new Axis
            {
                Name = "MB/s",
                MinLimit = 0,
                MaxLimit = DisplayMaxSpeed,
                LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(100, 116, 139, 45)),
                TextSize = 10,
                Labeler = value => $"{value:F0}"
            }
        ];
        
        TestProfiles = new ObservableCollection<TestProfileItem>
        {
            new() { Name = "Rychlý test (100 MB)", Description = "Rychlé ověření bez zápisu", IsSelected = true },
            new() { Name = "Plný test (1 GB)", Description = "Kompletní zápis a ověření" },
            new() { Name = "Test celého disku", Description = "Zápis a ověření celého disku" },
            new() { Name = "⚡ SANITIZACE DISKU", Description = "DESTRUKTIVNÍ! Přepíše disk nulama", IsDestructive = true }
        };
        
        ZoomWindowPresetsGb = new ObservableCollection<double> { 1, 5, 10, 20, 50, 100 };
        ZoomWindowPresetsPercent = new ObservableCollection<double> { 1, 2, 5, 10, 20, 50 };
    }

    public ObservableCollection<CoreDriveInfo> AvailableDrives { get; }
    public ObservableCollection<TestProfileItem> TestProfiles { get; }
    
    // Graph data collections (legacy)
    public ObservableCollection<SurfaceTestDataPoint> WriteSpeedHistory { get; }
    public ObservableCollection<SurfaceTestDataPoint> ReadSpeedHistory { get; }
    public ObservableCollection<TemperatureDataPoint> TemperatureHistory { get; }
    public ObservableCollection<GraphZoomLevel> ZoomLevels { get; }
    public ObservableCollection<ObservablePoint> WriteSeriesValues { get; }
    public ObservableCollection<ObservablePoint> ReadSeriesValues { get; }
    public ISeries[] SpeedSeries { get; }
    public Axis[] SpeedXAxes { get; }
    public Axis[] SpeedYAxes { get; }
    public int ChartPointCount => WriteSeriesValues.Count + ReadSeriesValues.Count;
    
    // Filtered collections for zoom display
    public IEnumerable<SurfaceTestDataPoint> VisibleWriteData => GetVisibleData(WriteSpeedHistory);
    public IEnumerable<SurfaceTestDataPoint> VisibleReadData => GetVisibleData(ReadSpeedHistory);
    public IEnumerable<TemperatureDataPoint> VisibleTemperatureData => GetVisibleTemperatureData();
    
    private IEnumerable<SurfaceTestDataPoint> GetVisibleData(ObservableCollection<SurfaceTestDataPoint> source)
    {
        if (source.Count == 0) return source;
        
        var zoomDuration = SelectedZoomDuration;
        if (zoomDuration == TimeSpan.MaxValue) return source;
        
        var elapsed = DateTime.UtcNow - _testStartTime;
        var cutoff = elapsed - zoomDuration;
        return source.Where(p => p.Elapsed >= cutoff);
    }
    
    private IEnumerable<TemperatureDataPoint> GetVisibleTemperatureData()
    {
        if (TemperatureHistory.Count == 0) return TemperatureHistory;
        
        var zoomDuration = SelectedZoomDuration;
        if (zoomDuration == TimeSpan.MaxValue) return TemperatureHistory;
        
        var elapsed = DateTime.UtcNow - _testStartTime;
        var cutoff = elapsed - zoomDuration;
        return TemperatureHistory.Where(p => p.Elapsed >= cutoff);
    }
    
    // Legacy collection for compatibility
    public ObservableCollection<SpeedDataPoint> SpeedHistory { get; }

    public double WriteProgress { get => _writeProgress; set => SetProperty(ref _writeProgress, value); }
    public double VerifyProgress { get => _verifyProgress; set => SetProperty(ref _verifyProgress, value); }
    
    // Combined statistics (current total speed)
    public double CurrentSpeed { get => _currentSpeed; set => SetProperty(ref _currentSpeed, value); }
    public double MinSpeed => _minSpeed == double.MaxValue ? 0 : _minSpeed;
    public double MaxSpeed { get => _maxSpeed; set => SetProperty(ref _maxSpeed, value); }
    public double AvgSpeed { get => _avgSpeed; set => SetProperty(ref _avgSpeed, value); }
    
    // Write phase statistics
    public double WriteCurrentSpeed 
    { 
        get => _writeCurrentSpeed; 
        set => SetProperty(ref _writeCurrentSpeed, value); 
    }
    public double WriteMinSpeed => _writeMinSpeed == double.MaxValue ? 0 : _writeMinSpeed;
    public double WriteMaxSpeed 
    { 
        get => _writeMaxSpeed; 
        set => SetProperty(ref _writeMaxSpeed, value); 
    }
    public double WriteAvgSpeed 
    { 
        get => _writeAvgSpeed; 
        set => SetProperty(ref _writeAvgSpeed, value); 
    }
    
    // Read phase statistics
    public double ReadCurrentSpeed 
    { 
        get => _readCurrentSpeed; 
        set => SetProperty(ref _readCurrentSpeed, value); 
    }
    public double ReadMinSpeed => _readMinSpeed == double.MaxValue ? 0 : _readMinSpeed;
    public double ReadMaxSpeed 
    { 
        get => _readMaxSpeed; 
        set => SetProperty(ref _readMaxSpeed, value); 
    }
    public double ReadAvgSpeed 
    { 
        get => _readAvgSpeed; 
        set => SetProperty(ref _readAvgSpeed, value); 
    }
    
    // Y positions for reference lines in graph (in pixels from top)
    // Graph height is 160px for speed, bars are aligned to bottom
    public double MaxSpeedLineY => Math.Max(0, 160 - (MaxSpeed / Math.Max(DisplayMaxSpeed, 1)) * 160);
    public double MinSpeedLineY => Math.Max(0, 160 - (MinSpeed / Math.Max(DisplayMaxSpeed, 1)) * 160);
    public double AvgSpeedLineY => Math.Max(0, 160 - (AvgSpeed / Math.Max(DisplayMaxSpeed, 1)) * 160);
    public int CurrentTemperature { get => _currentTemperature; set => SetProperty(ref _currentTemperature, value); }
    public int MinTemperature => _minTemperature == int.MaxValue ? 0 : _minTemperature;
    public int MaxTemperature => _maxTemperature;
    public double DisplayMaxSpeed { get; private set; } = 50; // Dynamic: max measured speed +10%
    public double DisplayMaxTemperature { get; private set; } = 80; // Fixed at 80°C for consistent graph scale
    public int ErrorCount { get => _errorCount; set => SetProperty(ref _errorCount, value); }
    public string TimeRemaining { get => _timeRemaining; set => SetProperty(ref _timeRemaining, value); }
    
    // Zoom properties for graph
    public int SelectedZoomIndex
    {
        get => _selectedZoomIndex;
        set
        {
            if (SetProperty(ref _selectedZoomIndex, value))
            {
                OnPropertyChanged(nameof(SelectedZoomDuration));
                UpdateXAxisLimits(100);
            }
        }
    }

    public bool IsDataWindowZoomEnabled
    {
        get => _isDataWindowZoomEnabled;
        set
        {
            if (SetProperty(ref _isDataWindowZoomEnabled, value))
            {
                UpdateXAxisLimits(100);
            }
        }
    }

    public ObservableCollection<double> ZoomWindowPresetsGb { get; }
    public ObservableCollection<double> ZoomWindowPresetsPercent { get; }

    public int ZoomWindowModeIndex
    {
        get => _zoomWindowModeIndex;
        set
        {
            if (SetProperty(ref _zoomWindowModeIndex, value))
            {
                OnPropertyChanged(nameof(IsZoomByGb));
                OnPropertyChanged(nameof(IsZoomByPercent));
                UpdateXAxisLimits(100);
            }
        }
    }

    public bool IsZoomByGb => ZoomWindowModeIndex == 0;
    public bool IsZoomByPercent => ZoomWindowModeIndex == 1;

    public double SelectedZoomPresetGb
    {
        get => _selectedZoomPresetGb;
        set
        {
            if (SetProperty(ref _selectedZoomPresetGb, value) && value > 0)
            {
                ZoomWindowGb = value;
            }
        }
    }

    public double SelectedZoomPresetPercent
    {
        get => _selectedZoomPresetPercent;
        set
        {
            if (SetProperty(ref _selectedZoomPresetPercent, value) && value > 0)
            {
                ZoomWindowPercent = value;
            }
        }
    }

    public double ZoomWindowGb
    {
        get => _zoomWindowGb;
        set
        {
            var normalized = Math.Max(0.1, value);
            if (SetProperty(ref _zoomWindowGb, normalized))
            {
                UpdateXAxisLimits(100);
            }
        }
    }

    public double ZoomWindowPercent
    {
        get => _zoomWindowPercent;
        set
        {
            var normalized = Math.Clamp(value, 0.5, 100);
            if (SetProperty(ref _zoomWindowPercent, normalized))
            {
                UpdateXAxisLimits(100);
            }
        }
    }

    public double CurrentDataPercent
    {
        get => _currentDataPercent;
        private set => SetProperty(ref _currentDataPercent, Math.Clamp(value, 0, 100));
    }

    public TimeSpan SelectedZoomDuration => ZoomLevels[_selectedZoomIndex].Duration;
    
    public int CurrentPhase
    {
        get => _currentPhase;
        set
        {
            if (_currentPhase == value)
            {
                return;
            }

            FlushPhaseBucket(_currentPhase);
            if (SetProperty(ref _currentPhase, value))
            {
                _phaseStartedAtUtc = DateTime.UtcNow;
                CurrentDataPercent = 0;
                UpdateXAxisLimits(0);
            }
        }
    }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public bool IsLoadingDrives
    {
        get => _isLoadingDrives;
        set
        {
            if (SetProperty(ref _isLoadingDrives, value))
            {
                OnPropertyChanged(nameof(CanStartTest));
                OnPropertyChanged(nameof(CanChangeDisk));
            }
        }
    }

    public bool IsTesting
    {
        get => _isTesting;
        set
        {
            if (SetProperty(ref _isTesting, value))
            {
                OnPropertyChanged(nameof(CanStartTest));
                OnPropertyChanged(nameof(CanChangeDisk));
            }
        }
    }

    public bool CanStartTest => !IsTesting && SelectedDrive != null && !IsLocked && !IsLoadingDrives;
    public bool CanChangeDisk => !IsTesting && !IsLoadingDrives;

    public CoreDriveInfo? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (SetProperty(ref _selectedDrive, value))
            {
                // Update IsLocked when disk selection changes
                if (value != null)
                {
                    _ = UpdateLockStatusAsync(value);
                }
                OnPropertyChanged(nameof(CanStartTest));
                OnPropertyChanged(nameof(SpeedXAxes));
            }
        }
    }

    private async Task UpdateLockStatusAsync(CoreDriveInfo drive)
    {
        try
        {
            var lockedDisks = await _settingsService.GetLockedDisksAsync();
            var isLocked = lockedDisks.Any(p => IsSameDisk(p, drive.Path)) || drive.IsSystemDisk;
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLocked = isLocked;
                _selectedDiskService.SelectedDisk = drive;
                _selectedDiskService.SelectedDiskDisplayName = drive.Name;
                _selectedDiskService.IsSelectedDiskLocked = isLocked;
                
                if (!IsLoadingDrives)
                {
                    StatusMessage = isLocked 
                        ? $"Vybrán disk: {drive.Name ?? drive.Path} ( zamknut)"
                        : $"Vybrán disk: {drive.Name ?? drive.Path}";
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Chyba při kontrole stavu disku: {ex.Message}";
            });
        }
    }

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (SetProperty(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(CanStartTest));
                OnPropertyChanged(nameof(LockWarningText));
            }
        }
    }

    public string LockWarningText => IsLocked ? "⚠ Disk je zamknut" : "";

    public void OnNavigatedTo()
    {
        if (AvailableDrives.Count == 0)
        {
            _ = LoadDrivesAsync();
        }
        else if (SelectedDrive == null && AvailableDrives.Count > 0)
        {
            StatusMessage = $"Vybrán disk: {AvailableDrives[0].Name ?? AvailableDrives[0].Path}";
        }
    }

    private async Task LoadDrivesAsync()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoadingDrives = true;
                StatusMessage = "Načítám disky...";
                AvailableDrives.Clear();
            });
            
            var drives = await _diskCacheService.GetDrivesAsync().ConfigureAwait(false);
            var lockedDisks = await _settingsService.GetLockedDisksAsync().ConfigureAwait(false);
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    foreach (var d in drives)
                        AvailableDrives.Add(d);

                    if (drives.Count == 0)
                    {
                        StatusMessage = "Nebyly nalezeny žádné disky";
                        IsLoadingDrives = false;
                        return;
                    }

                    // Prefer previously selected disk
                    if (_selectedDiskService.SelectedDisk != null)
                    {
                        var existing = drives.FirstOrDefault(d => d.Path == _selectedDiskService.SelectedDisk!.Path);
                        if (existing != null)
                        {
                            SelectedDrive = existing;
                            IsLocked = _selectedDiskService.IsSelectedDiskLocked;
                            StatusMessage = $"Vybrán disk: {SelectedDrive.Name ?? SelectedDrive.Path}";
                            IsLoadingDrives = false;
                            return;
                        }
                    }

                    // Select first non-system disk
                    var firstDisk = drives.FirstOrDefault(d => !d.IsSystemDisk) ?? drives[0];
                    SelectedDrive = firstDisk;
                    IsLocked = lockedDisks.Any(p => IsSameDisk(p, firstDisk.Path)) || firstDisk.IsSystemDisk;
                    
                    _selectedDiskService.SelectedDisk = firstDisk;
                    _selectedDiskService.SelectedDiskDisplayName = firstDisk.Name;
                    _selectedDiskService.IsSelectedDiskLocked = IsLocked;
                    
                    StatusMessage = $"Vybrán disk: {SelectedDrive?.Name ?? SelectedDrive?.Path}";
                }
                finally
                {
                    IsLoadingDrives = false;
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => 
            {
                StatusMessage = $"Chyba: {ex.Message}";
                IsLoadingDrives = false;
            });
        }
    }

    private static bool IsSameDisk(string id1, string id2)
    {
        if (string.IsNullOrEmpty(id1) || string.IsNullOrEmpty(id2)) return false;
        if (string.Equals(id1, id2, StringComparison.OrdinalIgnoreCase)) return true;
        
        var num1 = ExtractDriveNumber(id1);
        var num2 = ExtractDriveNumber(id2);
        return num1.HasValue && num2.HasValue && num1.Value == num2.Value;
    }

    private static int? ExtractDriveNumber(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var digits = new string(path.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var num) ? num : null;
    }

    private void AddSpeedPoint(double speed, double dataPercent)
    {
        var phase = _currentPhase;

        if (Dispatcher.UIThread.CheckAccess())
        {
            AddSpeedPointCore(speed, dataPercent, phase);
            return;
        }

        Dispatcher.UIThread.Post(() => AddSpeedPointCore(speed, dataPercent, phase));
    }

    private void AddSpeedPointCore(double speed, double dataPercent, int phase)
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _testStartTime;
        var xPosition = Math.Clamp(dataPercent, 0d, 100d);
        CurrentDataPercent = xPosition;

        // Add to legacy collection for compatibility
        SpeedHistory.Add(new SpeedDataPoint { Time = SpeedHistory.Count, Speed = speed });
        while (SpeedHistory.Count > 300)
            SpeedHistory.RemoveAt(0);

        // Add to phase-specific collection with dataPercent
        var dataPoint = new SurfaceTestDataPoint(now, elapsed, speed, CurrentTemperature, phase, xPosition);

        if (phase == 0)
        {
            AppendCapped(WriteSpeedHistory, dataPoint);
            _writePhaseMaxElapsedSeconds = Math.Max(_writePhaseMaxElapsedSeconds, xPosition);
            AppendDownsampledPoint(
                WriteSeriesValues,
                xPosition,
                speed,
                ref _writeBucketStart,
                ref _writeBucketSum,
                ref _writeBucketCount);
            TriggerSamplePulse(isWrite: true);
        }
        else
        {
            AppendCapped(ReadSpeedHistory, dataPoint);
            _readPhaseMaxElapsedSeconds = Math.Max(_readPhaseMaxElapsedSeconds, xPosition);
            AppendDownsampledPoint(
                ReadSeriesValues,
                xPosition,
                speed,
                ref _readBucketStart,
                ref _readBucketSum,
                ref _readBucketCount);
            TriggerSamplePulse(isWrite: false);
        }

        // Add temperature point
        if (CurrentTemperature > 0)
        {
            AppendCapped(TemperatureHistory, new TemperatureDataPoint(now, elapsed, CurrentTemperature));
        }

        UpdateStatisticsIncremental(speed);
        UpdateXAxisLimits(100);
        OnPropertyChanged(nameof(ChartPointCount));
    }

    private async Task<(SanitizationResult? result, string? successMessage, string? errorContext)> RunSanitizationAsync(CancellationToken cancellationToken)
    {
        if (SelectedDrive == null) return (null, null, null);

        var progress = new CallbackProgress<SanitizationProgress>(p =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            void ApplyProgress()
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Detect phase from status message
                var isReadPhase = p.Phase.Contains("Čtení") || p.Phase.Contains("ověření") || p.Phase.Contains("Read");
                var isWritePhase = p.Phase.Contains("Zápis") || p.Phase.Contains("Write");

                if ((isReadPhase || isWritePhase) && p.TotalBytes > 0)
                {
                    _currentPhaseTotalBytes = p.TotalBytes;
                    var phaseLabel = isReadPhase ? "Čtení/Ověření" : "Zápis";
                    var processedText = FormatDataSize(p.BytesProcessed);
                    var totalText = FormatDataSize(p.TotalBytes);
                    StatusMessage = $"{phaseLabel}: {p.ProgressPercent:F1}% ({processedText} / {totalText})";
                }
                else
                {
                    StatusMessage = p.Phase;
                }

                // Set phase BEFORE adding speed points
                if (isReadPhase && _currentPhase != 1)
                {
                    FlushPhaseBucket(_currentPhase);
                    CurrentPhase = 1;
                }
                else if (isWritePhase && _currentPhase != 0)
                {
                    FlushPhaseBucket(_currentPhase);
                    CurrentPhase = 0;
                }

                // Plot speed only for write/read phases to avoid return line (e.g. GPT/formatování fáze s 0%)
                if (isWritePhase || isReadPhase)
                {
                    // Draw both phases over the same 0-100% range
                    var phaseProgress = Math.Clamp(p.ProgressPercent, 0, 100);

                    if (isWritePhase)
                    {
                        WriteProgress = p.ProgressPercent;
                        VerifyProgress = 0;
                    }
                    else
                    {
                        WriteProgress = 100;
                        VerifyProgress = p.ProgressPercent;
                    }

                    CurrentSpeed = p.CurrentSpeedMBps;
                    ErrorCount = p.Errors;
                    AddSpeedPoint(p.CurrentSpeedMBps, phaseProgress);
                    if (p.EstimatedTimeRemaining.HasValue)
                        TimeRemaining = p.EstimatedTimeRemaining.Value.ToString(@"hh\:mm\:ss");
                }
                else
                {
                    ErrorCount = p.Errors;
                }
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyProgress();
                return;
            }

            Dispatcher.UIThread.Post(ApplyProgress);
        });

        // Note: Sanitization cannot be safely cancelled once started
        // We check cancellation before starting but the operation itself runs to completion
        cancellationToken.ThrowIfCancellationRequested();
        
        var result = await _sanitizationService.SanitizeDiskAsync(
            SelectedDrive.Path, SelectedDrive.TotalSize, true, true, "SCCM", progress, cancellationToken);

        // Check if user cancelled during operation
        if (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Sanitizace zrušena (disk může být částečně přepsán)";
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (result.Success)
        {
            // Calculate test duration
            var duration = result.Duration;
            
            StatusMessage = $"Dokončeno! Zápis: {result.WriteSpeedMBps:F1} MB/s, Čtení: {result.ReadSpeedMBps:F1} MB/s";

            var usbWarning = GetUsbBottleneckWarning(result);
            
            // Save to disk card
            string? errorContext = null;
            try
            {
                var card = await _cardTestService.GetOrCreateCardAsync(SelectedDrive!, cancellationToken);

                // Collect write samples with proper timestamps and progress
                var writeSamples = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var samples = new List<SpeedSample>();
                    foreach (var point in WriteSpeedHistory)
                    {
                        samples.Add(new SpeedSample
                        {
                            Timestamp = point.Timestamp,
                            SpeedMBps = point.Speed,
                            ProgressPercent = point.DataPercent,
                            BytesProcessed = (long)(point.DataPercent / 100.0 * SelectedDrive!.TotalSize)
                        });
                    }
                    return samples;
                });

                // Collect read samples with proper timestamps and progress
                var readSamples = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var samples = new List<SpeedSample>();
                    foreach (var point in ReadSpeedHistory)
                    {
                        samples.Add(new SpeedSample
                        {
                            Timestamp = point.Timestamp,
                            SpeedMBps = point.Speed,
                            ProgressPercent = point.DataPercent,
                            BytesProcessed = (long)(point.DataPercent / 100.0 * SelectedDrive!.TotalSize)
                        });
                    }
                    return samples;
                });

                await _cardTestService.SaveSanitizationAsync(card, result, writeSamples, readSamples, cancellationToken);
                
                StatusMessage = $"Sanitizace uložena - {card.ModelName}";
            }
            catch (InvalidOperationException ex)
            {
                errorContext = ex.Message;
                StatusMessage = $"Sanitizace dokončena, ale uložení karty selhalo: {ex.Message}";
            }
            catch (DbUpdateException ex)
            {
                var message = ex.InnerException?.Message ?? ex.Message;
                errorContext = message;
                StatusMessage = $"Sanitizace dokončena, ale uložení karty selhalo: {message}";
            }
            catch (Exception ex)
            {
                errorContext = ex.Message;
                StatusMessage = $"Sanitizace dokončena, ale uložení karty selhalo: {ex.Message}";
            }
            
            // Build success message but don't show dialog yet
            var successMessage = 
                $"Disk byl úspěšně sanitizován a uložen.\n\n" +
                $"📊 Výsledky sanitizace:\n" +
                $"━━━━━━━━━━━━━━━━━━━━\n" +
                $"💿 Disk: {SelectedDrive?.Name ?? "Unknown"}\n" +
                $"⏱ Doba: {duration:hh\\:mm\\:ss}\n" +
                $"💾 Zpracováno: {result.BytesWritten / (1024.0 * 1024 * 1024):F2} GB\n\n" +
                $"📝 ZÁPIS:\n" +
                $"   Rychlost: {result.WriteSpeedMBps:F1} MB/s\n\n" +
                $"📖 ČTENÍ/OVĚŘENÍ:\n" +
                $"   Rychlost: {result.ReadSpeedMBps:F1} MB/s\n\n" +
                $"✅ Stav: {(result.ErrorsDetected == 0 ? "Bez chyb" : $"{result.ErrorsDetected} chyb")}" +
                (string.IsNullOrWhiteSpace(usbWarning) ? string.Empty : $"\n\n⚠ {usbWarning}") +
                $"\n📁 Karta disku vytvořena/aktualizována";
                
            return (result, successMessage, errorContext);
        }
        else
        {
            StatusMessage = $"Chyba: {result.ErrorMessage}";
            return (result, null, null);
        }
    }

    private async Task RunTestAsync(CancellationToken cancellationToken)
    {
        StatusMessage = "Spouštím test povrchu...";
        
        var profile = TestProfiles.FirstOrDefault(p => p.IsSelected);
        var testDurationMs = profile?.Name switch
        {
            "Rychlý test (100 MB)" => 30000,
            "Plný test (1 GB)" => 60000,
            "Test celého disku" => 120000,
            _ => 30000
        };
        
        // Track test data for saving
        var testStartTime = DateTime.UtcNow;
        var writeSamples = new List<(double Speed, int Temp, DateTime Time)>();
        var readSamples = new List<(double Speed, int Temp, DateTime Time)>();
        var minWriteSpeed = double.MaxValue;
        var maxWriteSpeed = 0.0;
        var minReadSpeed = double.MaxValue;
        var maxReadSpeed = 0.0;
        
        CurrentPhase = 0;
        var writePhaseDuration = testDurationMs / 2;
        var readPhaseDuration = testDurationMs / 2;
        var syntheticTotalBytes = profile?.Name switch
        {
            "Rychlý test (100 MB)" => 100L * 1024 * 1024,
            "Plný test (1 GB)" => 1L * 1024 * 1024 * 1024,
            "Test celého disku" => Math.Max(SelectedDrive?.TotalSize ?? 0, 1L),
            _ => 100L * 1024 * 1024
        };
        _currentPhaseTotalBytes = syntheticTotalBytes;

        const int sampleIntervalMs = 20;
        
        // Write phase (0-50%)
        StatusMessage = "Zápis dat...";
        for (int i = 0; i <= writePhaseDuration; i += sampleIntervalMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var progress = (double)i / writePhaseDuration * 100; // 0-100% for write phase
            WriteProgress = progress * 0.5; // 0-50% for overall progress bar
            VerifyProgress = 0;
            
            var speed = 45 + Random.Shared.NextDouble() * 15;
            CurrentSpeed = speed;
            CurrentTemperature = 35 + Random.Shared.Next(0, 5);
            AddSpeedPoint(speed, progress);
            
            writeSamples.Add((speed, CurrentTemperature, DateTime.UtcNow));
            if (speed < minWriteSpeed) minWriteSpeed = speed;
            if (speed > maxWriteSpeed) maxWriteSpeed = speed;
            
            var remainingMs = testDurationMs - i - writePhaseDuration;
            TimeRemaining = TimeSpan.FromMilliseconds(Math.Max(0, remainingMs)).ToString(@"mm\:ss");
            
            await Task.Delay(sampleIntervalMs, cancellationToken);
        }
        
        // Read phase (50-100%)
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            FlushPhaseBucket(0);
            CurrentPhase = 1;
        });
        _currentPhaseTotalBytes = syntheticTotalBytes;
        StatusMessage = "Čtení a ověřování...";
        for (int i = 0; i <= readPhaseDuration; i += sampleIntervalMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var progress = (double)i / readPhaseDuration * 100; // 0-100% for read phase (same X axis as write)
            WriteProgress = 100;
            VerifyProgress = progress;
            
            var speed = 50 + Random.Shared.NextDouble() * 20;
            CurrentSpeed = speed;
            CurrentTemperature = 35 + Random.Shared.Next(0, 5);
            AddSpeedPoint(speed, progress);
            
            readSamples.Add((speed, CurrentTemperature, DateTime.UtcNow));
            if (speed < minReadSpeed) minReadSpeed = speed;
            if (speed > maxReadSpeed) maxReadSpeed = speed;
            
            var remaining = readPhaseDuration - i;
            TimeRemaining = TimeSpan.FromMilliseconds(remaining).ToString(@"mm\:ss");
            
            await Task.Delay(sampleIntervalMs, cancellationToken);
        }
        
        var testEndTime = DateTime.UtcNow;
        var duration = testEndTime - testStartTime;
        
        // Calculate averages
        var avgWriteSpeed = writeSamples.Count > 0 ? writeSamples.Average(s => s.Speed) : 0;
        var avgReadSpeed = readSamples.Count > 0 ? readSamples.Average(s => s.Speed) : 0;
        if (minWriteSpeed == double.MaxValue) minWriteSpeed = 0;
        if (minReadSpeed == double.MaxValue) minReadSpeed = 0;
        
        StatusMessage = "Ukládám výsledky testu...";
        
        // Create and save test result
        try
        {
            // Verify drive is selected
            if (SelectedDrive == null)
            {
                throw new InvalidOperationException("Žádný disk není vybrán");
            }
            
            // Determine operation type based on profile
            var operation = profile?.Name switch
            {
                "Rychlý test (100 MB)" => SurfaceTestOperation.ReadOnly,  // Read-only, no write
                "Plný test (1 GB)" => SurfaceTestOperation.WritePattern,   // Write and verify
                "Test celého disku" => SurfaceTestOperation.WritePattern,   // Full disk write and verify
                _ => SurfaceTestOperation.ReadOnly
            };
            
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Saving test result for drive: {SelectedDrive.Name ?? "Unknown"}");
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Serial: {SelectedDrive.SerialNumber ?? "N/A"}");
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Operation: {operation}");
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Samples: {writeSamples.Count} write + {readSamples.Count} read");
            
            var result = new SurfaceTestResult
            {
                TestId = Guid.NewGuid().ToString(),
                StartedAtUtc = testStartTime,
                CompletedAtUtc = testEndTime,
                DriveModel = SelectedDrive.Name ?? "Unknown",
                DriveSerialNumber = SelectedDrive.SerialNumber ?? "",
                DriveInterface = SelectedDrive.BusType.ToString() ?? "Unknown",
                DriveTotalBytes = SelectedDrive.TotalSize,
                Operation = operation,
                TotalBytesTested = (long)(100 * 1024 * 1024),
                ErrorCount = 0,
                AverageSpeedMbps = (avgWriteSpeed + avgReadSpeed) / 2,
                PeakSpeedMbps = Math.Max(maxWriteSpeed, maxReadSpeed),
                MinSpeedMbps = Math.Min(minWriteSpeed, minReadSpeed)
            };
            
            // Add samples (speed is already in MB/s)
            foreach (var sample in writeSamples)
            {
                result.Samples.Add(new SurfaceTestSample
                {
                    OffsetBytes = 0,
                    BlockSizeBytes = 1024 * 1024,
                    ThroughputMbps = sample.Speed,  // Already in MB/s
                    TimestampUtc = sample.Time
                });
            }
            
            foreach (var sample in readSamples)
            {
                result.Samples.Add(new SurfaceTestSample
                {
                    OffsetBytes = 0,
                    BlockSizeBytes = 1024 * 1024,
                    ThroughputMbps = sample.Speed,  // Already in MB/s
                    TimestampUtc = sample.Time
                });
            }
            
            // Save to disk card
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Getting or creating disk card...");
            var card = await _cardTestService.GetOrCreateCardAsync(SelectedDrive!, cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Card created/found: ID={card.Id}, Model={card.ModelName}");
            
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Saving surface test result...");
            var testSession = await _cardTestService.SaveSurfaceTestAsync(card, result, cancellationToken: cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Test result saved successfully");

            await TrySendCompletionEmailAsync(result, card, testSession, cancellationToken);
            
            // Calculate overall stats for display
            var overallMaxSpeed = Math.Max(maxWriteSpeed, maxReadSpeed);
            var overallMinSpeed = Math.Min(minWriteSpeed, minReadSpeed);
            var overallAvgSpeed = (avgWriteSpeed + avgReadSpeed) / 2;
            
            StatusMessage = $"Test dokončen - Průměr: {overallAvgSpeed:F1} MB/s";
            
            await _dialogService.ShowSuccessAsync("Test dokončen",
                $"Test povrchu byl úspěšně dokončen a uložen.\n\n" +
                $"📊 Výsledky testu:\n" +
                $"━━━━━━━━━━━━━━━━━━━━\n" +
                $"⏱ Doba testu: {duration:mm\\:ss}\n\n" +
                $"📝 ZÁPIS:\n" +
                $"   Min: {minWriteSpeed:F1} MB/s\n" +
                $"   Max: {maxWriteSpeed:F1} MB/s\n" +
                $"   Průměr: {avgWriteSpeed:F1} MB/s\n\n" +
                $"📖 ČTENÍ:\n" +
                $"   Min: {minReadSpeed:F1} MB/s\n" +
                $"   Max: {maxReadSpeed:F1} MB/s\n" +
                $"   Průměr: {avgReadSpeed:F1} MB/s\n\n" +
                $"💾 Celkem: {overallAvgSpeed:F1} MB/s\n" +
                $"📈 Rozsah: {overallMinSpeed:F1} - {overallMaxSpeed:F1} MB/s");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] ERROR saving test result: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Error message: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Stack trace: {ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Inner exception: {ex.InnerException.Message}");
            }
            
            StatusMessage = $"Chyba při ukládání: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", 
                $"Test byl dokončen, ale výsledky se nepodařilo uložit.\n\n" +
                $"Chyba: {ex.Message}\n\n" +
                $"Typ: {ex.GetType().Name}");
        }
    }

    private async Task TrySendCompletionEmailAsync(
        SurfaceTestResult result,
        DiskCard card,
        TestSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            var recipient = await _settingsService.GetReportRecipientEmailAsync();
            if (string.IsNullOrWhiteSpace(recipient))
            {
                return;
            }

            var certificate = await _certificateGenerator.GenerateCertificateAsync(session, card);
            var certificatePath = await _certificateGenerator.GeneratePdfAsync(certificate);

            if (!File.Exists(certificatePath))
            {
                StatusMessage = "Test dokončen, certifikát nebyl nalezen pro odesílání e-mailem";
                return;
            }

            var certificateBytes = await File.ReadAllBytesAsync(certificatePath, cancellationToken);
            var attachmentFileName = Path.GetFileName(certificatePath);

            await _notificationService.SendTestCompletionWithReportAsync(
                result,
                recipient,
                certificateBytes,
                card.ModelName,
                attachmentFileName,
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            StatusMessage = "Test dokončen. E-mail nebyl odeslán (SMTP není nakonfigurované).";
        }
        catch (IOException)
        {
            StatusMessage = "Test dokončen. E-mail s certifikátem se nepodařilo připravit.";
        }
    }

    private double GetCurrentPhaseElapsedSeconds()
    {
        if (!IsTesting)
        {
            return Math.Max(1, Math.Max(_writePhaseMaxElapsedSeconds, _readPhaseMaxElapsedSeconds));
        }

        return Math.Max(0, (DateTime.UtcNow - _phaseStartedAtUtc).TotalSeconds);
    }

    private static string FormatDataSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        const double mb = 1024d * 1024d;
        const double gb = mb * 1024d;
        const double tb = gb * 1024d;

        if (bytes >= tb)
        {
            return $"{bytes / tb:F2} TB";
        }

        if (bytes >= gb)
        {
            return $"{bytes / gb:F2} GB";
        }

        return $"{bytes / mb:F0} MB";
    }

    private string? GetUsbBottleneckWarning(SanitizationResult result)
    {
        if (SelectedDrive == null)
        {
            return null;
        }

        var isUsb = SelectedDrive.BusType == CoreBusType.Usb ||
                    SelectedDrive.IsRemovable ||
                    SelectedDrive.Interface.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                    SelectedDrive.Path.Contains("USB", StringComparison.OrdinalIgnoreCase);

        if (!isUsb)
        {
            return null;
        }

        var avgSpeed = (result.WriteSpeedMBps + result.ReadSpeedMBps) / 2d;

        if (avgSpeed < 55)
        {
            return $"Detekováno pravděpodobné omezení rychlosti USB 2.0 ({avgSpeed:F1} MB/s). Zkuste USB 3.x port (modrý), kratší kvalitní kabel a přímé připojení bez hubu.";
        }

        if (avgSpeed < 120)
        {
            return $"Detekováno možné omezení přenosu přes USB ({avgSpeed:F1} MB/s). Zkuste jiný port/kabel a přímé připojení bez hubu.";
        }

        return null;
    }

    [RelayCommand]
    private void CancelTest() 
    { 
        _testCancellation?.Cancel();
        StatusMessage = "Test zrušen"; 
    }

    [RelayCommand]
    private void GoBack() => _navigationService.NavigateTo<DiskSelectionViewModel>();

    [RelayCommand]
    private void SelectProfile(TestProfileItem? p)
    {
        if (p == null) return;
        foreach (var x in TestProfiles) x.IsSelected = false;
        p.IsSelected = true;
        StatusMessage = $"Vybrán profil: {p.Name}";
    }

    public void Dispose()
    {
        _testCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }

    private string FormatPercentWithCapacityLabel(double value)
    {
        var percent = Math.Clamp(value, 0d, 100d);
        var totalBytes = SelectedDrive?.TotalSize > 0 ? SelectedDrive.TotalSize : _currentPhaseTotalBytes;

        if (totalBytes <= 0)
        {
            return $"{percent:F0}%";
        }

        var bytesAtPoint = (long)(totalBytes * (percent / 100d));
        const double tbInBytes = GbInBytes * 1024d;

        string capacityText;
        if (bytesAtPoint >= tbInBytes)
        {
            var tbAtPoint = bytesAtPoint / tbInBytes;
            capacityText = $"{tbAtPoint:F2} TB";
        }
        else
        {
            var gbAtPoint = bytesAtPoint / GbInBytes;
            capacityText = gbAtPoint > 100d ? $"{gbAtPoint:F0} GB" : $"{gbAtPoint:F1} GB";
        }

        return $"{percent:F0}% - {capacityText}";
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        private readonly Action<T> _callback = callback ?? throw new ArgumentNullException(nameof(callback));

        public void Report(T value)
        {
            _callback(value);
        }
    }
}
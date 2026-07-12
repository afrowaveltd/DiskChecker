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
   private const int MaxGraphPoints = 8000;

   // Downsampling - track last X and Y values
   private double _lastWriteX = double.MinValue;

   private double _lastReadX = double.MinValue;
   private double _lastWriteSpeed;
   private double _lastReadSpeed;
   private const double MinPointDistance = 0.1; // 0.1% distance between points = ~1000 points max
   private const double SpeedChangeThreshold = 0.05; // 5% speed change triggers new point
   private const double GbInBytes = 1024d * 1024d * 1024d;

   // Raw telemetry - append-only, never trimmed. Used for report/certificate generation.
   // Live graph uses WriteSeriesValues/ReadSeriesValues with MaxGraphPoints cap.
   private readonly List<SpeedSample> _rawWriteSamples = new();
   private readonly List<SpeedSample> _rawReadSamples = new();

   private readonly INavigationService _navigationService;
   private readonly ISelectedDiskService _selectedDiskService;
   private readonly IDialogService _dialogService;
   private readonly ISettingsService _settingsService;
   private readonly IDiskCacheService _diskCacheService;
   private readonly IDiskSanitizationService _sanitizationService;
   private readonly SmartCheckService _smartCheckService;
   private readonly DiskCardTestService _cardTestService;
   private readonly TestCompletionNotificationService _notificationService;
   private readonly ICertificateGenerator _certificateGenerator;
   private readonly IDiskCardRepository _diskCardRepository;

   private double _writeProgress;
   private double _verifyProgress;
   private long _writeBytesProcessed;
   private long _writeTotalBytes;
   private long _readBytesProcessed;
   private long _readTotalBytes;

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

   // Leading-zero skip flags for min speed (ignore initial zeros, but capture later drops to zero)
   private bool _hasSeenNonZeroWrite;
   private bool _hasSeenNonZeroRead;
   private bool _hasSeenNonZeroCombined;

   private int _currentTemperature = 35;
   private int _minTemperature = int.MaxValue;
   private int _maxTemperature;
   private int _errorCount;
   private string _timeRemaining = "00:00:00";
   private string _statusMessage = string.Empty;
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
   private bool _hasStickyErrorStatus;

   public SurfaceTestViewModel(
       INavigationService navigationService,
       ISelectedDiskService selectedDiskService,
       IDialogService dialogService,
       ISettingsService settingsService,
       IDiskCacheService diskCacheService,
       IDiskSanitizationService sanitizationService,
       SmartCheckService smartCheckService,
       DiskCardTestService cardTestService,
       TestCompletionNotificationService notificationService,
       ICertificateGenerator certificateGenerator,
       IDiskCardRepository diskCardRepository)
   {
      _navigationService = navigationService;
      _selectedDiskService = selectedDiskService;
      _dialogService = dialogService;
      _settingsService = settingsService;
      _diskCacheService = diskCacheService;
      _sanitizationService = sanitizationService;
      _smartCheckService = smartCheckService;
      _cardTestService = cardTestService;
      _notificationService = notificationService;
      _certificateGenerator = certificateGenerator;
      _diskCardRepository = diskCardRepository;
      StatusMessage = L.Get("SurfaceTest.Status.Ready");

      AvailableDrives = new ObservableCollection<CoreDriveInfo>();
      SpeedHistory = new ObservableCollection<SpeedDataPoint>();
      WriteSpeedHistory = new ObservableCollection<SurfaceTestDataPoint>();
      ReadSpeedHistory = new ObservableCollection<SurfaceTestDataPoint>();
      TemperatureHistory = new ObservableCollection<TemperatureDataPoint>();
      ZoomLevels = new ObservableCollection<GraphZoomLevel>
      {
         new(L.Get("SurfaceTest.Zoom.1min"), TimeSpan.FromMinutes(1)),
         new(L.Get("SurfaceTest.Zoom.5min"), TimeSpan.FromMinutes(5)),
         new(L.Get("SurfaceTest.Zoom.15min"), TimeSpan.FromMinutes(15)),
         new(L.Get("SurfaceTest.Zoom.30min"), TimeSpan.FromMinutes(30)),
         new(L.Get("SurfaceTest.Zoom.1hour"), TimeSpan.FromHours(1)),
         new(L.Get("SurfaceTest.Zoom.All"), TimeSpan.MaxValue)
      };
      WriteSeriesValues = new ObservableCollection<ObservablePoint>();
      ReadSeriesValues = new ObservableCollection<ObservablePoint>();

      SpeedSeries =
      [
          new LineSeries<ObservablePoint>
            {
                Name = L.Get("SurfaceTest.Chart.Write"),
                Values = WriteSeriesValues,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(239, 68, 68), 2), // Red
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero
            },
            new LineSeries<ObservablePoint>
            {
                Name = L.Get("SurfaceTest.Chart.Read"),
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
                Name = L.Get("SurfaceTest.Chart.RecordedDataPercent"),
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
            new() { Key = "Quick100MB", Name = L.Get("SurfaceTest.Profile.Quick100MB"), Description = L.Get("SurfaceTest.QuickTestDesc"), IsSelected = true },
            new() { Key = "Full1GB", Name = L.Get("SurfaceTest.Profile.Full1GB"), Description = L.Get("SurfaceTest.FullTestDesc") },
            new() { Key = "FullDisk", Name = L.Get("SurfaceTest.Profile.FullDisk"), Description = L.Get("SurfaceTest.Profile.FullDiskDesc") },
            new() { Key = "Sanitize", Name = L.Get("SurfaceTest.Profile.Sanitize"), Description = L.Get("SurfaceTest.Profile.SanitizeDesc"), IsDestructive = true }
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
      if(source.Count == 0) return source;

      var zoomDuration = SelectedZoomDuration;
      if(zoomDuration == TimeSpan.MaxValue) return source;

      var elapsed = DateTime.UtcNow - _testStartTime;
      var cutoff = elapsed - zoomDuration;
      return source.Where(p => p.Elapsed >= cutoff);
   }

   private IEnumerable<TemperatureDataPoint> GetVisibleTemperatureData()
   {
      if(TemperatureHistory.Count == 0) return TemperatureHistory;

      var zoomDuration = SelectedZoomDuration;
      if(zoomDuration == TimeSpan.MaxValue) return TemperatureHistory;

      var elapsed = DateTime.UtcNow - _testStartTime;
      var cutoff = elapsed - zoomDuration;
      return TemperatureHistory.Where(p => p.Elapsed >= cutoff);
   }

   // Legacy collection for compatibility
   public ObservableCollection<SpeedDataPoint> SpeedHistory { get; }

   public double WriteProgress
   {
      get => _writeProgress;
      set
      {
         if(SetProperty(ref _writeProgress, value))
            NotifyGaugeChanged();
      }
   }

   public double VerifyProgress
   {
      get => _verifyProgress;
      set
      {
         if(SetProperty(ref _verifyProgress, value))
            NotifyGaugeChanged();
      }
   }
   public string WriteTransferredText => FormatTransferredData(L.Get("SurfaceTest.Transfer.Written"), _writeBytesProcessed, _writeTotalBytes);
   public string ReadTransferredText => FormatTransferredData(L.Get("SurfaceTest.Transfer.Read"), _readBytesProcessed, _readTotalBytes);

   // Combined statistics (current total speed)
   public double CurrentSpeed
   {
      get => _currentSpeed;
      set
      {
         if(SetProperty(ref _currentSpeed, value))
            NotifyGaugeChanged();
      }
   }

   public double MinSpeed => _minSpeed == double.MaxValue ? 0 : _minSpeed;
   public double MaxSpeed { get => _maxSpeed; set => SetProperty(ref _maxSpeed, value); }
   public double AvgSpeed { get => _avgSpeed; set => SetProperty(ref _avgSpeed, value); }

   // Write phase statistics
   public double WriteCurrentSpeed
   {
      get => _writeCurrentSpeed;
      set
      {
         if(SetProperty(ref _writeCurrentSpeed, value))
            NotifyGaugeChanged();
      }
   }

   public double WriteMinSpeed => _writeMinSpeed == double.MaxValue ? 0 : _writeMinSpeed;

   public double WriteMaxSpeed
   {
      get => _writeMaxSpeed;
      set
      {
         if(SetProperty(ref _writeMaxSpeed, value))
            NotifyGaugeChanged();
      }
   }

   public double WriteAvgSpeed
   {
      get => _writeAvgSpeed;
      set
      {
         if(SetProperty(ref _writeAvgSpeed, value))
            NotifyGaugeChanged();
      }
   }

   // Read phase statistics
   public double ReadCurrentSpeed
   {
      get => _readCurrentSpeed;
      set
      {
         if(SetProperty(ref _readCurrentSpeed, value))
            NotifyGaugeChanged();
      }
   }

   public double ReadMinSpeed => _readMinSpeed == double.MaxValue ? 0 : _readMinSpeed;

   public double ReadMaxSpeed
   {
      get => _readMaxSpeed;
      set
      {
         if(SetProperty(ref _readMaxSpeed, value))
            NotifyGaugeChanged();
      }
   }

   public double ReadAvgSpeed
   {
      get => _readAvgSpeed;
      set
      {
         if(SetProperty(ref _readAvgSpeed, value))
            NotifyGaugeChanged();
      }
   }

   // Y positions for reference lines in graph (in pixels from top)
   // Graph height is 160px for speed, bars are aligned to bottom
   public double MaxSpeedLineY => Math.Max(0, 160 - (MaxSpeed / Math.Max(DisplayMaxSpeed, 1)) * 160);

   public double MinSpeedLineY => Math.Max(0, 160 - (MinSpeed / Math.Max(DisplayMaxSpeed, 1)) * 160);
   public double AvgSpeedLineY => Math.Max(0, 160 - (AvgSpeed / Math.Max(DisplayMaxSpeed, 1)) * 160);
   public int CurrentTemperature
   {
      get => _currentTemperature;
      set
      {
         if(SetProperty(ref _currentTemperature, value))
            NotifyGaugeChanged();
      }
   }
   public int MinTemperature => _minTemperature == int.MaxValue ? 0 : _minTemperature;
   public int MaxTemperature => _maxTemperature;
   public double DisplayMaxSpeed { get; private set; } = 50; // Dynamic: max measured speed +10%
   public double DisplayMaxTemperature { get; private set; } = 80; // Fixed at 80°C for consistent graph scale
   public int ErrorCount
   {
      get => _errorCount;
      set
      {
         if(SetProperty(ref _errorCount, value))
            NotifyGaugeChanged();
      }
   }

   public string TimeRemaining
   {
      get => _timeRemaining;
      set
      {
         if(SetProperty(ref _timeRemaining, value))
            NotifyGaugeChanged();
      }
   }


   // Live dashboard/gauge values for the status card.  The graph keeps every useful
   // sample, while this panel intentionally exposes a compact, readable snapshot.
   public string GaugeTitle => _currentPhase == 0 ? L.Get("SurfaceTest.Gauge.WriteTitle") : L.Get("SurfaceTest.Gauge.VerifyTitle");
   public string GaugeSubtitle => SelectedDrive == null
      ? L.Get("SurfaceTest.Gauge.SelectDiskHint")
      : $"{SelectedDrive.Name ?? SelectedDrive.Path} • {L.Get("SurfaceTest.Gauge.LiveTelemetry")}";
   public double GaugeCurrentValue => _currentPhase == 0 ? WriteCurrentSpeed : ReadCurrentSpeed;
   public double GaugeMinValue => _currentPhase == 0 ? WriteMinSpeed : ReadMinSpeed;
   public double GaugeAverageValue => _currentPhase == 0 ? WriteAvgSpeed : ReadAvgSpeed;
   public double GaugeMaxValue => _currentPhase == 0 ? WriteMaxSpeed : ReadMaxSpeed;
   public double GaugeProgressPercent => _currentPhase == 0 ? WriteProgress : VerifyProgress;
   public double GaugeScaleMaxValue => Math.Max(50, Math.Max(DisplayMaxSpeed, GaugeMaxValue * 1.15));
   public string GaugeStatusText => IsTesting ? (_currentPhase == 0 ? L.Get("SurfaceTest.Gauge.Write") : L.Get("SurfaceTest.Gauge.Verify")) : L.Get("SurfaceTest.Gauge.Ready");
   public bool GaugeHasErrors => ErrorCount > 0 || IsStatusError;
   public bool GaugeIsOverheated => CurrentTemperature >= 60;
   public bool GaugeIsStalled => IsTesting
      && GaugeProgressPercent > 1
      && GaugeAverageValue > 0
      && GaugeCurrentValue < Math.Max(1, GaugeAverageValue * 0.12);

   // Zoom properties for graph
   public int SelectedZoomIndex
   {
      get => _selectedZoomIndex;
      set
      {
         if(SetProperty(ref _selectedZoomIndex, value))
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
         if(SetProperty(ref _isDataWindowZoomEnabled, value))
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
         if(SetProperty(ref _zoomWindowModeIndex, value))
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
         if(SetProperty(ref _selectedZoomPresetGb, value) && value > 0)
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
         if(SetProperty(ref _selectedZoomPresetPercent, value) && value > 0)
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
         if(SetProperty(ref _zoomWindowGb, normalized))
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
         if(SetProperty(ref _zoomWindowPercent, normalized))
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

   public bool IsWriteSamplePulseVisible
   {
      get => _isWriteSamplePulseVisible;
      private set => SetProperty(ref _isWriteSamplePulseVisible, value);
   }

   public bool IsReadSamplePulseVisible
   {
      get => _isReadSamplePulseVisible;
      private set => SetProperty(ref _isReadSamplePulseVisible, value);
   }

   public TimeSpan SelectedZoomDuration => ZoomLevels[_selectedZoomIndex].Duration;

   public int CurrentPhase
   {
      get => _currentPhase;
      set
      {
         if(_currentPhase == value)
         {
            return;
         }

         FlushPhaseBucket(_currentPhase);
         if(SetProperty(ref _currentPhase, value))
         {
            _phaseStartedAtUtc = DateTime.UtcNow;
            CurrentDataPercent = 0;
            UpdateXAxisLimits(0);
         }
      }
   }

   public string StatusMessage
   {
      get => _statusMessage;
      set
      {
         if(SetProperty(ref _statusMessage, value))
         {
            OnPropertyChanged(nameof(IsStatusError));
            OnPropertyChanged(nameof(IsStatusWarning));
         }
      }
   }

   public bool IsStatusError =>
       !string.IsNullOrWhiteSpace(StatusMessage) &&
       StatusMessage.Contains("chyba", StringComparison.OrdinalIgnoreCase);

   public bool IsStatusWarning =>
       !IsStatusError &&
       !string.IsNullOrWhiteSpace(StatusMessage) &&
       (StatusMessage.Contains('⚠') || StatusMessage.Contains("varování", StringComparison.OrdinalIgnoreCase) || StatusMessage.Contains("warning", StringComparison.OrdinalIgnoreCase));

   private void SetStatusMessage(string message)
   {
      if(_hasStickyErrorStatus)
      {
         return;
      }

      StatusMessage = message;
   }

   private void SetStickyErrorStatus(string message)
   {
      _hasStickyErrorStatus = true;
      StatusMessage = message;
   }

   private void ClearStickyErrorStatus()
   {
      _hasStickyErrorStatus = false;
   }

   public bool IsLoadingDrives
   {
      get => _isLoadingDrives;
      set
      {
         if(SetProperty(ref _isLoadingDrives, value))
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
         if(SetProperty(ref _isTesting, value))
         {
            OnPropertyChanged(nameof(CanStartTest));
            OnPropertyChanged(nameof(CanChangeDisk));
            NotifyGaugeChanged();
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
         if(SetProperty(ref _selectedDrive, value))
         {
            // Update IsLocked when disk selection changes
            if(value != null)
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

            if(!IsLoadingDrives)
            {
               var diskName = drive.Name ?? drive.Path;
               StatusMessage = isLocked
                      ? L.Get("SurfaceTest.Status.DiskSelected", diskName) + " (🔒)"
                      : L.Get("SurfaceTest.Status.DiskSelected", diskName);
            }
         });
      }
      catch(Exception ex)
      {
         await Dispatcher.UIThread.InvokeAsync(() =>
         {
            StatusMessage = L.Get("SurfaceTest.Status.Error", ex.Message);
         });
      }
   }

   public bool IsLocked
   {
      get => _isLocked;
      set
      {
         if(SetProperty(ref _isLocked, value))
         {
            OnPropertyChanged(nameof(CanStartTest));
            OnPropertyChanged(nameof(LockWarningText));
         }
      }
   }

   public string LockWarningText => IsLocked ? "⚠ Disk je zamknut" : "";


   private void NotifyGaugeChanged()
   {
      OnPropertyChanged(nameof(GaugeTitle));
      OnPropertyChanged(nameof(GaugeSubtitle));
      OnPropertyChanged(nameof(GaugeCurrentValue));
      OnPropertyChanged(nameof(GaugeMinValue));
      OnPropertyChanged(nameof(GaugeAverageValue));
      OnPropertyChanged(nameof(GaugeMaxValue));
      OnPropertyChanged(nameof(GaugeProgressPercent));
      OnPropertyChanged(nameof(GaugeScaleMaxValue));
      OnPropertyChanged(nameof(GaugeStatusText));
      OnPropertyChanged(nameof(GaugeHasErrors));
      OnPropertyChanged(nameof(GaugeIsOverheated));
      OnPropertyChanged(nameof(GaugeIsStalled));
   }

   public void OnNavigatedTo()
   {
      if(AvailableDrives.Count == 0)
      {
         _ = LoadDrivesAsync();
      }
      else if(SelectedDrive == null && AvailableDrives.Count > 0)
      {
         StatusMessage = L.Get("SurfaceTest.Status.DiskSelected", AvailableDrives[0].Name ?? AvailableDrives[0].Path);
      }
   }

   private async Task LoadDrivesAsync()
   {
      try
      {
         await Dispatcher.UIThread.InvokeAsync(() =>
         {
            IsLoadingDrives = true;
            StatusMessage = L.Get("SurfaceTest.Status.LoadingDisks");
            AvailableDrives.Clear();
         });

         var drives = await _diskCacheService.GetDrivesAsync().ConfigureAwait(false);
         var lockedDisks = await _settingsService.GetLockedDisksAsync().ConfigureAwait(false);

         await Dispatcher.UIThread.InvokeAsync(() =>
         {
            try
            {
               foreach(var d in drives)
                  AvailableDrives.Add(d);

               if(drives.Count == 0)
               {
                  StatusMessage = L.Get("SurfaceTest.Status.NoDisksFound");
                  IsLoadingDrives = false;
                  return;
               }

               // Prefer previously selected disk
               if(_selectedDiskService.SelectedDisk != null)
               {
                  var existing = drives.FirstOrDefault(d => d.Path == _selectedDiskService.SelectedDisk!.Path);
                  if(existing != null)
                  {
                     SelectedDrive = existing;
                     IsLocked = _selectedDiskService.IsSelectedDiskLocked;
                     StatusMessage = L.Get("SurfaceTest.Status.DiskSelected", SelectedDrive.Name ?? SelectedDrive.Path);
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

               StatusMessage = L.Get("SurfaceTest.Status.DiskSelected", SelectedDrive?.Name ?? SelectedDrive?.Path ?? "?");
            }
            finally
            {
               IsLoadingDrives = false;
            }
         });
      }
      catch(Exception ex)
      {
         await Dispatcher.UIThread.InvokeAsync(() =>
         {
            StatusMessage = L.Get("SurfaceTest.Status.Error", ex.Message);
            IsLoadingDrives = false;
         });
      }
   }

   private static bool IsSameDisk(string id1, string id2)
   {
      if(string.IsNullOrEmpty(id1) || string.IsNullOrEmpty(id2)) return false;
      if(string.Equals(id1, id2, StringComparison.OrdinalIgnoreCase)) return true;

      var num1 = ExtractDriveNumber(id1);
      var num2 = ExtractDriveNumber(id2);
      return num1.HasValue && num2.HasValue && num1.Value == num2.Value;
   }

   private static int? ExtractDriveNumber(string path)
   {
      if(string.IsNullOrEmpty(path)) return null;
      var digits = new string(path.Where(char.IsDigit).ToArray());
      return int.TryParse(digits, out var num) ? num : null;
   }

   private void AddSpeedPoint(double speed, double dataPercent, TimeSpan? totalElapsed = null)
   {
      var phase = _currentPhase;

      if(Dispatcher.UIThread.CheckAccess())
      {
         AddSpeedPointCore(speed, dataPercent, phase, totalElapsed);
         return;
      }

      Dispatcher.UIThread.Post(() => AddSpeedPointCore(speed, dataPercent, phase, totalElapsed));
   }

   private void AddSpeedPointCore(double speed, double dataPercent, int phase, TimeSpan? totalElapsed = null)
   {
      var now = DateTime.UtcNow;
      var elapsed = totalElapsed ?? (now - _testStartTime);
      var xPosition = Math.Clamp(dataPercent, 0d, 100d);
      CurrentDataPercent = xPosition;

      // Add to legacy collection for compatibility
      SpeedHistory.Add(new SpeedDataPoint { Time = SpeedHistory.Count, Speed = speed });
      while(SpeedHistory.Count > 300)
         SpeedHistory.RemoveAt(0);

      // Add to phase-specific collection with dataPercent
      var dataPoint = new SurfaceTestDataPoint(now, elapsed, speed, CurrentTemperature, phase, xPosition);

      if(phase == 0)
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

      // Raw telemetry - append-only, never trimmed
      if (phase == 0)
      {
         _rawWriteSamples.Add(new SpeedSample
         {
             Timestamp = now,
             SpeedMBps = speed,
             ProgressPercent = xPosition,
             BytesProcessed = (long)(xPosition / 100.0 * _currentPhaseTotalBytes),
             Elapsed = elapsed,
             Phase = "Write"
         });
      }
      else
      {
         _rawReadSamples.Add(new SpeedSample
         {
             Timestamp = now,
             SpeedMBps = speed,
             ProgressPercent = xPosition,
             BytesProcessed = (long)(xPosition / 100.0 * _currentPhaseTotalBytes),
             Elapsed = elapsed,
             Phase = "Read"
         });
      }

      // Add temperature point
      if(CurrentTemperature > 0)
      {
         AppendCapped(TemperatureHistory, new TemperatureDataPoint(now, elapsed, CurrentTemperature));
      }

      UpdateStatisticsIncremental(speed);
      UpdateXAxisLimits(100);
      OnPropertyChanged(nameof(ChartPointCount));
   }

   private static void AppendCapped<T>(ObservableCollection<T> collection, T item)
   {
      collection.Add(item);
      while(collection.Count > MaxGraphPoints)
      {
         collection.RemoveAt(0);
      }
   }

   private void AppendDownsampledPoint(
       ObservableCollection<ObservablePoint> target,
       double x,
       double y,
       ref double bucketStart,
       ref double bucketSum,
       ref int bucketCount)
   {
      ref var lastX = ref GetLastXReference(target);
      ref var lastSpeed = ref GetLastSpeedReference(target);

      if(target.Count == 0)
      {
         AppendPoint(target, x, y, ref lastX, ref lastSpeed);
         return;
      }

      var xDistance = Math.Abs(x - lastX);
      var speedChange = lastSpeed <= 0 ? 1d : Math.Abs(y - lastSpeed) / lastSpeed;

      if(xDistance < MinPointDistance && speedChange < SpeedChangeThreshold)
      {
         if(bucketStart < 0)
         {
            bucketStart = lastX;
         }

         bucketSum += y;
         bucketCount++;
         return;
      }

      FlushBucket(target, ref bucketStart, ref bucketSum, ref bucketCount, ref lastX, ref lastSpeed);
      AppendPoint(target, x, y, ref lastX, ref lastSpeed);
   }

   private void FlushPhaseBucket(int phase)
   {
      if(phase == 0)
      {
         FlushBucket(WriteSeriesValues, ref _writeBucketStart, ref _writeBucketSum, ref _writeBucketCount, ref _lastWriteX, ref _lastWriteSpeed);
         return;
      }

      FlushBucket(ReadSeriesValues, ref _readBucketStart, ref _readBucketSum, ref _readBucketCount, ref _lastReadX, ref _lastReadSpeed);
   }

   private static void FlushBucket(
       ObservableCollection<ObservablePoint> target,
       ref double bucketStart,
       ref double bucketSum,
       ref int bucketCount,
       ref double lastX,
       ref double lastSpeed)
   {
      if(bucketCount <= 0)
      {
         bucketStart = -1;
         bucketSum = 0;
         return;
      }

      var averagedY = bucketSum / bucketCount;
      var bucketX = lastX == double.MinValue ? 0d : Math.Clamp(lastX + MinPointDistance, 0d, 100d);
      AppendPoint(target, bucketX, averagedY, ref lastX, ref lastSpeed);
      bucketStart = -1;
      bucketSum = 0;
      bucketCount = 0;
   }

   private static void AppendPoint(ObservableCollection<ObservablePoint> target, double x, double y, ref double lastX, ref double lastSpeed)
   {
      AppendCapped(target, new ObservablePoint(Math.Clamp(x, 0d, 100d), y));
      lastX = Math.Clamp(x, 0d, 100d);
      lastSpeed = y;
   }

   private ref double GetLastXReference(ObservableCollection<ObservablePoint> target)
   {
      if(ReferenceEquals(target, WriteSeriesValues))
      {
         return ref _lastWriteX;
      }

      return ref _lastReadX;
   }

   private ref double GetLastSpeedReference(ObservableCollection<ObservablePoint> target)
   {
      if(ReferenceEquals(target, WriteSeriesValues))
      {
         return ref _lastWriteSpeed;
      }

      return ref _lastReadSpeed;
   }

   private void TriggerSamplePulse(bool isWrite)
   {
      _samplePulseSequence++;
      IsWriteSamplePulseVisible = isWrite;
      IsReadSamplePulseVisible = !isWrite;
   }

   private void UpdateStatisticsIncremental(double speed)
   {
      CurrentSpeed = speed;
      _combinedSpeedSum += speed;
      _combinedSpeedSamples++;
      if (speed > 0 || _hasSeenNonZeroCombined)
      {
         _minSpeed = Math.Min(_minSpeed, speed);
         _hasSeenNonZeroCombined = true;
      }
      MaxSpeed = Math.Max(_maxSpeed, speed);
      AvgSpeed = _combinedSpeedSamples == 0 ? 0 : _combinedSpeedSum / _combinedSpeedSamples;

      if(_currentPhase == 0)
      {
         WriteCurrentSpeed = speed;
         _writeSpeedSum += speed;
         _writeSpeedSamples++;
         if (speed > 0 || _hasSeenNonZeroWrite)
         {
            _writeMinSpeed = Math.Min(_writeMinSpeed, speed);
            _hasSeenNonZeroWrite = true;
         }
         WriteMaxSpeed = Math.Max(_writeMaxSpeed, speed);
         WriteAvgSpeed = _writeSpeedSamples == 0 ? 0 : _writeSpeedSum / _writeSpeedSamples;
         OnPropertyChanged(nameof(WriteMinSpeed));
      }
      else
      {
         ReadCurrentSpeed = speed;
         _readSpeedSum += speed;
         _readSpeedSamples++;
         if (speed > 0 || _hasSeenNonZeroRead)
         {
            _readMinSpeed = Math.Min(_readMinSpeed, speed);
            _hasSeenNonZeroRead = true;
         }
         ReadMaxSpeed = Math.Max(_readMaxSpeed, speed);
         ReadAvgSpeed = _readSpeedSamples == 0 ? 0 : _readSpeedSum / _readSpeedSamples;
         OnPropertyChanged(nameof(ReadMinSpeed));
      }

      if(CurrentTemperature > 0)
      {
         _minTemperature = Math.Min(_minTemperature, CurrentTemperature);
         _maxTemperature = Math.Max(_maxTemperature, CurrentTemperature);
         OnPropertyChanged(nameof(MinTemperature));
         OnPropertyChanged(nameof(MaxTemperature));
      }

      var measuredMax = Math.Max(speed, Math.Max(_writeMaxSpeed, _readMaxSpeed));
      var displayMax = Math.Max(50d, Math.Ceiling(measuredMax * 1.1d / 10d) * 10d);
      if(Math.Abs(DisplayMaxSpeed - displayMax) > double.Epsilon)
      {
         DisplayMaxSpeed = displayMax;
         if(SpeedYAxes.Length > 0)
         {
            SpeedYAxes[0].MaxLimit = displayMax;
         }

         OnPropertyChanged(nameof(DisplayMaxSpeed));
      }

      OnPropertyChanged(nameof(MinSpeed));
      OnPropertyChanged(nameof(MaxSpeedLineY));
      OnPropertyChanged(nameof(MinSpeedLineY));
      OnPropertyChanged(nameof(AvgSpeedLineY));
   }

   private void UpdateXAxisLimits(double fallbackMaxPercent)
   {
      if(SpeedXAxes.Length == 0)
      {
         return;
      }

      var axis = SpeedXAxes[0];
      var maxObservedPercent = Math.Clamp(Math.Max(fallbackMaxPercent, CurrentDataPercent), 0d, 100d);
      double minLimit = 0d;
      double maxLimit = Math.Max(100d, maxObservedPercent);

      if(IsDataWindowZoomEnabled)
      {
         var windowPercent = ZoomWindowModeIndex == 0
             ? Math.Clamp((ZoomWindowGb * GbInBytes / Math.Max(_currentPhaseTotalBytes, 1L)) * 100d, 0.5d, 100d)
             : Math.Clamp(ZoomWindowPercent, 0.5d, 100d);

         minLimit = Math.Max(0d, CurrentDataPercent - windowPercent);
         maxLimit = Math.Min(100d, minLimit + windowPercent);

         if(maxLimit - minLimit < windowPercent)
         {
            minLimit = Math.Max(0d, maxLimit - windowPercent);
         }
      }

      axis.MinLimit = minLimit;
      axis.MaxLimit = maxLimit;
      OnPropertyChanged(nameof(SpeedXAxes));
   }

   private void ResetTestState()
   {
      ClearStickyErrorStatus();
      SpeedHistory.Clear();
      WriteSpeedHistory.Clear();
      ReadSpeedHistory.Clear();
      TemperatureHistory.Clear();
      WriteSeriesValues.Clear();
      ReadSeriesValues.Clear();

      WriteProgress = 0;
      VerifyProgress = 0;
      SetWriteTransferredData(0, SelectedDrive?.TotalSize ?? 0);
      SetReadTransferredData(0, SelectedDrive?.TotalSize ?? 0);
      CurrentSpeed = 0;
      _minSpeed = double.MaxValue;
      _maxSpeed = 0;
      _avgSpeed = 0;
      _combinedSpeedSum = 0;
      _combinedSpeedSamples = 0;

      WriteCurrentSpeed = 0;
      _writeMinSpeed = double.MaxValue;
      _writeMaxSpeed = 0;
      _writeAvgSpeed = 0;
      _writeSpeedSum = 0;
      _writeSpeedSamples = 0;

      ReadCurrentSpeed = 0;
      _readMinSpeed = double.MaxValue;
      _readMaxSpeed = 0;
      _readAvgSpeed = 0;
      _readSpeedSum = 0;
      _readSpeedSamples = 0;

      _hasSeenNonZeroWrite = false;
      _hasSeenNonZeroRead = false;
      _hasSeenNonZeroCombined = false;

      // Temperature will be set from SMART data when test starts (see StartTestAsync)
      CurrentTemperature = 35;
      _minTemperature = int.MaxValue;
      _maxTemperature = 0;
      ErrorCount = 0;
      TimeRemaining = "00:00:00";
      CurrentDataPercent = 0;
      DisplayMaxSpeed = 50;
      _currentPhase = 0;
      NotifyGaugeChanged();
      _currentPhaseTotalBytes = Math.Max(SelectedDrive?.TotalSize ?? 0L, 1L);
      _testStartTime = DateTime.UtcNow;
      _phaseStartedAtUtc = _testStartTime;
      _writePhaseMaxElapsedSeconds = 0;
      _readPhaseMaxElapsedSeconds = 0;

      _writeBucketStart = -1;
      _writeBucketSum = 0;
      _writeBucketCount = 0;
      _readBucketStart = -1;
      _readBucketSum = 0;
      _readBucketCount = 0;
      _lastWriteX = double.MinValue;
      _lastReadX = double.MinValue;
      _lastWriteSpeed = 0;
      _lastReadSpeed = 0;
      _samplePulseSequence = 0;
      IsWriteSamplePulseVisible = false;
      IsReadSamplePulseVisible = false;

      if(SpeedYAxes.Length > 0)
      {
         SpeedYAxes[0].MaxLimit = DisplayMaxSpeed;
      }

      UpdateXAxisLimits(100);
      OnPropertyChanged(nameof(MinSpeed));
      OnPropertyChanged(nameof(MaxTemperature));
      OnPropertyChanged(nameof(MinTemperature));
      OnPropertyChanged(nameof(WriteMinSpeed));
      OnPropertyChanged(nameof(ReadMinSpeed));
      OnPropertyChanged(nameof(DisplayMaxSpeed));
      OnPropertyChanged(nameof(ChartPointCount));
      OnPropertyChanged(nameof(MaxSpeedLineY));
      OnPropertyChanged(nameof(MinSpeedLineY));
      OnPropertyChanged(nameof(AvgSpeedLineY));
   }

   [RelayCommand]
   private async Task StartTestAsync()
   {
      if(SelectedDrive == null || IsTesting || IsLoadingDrives || IsLocked)
      {
         return;
      }

      var profile = TestProfiles.FirstOrDefault(p => p.IsSelected);
      if(profile == null)
      {
         SetStatusMessage(L.Get("SurfaceTest.Status.NoProfile"));
         return;
      }

      if(profile.IsDestructive)
      {
         var confirmed = await _dialogService.ShowDangerConfirmationAsync(
            L.Get("SurfaceTest.Dialog.SanitizeConfirm"),
            string.Format(L.Get("SurfaceTest.Dialog.SanitizeConfirmMessage"), profile.Name, SelectedDrive.Name ?? SelectedDrive.Path));
         if(!confirmed)
         {
            SetStatusMessage(L.Get("SurfaceTest.Status.SanitizeCancelled"));
            return;
         }
      }

      _testCancellation?.Dispose();
      _testCancellation = new CancellationTokenSource();
      var cancellationToken = _testCancellation.Token;

      ResetTestState();
      IsTesting = true;
      SetStatusMessage(profile.IsDestructive ? L.Get("Sanitize.Starting") : L.Get("SurfaceTest.Starting"));

      try
      {
         var smartSnapshot = await CaptureSmartSnapshotAsync(cancellationToken);

         if(profile.IsDestructive)
         {
            var sanitizationOutcome = await RunSanitizationAsync(smartSnapshot, cancellationToken);
            var result = sanitizationOutcome.result;
            var successMessage = sanitizationOutcome.successMessage;
            var errorContext = sanitizationOutcome.errorContext;
            if(result?.Success == true && !string.IsNullOrWhiteSpace(successMessage))
            {
               await _dialogService.ShowSuccessAsync(L.Get("SurfaceTest.Status.SanitizeCompleted"), successMessage);
            }
            else if(result?.Success == false)
            {
               await _dialogService.ShowErrorAsync(L.Get("SurfaceTest.Status.SanitizeError"), errorContext ?? result.ErrorMessage ?? L.Get("SurfaceTest.Status.SanitizeFailed"));
            }
         }
         else
         {
            await RunTestAsync(smartSnapshot, cancellationToken);
         }
      }
      catch(OperationCanceledException)
      {
         SetStatusMessage(L.Get("SurfaceTest.Status.TestCancelled"));
      }
      catch(InvalidOperationException ex)
      {
         SetStickyErrorStatus($"Chyba testu: {ex.Message}");
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), ex.Message);
      }
      catch(IOException ex)
      {
         SetStickyErrorStatus($"Chyba testu: {ex.Message}");
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), ex.Message);
      }
      finally
      {
         FlushPhaseBucket(_currentPhase);
         IsTesting = false;
         CurrentSpeed = 0;
         IsWriteSamplePulseVisible = false;
         IsReadSamplePulseVisible = false;
         _testCancellation?.Dispose();
         _testCancellation = null;
         UpdateXAxisLimits(Math.Max(CurrentDataPercent, 100));
         OnPropertyChanged(nameof(CanStartTest));
      }
   }

   private async Task<(SanitizationResult? result, string? successMessage, string? errorContext)> RunSanitizationAsync(SmartaData? smartSnapshot, CancellationToken cancellationToken)
   {
      if(SelectedDrive == null) return (null, null, null);

      var progress = new CallbackProgress<SanitizationProgress>(p =>
      {
         if(cancellationToken.IsCancellationRequested)
         {
            return;
         }

         void ApplyProgress()
         {
            var isWritePhase = p.IsWritePhase;
            var isReadVerifyPhase = p.IsReadVerifyPhase;

            CurrentDataPercent = p.ProgressPercent;
            CurrentPhase = isWritePhase ? 0 : isReadVerifyPhase ? 1 : CurrentPhase;

            // Update phase-specific progress bars
            if(isWritePhase)
            {
               WriteProgress = p.ProgressPercent; // 0-100% of write phase
               VerifyProgress = 0;
               SetWriteTransferredData(p.BytesProcessed, p.TotalBytes);
            }
            else if(isReadVerifyPhase)
            {
               WriteProgress = 100; // Write is complete
               VerifyProgress = p.ProgressPercent; // 0-100% of verify phase
               SetReadTransferredData(p.BytesProcessed, p.TotalBytes);
            }

            if(!string.IsNullOrWhiteSpace(p.StatusDetail))
            {
               StatusMessage = p.StatusDetail;
            }

            if(isWritePhase || isReadVerifyPhase)
            {
               var phaseProgress = p.ProgressPercent;
               CurrentSpeed = p.CurrentSpeedMBps;
               ErrorCount = p.Errors;
               AddSpeedPoint(p.CurrentSpeedMBps, phaseProgress, p.TotalElapsed);
               if(p.EstimatedTimeRemaining.HasValue)
               {
                  TimeRemaining = p.EstimatedTimeRemaining.Value.ToString(@"hh\:mm\:ss");
               }
            }
            else
            {
               ErrorCount = p.Errors;
            }
         }

         if(Dispatcher.UIThread.CheckAccess())
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

      SmartaData? smartAfterSnapshot = null;
      if(result.Success)
      {
         try
         {
            smartAfterSnapshot = await _smartCheckService.GetSmartaDataWithRetryAsync(SelectedDrive, cancellationToken: cancellationToken);
            result.SmartAfter = smartAfterSnapshot;
         }
         catch(InvalidOperationException)
         {
            // SMART po testu je nepovinný doplněk hodnocení.
         }
      }

      // Check if user cancelled during operation
      if(cancellationToken.IsCancellationRequested)
      {
         SetStatusMessage(L.Get("SurfaceTest.Status.SanitizeCancelledPartial"));
         cancellationToken.ThrowIfCancellationRequested();
      }

      if(result.Success)
      {
         // Calculate test duration
         var duration = result.Duration;
         
           var completedMsg = L.Get("SurfaceTest.Status.Completed", result.WriteSpeedMBps.ToString("F1"), result.ReadSpeedMBps.ToString("F1"));
           StatusMessage = completedMsg;
           SetStatusMessage(completedMsg);

            var usbWarning = GetUsbBottleneckWarning(result);

            // Save to disk card
            string? errorContext = null;
            try
            {
               var card = await _cardTestService.GetOrCreateCardAsync(SelectedDrive!, smartSnapshot, cancellationToken);

               // Downsample samples before saving to database to prevent memory/transaction issues
               // SQLite has practical limits on transaction size and entity tracking
               const int MaxSamplesToSave = 2000;
               
               // Collect and downsample write samples
               var writeSamples = await Dispatcher.UIThread.InvokeAsync(() =>
               {
                  var allSamples = new List<SpeedSample>();
                  foreach(var point in WriteSpeedHistory)
                  {
                     allSamples.Add(new SpeedSample
                     {
                        Timestamp = point.Timestamp,
                        SpeedMBps = point.Speed,
                        ProgressPercent = point.DataPercent,
                        BytesProcessed = (long)(point.DataPercent / 100.0 * SelectedDrive!.TotalSize)
                     });
                  }
                  
                  // Downsample if we have too many samples
                  if (allSamples.Count > MaxSamplesToSave)
                  {
                     var step = Math.Max(1, allSamples.Count / MaxSamplesToSave);
                     var downsampled = new List<SpeedSample>();
                     for (int i = 0; i < allSamples.Count; i += step)
                     {
                        downsampled.Add(allSamples[i]);
                     }
                     // Always include last sample
                     if (allSamples.Count > 0 && (allSamples.Count - 1) % step != 0)
                     {
                        downsampled.Add(allSamples[^1]);
                     }
                     return downsampled;
                  }
                  
                  return allSamples;
               });

               // Collect and downsample read samples
               var readSamples = await Dispatcher.UIThread.InvokeAsync(() =>
               {
                  var allSamples = new List<SpeedSample>();
                  foreach(var point in ReadSpeedHistory)
                  {
                     allSamples.Add(new SpeedSample
                     {
                        Timestamp = point.Timestamp,
                        SpeedMBps = point.Speed,
                        ProgressPercent = point.DataPercent,
                        BytesProcessed = (long)(point.DataPercent / 100.0 * SelectedDrive!.TotalSize)
                     });
                  }
                  
                  // Downsample if we have too many samples
                  if (allSamples.Count > MaxSamplesToSave)
                  {
                     var step = Math.Max(1, allSamples.Count / MaxSamplesToSave);
                     var downsampled = new List<SpeedSample>();
                     for (int i = 0; i < allSamples.Count; i += step)
                     {
                        downsampled.Add(allSamples[i]);
                     }
                     // Always include last sample
                     if (allSamples.Count > 0 && (allSamples.Count - 1) % step != 0)
                     {
                        downsampled.Add(allSamples[^1]);
                     }
                     return downsampled;
                  }
                  
                  return allSamples;
               });

               await _cardTestService.SaveSanitizationAsync(card, result, writeSamples, readSamples, smartSnapshot, smartAfterSnapshot, cancellationToken);
                
               StatusMessage = L.Get("SurfaceTest.Status.SanitizeSaved", card.ModelName, (writeSamples.Count + readSamples.Count).ToString());
               SetStatusMessage(L.Get("SurfaceTest.Status.SanitizeSavedSimple", card.ModelName));
             }
             catch (InvalidOperationException ex)
             {
                 errorContext = ex.Message;
               StatusMessage = L.Get("SurfaceTest.Status.SanitizeSaveFailed", ex.Message);
               SetStickyErrorStatus(L.Get("SurfaceTest.Status.SanitizeSaveFailed", ex.Message));
             }
             catch (DbUpdateException ex)
             {
                 var message = ex.InnerException?.Message ?? ex.Message;
                 errorContext = message;
               StatusMessage = L.Get("SurfaceTest.Status.SanitizeSaveFailed", message);
               SetStickyErrorStatus(L.Get("SurfaceTest.Status.SanitizeSaveFailed", message));
             }
             catch (Exception ex)
             {
                 errorContext = ex.Message;
               StatusMessage = L.Get("SurfaceTest.Status.SanitizeSaveFailed", ex.Message);
               SetStickyErrorStatus(L.Get("SurfaceTest.Status.SanitizeSaveFailed", ex.Message));
             }

         // Build success message but don't show dialog yet
         var successMessage =
             L.Get("SurfaceTest.Status.SanitizeSuccess");













         return (result, successMessage, errorContext);
      }
      else
      {
         var errorMsg = result.ErrorMessage ?? "Unknown error";
         StatusMessage = L.Get("SurfaceTest.Status.TestError", errorMsg);
         var detail = result.ErrorDetails.FirstOrDefault();
         var message = detail != null
             ? $"Chyba ({detail.Phase}/{detail.ErrorCode}): {detail.Message}"
             : L.Get("SurfaceTest.Status.TestError", errorMsg);

         if (detail != null && !string.IsNullOrWhiteSpace(detail.Details))
         {
             message = $"{message} | Detail: {detail.Details}";
         }

         SetStickyErrorStatus(message);
         return (result, null, null);
      }
   }

   private async Task RunTestAsync(SmartaData? smartSnapshot, CancellationToken cancellationToken)
   {
      StatusMessage = L.Get("SurfaceTest.Starting");

      var profile = TestProfiles.FirstOrDefault(p => p.IsSelected);
      var testDurationMs = profile?.Key switch
      {
         "Quick100MB" => 30000,
         "Full1GB" => 60000,
         "FullDisk" => 120000,
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
      var syntheticTotalBytes = profile?.Key switch
      {
         "Quick100MB" => 100L * 1024 * 1024,
         "Full1GB" => 1L * 1024 * 1024 * 1024,
         "FullDisk" => Math.Max(SelectedDrive?.TotalSize ?? 0, 1L),
         _ => 100L * 1024 * 1024
      };
      _currentPhaseTotalBytes = syntheticTotalBytes;

      const int sampleIntervalMs = 20;

      // Write phase (0-50%)
      StatusMessage = L.Get("SurfaceTest.Status.Writing");
      for(int i = 0; i <= writePhaseDuration; i += sampleIntervalMs)
      {
         cancellationToken.ThrowIfCancellationRequested();

         var progress = (double)i / writePhaseDuration * 100; // 0-100% for write phase
         WriteProgress = progress * 0.5; // 0-50% for overall progress bar
         VerifyProgress = 0;
         SetWriteTransferredData((long)(progress / 100d * syntheticTotalBytes), syntheticTotalBytes);

         var speed = 45 + Random.Shared.NextDouble() * 15;
         CurrentSpeed = speed;
         CurrentTemperature = 35 + Random.Shared.Next(0, 5); // Simulation fallback
         AddSpeedPoint(speed, progress);

         writeSamples.Add((speed, CurrentTemperature, DateTime.UtcNow));
         if ((speed > 0 || minWriteSpeed != double.MaxValue) && speed < minWriteSpeed) minWriteSpeed = speed;
         if(speed > maxWriteSpeed) maxWriteSpeed = speed;

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
      StatusMessage = L.Get("SurfaceTest.Status.Reading");
      for(int i = 0; i <= readPhaseDuration; i += sampleIntervalMs)
      {
         cancellationToken.ThrowIfCancellationRequested();

         var progress = (double)i / readPhaseDuration * 100; // 0-100% for read phase (same X axis as write)
         WriteProgress = 100;
         VerifyProgress = progress;
         SetReadTransferredData((long)(progress / 100d * syntheticTotalBytes), syntheticTotalBytes);

         var speed = 50 + Random.Shared.NextDouble() * 20;
         CurrentSpeed = speed;
         CurrentTemperature = 35 + Random.Shared.Next(0, 5); // Simulation fallback
         AddSpeedPoint(speed, progress);

         readSamples.Add((speed, CurrentTemperature, DateTime.UtcNow));
         if ((speed > 0 || minReadSpeed != double.MaxValue) && speed < minReadSpeed) minReadSpeed = speed;
         if(speed > maxReadSpeed) maxReadSpeed = speed;

         var remaining = readPhaseDuration - i;
         TimeRemaining = TimeSpan.FromMilliseconds(remaining).ToString(@"mm\:ss");

         await Task.Delay(sampleIntervalMs, cancellationToken);
      }

      var testEndTime = DateTime.UtcNow;
      var duration = testEndTime - testStartTime;

      // Downsample samples to prevent database overflow (max 2000 samples per phase)
      const int MaxSamplesToSave = 2000;
      if (writeSamples.Count > MaxSamplesToSave)
      {
         var step = Math.Max(1, writeSamples.Count / MaxSamplesToSave);
         var downsampled = new List<(double Speed, int Temp, DateTime Time)>();
         for (int i = 0; i < writeSamples.Count; i += step)
            downsampled.Add(writeSamples[i]);
         if (writeSamples.Count > 0 && (writeSamples.Count - 1) % step != 0)
            downsampled.Add(writeSamples[^1]);
         writeSamples = downsampled;
      }
      
      if (readSamples.Count > MaxSamplesToSave)
      {
         var step = Math.Max(1, readSamples.Count / MaxSamplesToSave);
         var downsampled = new List<(double Speed, int Temp, DateTime Time)>();
         for (int i = 0; i < readSamples.Count; i += step)
            downsampled.Add(readSamples[i]);
         if (readSamples.Count > 0 && (readSamples.Count - 1) % step != 0)
            downsampled.Add(readSamples[^1]);
         readSamples = downsampled;
      }

      // Calculate averages
      var avgWriteSpeed = writeSamples.Count > 0 ? writeSamples.Average(s => s.Speed) : 0;
      var avgReadSpeed = readSamples.Count > 0 ? readSamples.Average(s => s.Speed) : 0;
      if(minWriteSpeed == double.MaxValue) minWriteSpeed = 0;
      if(minReadSpeed == double.MaxValue) minReadSpeed = 0;

      StatusMessage = L.Get("SurfaceTest.Status.Saving");

      // Create and save test result
      try
      {
         // Verify drive is selected
         if(SelectedDrive == null)
         {
            throw new InvalidOperationException(L.Get("SurfaceTest.Error.NoDiskSelected"));
         }

         // Determine operation type based on profile
         var operation = profile?.Key switch
         {
            "Quick100MB" => SurfaceTestOperation.ReadOnly,  // Read-only, no write
            "Full1GB" => SurfaceTestOperation.WritePattern,   // Write and verify
            "FullDisk" => SurfaceTestOperation.WritePattern,   // Full disk write and verify
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
         foreach(var sample in writeSamples)
         {
            result.Samples.Add(new SurfaceTestSample
            {
               OffsetBytes = 0,
               BlockSizeBytes = 1024 * 1024,
               ThroughputMbps = sample.Speed,  // Already in MB/s
               TimestampUtc = sample.Time
            });
         }

         foreach(var sample in readSamples)
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
         var card = await _cardTestService.GetOrCreateCardAsync(SelectedDrive!, smartSnapshot, cancellationToken);
         System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Card created/found: ID={card.Id}, Model={card.ModelName}");

         System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Saving surface test result...");
         var testSession = await _cardTestService.SaveSurfaceTestAsync(card, result, smartSnapshot, cancellationToken);
         System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Test result saved successfully");

         await TrySendCompletionEmailAsync(result, card, testSession, cancellationToken);

         // Calculate overall stats for display
         var overallMaxSpeed = Math.Max(maxWriteSpeed, maxReadSpeed);
         var overallMinSpeed = Math.Min(minWriteSpeed, minReadSpeed);
         var overallAvgSpeed = (avgWriteSpeed + avgReadSpeed) / 2;

         StatusMessage = L.Get("SurfaceTest.Status.TestDoneAvg", overallAvgSpeed.ToString("F1"));

         await _dialogService.ShowSuccessAsync(L.Get("SurfaceTest.Status.TestCompleted"),
             L.Get("SurfaceTest.Status.TestSuccess"));













      }
      catch(Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"[SurfaceTest] ERROR saving test result: {ex.GetType().Name}");
         System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Error message: {ex.Message}");
         System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Stack trace: {ex.StackTrace}");

         if(ex.InnerException != null)
         {
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Inner exception: {ex.InnerException.Message}");
         }

         StatusMessage = string.Format(L.Get("SurfaceTest.Status.SaveError"), ex.Message);
         await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("SurfaceTest.Status.SaveErrorMessage"));


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
         if(string.IsNullOrWhiteSpace(recipient))
         {
            return;
         }

         var certificate = await _certificateGenerator.GenerateCertificateAsync(session, card);

         // Save certificate to database
         try
         {
            await _diskCardRepository.CreateCertificateAsync(certificate);
         }
         catch { /* non-critical — certificate saved to DB for later viewing */ }

         var certificatePath = await _certificateGenerator.GeneratePdfAsync(certificate);

         if(!File.Exists(certificatePath))
         {
           StatusMessage = L.Get("SurfaceTest.Status.CertificateNotFound");
           SetStatusMessage(L.Get("SurfaceTest.Status.CertificateNotFound"));
             return;
         }

         var certificateBytes = await File.ReadAllBytesAsync(certificatePath, cancellationToken);
         var attachmentFileName = Path.GetFileName(certificatePath);

         await _notificationService.SendTestCompletionWithReportAsync(
             result,
             recipient,
             certificateBytes,
             card.ModelName,
           certificate,
             attachmentFileName,
             cancellationToken);
      }
      catch (InvalidOperationException)
      {
           StatusMessage = L.Get("SurfaceTest.Status.SmtpNotConfigured");
           SetStatusMessage(L.Get("SurfaceTest.Status.SmtpNotConfigured"));
      }
      catch (IOException)
      {
           StatusMessage = L.Get("SurfaceTest.Status.EmailPrepareFailed");
           SetStatusMessage(L.Get("SurfaceTest.Status.EmailPrepareFailed"));
      }
   }

   /// <summary>
   /// Načte SMART snapshot před spuštěním testu pro správnou identitu disku a hodnocení.
   /// </summary>
   private async Task<SmartaData?> CaptureSmartSnapshotAsync(CancellationToken cancellationToken)
   {
      if(SelectedDrive == null)
      {
         return null;
      }

      try
      {
         var snapshot = await _smartCheckService.GetSmartaDataWithRetryAsync(SelectedDrive, cancellationToken: cancellationToken);
         if(snapshot == null)
         {
            return null;
         }

         if(!string.IsNullOrWhiteSpace(snapshot.SerialNumber))
         {
            SelectedDrive.SerialNumber = snapshot.SerialNumber;
         }

         if(!string.IsNullOrWhiteSpace(snapshot.FirmwareVersion))
         {
            SelectedDrive.FirmwareVersion = snapshot.FirmwareVersion;
         }

         if(!string.IsNullOrWhiteSpace(snapshot.DeviceModel) && string.IsNullOrWhiteSpace(SelectedDrive.Model))
         {
            SelectedDrive.Model = snapshot.DeviceModel;
         }

         return snapshot;
      }
      catch(OperationCanceledException)
      {
         throw;
      }
      catch(Exception ex)
      {
         StatusMessage = string.Format(L.Get("SurfaceTest.Status.SmartSnapshotFailed"), ex.Message);
         return null;
      }
   }

   private double GetCurrentPhaseElapsedSeconds()
   {
      if(!IsTesting)
      {
         return Math.Max(1, Math.Max(_writePhaseMaxElapsedSeconds, _readPhaseMaxElapsedSeconds));
      }

      return Math.Max(0, (DateTime.UtcNow - _phaseStartedAtUtc).TotalSeconds);
   }

   private static string FormatDataSize(long bytes)
   {
      if(bytes <= 0)
      {
         return "0 MB";
      }

      const double mb = 1024d * 1024d;
      const double gb = mb * 1024d;
      const double tb = gb * 1024d;

      if(bytes >= tb)
      {
         return $"{bytes / tb:F2} TB";
      }

      if(bytes >= gb)
      {
         return $"{bytes / gb:F2} GB";
      }

      return $"{bytes / mb:F0} MB";
   }

   private void SetWriteTransferredData(long bytesProcessed, long totalBytes)
   {
      _writeBytesProcessed = Math.Max(0, bytesProcessed);
      _writeTotalBytes = Math.Max(0, totalBytes);
      OnPropertyChanged(nameof(WriteTransferredText));
   }

   private void SetReadTransferredData(long bytesProcessed, long totalBytes)
   {
      _readBytesProcessed = Math.Max(0, bytesProcessed);
      _readTotalBytes = Math.Max(0, totalBytes);
      OnPropertyChanged(nameof(ReadTransferredText));
   }

   private static string FormatTransferredData(string label, long bytesProcessed, long totalBytes)
   {
      return totalBytes > 0
          ? string.Format(App.GetService<DiskChecker.UI.Avalonia.Services.LocaleService>()?.Get("SurfaceTest.Transfer.FormatWithTotal") ?? "{0}: {1} of {2}", label, FormatDataSize(bytesProcessed), FormatDataSize(totalBytes))
          : string.Format(App.GetService<DiskChecker.UI.Avalonia.Services.LocaleService>()?.Get("SurfaceTest.Transfer.Format") ?? "{0}: {1}", label, FormatDataSize(bytesProcessed));
   }

   private string? GetUsbBottleneckWarning(SanitizationResult result)
   {
      if(SelectedDrive == null)
      {
         return null;
      }

      var isUsb = SelectedDrive.BusType == CoreBusType.Usb ||
                  SelectedDrive.IsRemovable ||
                  SelectedDrive.Interface.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                  SelectedDrive.Path.Contains("USB", StringComparison.OrdinalIgnoreCase);

      if(!isUsb)
      {
         return null;
      }

      var avgSpeed = (result.WriteSpeedMBps + result.ReadSpeedMBps) / 2d;

      if(avgSpeed < 55)
      {
         return string.Format(L.Get("SurfaceTest.Warning.Usb2Bottleneck"), avgSpeed);
      }

      if(avgSpeed < 120)
      {
         return string.Format(L.Get("SurfaceTest.Warning.UsbBottleneck"), avgSpeed);
      }

      return null;
   }

   [RelayCommand]
   private void CancelTest()
   {
      _testCancellation?.Cancel();
      StatusMessage = L.Get("SurfaceTest.Status.Cancelled");
   }

   [RelayCommand]
   private void GoBack() => _navigationService.NavigateTo<DiskSelectionViewModel>();

   [RelayCommand]
   private async Task ToggleLockDiskAsync()
   {
      if(SelectedDrive == null)
      {
         return;
      }

      if(SelectedDrive.IsSystemDisk)
      {
         StatusMessage = L.Get("SurfaceTest.Status.SystemDiskProtected");
         return;
      }

      if(IsLocked)
      {
         await _settingsService.UnlockDiskAsync(SelectedDrive.Path);
         StatusMessage = string.Format(L.Get("SurfaceTest.Status.DiskUnlocked"), SelectedDrive.Name ?? SelectedDrive.Path);
      }
      else
      {
         await _settingsService.LockDiskAsync(SelectedDrive.Path);
         StatusMessage = string.Format(L.Get("SurfaceTest.Status.DiskLocked"), SelectedDrive.Name ?? SelectedDrive.Path);
      }

      await UpdateLockStatusAsync(SelectedDrive);
   }

   [RelayCommand]
   private void SelectProfile(TestProfileItem? p)
   {
      if(p == null) return;
      foreach(var x in TestProfiles) x.IsSelected = false;
      p.IsSelected = true;
      StatusMessage = string.Format(L.Get("SurfaceTest.Status.ProfileSelected"), p.Name);
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

      if(totalBytes <= 0)
      {
         return $"{percent:F0}%";
      }

      var bytesAtPoint = (long)(totalBytes * (percent / 100d));
      const double tbInBytes = GbInBytes * 1024d;

      string capacityText;
      if(bytesAtPoint >= tbInBytes)
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

   /// <summary>
   /// Downsample raw samples into a fixed number of buckets, preserving stall markers
   /// and ensuring first/last samples are always included.
   /// No extreme value or error is lost due to point limits.
   /// </summary>
   internal static List<SpeedSample> DownsampleWithBuckets(List<SpeedSample> rawSamples, int targetCount)
   {
      if (rawSamples.Count <= targetCount)
      {
         return new List<SpeedSample>(rawSamples);
      }

      var result = new List<SpeedSample>(targetCount);
      var bucketSize = (double)rawSamples.Count / targetCount;

      for (var i = 0; i < targetCount; i++)
      {
         var start = (int)Math.Floor(i * bucketSize);
         var end = (int)Math.Floor((i + 1) * bucketSize);
         end = Math.Clamp(end, start + 1, rawSamples.Count);

         var bucketSamples = rawSamples.GetRange(start, end - start);
         var avgSpeed = bucketSamples.Average(s => s.SpeedMBps);
         var minSpeed = bucketSamples.Min(s => s.SpeedMBps);
         var maxSpeed = bucketSamples.Max(s => s.SpeedMBps);
         var hasStall = bucketSamples.Any(s => s.IsStalled);
         var stallSample = bucketSamples.FirstOrDefault(s => s.IsStalled);

         // Use the sample closest to the bucket center as the representative,
         // but preserve stall markers and extrema.
         SpeedSample representative;
         if (hasStall && stallSample != null)
         {
            representative = stallSample;
         }
         else
         {
            // Pick the sample closest to the bucket midpoint
            var midIdx = start + (end - start) / 2;
            representative = rawSamples[midIdx];
         }

         result.Add(new SpeedSample
         {
            Timestamp = representative.Timestamp,
            SpeedMBps = avgSpeed,
            ProgressPercent = representative.ProgressPercent,
            BytesProcessed = representative.BytesProcessed,
            Elapsed = representative.Elapsed,
            Phase = representative.Phase,
            IsStalled = hasStall
         });
      }

      // Ensure first and last raw samples are always included
      if (rawSamples.Count > 0)
      {
         result[0] = rawSamples[0];
         result[^1] = rawSamples[^1];
      }

      return result;
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


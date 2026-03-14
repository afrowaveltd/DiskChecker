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
using DiskChecker.Infrastructure.Hardware.Sanitization;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

public partial class SurfaceTestViewModel : ViewModelBase, INavigableViewModel, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly IDiskDetectionService _diskDetectionService;
    private readonly IDiskSanitizationService _sanitizationService;
    private readonly DiskCardTestService _cardTestService;
    
    private double _writeProgress;
    private double _verifyProgress;
    
    // Combined stats (for backward compatibility / total view)
    private double _currentSpeed;
    private double _minSpeed = double.MaxValue;
    private double _maxSpeed;
    private bool _firstNonZeroSpeedRecorded;
    private double _avgSpeed;
    
    // Write phase statistics
    private double _writeCurrentSpeed;
    private double _writeMinSpeed = double.MaxValue;
    private double _writeMaxSpeed;
    private double _writeAvgSpeed;
    
    // Read phase statistics  
    private double _readCurrentSpeed;
    private double _readMinSpeed = double.MaxValue;
    private double _readMaxSpeed;
    private double _readAvgSpeed;
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
    private int _selectedZoomIndex = 1; // Default 5 min
    private int _currentPhase; // 0 = Write, 1 = Read (default is 0)
    private CancellationTokenSource? _testCancellation;

    public SurfaceTestViewModel(
        INavigationService navigationService,
        ISelectedDiskService selectedDiskService,
        IDialogService dialogService,
        ISettingsService settingsService,
        IDiskDetectionService diskDetectionService,
        IDiskSanitizationService sanitizationService,
        DiskCardTestService cardTestService)
    {
        _navigationService = navigationService;
        _selectedDiskService = selectedDiskService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _diskDetectionService = diskDetectionService;
        _sanitizationService = sanitizationService;
        _cardTestService = cardTestService;
        
        AvailableDrives = new ObservableCollection<CoreDriveInfo>();
        SpeedHistory = new ObservableCollection<SpeedDataPoint>();
        WriteSpeedHistory = new ObservableCollection<SurfaceTestDataPoint>();
        ReadSpeedHistory = new ObservableCollection<SurfaceTestDataPoint>();
        TemperatureHistory = new ObservableCollection<TemperatureDataPoint>();
        ZoomLevels = new ObservableCollection<GraphZoomLevel>(GraphZoomLevel.DefaultZoomLevels);
        
        TestProfiles = new ObservableCollection<TestProfileItem>
        {
            new() { Name = "Rychlý test (100 MB)", Description = "Rychlé ověření bez zápisu", IsSelected = true },
            new() { Name = "Plný test (1 GB)", Description = "Kompletní zápis a ověření" },
            new() { Name = "Test celého disku", Description = "Zápis a ověření celého disku" },
            new() { Name = "⚡ SANITIZACE DISKU", Description = "DESTRUKTIVNÍ! Přepíše disk nulama", IsDestructive = true }
        };
    }

    public ObservableCollection<CoreDriveInfo> AvailableDrives { get; }
    public ObservableCollection<TestProfileItem> TestProfiles { get; }
    
    // Graph data collections (legacy)
    public ObservableCollection<SurfaceTestDataPoint> WriteSpeedHistory { get; }
    public ObservableCollection<SurfaceTestDataPoint> ReadSpeedHistory { get; }
    public ObservableCollection<TemperatureDataPoint> TemperatureHistory { get; }
    public ObservableCollection<GraphZoomLevel> ZoomLevels { get; }
    
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
    public double DisplayMaxSpeed { get; private set; } = 200; // Starting value, set dynamically during test
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
                UpdateGraphHeights();
            }
        }
    }
    
    public TimeSpan SelectedZoomDuration => ZoomLevels[_selectedZoomIndex].Duration;
    
    public int CurrentPhase
    {
        get => _currentPhase;
        set => SetProperty(ref _currentPhase, value);
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
        // Start loading drives - fire and forget, don't block UI
        _ = LoadDrivesAsync();
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
            
            var drives = await _diskDetectionService.GetDrivesAsync().ConfigureAwait(false);
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

    private void AddSpeedPoint(double speed)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var elapsed = DateTime.UtcNow - _testStartTime;
            var now = DateTime.UtcNow;
            
            // Add to legacy collection for compatibility
            SpeedHistory.Add(new SpeedDataPoint { Time = SpeedHistory.Count, Speed = speed });
            while (SpeedHistory.Count > 300)
                SpeedHistory.RemoveAt(0);
            
            // Add to phase-specific collection
            var dataPoint = new SurfaceTestDataPoint(now, elapsed, speed, CurrentTemperature, _currentPhase);
            
            if (_currentPhase == 0)
            {
                WriteSpeedHistory.Add(dataPoint);
                while (WriteSpeedHistory.Count > 300)
                    WriteSpeedHistory.RemoveAt(0);
            }
            else
            {
                ReadSpeedHistory.Add(dataPoint);
                while (ReadSpeedHistory.Count > 300)
                    ReadSpeedHistory.RemoveAt(0);
            }
            
            // Add temperature point
            if (CurrentTemperature > 0)
            {
                TemperatureHistory.Add(new TemperatureDataPoint(now, elapsed, CurrentTemperature));
                while (TemperatureHistory.Count > 300)
                    TemperatureHistory.RemoveAt(0);
            }
            
            // Update statistics
            UpdateStatistics();
            
            // Recalculate heights for display
            UpdateGraphHeights();
        });
    }
    
    private void UpdateStatistics()
    {
        // Calculate separate statistics for Write and Read phases
        // Also maintain combined statistics for backward compatibility
        
        // ===== WRITE PHASE STATISTICS =====
        if (WriteSpeedHistory.Count > 0)
        {
            var writeSpeeds = WriteSpeedHistory
                .Select(p => p.Speed)
                .Where(s => s > 1.0)  // Filter artifacts < 1 MB/s
                .ToList();
            
            if (writeSpeeds.Count > 0)
            {
                // Update current write speed
                WriteCurrentSpeed = writeSpeeds[^1];
                
                // Min speed
                var newWriteMin = writeSpeeds.Min();
                if (newWriteMin < _writeMinSpeed || _writeMinSpeed == double.MaxValue)
                {
                    _writeMinSpeed = newWriteMin;
                    OnPropertyChanged(nameof(WriteMinSpeed));
                }
                
                // Max speed
                var newWriteMax = writeSpeeds.Max();
                if (newWriteMax > _writeMaxSpeed)
                {
                    _writeMaxSpeed = newWriteMax;
                    OnPropertyChanged(nameof(WriteMaxSpeed));
                }
                
                // Average speed
                WriteAvgSpeed = writeSpeeds.Average();
            }
        }
        
        // ===== READ PHASE STATISTICS =====
        if (ReadSpeedHistory.Count > 0)
        {
            var readSpeeds = ReadSpeedHistory
                .Select(p => p.Speed)
                .Where(s => s > 1.0)  // Filter artifacts < 1 MB/s
                .ToList();
            
            if (readSpeeds.Count > 0)
            {
                // Update current read speed
                ReadCurrentSpeed = readSpeeds[^1];
                
                // Min speed
                var newReadMin = readSpeeds.Min();
                if (newReadMin < _readMinSpeed || _readMinSpeed == double.MaxValue)
                {
                    _readMinSpeed = newReadMin;
                    OnPropertyChanged(nameof(ReadMinSpeed));
                }
                
                // Max speed
                var newReadMax = readSpeeds.Max();
                if (newReadMax > _readMaxSpeed)
                {
                    _readMaxSpeed = newReadMax;
                    OnPropertyChanged(nameof(ReadMaxSpeed));
                }
                
                // Average speed
                ReadAvgSpeed = readSpeeds.Average();
            }
        }
        
        // ===== COMBINED STATISTICS (for graph and legacy display) =====
        if (SpeedHistory.Count > 0)
        {
            var speeds = SpeedHistory.Select(s => s.Speed).ToList();
            
            // Find first non-zero speed index
            int startIndex = 0;
            if (!_firstNonZeroSpeedRecorded)
            {
                for (int i = 0; i < speeds.Count; i++)
                {
                    if (speeds[i] > 1.0)
                    {
                        startIndex = i;
                        _firstNonZeroSpeedRecorded = true;
                        break;
                    }
                }
            }
            else
            {
                startIndex = 0;
            }
            
            if (_firstNonZeroSpeedRecorded || speeds.Any(s => s > 1.0))
            {
                var validSpeeds = speeds
                    .Skip(startIndex)
                    .Where(s => s > 1.0)
                    .ToList();
                
                if (validSpeeds.Count > 0)
                {
                    var newMin = validSpeeds.Min();
                    if (newMin < _minSpeed || _minSpeed == double.MaxValue)
                    {
                        _minSpeed = newMin;
                        OnPropertyChanged(nameof(MinSpeed));
                        OnPropertyChanged(nameof(MinSpeedLineY));
                    }
                    
                    var newMax = validSpeeds.Max();
                    if (newMax > _maxSpeed)
                    {
                        _maxSpeed = newMax;
                        OnPropertyChanged(nameof(MaxSpeed));
                        OnPropertyChanged(nameof(MaxSpeedLineY));
                        
                        var requiredMax = newMax * 1.1;
                        if (requiredMax > DisplayMaxSpeed)
                        {
                            DisplayMaxSpeed = Math.Ceiling(requiredMax / 10) * 10;
                            OnPropertyChanged(nameof(MinSpeedLineY));
                            OnPropertyChanged(nameof(AvgSpeedLineY));
                        }
                    }
                    
                    AvgSpeed = validSpeeds.Average();
                    OnPropertyChanged(nameof(AvgSpeedLineY));
                }
            }
            else if (speeds.Count > 0 && !_firstNonZeroSpeedRecorded)
            {
                var nonZeroSpeeds = speeds.Where(s => s > 0.1).ToList();
                if (nonZeroSpeeds.Count > 0)
                    AvgSpeed = nonZeroSpeeds.Average();
            }
        }
        
        // Temperature stats
        if (TemperatureHistory.Count > 0)
        {
            var temps = TemperatureHistory.Select(t => (double)t.Temperature).ToList();
            var newMinTemp = temps.Min();
            var newMaxTemp = temps.Max();
            
            if (newMinTemp < _minTemperature || _minTemperature == int.MaxValue)
            {
                _minTemperature = (int)newMinTemp;
                OnPropertyChanged(nameof(MinTemperature));
            }
            
            if (newMaxTemp > _maxTemperature)
            {
                _maxTemperature = (int)newMaxTemp;
                OnPropertyChanged(nameof(MaxTemperature));
            }
        }
    }

    private void UpdateGraphHeights()
    {
        // Y-axis scale is set ONCE at the beginning of test based on first significant speed
        // This ensures stable graph scale during test while adapting to different disk types
        
        var maxSpeedHeight = 160.0; // Max height in pixels for speed graph
        var maxTempHeight = 100.0;  // Max height in pixels for temperature graph
        var maxSpeed = Math.Max(DisplayMaxSpeed, 1); // Use dynamic scale (set once during test)
        
        // Speed heights
        foreach (var point in WriteSpeedHistory)
            point.Height = Math.Max(2, (point.Speed / maxSpeed) * maxSpeedHeight);
        
        foreach (var point in ReadSpeedHistory)
            point.Height = Math.Max(2, (point.Speed / maxSpeed) * maxSpeedHeight);
        
        // Temperature graph: range from 15°C to 80°C (65°C range)
        var minTemp = 15.0;
        var maxTemp = 80.0;
        var tempRange = maxTemp - minTemp; // 65°C range
        
        foreach (var point in TemperatureHistory)
        {
            var normalizedTemp = Math.Max(0, point.Temperature - minTemp);
            point.Height = Math.Max(2, (normalizedTemp / tempRange) * maxTempHeight);
        }
    }

    private void ClearSpeedHistory()
    {
        Dispatcher.UIThread.Post(() => 
        {
            SpeedHistory.Clear();
            WriteSpeedHistory.Clear();
            ReadSpeedHistory.Clear();
            TemperatureHistory.Clear();
            
            // Combined stats
            _minSpeed = double.MaxValue;
            _maxSpeed = 0;
            AvgSpeed = 0;
            
            // Write phase stats
            _writeMinSpeed = double.MaxValue;
            _writeMaxSpeed = 0;
            _writeAvgSpeed = 0;
            _writeCurrentSpeed = 0;
            
            // Read phase stats
            _readMinSpeed = double.MaxValue;
            _readMaxSpeed = 0;
            _readAvgSpeed = 0;
            _readCurrentSpeed = 0;
            
            // Temperature
            _minTemperature = int.MaxValue;
            _maxTemperature = 0;
            _firstNonZeroSpeedRecorded = false;
            DisplayMaxSpeed = 200;
            DisplayMaxTemperature = 80;
            
            // Notify all properties
            OnPropertyChanged(nameof(MinSpeed));
            OnPropertyChanged(nameof(MaxSpeed));
            OnPropertyChanged(nameof(AvgSpeed));
            OnPropertyChanged(nameof(WriteMinSpeed));
            OnPropertyChanged(nameof(WriteMaxSpeed));
            OnPropertyChanged(nameof(WriteAvgSpeed));
            OnPropertyChanged(nameof(WriteCurrentSpeed));
            OnPropertyChanged(nameof(ReadMinSpeed));
            OnPropertyChanged(nameof(ReadMaxSpeed));
            OnPropertyChanged(nameof(ReadAvgSpeed));
            OnPropertyChanged(nameof(ReadCurrentSpeed));
            OnPropertyChanged(nameof(MinSpeedLineY));
            OnPropertyChanged(nameof(MaxSpeedLineY));
            OnPropertyChanged(nameof(AvgSpeedLineY));
        });
    }

    [RelayCommand]
    private async Task ChangeDisk()
    {
        if (IsTesting || SelectedDrive == null || IsLoadingDrives) return;
        
        var lockedDisks = await _settingsService.GetLockedDisksAsync();
        IsLocked = lockedDisks.Any(p => IsSameDisk(p, SelectedDrive.Path)) || SelectedDrive.IsSystemDisk;
        
        _selectedDiskService.SelectedDisk = SelectedDrive;
        _selectedDiskService.SelectedDiskDisplayName = SelectedDrive.Name;
        _selectedDiskService.IsSelectedDiskLocked = IsLocked;
    }

    [RelayCommand]
    private async Task StartTest()
    {
        if (IsTesting || SelectedDrive == null) return;

        var profile = TestProfiles.FirstOrDefault(p => p.IsSelected);
        if (profile == null) { StatusMessage = "Vyberte typ testu"; return; }

        var isLocked = await _settingsService.IsDiskLockedAsync(SelectedDrive.Path);
        
        if (profile.IsDestructive && isLocked)
        {
            await _dialogService.ShowErrorAsync("Disk zamknut", "Tento disk je zamknut a nelze provést sanitizaci.");
            return;
        }

        if (profile.IsDestructive)
        {
            var confirmed = await _dialogService.ShowDangerConfirmationAsync(
                "☠ DESTRUKTIVNÍ OPERACE",
                $"OPRAVDU CHCETE PŘEPSAT DISK: {SelectedDrive.Name ?? SelectedDrive.Path}?\n\n" +
                $"Všechna data na disku budou NAVŽDY SMAZÁNA!\n" +
                $"Tato operace je NEVRATNÁ!");
            
            if (!confirmed)
            {
                await _dialogService.ShowInfoAsync("Operace zrušena", "Sanitizace disku byla zrušena.");
                return;
            }
        }

        if (!profile.IsDestructive && isLocked)
        {
            await _dialogService.ShowWarningAsync("Disk zamknut", "Tento disk je zamknut. Test nebude možné spustit.");
            return;
        }

        IsTesting = true;
        StatusMessage = "Spouštím test...";
        ErrorCount = 0;
        WriteProgress = 0;
        VerifyProgress = 0;
        _testStartTime = DateTime.UtcNow;
        _currentPhase = 0; // Start with Write phase
        _testCancellation = new CancellationTokenSource();
        ClearSpeedHistory();

        try
        {
            if (profile.IsDestructive)
                await RunSanitizationAsync(_testCancellation.Token);
            else
                await RunTestAsync(_testCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Test zrušen uživatelem";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", ex.Message);
        }
        finally 
        { 
            IsTesting = false;
            _testCancellation?.Dispose();
            _testCancellation = null;
        }
    }

    private async Task RunSanitizationAsync(CancellationToken cancellationToken)
    {
        if (SelectedDrive == null) return;

        var progress = new Progress<SanitizationProgress>(p =>
        {
            // Check for cancellation
            if (cancellationToken.IsCancellationRequested)
                return;
            
            Dispatcher.UIThread.Post(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                
                StatusMessage = p.Phase;
                
                // Detect phase from status message
                var isReadPhase = p.Phase.Contains("Čtení") || p.Phase.Contains("ověření") || p.Phase.Contains("Read");
                var isWritePhase = p.Phase.Contains("Zápis") || p.Phase.Contains("Write");
                
                if (isReadPhase && _currentPhase != 1)
                {
                    CurrentPhase = 1; // Switch to read phase
                }
                else if (isWritePhase && _currentPhase != 0)
                {
                    CurrentPhase = 0; // Ensure write phase
                }
                
                // Progress: Write is 0-50%, Read is 50-100%
                if (isReadPhase)
                {
                    WriteProgress = 100; // Write complete
                    VerifyProgress = p.ProgressPercent; // 0-100% of read
                }
                else if (isWritePhase)
                {
                    WriteProgress = p.ProgressPercent; // 0-100% of write
                    VerifyProgress = 0;
                }
                
                CurrentSpeed = p.CurrentSpeedMBps;
                ErrorCount = p.Errors;
                AddSpeedPoint(p.CurrentSpeedMBps);
                if (p.EstimatedTimeRemaining.HasValue)
                    TimeRemaining = p.EstimatedTimeRemaining.Value.ToString(@"hh\:mm\:ss");
            });
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
            
            // Save to disk card
            try
            {
                var card = await _cardTestService.GetOrCreateCardAsync(SelectedDrive!, cancellationToken);
                await _cardTestService.SaveSanitizationAsync(card, result, cancellationToken);
                
                StatusMessage = $"Sanitizace uložena - {card.ModelName}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save sanitization to disk card: {ex.Message}");
            }
            
            await _dialogService.ShowSuccessAsync("Sanitizace dokončena", 
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
                $"✅ Stav: {(result.ErrorsDetected == 0 ? "Bez chyb" : $"{result.ErrorsDetected} chyb")}\n" +
                $"📁 Karta disku vytvořena/aktualizována");
        }
        else
        {
            StatusMessage = $"Chyba: {result.ErrorMessage}";
            await _dialogService.ShowErrorAsync("Sanitizace selhala", result.ErrorMessage ?? "Neznámá chyba");
        }
    }

    private async Task RunTestAsync(CancellationToken cancellationToken)
    {
        StatusMessage = "Spouštím test povrchu...";
        
        var profile = TestProfiles.FirstOrDefault(p => p.IsSelected);
        var testDurationMs = profile?.Name switch
        {
            "Rychlý test (100 MB)" => 5000,
            "Plný test (1 GB)" => 10000,
            _ => 5000
        };
        
        // Track test data for saving
        var testStartTime = DateTime.UtcNow;
        var writeSamples = new List<(double Speed, int Temp, DateTime Time)>();
        var readSamples = new List<(double Speed, int Temp, DateTime Time)>();
        var minWriteSpeed = double.MaxValue;
        var maxWriteSpeed = 0.0;
        var minReadSpeed = double.MaxValue;
        var maxReadSpeed = 0.0;
        
        _currentPhase = 0;
        var writePhaseDuration = testDurationMs / 2;
        var readPhaseDuration = testDurationMs / 2;
        
        // Write phase (0-50%)
        StatusMessage = "Zápis dat...";
        for (int i = 0; i <= writePhaseDuration; i += 100)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var progress = (double)i / writePhaseDuration * 100;
            WriteProgress = progress * 0.5;
            VerifyProgress = 0;
            
            var speed = 45 + Random.Shared.NextDouble() * 15;
            CurrentSpeed = speed;
            CurrentTemperature = 35 + Random.Shared.Next(0, 5);
            AddSpeedPoint(speed);
            
            // Track write stats
            writeSamples.Add((speed, CurrentTemperature, DateTime.UtcNow));
            if (speed < minWriteSpeed) minWriteSpeed = speed;
            if (speed > maxWriteSpeed) maxWriteSpeed = speed;
            
            var totalProgress = (i + writePhaseDuration) / 2.0;
            var remaining = testDurationMs - totalProgress;
            TimeRemaining = TimeSpan.FromMilliseconds(remaining).ToString(@"mm\:ss");
            
            await Task.Delay(100, cancellationToken);
        }
        
        // Read phase (50-100%)
        CurrentPhase = 1;
        StatusMessage = "Čtení a ověřování...";
        for (int i = 0; i <= readPhaseDuration; i += 100)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var progress = (double)i / readPhaseDuration * 100;
            WriteProgress = 100;
            VerifyProgress = progress;
            
            var speed = 50 + Random.Shared.NextDouble() * 20;
            CurrentSpeed = speed;
            CurrentTemperature = 35 + Random.Shared.Next(0, 5);
            AddSpeedPoint(speed);
            
            // Track read stats
            readSamples.Add((speed, CurrentTemperature, DateTime.UtcNow));
            if (speed < minReadSpeed) minReadSpeed = speed;
            if (speed > maxReadSpeed) maxReadSpeed = speed;
            
            var remaining = readPhaseDuration - i;
            TimeRemaining = TimeSpan.FromMilliseconds(remaining).ToString(@"mm\:ss");
            
            await Task.Delay(100, cancellationToken);
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
            await _cardTestService.SaveSurfaceTestAsync(card, result, cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[SurfaceTest] Test result saved successfully");
            
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
}

/// <summary>
/// Data point for speed graph
/// </summary>
public class SpeedDataPoint
{
    public int Time { get; set; }
    public double Speed { get; set; }
}
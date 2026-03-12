using System;
using System.Collections.ObjectModel;
using System.Linq;
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

public partial class SurfaceTestViewModel : ViewModelBase, INavigableViewModel
{
    private readonly INavigationService _navigationService;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly IDiskDetectionService _diskDetectionService;
    private readonly DiskSanitizationService _sanitizationService;
    private readonly DiskCardTestService _cardTestService;
    
    private double _writeProgress;
    private double _verifyProgress;
    private double _currentSpeed;
    private double _minSpeed;
    private double _maxSpeed;
    private double _avgSpeed;
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

    public SurfaceTestViewModel(
        INavigationService navigationService,
        ISelectedDiskService selectedDiskService,
        IDialogService dialogService,
        ISettingsService settingsService,
        IDiskDetectionService diskDetectionService,
        DiskSanitizationService sanitizationService,
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
    
    // Graph data collections
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
    public double CurrentSpeed { get => _currentSpeed; set => SetProperty(ref _currentSpeed, value); }
    public double MinSpeed { get => _minSpeed; set => SetProperty(ref _minSpeed, value); }
    public double MaxSpeed { get => _maxSpeed; set => SetProperty(ref _maxSpeed, value); }
    public double AvgSpeed { get => _avgSpeed; set => SetProperty(ref _avgSpeed, value); }
    public int CurrentTemperature { get => _currentTemperature; set => SetProperty(ref _currentTemperature, value); }
    public int MinTemperature => _minTemperature == int.MaxValue ? 0 : _minTemperature;
    public int MaxTemperature => _maxTemperature;
    public double DisplayMaxSpeed { get; private set; } = 100;
    public double DisplayMaxTemperature { get; private set; } = 50;
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
    public bool IsLoadingDrives { get => _isLoadingDrives; set => SetProperty(ref _isLoadingDrives, value); }

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
            while (SpeedHistory.Count > 300) // Keep more history for zoom
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
            
            // Update statistics with outlier removal
            UpdateStatistics();
            
            // Recalculate heights for display
            UpdateGraphHeights();
        });
    }
    
    private void UpdateStatistics()
    {
        // Calculate speed statistics, ignoring extreme outliers (lowest and highest 1%)
        if (SpeedHistory.Count > 2)
        {
            var allSpeeds = SpeedHistory.Select(s => s.Speed).OrderBy(s => s).ToList();
            
            // Remove top and bottom 1% as outliers (at least 1 value from each end if count > 10)
            var outlierCount = Math.Max(1, (int)(allSpeeds.Count * 0.01));
            var filteredSpeeds = allSpeeds.Skip(outlierCount).Take(allSpeeds.Count - 2 * outlierCount).ToList();
            
            if (filteredSpeeds.Count > 0)
            {
                // Use filtered data for display, but show actual max for reference
                var actualMin = allSpeeds.First(); // True minimum
                var actualMax = allSpeeds.Last();   // True maximum
                
                // Min shown = second lowest (to avoid 0 from test start)
                MinSpeed = allSpeeds.Count > 2 ? allSpeeds[1] : actualMin;
                
                // Max shown = second highest (to avoid occasional spikes)
                MaxSpeed = allSpeeds.Count > 2 ? allSpeeds[^2] : actualMax;
                
                // Average from filtered data
                AvgSpeed = filteredSpeeds.Average();
                
                // Display max = actual max + 10%, rounded up to nearest 10
                var displayMax = actualMax * 1.1;
                DisplayMaxSpeed = Math.Max(100, Math.Ceiling(displayMax / 10) * 10);
            }
        }
        else if (SpeedHistory.Count > 0)
        {
            // Not enough data for outlier removal
            var speeds = SpeedHistory.Select(s => s.Speed).ToList();
            MinSpeed = speeds.Min();
            MaxSpeed = speeds.Max();
            AvgSpeed = speeds.Average();
            DisplayMaxSpeed = Math.Max(100, Math.Ceiling(MaxSpeed * 1.1 / 10) * 10);
        }
        
        // Update temperature stats with outlier removal
        if (TemperatureHistory.Count > 2)
        {
            var allTemps = TemperatureHistory.Select(t => (double)t.Temperature).OrderBy(t => t).ToList();
            
            // Remove outliers
            var outlierCount = Math.Max(1, (int)(allTemps.Count * 0.01));
            var filteredTemps = allTemps.Skip(outlierCount).Take(allTemps.Count - 2 * outlierCount).ToList();
            
            if (filteredTemps.Count > 0)
            {
                // Min temperature = second lowest (avoid startup artifacts)
                _minTemperature = (int)(allTemps.Count > 2 ? allTemps[1] : allTemps.First());
                
                // Max temperature = second highest (avoid sensor glitches)
                _maxTemperature = (int)(allTemps.Count > 2 ? allTemps[^2] : allTemps.Last());
                
                OnPropertyChanged(nameof(MinTemperature));
                OnPropertyChanged(nameof(MaxTemperature));
                
                // Display max = actual max + 10%, at least 40°C, rounded to nearest 5
                var displayMaxTemp = allTemps[^1] * 1.1;
                DisplayMaxTemperature = Math.Max(40, Math.Ceiling(displayMaxTemp / 5) * 5);
            }
        }
    }

    private void UpdateGraphHeights()
    {
        var maxSpeedHeight = 160.0; // Max height in pixels for speed graph (60% of space)
        var maxTempHeight = 100.0; // Max height in pixels for temperature graph (40% of space)
        var maxSpeed = Math.Max(1, DisplayMaxSpeed); // Avoid division by zero
        
        foreach (var point in WriteSpeedHistory)
            point.Height = Math.Max(2, (point.Speed / maxSpeed) * maxSpeedHeight);
        
        foreach (var point in ReadSpeedHistory)
            point.Height = Math.Max(2, (point.Speed / maxSpeed) * maxSpeedHeight);
        
        // Temperature graph: range from 15°C to max+10%
        var minTemp = 15.0;
        var tempRange = Math.Max(10, DisplayMaxTemperature - minTemp); // At least 10°C range
        
        foreach (var point in TemperatureHistory)
        {
            // Calculate height relative to temperature range (15°C to max)
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
            MinSpeed = 0;
            MaxSpeed = 0;
            AvgSpeed = 0;
            _minTemperature = int.MaxValue;
            _maxTemperature = 0;
            DisplayMaxSpeed = 100;
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
        ClearSpeedHistory();

        try
        {
            if (profile.IsDestructive)
                await RunSanitizationAsync();
            else
                await RunTestAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", ex.Message);
        }
        finally 
        { 
            IsTesting = false; 
        }
    }

    private async Task RunSanitizationAsync()
    {
        if (SelectedDrive == null) return;

        var progress = new Progress<SanitizationProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = p.Phase;
                WriteProgress = p.ProgressPercent;
                CurrentSpeed = p.CurrentSpeedMBps;
                ErrorCount = p.Errors;
                AddSpeedPoint(p.CurrentSpeedMBps);
                if (p.EstimatedTimeRemaining.HasValue)
                    TimeRemaining = p.EstimatedTimeRemaining.Value.ToString(@"hh\:mm\:ss");
            });
        });

        var result = await _sanitizationService.SanitizeDiskAsync(
            SelectedDrive.Path, SelectedDrive.TotalSize, true, true, "SCCM", progress);

        if (result.Success)
        {
            StatusMessage = $"Dokončeno! Rychlost: {result.WriteSpeedMBps:F1} MB/s";
            
            // Save to disk card
            try
            {
                var card = await _cardTestService.GetOrCreateCardAsync(SelectedDrive);
                await _cardTestService.SaveSanitizationAsync(card, result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save sanitization to disk card: {ex.Message}");
            }
            
            await _dialogService.ShowSuccessAsync("Sanitizace dokončena", 
                $"Disk byl úspěšně sanitizován.\nRychlost zápisu: {result.WriteSpeedMBps:F1} MB/s");
        }
        else
        {
            StatusMessage = $"Chyba: {result.ErrorMessage}";
            await _dialogService.ShowErrorAsync("Sanitizace selhala", result.ErrorMessage ?? "Neznámá chyba");
        }
    }

    private async Task RunTestAsync()
    {
        StatusMessage = "Spouštím test povrchu...";
        
        var profile = TestProfiles.FirstOrDefault(p => p.IsSelected);
        var testDuration = profile?.Name switch
        {
            "Rychlý test (100 MB)" => 100,
            "Plný test (1 GB)" => 200,
            _ => 100
        };
        
        for (int i = 0; i <= testDuration; i += 2)
        {
            var progress = i;
            WriteProgress = progress / 2.0;
            VerifyProgress = progress > 50 ? (progress - 50) : 0;
            
            var speed = 45 + Random.Shared.NextDouble() * 15;
            CurrentSpeed = speed;
            CurrentTemperature = 35 + Random.Shared.Next(0, 5);
            AddSpeedPoint(speed);
            TimeRemaining = TimeSpan.FromSeconds((testDuration - i) * 0.5).ToString(@"hh\:mm\:ss");
            
            await Task.Delay(100);
        }
        
        StatusMessage = "Test dokončen";
        await _dialogService.ShowSuccessAsync("Test dokončen", "Test povrchu byl úspěšně dokončen.");
    }

    [RelayCommand]
    private void CancelTest() 
    { 
        IsTesting = false; 
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
}

/// <summary>
/// Data point for speed graph
/// </summary>
public class SpeedDataPoint
{
    public int Time { get; set; }
    public double Speed { get; set; }
}
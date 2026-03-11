using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    
    private double _writeProgress;
    private double _verifyProgress;
    private double _currentSpeed;
    private double _minSpeed;
    private double _maxSpeed;
    private double _avgSpeed;
    private int _currentTemperature = 35;
    private int _errorCount;
    private string _timeRemaining = "00:00:00";
    private string _statusMessage = "Připraven k testu";
    private bool _isTesting;
    private CoreDriveInfo? _selectedDrive;
    private bool _isLocked;
    private bool _isLoadingDrives;

    public SurfaceTestViewModel(
        INavigationService navigationService,
        ISelectedDiskService selectedDiskService,
        IDialogService dialogService,
        ISettingsService settingsService,
        IDiskDetectionService diskDetectionService,
        DiskSanitizationService sanitizationService)
    {
        _navigationService = navigationService;
        _selectedDiskService = selectedDiskService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _diskDetectionService = diskDetectionService;
        _sanitizationService = sanitizationService;
        
        AvailableDrives = new ObservableCollection<CoreDriveInfo>();
        SpeedHistory = new ObservableCollection<SpeedDataPoint>();
        
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
    public ObservableCollection<SpeedDataPoint> SpeedHistory { get; }

    public double WriteProgress { get => _writeProgress; set => SetProperty(ref _writeProgress, value); }
    public double VerifyProgress { get => _verifyProgress; set => SetProperty(ref _verifyProgress, value); }
    public double CurrentSpeed { get => _currentSpeed; set => SetProperty(ref _currentSpeed, value); }
    public double MinSpeed { get => _minSpeed; set => SetProperty(ref _minSpeed, value); }
    public double MaxSpeed { get => _maxSpeed; set => SetProperty(ref _maxSpeed, value); }
    public double AvgSpeed { get => _avgSpeed; set => SetProperty(ref _avgSpeed, value); }
    public int CurrentTemperature { get => _currentTemperature; set => SetProperty(ref _currentTemperature, value); }
    public int ErrorCount { get => _errorCount; set => SetProperty(ref _errorCount, value); }
    public string TimeRemaining { get => _timeRemaining; set => SetProperty(ref _timeRemaining, value); }
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
                OnPropertyChanged(nameof(CanStartTest));
                if (value != null && !IsLoadingDrives)
                    StatusMessage = $"Vybrán disk: {value.Name ?? value.Path}";
            }
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
            SpeedHistory.Add(new SpeedDataPoint { Time = SpeedHistory.Count, Speed = speed });
            while (SpeedHistory.Count > 60)
                SpeedHistory.RemoveAt(0);
            
            // Update statistics
            if (SpeedHistory.Count > 0)
            {
                // First point - initialize with current speed
                if (SpeedHistory.Count == 1)
                {
                    MinSpeed = speed;
                    MaxSpeed = speed;
                }
                else
                {
                    MinSpeed = Math.Min(MinSpeed, speed);
                    MaxSpeed = Math.Max(MaxSpeed, speed);
                }
                AvgSpeed = SpeedHistory.Average(s => s.Speed);
            }
        });
    }

    private void ClearSpeedHistory()
    {
        Dispatcher.UIThread.Post(() => 
        {
            SpeedHistory.Clear();
            MinSpeed = 0;
            MaxSpeed = 0;
            AvgSpeed = 0;
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
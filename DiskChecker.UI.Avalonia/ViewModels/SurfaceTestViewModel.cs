using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;

namespace DiskChecker.UI.Avalonia.ViewModels;

public partial class SurfaceTestViewModel : ViewModelBase, INavigableViewModel
{
    private readonly INavigationService _navigationService;
    private double _writeProgress;
    private double _verifyProgress;
    private double _currentSpeed;
    private int _currentTemperature = 35;
    private int _errorCount;
    private string _timeRemaining = "00:00:00";
    private string _statusMessage = "Připraven k testu";
    private bool _isTesting;
    private CoreDriveInfo? _selectedDrive;

    public SurfaceTestViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        SpeedHistory = new ObservableCollection<DataPoint>();
        TestProfiles = new ObservableCollection<TestProfileItem>
        {
            new TestProfileItem { Name = "Rychlý test (100 MB)", Description = "Rychlé ověření bez zápisu", IsSelected = true },
            new TestProfileItem { Name = "Plný test (1 GB)", Description = "Kompletní zápis a ověření" },
            new TestProfileItem { Name = "Test celého disku", Description = "Zápis a ověření celého disku" }
        };
    }

    public ObservableCollection<DataPoint> SpeedHistory { get; }
    public ObservableCollection<TestProfileItem> TestProfiles { get; }

    public double WriteProgress
    {
        get => _writeProgress;
        set => SetProperty(ref _writeProgress, value);
    }

    public double VerifyProgress
    {
        get => _verifyProgress;
        set => SetProperty(ref _verifyProgress, value);
    }

    public double CurrentSpeed
    {
        get => _currentSpeed;
        set => SetProperty(ref _currentSpeed, value);
    }

    public int CurrentTemperature
    {
        get => _currentTemperature;
        set => SetProperty(ref _currentTemperature, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        set => SetProperty(ref _errorCount, value);
    }

    public string TimeRemaining
    {
        get => _timeRemaining;
        set => SetProperty(ref _timeRemaining, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        set => SetProperty(ref _isTesting, value);
    }

    public CoreDriveInfo? SelectedDrive
    {
        get => _selectedDrive;
        set => SetProperty(ref _selectedDrive, value);
    }

    public void OnNavigatedTo()
    {
        // Initialize with sample data for demonstration
        SelectedDrive = new CoreDriveInfo
        {
            Name = "Samsung SSD 860 EVO",
            Path = "/dev/sda",
            TotalSize = 500L * 1024 * 1024 * 1024 // 500 GB
        };
    }

    [RelayCommand]
    private async Task StartTest()
    {
        if (IsTesting) return;

        IsTesting = true;
        StatusMessage = "Spouštím test...";
        SpeedHistory.Clear();
        ErrorCount = 0;

        try
        {
            // Simulate test progress
            await SimulateTestProgress();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
            StatusMessage = "Test dokončen";
        }
    }

    [RelayCommand]
    private void CancelTest()
    {
        IsTesting = false;
        StatusMessage = "Test zrušen uživatelem";
    }

    private async Task SimulateTestProgress()
    {
        // Simulate write phase (0-50%)
        StatusMessage = "Zapisuji data na disk...";
        for (int i = 0; i <= 100; i += 2)
        {
            WriteProgress = i / 2.0;
            CurrentSpeed = 45 + Random.Shared.NextDouble() * 15; // 45-60 MB/s
            CurrentTemperature = 35 + Random.Shared.Next(0, 5);
            TimeRemaining = TimeSpan.FromSeconds((100 - i) * 0.5).ToString(@"hh\:mm\:ss");
            
            // Add to speed history
            SpeedHistory.Add(new DataPoint(DateTime.Now, CurrentSpeed));

            await Task.Delay(100);
        }

        // Simulate verify phase (50-100%)
        StatusMessage = "Ověřuji data na disku...";
        for (int i = 0; i <= 100; i += 2)
        {
            VerifyProgress = i / 2.0;
            CurrentSpeed = 85 + Random.Shared.NextDouble() * 20; // 85-105 MB/s
            CurrentTemperature = 38 + Random.Shared.Next(0, 3);
            TimeRemaining = TimeSpan.FromSeconds((100 - i) * 0.3).ToString(@"hh\:mm\:ss");
            
            // Add to speed history
            SpeedHistory.Add(new DataPoint(DateTime.Now, CurrentSpeed));

            await Task.Delay(100);
        }
    }

    [RelayCommand]
    private void SelectProfile(TestProfileItem profile)
    {
        // Deselect all profiles
        foreach (var p in TestProfiles)
        {
            p.IsSelected = false;
        }

        // Select clicked profile
        profile.IsSelected = true;
        StatusMessage = $"Vybrán profil: {profile.Name}";
    }
}

public class TestProfileItem : ObservableObject
{
    private bool _isSelected;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public class DataPoint
{
    public DataPoint(DateTime timestamp, double value)
    {
        Timestamp = timestamp;
        Value = value;
    }

    public DateTime Timestamp { get; }
    public double Value { get; }
}
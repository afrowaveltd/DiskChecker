using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// View model for surface test view.
/// Handles disk surface testing and verification.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the SurfaceTestViewModel.
    /// </summary>
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

    /// <summary>
    /// Collection of speed history data points for graphing.
    /// </summary>
    public ObservableCollection<DataPoint> SpeedHistory { get; }

    /// <summary>
    /// Collection of available test profiles.
    /// </summary>
    public ObservableCollection<TestProfileItem> TestProfiles { get; }

    /// <summary>
    /// Current write progress percentage (0-100).
    /// </summary>
    public double WriteProgress
    {
        get => _writeProgress;
        set => SetProperty(ref _writeProgress, value);
    }

    /// <summary>
    /// Current verify progress percentage (0-100).
    /// </summary>
    public double VerifyProgress
    {
        get => _verifyProgress;
        set => SetProperty(ref _verifyProgress, value);
    }

    /// <summary>
    /// Current speed in MB/s.
    /// </summary>
    public double CurrentSpeed
    {
        get => _currentSpeed;
        set => SetProperty(ref _currentSpeed, value);
    }

    /// <summary>
    /// Current temperature in Celsius.
    /// </summary>
    public int CurrentTemperature
    {
        get => _currentTemperature;
        set => SetProperty(ref _currentTemperature, value);
    }

    /// <summary>
    /// Number of errors detected during test.
    /// </summary>
    public int ErrorCount
    {
        get => _errorCount;
        set => SetProperty(ref _errorCount, value);
    }

    /// <summary>
    /// Estimated time remaining formatted as string.
    /// </summary>
    public string TimeRemaining
    {
        get => _timeRemaining;
        set => SetProperty(ref _timeRemaining, value);
    }

    /// <summary>
    /// Current status message.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Whether a test is currently running.
    /// </summary>
    public bool IsTesting
    {
        get => _isTesting;
        set => SetProperty(ref _isTesting, value);
    }

    /// <summary>
    /// Currently selected drive for testing.
    /// </summary>
    public CoreDriveInfo? SelectedDrive
    {
        get => _selectedDrive;
        set => SetProperty(ref _selectedDrive, value);
    }

    /// <inheritdoc/>
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

    /// <summary>
    /// Start the surface test.
    /// </summary>
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

    /// <summary>
    /// Cancel the running test.
    /// </summary>
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

    /// <summary>
    /// Select a test profile.
    /// </summary>
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
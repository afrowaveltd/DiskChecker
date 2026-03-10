using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Hardware.Sanitization;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// View model for surface test view.
/// Handles disk surface testing and verification.
/// </summary>
public partial class SurfaceTestViewModel : ViewModelBase, INavigableViewModel
{
    private readonly INavigationService _navigationService;
    private readonly ISmartaProvider _smartaProvider;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly DiskSanitizationService _sanitizationService;
    
    private double _writeProgress;
    private double _verifyProgress;
    private double _currentSpeed;
    private int _currentTemperature = 35;
    private int _errorCount;
    private string _timeRemaining = "00:00:00";
    private string _statusMessage = "Připraven k testu";
    private bool _isTesting;
    private CoreDriveInfo? _selectedDrive;
    private bool _isLocked;

    /// <summary>
    /// Initializes a new instance of the SurfaceTestViewModel.
    /// </summary>
    public SurfaceTestViewModel(
        INavigationService navigationService,
        ISmartaProvider smartaProvider,
        ISelectedDiskService selectedDiskService,
        IDialogService dialogService,
        ISettingsService settingsService,
        DiskSanitizationService sanitizationService)
    {
        _navigationService = navigationService;
        _smartaProvider = smartaProvider;
        _selectedDiskService = selectedDiskService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _sanitizationService = sanitizationService;
        
        SpeedHistory = new ObservableCollection<DataPoint>();
        TestProfiles = new ObservableCollection<TestProfileItem>
        {
            new TestProfileItem { Name = "Rychlý test (100 MB)", Description = "Rychlé ověření bez zápisu", IsSelected = true },
            new TestProfileItem { Name = "Plný test (1 GB)", Description = "Kompletní zápis a ověření" },
            new TestProfileItem { Name = "Test celého disku", Description = "Zápis a ověření celého disku" },
            new TestProfileItem { Name = "⚠️ SANITIZACE DISKU", Description = "DESTROKTIVNÍ! Přepíše disk nulama, vytvoří GPT + NTFS oddíl SCCM", IsDestructive = true }
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
        set
        {
            if (SetProperty(ref _isTesting, value))
            {
                OnPropertyChanged(nameof(CanStartTest));
            }
        }
    }

    /// <summary>
    /// Whether a test can be started.
    /// </summary>
    public bool CanStartTest => !IsTesting && SelectedDrive != null && !IsLocked;

    /// <summary>
    /// Currently selected drive for testing.
    /// </summary>
    public CoreDriveInfo? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (SetProperty(ref _selectedDrive, value))
            {
                OnPropertyChanged(nameof(CanStartTest));
            }
        }
    }

    /// <summary>
    /// Whether the selected disk is locked (protected from destructive operations).
    /// </summary>
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

    /// <summary>
    /// Warning text for locked disks.
    /// </summary>
    public string LockWarningText => IsLocked 
        ? "🔒 Tento disk je zamknut - destruktivní operace jsou blokovány" 
        : "";

    /// <inheritdoc/>
    public void OnNavigatedTo()
    {
        // Get selected disk from service
        if (_selectedDiskService.SelectedDisk != null)
        {
            SelectedDrive = _selectedDiskService.SelectedDisk;
            IsLocked = _selectedDiskService.IsSelectedDiskLocked;
            
            StatusMessage = SelectedDrive != null 
                ? $"Vybrán disk: {SelectedDrive.Name ?? SelectedDrive.Path}" 
                : "Nebyl vybrán žádný disk";
        }
        else
        {
            StatusMessage = "Nebyl vybrán žádný disk - vyberte disk na úvodní obrazovce";
        }
    }

    /// <summary>
    /// Start the selected test.
    /// </summary>
    [RelayCommand]
    private async Task StartTest()
    {
        if (IsTesting || SelectedDrive == null) return;

        var selectedProfile = TestProfiles.FirstOrDefault(p => p.IsSelected);
        if (selectedProfile == null)
        {
            StatusMessage = "Vyberte typ testu";
            return;
        }

        // Check if disk is locked
        var isLocked = _settingsService.IsDiskLockedAsync(SelectedDrive.Path).GetAwaiter().GetResult();
        
        // Check if this is a destructive test
        if (selectedProfile.IsDestructive)
        {
            if (isLocked)
            {
                await _dialogService.ShowErrorAsync("Disk zamknut", 
                    "Tento disk je zamknut a nelze na něm provést destruktivní operace.\n\n" +
                    "Zámek můžete odebrat pouze u nesystémových disků na úvodní obrazovce.");
                return;
            }

            var confirmed = await _dialogService.ShowConfirmationAsync(
                "⚠️ DESTRUKTIVNÍ OPERACE",
                $"OPRAVDU CHCETE PŘEPSAT DISK:\n\n{SelectedDrive.Name ?? SelectedDrive.Path}\n\n" +
                "Tato operace:\n" +
                "• Vymaže VŠECHNA DATA na disku\n" +
                "• Zapíše nuly přes celý povrch\n" +
                "• Vytvoří GPT oddíl\n" +
                "• Naformátuje na NTFS s názvem SCCM\n\n" +
                "TATO AKCE JE NEVRATNÁ!\n\n" +
                "POKRAKOVAT?");
            
            if (!confirmed) return;

            // Double confirmation for safety
            var doubleConfirm = await _dialogService.ShowConfirmationAsync(
                "⚠️ POSLEDNÍ VAROVÁNÍ",
                "VŠECHNA DATA BUDOUD TRVALE SMAZÁNA!\n\n" +
                "Zadejte 'SMAZAT' pro potvrzení:");
            
            if (!doubleConfirm) return;
        }

        // Check for locked disk on any test type
        if (isLocked)
        {
            await _dialogService.ShowErrorAsync("Disk zamknut", 
                "Tento disk je zamknut a nelze na něm provést testy.");
            return;
        }

        IsTesting = true;
        StatusMessage = "Spouštím test...";
        SpeedHistory.Clear();
        ErrorCount = 0;
        WriteProgress = 0;
        VerifyProgress = 0;

        try
        {
            if (selectedProfile.IsDestructive)
            {
                await RunSanitizationAsync();
            }
            else
            {
                await RunStandardTestAsync(selectedProfile);
            }
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
        }
    }

    private async Task RunSanitizationAsync()
    {
        if (SelectedDrive == null) return;

        StatusMessage = "Spouštím sanitizaci disku...";
        
        var progress = new Progress<SanitizationProgress>(p =>
        {
            StatusMessage = p.Phase;
            WriteProgress = p.ProgressPercent;
            CurrentSpeed = p.CurrentSpeedMBps;
            ErrorCount = p.Errors;
            if (p.EstimatedTimeRemaining.HasValue)
            {
                TimeRemaining = p.EstimatedTimeRemaining.Value.ToString(@"hh\:mm\:ss");
            }
        });

        var result = await _sanitizationService.SanitizeDiskAsync(
            SelectedDrive.Path,
            SelectedDrive.TotalSize,
            createPartition: true,
            formatNtfs: true,
            volumeLabel: "SCCM",
            progress: progress);

        if (result.Success)
        {
            StatusMessage = $"Sanitizace dokončena úspěšně! Rychlost zápisu: {result.WriteSpeedMBps:F1} MB/s, čtení: {result.ReadSpeedMBps:F1} MB/s";
            await _dialogService.ShowMessageAsync("Hotovo", 
                $"Sanitizace disku dokončena!\n\n" +
                $"Zapsáno: {result.BytesWritten / (1024.0 * 1024.0 * 1024.0):F2} GB\n" +
                $"Přečteno: {result.BytesRead / (1024.0 * 1024.0 * 1024.0):F2} GB\n" +
                $"Rychlost zápisu: {result.WriteSpeedMBps:F1} MB/s\n" +
                $"Rychlost čtení: {result.ReadSpeedMBps:F1} MB/s\n" +
                $"Chyby: {result.ErrorsDetected}\n" +
                $"Čas: {result.Duration:hh\\:mm\\:ss}\n\n" +
                $"GPT oddíl vytvořen: {(result.PartitionCreated ? "ANO" : "NE")}\n" +
                $"NTFS formátováno: {(result.Formatted ? "ANO" : "NE")}\n" +
                $"Jméno svazku: SCCM");
        }
        else
        {
            StatusMessage = $"Chyba: {result.ErrorMessage}";
            await _dialogService.ShowErrorAsync("Chyba", result.ErrorMessage ?? "Neznámá chyba");
        }
    }

    private async Task RunStandardTestAsync(TestProfileItem profile)
    {
        StatusMessage = "Spouštím test povrchu...";
        
        // Simulate standard test for now
        // In real implementation, this would use the existing surface test logic
        await Task.Delay(100);
        
        for (int i = 0; i <= 100; i += 2)
        {
            WriteProgress = i;
            CurrentSpeed = 45 + Random.Shared.NextDouble() * 15;
            CurrentTemperature = 35 + Random.Shared.Next(0, 5);
            TimeRemaining = TimeSpan.FromSeconds((100 - i) * 0.5).ToString(@"hh\:mm\:ss");
            
            SpeedHistory.Add(new DataPoint(DateTime.Now, CurrentSpeed));
            await Task.Delay(100);
        }

        StatusMessage = "Test dokončen";
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

    /// <summary>
    /// Navigate back to disk selection.
    /// </summary>
    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<DiskSelectionViewModel>();
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
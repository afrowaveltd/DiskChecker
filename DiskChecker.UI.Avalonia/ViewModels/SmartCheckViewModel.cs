using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Full-featured SMART data viewer and disk health monitor.
/// Supports ATA/SATA, NVMe, and SCSI/SAS drives.
/// </summary>
public partial class SmartCheckViewModel : ViewModelBase, INavigableViewModel
{
    private readonly ISmartaProvider _smartaProvider;
    private readonly IDiskDetectionService _diskDetectionService;
    private readonly IQualityCalculator _qualityCalculator;
    private readonly IDialogService _dialogService;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly ISettingsService _settingsService;
    
    private ObservableCollection<DiskStatusCardItem> _disks = new();
    private DiskStatusCardItem? _selectedDisk;
    private bool _isChecking;
    private bool _isLoading;
    private string _statusMessage = "Načítám disky...";
    private SmartaData? _currentSmartData;
    private QualityRating? _currentQuality;
    private ObservableCollection<SmartaAttributeItem> _smartAttributes = new();
    private ObservableCollection<SmartaSelfTestEntry> _selfTestLog = new();
    private ObservableCollection<SmartaAttributeItem> _criticalAttributes = new();
    private string _selectedTestType = "Short";

    public SmartCheckViewModel(
        ISmartaProvider smartaProvider, 
        IDiskDetectionService diskDetectionService,
        IQualityCalculator qualityCalculator, 
        IDialogService dialogService,
        ISelectedDiskService selectedDiskService,
        ISettingsService settingsService)
    {
        _smartaProvider = smartaProvider ?? throw new ArgumentNullException(nameof(smartaProvider));
        _diskDetectionService = diskDetectionService ?? throw new ArgumentNullException(nameof(diskDetectionService));
        _qualityCalculator = qualityCalculator ?? throw new ArgumentNullException(nameof(qualityCalculator));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _selectedDiskService = selectedDiskService ?? throw new ArgumentNullException(nameof(selectedDiskService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        
        LoadDisksCommand = new AsyncRelayCommand(LoadDisksAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => SelectedDisk != null && !IsChecking);
        RunShortTestCommand = new AsyncRelayCommand(RunShortTestAsync, () => SelectedDisk != null && !IsChecking);
        RunLongTestCommand = new AsyncRelayCommand(RunLongTestAsync, () => SelectedDisk != null && !IsChecking);
        AbortTestCommand = new AsyncRelayCommand(AbortTestAsync, () => SelectedDisk != null && !IsChecking);
    }

    #region Properties

    public ObservableCollection<DiskStatusCardItem> Disks
    {
        get => _disks;
        set => SetProperty(ref _disks, value);
    }

    public DiskStatusCardItem? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (SetProperty(ref _selectedDisk, value))
            {
                _ = LoadSmartDataAsync();
            }
        }
    }

    public bool IsChecking
    {
        get => _isChecking;
        set
        {
            if (SetProperty(ref _isChecking, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                RunShortTestCommand.NotifyCanExecuteChanged();
                RunLongTestCommand.NotifyCanExecuteChanged();
                AbortTestCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public SmartaData? CurrentSmartData
    {
        get => _currentSmartData;
        set
        {
            if (SetProperty(ref _currentSmartData, value))
            {
                OnPropertyChanged(nameof(HasData));
                OnPropertyChanged(nameof(HasHealthData));
                UpdateComputedProperties();
            }
        }
    }

    public QualityRating? CurrentQuality
    {
        get => _currentQuality;
        set
        {
            if (SetProperty(ref _currentQuality, value))
            {
                UpdateComputedProperties();
            }
        }
    }

    public ObservableCollection<SmartaAttributeItem> SmartAttributes
    {
        get => _smartAttributes;
        set => SetProperty(ref _smartAttributes, value);
    }

    public ObservableCollection<SmartaAttributeItem> CriticalAttributes
    {
        get => _criticalAttributes;
        set => SetProperty(ref _criticalAttributes, value);
    }

    public ObservableCollection<SmartaSelfTestEntry> SelfTestLog
    {
        get => _selfTestLog;
        set => SetProperty(ref _selfTestLog, value);
    }

    public string SelectedTestType
    {
        get => _selectedTestType;
        set => SetProperty(ref _selectedTestType, value);
    }

    // Computed properties for display
    public bool HasData => CurrentSmartData != null;
    public bool HasHealthData => CurrentSmartData?.IsHealthy == true;
    
    // Safe property accessors with fallbacks
    public string DiskName => SelectedDisk?.DisplayName ?? "Nevybrán žádný disk";
    public string DiskPath => SelectedDisk?.DisplayPath ?? "";
    
    public string DeviceModel => CurrentSmartData?.DeviceModel ?? "Načítám...";
    public string SerialNumber => CurrentSmartData?.SerialNumber ?? "-";
    public string FirmwareVersion => CurrentSmartData?.FirmwareVersion ?? "-";
    public string DeviceType => CurrentSmartData?.DeviceType ?? "Neznámý";
    
    public string Temperature => CurrentSmartData?.Temperature > 0 
        ? $"{CurrentSmartData.Temperature}°C" : "-";
    public string TemperatureStatus => CurrentSmartData?.TemperatureStatus ?? "-";
    public string PowerOnHours => CurrentSmartData?.PowerOnHours > 0 
        ? $"{CurrentSmartData.PowerOnHours:N0} h" : "-";
    public string LifetimeStatus => CurrentSmartData?.LifetimeStatus ?? "-";
    
    public string PowerCycles => CurrentSmartData?.PowerCycleCount > 0 
        ? $"{CurrentSmartData.PowerCycleCount:N0}" : "-";
    
    public string ReallocatedSectors => CurrentSmartData?.ReallocatedSectorCount > 0 
        ? $"{CurrentSmartData.ReallocatedSectorCount:N0}" : "0 ✓";
    public string PendingSectors => CurrentSmartData?.PendingSectorCount > 0 
        ? $"{CurrentSmartData.PendingSectorCount:N0}" : "0 ✓";
    public string UncorrectableErrors => CurrentSmartData?.UncorrectableErrorCount > 0 
        ? $"{CurrentSmartData.UncorrectableErrorCount:N0}" : "0 ✓";
    
    // NVMe specific
    public bool IsNvMe => CurrentSmartData?.DeviceType?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) == true;
    public string NvMePercentageUsed => CurrentSmartData?.PercentageUsed > 0 
        ? $"{CurrentSmartData.PercentageUsed}%" : "-";
    public string NvMeMediaErrors => CurrentSmartData?.MediaErrors > 0 
        ? $"{CurrentSmartData.MediaErrors:N0}" : "0 ✓";
    public string NvMeUnsafeShutdowns => CurrentSmartData?.UnsafeShutdowns > 0 
        ? $"{CurrentSmartData.UnsafeShutdowns:N0}" : "-";
    
    // Quality display
    public string Grade => CurrentQuality?.Grade.ToString() ?? "-";
    public string Score => CurrentQuality?.Score > 0 
        ? $"{CurrentQuality.Score:F0}%" : "-";
    public string HealthStatus => CurrentSmartData?.HealthStatus ?? "-";
    
    // Color helpers
    public string GradeColor => GetGradeColor(CurrentQuality?.Grade);
    public string TempColor => GetTemperatureColor(CurrentSmartData?.Temperature);
    public string HealthColor => GetHealthColor(CurrentQuality?.Score);

    // Test type options for dropdown
    public string[] TestTypes { get; } = new[] { "Short", "Extended", "Conveyance" };

    #endregion

    #region Commands

    public IAsyncRelayCommand LoadDisksCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RunShortTestCommand { get; }
    public IAsyncRelayCommand RunLongTestCommand { get; }
    public IAsyncRelayCommand AbortTestCommand { get; }

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        // Check if a disk was selected from disk selection view
        if (_selectedDiskService.SelectedDisk != null)
        {
            // Create disk card from selected disk
            var card = new DiskStatusCardItem
            {
                Drive = _selectedDiskService.SelectedDisk,
                DisplayName = _selectedDiskService.SelectedDiskDisplayName ?? _selectedDiskService.SelectedDisk.Name ?? "Unknown",
                DisplayPath = _selectedDiskService.SelectedDisk.Path,
                IsLocked = _selectedDiskService.IsSelectedDiskLocked
            };
            
            Disks.Clear();
            Disks.Add(card);
            SelectedDisk = card;
        }
        else
        {
            _ = LoadDisksAsync();
        }
    }

    #endregion

    #region Private Methods

    private async Task LoadDisksAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Načítám seznam disků...";
            
            var drives = await _diskDetectionService.GetDrivesAsync();
            Disks.Clear();
            
            // Identify system disk (usually PhysicalDrive0)
            var systemDiskPath = "\\\\.\\PhysicalDrive0";
            
            foreach (var drive in drives.OrderByDescending(d => d.Path.Contains(systemDiskPath)))
            {
                SmartaData? smartData = null;
                try
                {
                    smartData = await _smartaProvider.GetSmartaDataAsync(drive.Path);
                }
                catch { /* Ignore SMART errors during list load */ }
                
                var quality = smartData != null 
                    ? _qualityCalculator.CalculateQuality(smartData) 
                    : new QualityRating(QualityGrade.F, 0);
                
                var isSystemDisk = drive.Path.Contains(systemDiskPath) || IsSystemDisk(drive);
                var card = new DiskStatusCardItem
                {
                    Drive = drive,
                    DisplayName = !string.IsNullOrEmpty(smartData?.DeviceModel) 
                        ? smartData.DeviceModel.Trim() 
                        : drive.Name ?? "Unknown Drive",
                    DisplayPath = drive.Path,
                    CapacityText = FormatCapacity(drive.TotalSize),
                    GradeText = quality.Grade.ToString(),
                    TemperatureText = smartData?.Temperature > 0 ? $"{smartData.Temperature}°C" : "N/A",
                    SmartData = smartData,
                    Quality = quality,
                    IsSystemDisk = isSystemDisk,
                    IsSystemDiskLabel = isSystemDisk ? "Systémový" : ""
                };
                
                Disks.Add(card);
            }
            
            // Auto-select first non-system disk if available, otherwise first disk
            SelectedDisk = Disks.FirstOrDefault(d => !d.IsSystemDisk) ?? Disks.FirstOrDefault();
            
            StatusMessage = $"Nalezeno {Disks.Count} disků";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst disky: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSmartDataAsync()
    {
        if (SelectedDisk?.Drive == null)
        {
            CurrentSmartData = null;
            CurrentQuality = null;
            SmartAttributes.Clear();
            CriticalAttributes.Clear();
            SelfTestLog.Clear();
            return;
        }

        try
        {
            IsChecking = true;
            StatusMessage = $"Načítám SMART data: {SelectedDisk.DisplayName}";
            
            var smartData = await _smartaProvider.GetSmartaDataAsync(SelectedDisk.Drive.Path);
            
            if (smartData == null)
            {
                StatusMessage = "Nepodařilo se načíst SMART data";
                await _dialogService.ShowErrorAsync("Chyba", 
                    $"Nepodařilo se načíst SMART data pro disk {SelectedDisk.DisplayName}.\n\n" +
                    "Ujistěte se, že:\n" +
                    "1. Aplikace běží s administrátorskými právy\n" +
                    "2. Disk podporuje SMART\n" +
                    "3. je nainstalován smartmontools");
                return;
            }
            
            // Determine device type from SMART data
            if (smartData.Attributes?.Any(a => a.Id == 5) == true && 
                smartData.Attributes.Any(a => a.Id == 194))
            {
                smartData.DeviceType = "SATA/ATA";
            }
            
            CurrentSmartData = smartData;
            CurrentQuality = _qualityCalculator.CalculateQuality(smartData);
            
            // Update disk card
            SelectedDisk.SmartData = smartData;
            SelectedDisk.Quality = CurrentQuality;
            SelectedDisk.TemperatureText = smartData.Temperature > 0 ? $"{smartData.Temperature}°C" : "N/A";
            SelectedDisk.GradeText = CurrentQuality?.Grade.ToString() ?? "?";
            
            // Load attributes
            if (_smartaProvider is IAdvancedSmartaProvider advancedProvider)
            {
                try
                {
                    var attributes = await advancedProvider.GetSmartAttributesAsync(SelectedDisk.Drive.Path);
                    SmartAttributes = new ObservableCollection<SmartaAttributeItem>(attributes);
                    System.Diagnostics.Debug.WriteLine($"Loaded {attributes.Count} SMART attributes");
                    
                    // Extract critical attributes
                    var criticalIds = new[] { 5, 177, 179, 181, 182, 187, 188, 190, 194, 195, 196, 197, 198, 199, 231, 233 };
                    CriticalAttributes = new ObservableCollection<SmartaAttributeItem>(
                        attributes.Where(a => criticalIds.Contains(a.Id))
                            .OrderBy(a => Array.IndexOf(criticalIds, a.Id)));
                    System.Diagnostics.Debug.WriteLine($"Found {CriticalAttributes.Count} critical attributes");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading SMART attributes: {ex.Message}");
                    // Attributes not supported
                }
                
                try
                {
                    var log = await advancedProvider.GetSelfTestLogAsync(SelectedDisk.Drive.Path);
                    SelfTestLog = new ObservableCollection<SmartaSelfTestEntry>(log);
                    
                    // Check for running test
                    var status = await advancedProvider.GetSelfTestStatusAsync(SelectedDisk.Drive.Path);
                    if (status == SmartaSelfTestStatus.InProgress)
                    {
                        CurrentSmartData.SelfTestInProgress = true;
                        StatusMessage = "⚠️ Self-test právě běží...";
                    }
                }
                catch { /* Self-test log not supported */ }
            }
            
            UpdateComputedProperties();
            StatusMessage = $"SMART načten: {CurrentSmartData.HealthStatus}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task RefreshAsync()
    {
        await LoadSmartDataAsync();
    }

    private async Task RunShortTestAsync()
    {
        await RunSelfTestAsync(SmartaSelfTestType.ShortTest, "krátký");
    }

    private async Task RunLongTestAsync()
    {
        await RunSelfTestAsync(SmartaSelfTestType.Extended, "rozšířený");
    }

    private async Task RunSelfTestAsync(SmartaSelfTestType testType, string testName)
    {
        if (SelectedDisk?.Drive == null) return;
        
        if (SelectedDisk.IsSystemDisk)
        {
            await _dialogService.ShowErrorAsync("Upozornění", 
                "Na systémovém disku nelze spustit self-test.\n\nVyberte jiný disk.");
            return;
        }

        var confirmed = await _dialogService.ShowConfirmationAsync("Potvrzení", 
            $"Opravdu chcete spustit {testName} self-test na disku:\n\n{SelectedDisk.DisplayName}\n\n" +
            "Během testu může být disk nedostupný.");
        
        if (!confirmed) return;

        try
        {
            IsChecking = true;
            StatusMessage = $"Spouštím {testName} self-test...";
            
            if (_smartaProvider is IAdvancedSmartaProvider advancedProvider)
            {
                var success = await advancedProvider.StartSelfTestAsync(SelectedDisk.Drive.Path, testType);
                
                if (success)
                {
                    StatusMessage = $"✅ {testName.Capitalize()} self-test spuštěn";
                    await _dialogService.ShowMessageAsync("Self-Test", 
                        $"{testName.Capitalize()} self-test byl úspěšně spuštěn.\n\n" +
                        "Výsledek zkontrolujte obnovením dat za několik minut.");
                }
                else
                {
                    StatusMessage = "❌ Nepodařilo se spustit self-test";
                    await _dialogService.ShowErrorAsync("Chyba", 
                        "Nepodařilo se spustit self-test. Ujistěte se, že disk podporuje SMART.");
                }
            }
            else
            {
                await _dialogService.ShowErrorAsync("Nepodporováno", 
                    "Self-test není podporován na tomto systému.\n\n" +
                    "Ujistěte se, že je nainstalován smartmontools.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", ex.Message);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task AbortTestAsync()
    {
        if (SelectedDisk?.Drive == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync("Potvrzení", 
            "Opravdu chcete přerušit běžící self-test?");
        
        if (!confirmed) return;

        try
        {
            IsChecking = true;
            
            if (_smartaProvider is IAdvancedSmartaProvider advancedProvider)
            {
                var success = await advancedProvider.StartSelfTestAsync(SelectedDisk.Drive.Path, SmartaSelfTestType.Abort);
                StatusMessage = success ? "✅ Self-test přerušen" : "❌ Nepodařilo se přerušit test";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void UpdateComputedProperties()
    {
        OnPropertyChanged(nameof(DeviceModel));
        OnPropertyChanged(nameof(SerialNumber));
        OnPropertyChanged(nameof(FirmwareVersion));
        OnPropertyChanged(nameof(DeviceType));
        OnPropertyChanged(nameof(Temperature));
        OnPropertyChanged(nameof(TemperatureStatus));
        OnPropertyChanged(nameof(PowerOnHours));
        OnPropertyChanged(nameof(LifetimeStatus));
        OnPropertyChanged(nameof(PowerCycles));
        OnPropertyChanged(nameof(Grade));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(HealthStatus));
        OnPropertyChanged(nameof(GradeColor));
        OnPropertyChanged(nameof(TempColor));
        OnPropertyChanged(nameof(HealthColor));
        OnPropertyChanged(nameof(IsNvMe));
        OnPropertyChanged(nameof(NvMePercentageUsed));
        OnPropertyChanged(nameof(NvMeMediaErrors));
        OnPropertyChanged(nameof(NvMeUnsafeShutdowns));
    }

    private static bool IsSystemDisk(CoreDriveInfo drive)
    {
        // PhysicalDrive0 is usually the system disk on Windows
        return drive.Path.Contains("PhysicalDrive0") || 
               drive.IsPhysical && drive.Path.Contains('0');
    }

    private static string FormatCapacity(long bytes)
    {
        if (bytes <= 0) return "Unknown";
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        if (gb >= 1000) return $"{gb / 1024.0:F1} TB";
        return $"{gb:F0} GB";
    }

    private static string GetGradeColor(QualityGrade? grade)
    {
        return grade switch
        {
            QualityGrade.A => "#27AE60",
            QualityGrade.B => "#2ECC71",
            QualityGrade.C => "#F39C12",
            QualityGrade.D => "#E67E22",
            QualityGrade.E => "#E74C3C",
            QualityGrade.F => "#C0392B",
            _ => "#95A5A6"
        };
    }

    private static string GetTemperatureColor(int? temp)
    {
        if (temp == null) return "#888888";
        return temp switch
        {
            < 40 => "#27AE60",
            < 50 => "#2ECC71",
            < 60 => "#F39C12",
            < 70 => "#E67E22",
            _ => "#E74C3C"
        };
    }

    private static string GetHealthColor(double? score)
    {
        if (score == null) return "#888888";
        return score switch
        {
            >= 90 => "#27AE60",
            >= 80 => "#2ECC71",
            >= 70 => "#F1C40F",
            >= 60 => "#E67E22",
            >= 50 => "#E74C3C",
            _ => "#C0392B"
        };
    }

    #endregion
}

public static class StringExtensions
{
    public static string Capitalize(this string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
    }
}
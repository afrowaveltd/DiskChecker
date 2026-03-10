using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// View model for disk selection view.
/// Displays list of available disks with their health status.
/// </summary>
public partial class DiskSelectionViewModel : ViewModelBase, INavigableViewModel
{
    private readonly INavigationService _navigationService;
    private readonly IDiskDetectionService _diskDetectionService;
    private readonly ISmartaProvider _smartaProvider;
    private readonly IQualityCalculator _qualityCalculator;
    private readonly ISettingsService _settingsService;
    private readonly ISelectedDiskService _selectedDiskService;
    
    private string _loadingState = "Načítám disky...";
    private bool _isBusy = true;
    private string _statusMessage = "Připraven";
    private CoreDriveInfo? _selectedDrive;
    
    // System disk quick report properties
    private string _systemDiskName = "Načítám...";
    private string _systemDiskGrade = "?";
    private string _systemDiskTemperature = "N/A";
    private string _systemDiskSummary = "Načítám data...";

    /// <summary>
    /// Initializes a new instance of the DiskSelectionViewModel.
    /// </summary>
    public DiskSelectionViewModel(
        INavigationService navigationService, 
        IDiskDetectionService diskDetectionService,
        ISmartaProvider smartaProvider, 
        IQualityCalculator qualityCalculator,
        ISettingsService settingsService,
        ISelectedDiskService selectedDiskService)
    {
        _navigationService = navigationService;
        _diskDetectionService = diskDetectionService;
        _smartaProvider = smartaProvider;
        _qualityCalculator = qualityCalculator;
        _settingsService = settingsService;
        _selectedDiskService = selectedDiskService;
        DiskCards = new ObservableCollection<DiskStatusCardItem>();
        RecentTests = new ObservableCollection<TestHistoryItem>();
    }

    /// <summary>
    /// Collection of disk cards to display.
    /// </summary>
    public ObservableCollection<DiskStatusCardItem> DiskCards { get; }

    /// <summary>
    /// Collection of recent tests.
    /// </summary>
    public ObservableCollection<TestHistoryItem> RecentTests { get; }

    /// <summary>
    /// Current loading state message.
    /// </summary>
    public string LoadingState
    {
        get => _loadingState;
        set => SetProperty(ref _loadingState, value);
    }

    /// <summary>
    /// Whether the view is currently busy loading.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
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
    /// Currently selected drive.
    /// </summary>
    public CoreDriveInfo? SelectedDrive
    {
        get => _selectedDrive;
        set => SetProperty(ref _selectedDrive, value);
    }

    /// <summary>
    /// System disk display name for quick report.
    /// </summary>
    public string SystemDiskName
    {
        get => _systemDiskName;
        set => SetProperty(ref _systemDiskName, value);
    }

    /// <summary>
    /// System disk SMART grade for quick report.
    /// </summary>
    public string SystemDiskGrade
    {
        get => _systemDiskGrade;
        set => SetProperty(ref _systemDiskGrade, value);
    }

    /// <summary>
    /// System disk temperature for quick report.
    /// </summary>
    public string SystemDiskTemperature
    {
        get => _systemDiskTemperature;
        set => SetProperty(ref _systemDiskTemperature, value);
    }

    /// <summary>
    /// System disk health summary for quick report.
    /// </summary>
    public string SystemDiskSummary
    {
        get => _systemDiskSummary;
        set => SetProperty(ref _systemDiskSummary, value);
    }

    /// <inheritdoc/>
    public void OnNavigatedTo()
    {
        // Load data when navigated to
        Task.Run(LoadDataAsync);
    }

        private async Task LoadDataAsync()
    {
        try
        {
            IsBusy = true;
            LoadingState = "NaÄŤĂ­tĂˇm disky...";

            // Get list of locked disks from settings
            var lockedDisks = await _settingsService.GetLockedDisksAsync();

            // Get list of drives from detection service
            var drives = await _diskDetectionService.GetDrivesAsync();
            
            // Clear existing items
            DiskCards.Clear();
            
            // Load each drive and build disk cards
            foreach (var drive in drives)
            {
                try
                {
                    await LoadDriveAsync(drive, lockedDisks);
                }
                catch (Exception)
                {
                    // If we can't get SMART data, add the drive with basic info
                    AddBasicDriveCard(drive, lockedDisks);
                }
            }
            
            // SINGLE SOURCE OF TRUTH: Find system disk from the already-loaded disk cards
            // The disk card with IsSystemDisk = true is the system disk - use it for quick report
            var systemDiskCard = DiskCards.FirstOrDefault(c => c.IsSystemDisk);
            if (systemDiskCard != null)
            {
                UpdateSystemDiskReport(systemDiskCard.Drive, systemDiskCard.SmartData);
            }
            else
            {
                // No system disk found
                UpdateSystemDiskReport(null, null);
            }

            LoadingState = DiskCards.Count > 0 ? "Disky naÄŤteny" : "Ĺ˝ĂˇdnĂ© disky nalezeny";
            StatusMessage = $"Nalezeno {DiskCards.Count} diskĹŻ";
        }
        catch (Exception ex)
        {
            LoadingState = $"Chyba: {ex.Message}";
            StatusMessage = "NepodaĹ™ilo se naÄŤĂ­st disky";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<SmartaData?> LoadDriveAsync(CoreDriveInfo drive, List<string> lockedDisks)
    {
        // Get SMART data for the drive
        var smartData = await _smartaProvider.GetSmartaDataAsync(drive.Path);
        
        // Calculate quality rating
        var quality = smartData != null 
            ? _qualityCalculator.CalculateQuality(smartData) 
            : new QualityRating(QualityGrade.F, 0);
        
        // Format capacity
        var capacityText = FormatCapacity(drive.TotalSize);
        
        // Format temperature (handle nullable Temperature)
        var temperatureText = smartData?.Temperature.HasValue == true && smartData.Temperature.Value > 0 
            ? $"{smartData.Temperature.Value}°C" 
            : "N/A";
        
        // Use ONLY the IsSystemDisk property set by DiskDetectionService
        // Do NOT use PhysicalDrive0 fallback - that can incorrectly match USB disks
        var isSystemDisk = drive.IsSystemDisk;
        
        // Check if disk is locked
        var isLocked = lockedDisks.Any(p => IsSameDiskk(p, drive.Path)) || isSystemDisk;
        
        var card = new DiskStatusCardItem
        {
            Drive = drive,
            DisplayName = !string.IsNullOrEmpty(smartData?.DeviceModel) 
                ? smartData.DeviceModel 
                : drive.Name ?? "Unknown",
            DisplayPath = drive.Path,
            CapacityText = capacityText,
            GradeText = quality.Grade.ToString(),
            TemperatureText = temperatureText,
            SmartData = smartData,
            Quality = quality,
            IsSystemDisk = isSystemDisk,
            IsSystemDiskLabel = isSystemDisk ? "Systémový disk" : "",
            IsLocked = isLocked
        };
        
        DiskCards.Add(card);
        
        return smartData;
    }
    
    private static bool IsSameDiskk(string identifier1, string identifier2)
    {
        if (string.IsNullOrEmpty(identifier1) || string.IsNullOrEmpty(identifier2)) return false;
        
        // Direct match
        if (string.Equals(identifier1, identifier2, StringComparison.OrdinalIgnoreCase)) return true;
        
        // Extract drive number from path
        var num1 = ExtractDriveNumber(identifier1);
        var num2 = ExtractDriveNumber(identifier2);
        
        if (num1.HasValue && num2.HasValue)
        {
            return num1.Value == num2.Value;
        }
        
        return false;
    }
    
    private static int? ExtractDriveNumber(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var digits = new string(path.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var num))
        {
            return num;
        }
        return null;
    }

    private void AddBasicDriveCard(CoreDriveInfo drive, List<string> lockedDisks)
    {
        // Use ONLY the IsSystemDisk property set by DiskDetectionService
        // Do NOT use PhysicalDrive0 fallback - that can incorrectly match USB disks
        var isSystemDisk = drive.IsSystemDisk;
        var isLocked = lockedDisks.Any(p => IsSameDiskk(p, drive.Path)) || isSystemDisk;
        
        DiskCards.Add(new DiskStatusCardItem
        {
            Drive = drive,
            DisplayName = drive.Name ?? "Unknown",
            DisplayPath = drive.Path,
            CapacityText = FormatCapacity(drive.TotalSize),
            GradeText = "?",
            TemperatureText = "N/A",
            IsSystemDisk = isSystemDisk,
            IsSystemDiskLabel = isSystemDisk ? "Systémový disk" : "",
            IsLocked = isLocked
        });
    }

    private void UpdateSystemDiskReport(CoreDriveInfo? systemDisk, SmartaData? smartData)
    {
        if (systemDisk == null)
        {
            SystemDiskName = "Systémový disk nenalezen";
            SystemDiskGrade = "?";
            SystemDiskTemperature = "N/A";
            SystemDiskSummary = "Nelze najít systémový disk";
            return;
        }

        SystemDiskName = !string.IsNullOrEmpty(smartData?.DeviceModel)
            ? smartData.DeviceModel
            : systemDisk.Name ?? "Neznámý disk";

        if (smartData != null)
        {
            var quality = _qualityCalculator.CalculateQuality(smartData);
            SystemDiskGrade = quality.Grade.ToString();
            SystemDiskTemperature = smartData.Temperature.HasValue && smartData.Temperature.Value > 0
                ? $"{smartData.Temperature.Value}°C"
                : "N/A";
            
            // Build summary
            var summaryParts = new System.Collections.Generic.List<string>();
            if (smartData.PowerOnHours > 0)
                summaryParts.Add($"Hodiny: {smartData.PowerOnHours}h");
            if (smartData.ReallocatedSectorCount > 0)
                summaryParts.Add($"Reallocated: {smartData.ReallocatedSectorCount}");
            if (smartData.PendingSectorCount > 0)
                summaryParts.Add($"Pending: {smartData.PendingSectorCount}");
            
            SystemDiskSummary = summaryParts.Count > 0
                ? string.Join(" | ", summaryParts)
                : "Stav disku: OK";
        }
        else
        {
            SystemDiskGrade = "?";
            SystemDiskTemperature = "N/A";
            SystemDiskSummary = "SMART data nedostupná";
        }
    }
    
    private static string FormatCapacity(long bytes)
    {
        if (bytes <= 0) return "Unknown";
        
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        if (gb >= 1000)
        {
            return $"{gb / 1024.0:F1} TB";
        }
        return $"{gb:F0} GB";
    }

    /// <summary>
    /// Reload the list of disks.
    /// </summary>
    [RelayCommand]
    private async Task ReloadDisks()
    {
        DiskCards.Clear();
        await LoadDataAsync();
    }

    /// <summary>
    /// Select a disk card and navigate to tests.
    /// </summary>
    [RelayCommand]
    private void SelectDisk(DiskStatusCardItem card)
    {
        // Deselect all cards
        foreach (var c in DiskCards)
        {
            c.IsSelected = false;
        }

        // Select clicked card
        card.IsSelected = true;
        SelectedDrive = card.Drive;
        
        // Store selected disk for navigation
        _selectedDiskService.SelectedDisk = card.Drive;
        _selectedDiskService.SelectedDiskDisplayName = card.DisplayName;
        _selectedDiskService.IsSelectedDiskLocked = card.IsLocked;
        
        StatusMessage = $"Vybrán disk: {card.DisplayName}";
        
        // Navigate to SMART Check view
        _navigationService.NavigateTo<SmartCheckViewModel>();
    }

    /// <summary>
    /// Toggle disk lock status.
    /// </summary>
    [RelayCommand]
    private async Task ToggleLockDisk(DiskStatusCardItem card)
    {
        if (card?.Drive == null) return;

        var diskPath = card.Drive.Path;

        if (card.IsLocked)
        {
            // Unlock - but warn for system disk
            if (card.IsSystemDisk)
            {
                StatusMessage = "Systémový disk nelze odemknout - je chráněn automaticky";
                return;
            }
            await _settingsService.UnlockDiskAsync(diskPath);
            card.IsLocked = false;
            StatusMessage = $"Disk {card.DisplayName} odemčen";
        }
        else
        {
            // Lock
            await _settingsService.LockDiskAsync(diskPath);
            card.IsLocked = true;
            StatusMessage = $"Disk {card.DisplayName} zamčen 🔒";
        }
    }

    /// <summary>
    /// Lock a disk.
    /// </summary>
    [RelayCommand]
    private async Task LockDisk(DiskStatusCardItem card)
    {
        if (card?.Drive == null) return;
        await _settingsService.LockDiskAsync(card.Drive.Path);
        card.IsLocked = true;
        StatusMessage = $"Disk {card.DisplayName} zamčen 🔒";
    }

    /// <summary>
    /// Unlock a disk.
    /// </summary>
    [RelayCommand]
    private async Task UnlockDisk(DiskStatusCardItem card)
    {
        if (card?.Drive == null) return;
        
        if (card.IsSystemDisk)
        {
            StatusMessage = "Systémový disk nelze odemknout";
            return;
        }
        
        await _settingsService.UnlockDiskAsync(card.Drive.Path);
        card.IsLocked = false;
        StatusMessage = $"Disk {card.DisplayName} odemčen";
    }
}
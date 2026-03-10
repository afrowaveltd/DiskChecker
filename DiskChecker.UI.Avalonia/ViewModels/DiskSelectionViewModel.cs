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
    
    private string _loadingState = "Načítám disky...";
    private bool _isBusy = true;
    private string _statusMessage = "Připraven";
    private CoreDriveInfo? _selectedDrive;

    /// <summary>
    /// Initializes a new instance of the DiskSelectionViewModel.
    /// </summary>
    public DiskSelectionViewModel(
        INavigationService navigationService, 
        IDiskDetectionService diskDetectionService,
        ISmartaProvider smartaProvider, 
        IQualityCalculator qualityCalculator)
    {
        _navigationService = navigationService;
        _diskDetectionService = diskDetectionService;
        _smartaProvider = smartaProvider;
        _qualityCalculator = qualityCalculator;
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
            LoadingState = "Načítám disky...";

            // Get list of drives from detection service
            var drives = await _diskDetectionService.GetDrivesAsync();
            
            // Clear existing items
            DiskCards.Clear();
            
            // Load each drive
            foreach (var drive in drives)
            {
                try
                {
                    await LoadDriveAsync(drive);
                }
                catch (Exception)
                {
                    // If we can't get SMART data, add the drive with basic info
                    AddBasicDriveCard(drive);
                }
            }

            LoadingState = DiskCards.Count > 0 ? "Disky načteny" : "Žádné disky nenalezeny";
            StatusMessage = $"Nalezeno {DiskCards.Count} disků";
        }
        catch (Exception ex)
        {
            LoadingState = $"Chyba: {ex.Message}";
            StatusMessage = "Nepodařilo se načíst disky";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDriveAsync(CoreDriveInfo drive)
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
        
        // Determine if this is likely the system disk
        var isSystemDisk = drive.Path.Contains('0') || (drive.Name != null && drive.Name.Contains("C:"));
        
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
            IsSystemDiskLabel = isSystemDisk ? "Systémový disk" : ""
        };
        
        DiskCards.Add(card);
    }

    private void AddBasicDriveCard(CoreDriveInfo drive)
    {
        DiskCards.Add(new DiskStatusCardItem
        {
            Drive = drive,
            DisplayName = drive.Name ?? "Unknown",
            DisplayPath = drive.Path,
            CapacityText = FormatCapacity(drive.TotalSize),
            GradeText = "?",
            TemperatureText = "N/A",
            IsSystemDisk = drive.Path.Contains('0')
        });
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
    /// Select a disk card.
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
        StatusMessage = $"Vybrán disk: {card.DisplayName}";
    }
}
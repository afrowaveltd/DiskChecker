using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;

using DiskChecker.Application.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

public partial class DiskSelectionViewModel : ViewModelBase, INavigableViewModel
{
    private readonly INavigationService _navigationService;
    private readonly ISmartaProvider _smartaProvider;
    private readonly IQualityCalculator _qualityCalculator;
    private string _loadingState = "Načítám disky...";
    private bool _isBusy = true;
    private string _statusMessage = "Připraven";
    private CoreDriveInfo? _selectedDrive;

    public DiskSelectionViewModel(INavigationService navigationService, ISmartaProvider smartaProvider, IQualityCalculator qualityCalculator)
    {
        _navigationService = navigationService;
        _smartaProvider = smartaProvider;
        _qualityCalculator = qualityCalculator;
        DiskCards = new ObservableCollection<DiskStatusCardItem>();
        RecentTests = new ObservableCollection<TestHistoryItem>();
    }

    public ObservableCollection<DiskStatusCardItem> DiskCards { get; }
    public ObservableCollection<TestHistoryItem> RecentTests { get; }

    public string LoadingState
    {
        get => _loadingState;
        set => SetProperty(ref _loadingState, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public CoreDriveInfo? SelectedDrive
    {
        get => _selectedDrive;
        set => SetProperty(ref _selectedDrive, value);
    }

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

            // Get list of drives from SMART provider
            var drives = await _smartaProvider.ListDrivesAsync();
            
            // Clear existing items
            DiskCards.Clear();
            
            // Load each drive
            foreach (var drive in drives)
            {
                try
                {
                    // Get SMART data for the drive
                    var smartData = await _smartaProvider.GetSmartaDataAsync(drive.Path);
                    
                    // Calculate quality rating
                    var quality = smartData != null 
                        ? _qualityCalculator.CalculateQuality(smartData) 
                        : new QualityRating { Grade = QualityGrade.F, Score = 0 };
                    
                    // Format capacity
                    var capacityText = FormatCapacity(drive.TotalSize);
                    
                    // Format temperature
                    var temperatureText = smartData?.Temperature > 0 
                        ? $"{smartData.Temperature}°C" 
                        : "N/A";
                    
                    // Determine if this is likely the system disk (by checking if it has Windows installed)
                    var isSystemDisk = drive.Path.Contains('0'); // PhysicalDrive0 is usually system
                    
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
                catch (Exception)
                {
                    // If we can't get SMART data, at least add the drive with basic info
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

    [RelayCommand]
    private async Task ReloadDisks()
    {
        DiskCards.Clear();
        await LoadDataAsync();
    }

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

public class DiskStatusCardItem : ObservableObject
{
    private bool _isSelected;
    private CoreDriveInfo? _drive;
    private SmartaData? _smartData;
    private QualityRating? _quality;

    public string DisplayName { get; set; } = string.Empty;
    public string DisplayPath { get; set; } = string.Empty;
    public string CapacityText { get; set; } = string.Empty;
    public string GradeText { get; set; } = string.Empty;
    public string TemperatureText { get; set; } = string.Empty;
    public bool IsSystemDisk { get; set; }
    public string IsSystemDiskLabel { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public CoreDriveInfo? Drive
    {
        get => _drive;
        set => SetProperty(ref _drive, value);
    }

    public SmartaData? SmartData
    {
        get => _smartData;
        set => SetProperty(ref _smartData, value);
    }

    public QualityRating? Quality
    {
        get => _quality;
        set => SetProperty(ref _quality, value);
    }
}
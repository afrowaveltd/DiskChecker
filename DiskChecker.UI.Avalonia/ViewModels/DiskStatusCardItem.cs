using CommunityToolkit.Mvvm.ComponentModel;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Represents a disk status card item for display in the UI.
/// </summary>
public class DiskStatusCardItem : ObservableObject
{
        private bool _isLoading;
        private string? _errorMessage;
    private bool _isSelected;
    private bool _isLocked;
    private CoreDriveInfo? _drive;
    private SmartaData? _smartData;
    private QualityRating? _quality;

    /// <summary>
    /// Display name of the disk (model or generic name).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Path to the disk device.
    /// </summary>
    public string DisplayPath { get; set; } = string.Empty;

    /// <summary>
    /// Formatted capacity text (e.g., "500 GB").
    /// </summary>
    public string CapacityText { get; set; } = string.Empty;

    /// <summary>
    /// Quality grade text (A-F).
    /// </summary>
    public string GradeText { get; set; } = string.Empty;

    /// <summary>
    /// Formatted temperature text (e.g., "42°C").
    /// </summary>
    public string TemperatureText { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the system disk.
    /// </summary>
    public bool IsSystemDisk { get; set; }

    /// <summary>
    /// Label for system disk (e.g., "Systémový disk").
    /// </summary>
    public string IsSystemDiskLabel { get; set; } = string.Empty;

    /// <summary>
    /// Lock symbol to display (🔒 if locked, empty if not).
    /// </summary>
    public string LockSymbol => IsLocked ? "🔒" : "";

    /// <summary>
    /// Status text for locked disk.
    /// </summary>
    public string LockStatusText => IsLocked ? "Zamčeno - destruktivní operace blokovány" : "";

    /// <summary>
    /// Whether this card is currently selected.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Whether this card is currently loading SMART data.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// Error message if SMART probe failed for this device.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>
    /// Whether this disk is locked (protected from destructive operations).
    /// </summary>
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (SetProperty(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(LockSymbol));
                OnPropertyChanged(nameof(LockStatusText));
            }
        }
    }

    /// <summary>
    /// The drive information.
    /// </summary>
    public CoreDriveInfo? Drive
    {
        get => _drive;
        set
        {
            if (SetProperty(ref _drive, value))
            {
                // Update derived volume properties when drive changes
                if (_drive != null && _drive.Volumes != null && _drive.Volumes.Count > 0)
                {
                    VolumesCount = _drive.Volumes.Count;
                    VolumesSummary = string.Join(", ", _drive.Volumes.Select(v =>
                        {
                            var name = string.IsNullOrEmpty(v.Name) ? v.Path : v.Name;
                            // prefer drive letter if available
                            if (!string.IsNullOrEmpty(v.Path) && v.Path.Length >= 2 && v.Path[1] == ':')
                                return v.Path.TrimEnd('\\');
                            return name;
                        })) ;
                }
                else
                {
                    VolumesCount = 0;
                    VolumesSummary = string.Empty;
                }
                OnPropertyChanged(nameof(VolumesCount));
                OnPropertyChanged(nameof(VolumesSummary));
            }
        }
    }

    private int _volumesCount;
    private string _volumesSummary = string.Empty;

    /// <summary>
    /// Number of logical volumes associated with this drive.
    /// </summary>
    public int VolumesCount
    {
        get => _volumesCount;
        set => SetProperty(ref _volumesCount, value);
    }

    /// <summary>
    /// Short summary of logical volumes (e.g., "C:\, D:\").
    /// </summary>
    public string VolumesSummary
    {
        get => _volumesSummary;
        set => SetProperty(ref _volumesSummary, value);
    }

    /// <summary>
    /// SMART data for the drive.
    /// </summary>
    public SmartaData? SmartData
    {
        get => _smartData;
        set => SetProperty(ref _smartData, value);
    }

    /// <summary>
    /// Quality rating based on SMART data.
    /// </summary>
    public QualityRating? Quality
    {
        get => _quality;
        set => SetProperty(ref _quality, value);
    }
}
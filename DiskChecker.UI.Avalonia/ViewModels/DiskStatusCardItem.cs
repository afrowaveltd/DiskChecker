using CommunityToolkit.Mvvm.ComponentModel;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Represents a disk status card item for display in the UI.
/// </summary>
public class DiskStatusCardItem : ObservableObject
{
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
        set => SetProperty(ref _drive, value);
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
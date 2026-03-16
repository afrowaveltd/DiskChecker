using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Represents a disk card item for display in the UI.
/// </summary>
public partial class DiskStatusCardItem : ViewModelBase
{
    private bool _isSelected;
    private bool _isLocked;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    
    /// <summary>
    /// The underlying drive information.
    /// </summary>
    public CoreDriveInfo Drive { get; set; } = null!;
    
    /// <summary>
    /// Display name for the disk (model name).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Display path (/dev/sda or \\.\PhysicalDrive0).
    /// </summary>
    public string DisplayPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Formatted capacity text (e.g., "500 GB").
    /// </summary>
    public string CapacityText { get; set; } = string.Empty;
    
    /// <summary>
    /// SMART grade letter (A-F).
    /// </summary>
    public string GradeText { get; set; } = "?";
    
    /// <summary>
    /// Formatted temperature text (e.g., "45°C").
    /// </summary>
    public string TemperatureText { get; set; } = "N/A";
    
    /// <summary>
    /// SMART data for this disk.
    /// </summary>
    public SmartaData? SmartData { get; set; }
    
    /// <summary>
    /// Quality rating calculated from SMART data.
    /// </summary>
    public QualityRating Quality { get; set; } = new QualityRating(QualityGrade.F, 0);
    
    /// <summary>
    /// Whether this disk is the system disk.
    /// </summary>
    public bool IsSystemDisk { get; set; }
    
    /// <summary>
    /// System disk label text.
    /// </summary>
    public string IsSystemDiskLabel { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this disk card is selected.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
    
    /// <summary>
    /// Whether this disk is locked (cannot be tested).
    /// </summary>
    public bool IsLocked
    {
        get => _isLocked;
        set => SetProperty(ref _isLocked, value);
    }
    
    /// <summary>
    /// Lock status explanation text.
    /// </summary>
    public string LockStatusText { get; set; } = string.Empty;
    
    /// <summary>
    /// Serial number of the disk.
    /// </summary>
    public string? SerialNumber { get; set; }
    
    /// <summary>
    /// Interface type (SATA, NVMe, USB).
    /// </summary>
    public string Interface { get; set; } = string.Empty;
    
    /// <summary>
    /// Formatted list of partitions/volumes for display.
    /// </summary>
    public string PartitionsDisplay { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of partitions.
    /// </summary>
    public int PartitionCount { get; set; }
    
    /// <summary>
    /// Whether the disk has any mounted partitions.
    /// </summary>
    public bool HasMountPoints => !string.IsNullOrEmpty(PartitionsDisplay);
    
    /// <summary>
    /// Health status summary (OK, Warning, Critical).
    /// </summary>
    public string HealthStatus { get; set; } = "Unknown";
    
    /// <summary>
    /// Health status color for UI.
    /// </summary>
    public string HealthStatusColor
    {
        get
        {
            return HealthStatus switch
            {
                "OK" => "#27AE60",
                "Warning" => "#F39C12",
                "Critical" => "#E74C3C",
                _ => "#6C757D"
            };
        }
    }
    
    /// <summary>
    /// Whether the disk is currently loading data.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
    
    /// <summary>
    /// Error message if loading failed.
    /// </summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }
    
    /// <summary>
    /// Bus type display text.
    /// </summary>
    public string BusTypeDisplay
    {
        get
        {
            if (SmartData != null && !string.IsNullOrEmpty(SmartData.DeviceModel))
            {
                // Detect from device model
                var model = SmartData.DeviceModel.ToLowerInvariant();
                if (model.Contains("nvme")) return "NVMe";
                if (model.Contains("usb")) return "USB";
            }
            
            return Drive?.BusType switch
            {
                CoreBusType.Nvme => "NVMe",
                CoreBusType.Sata => "SATA",
                CoreBusType.Usb => "USB",
                CoreBusType.Sas => "SAS",
                CoreBusType.Ide => "IDE",
                CoreBusType.Scsi => "SCSI",
                CoreBusType.Virtual => "Virtual",
                _ => "SATA"
            };
        }
    }
    
    /// <summary>
    /// Whether this disk has test history (disk card exists).
    /// </summary>
    public bool HasTestHistory { get; set; }
    
    /// <summary>
    /// Information about the latest test session for this disk.
    /// </summary>
    public TestSession? LatestTest { get; set; }
    
    /// <summary>
    /// Date of the last test for display.
    /// </summary>
    public string LastTestedDate => LatestTest?.TestedAt.ToString("dd.MM.yyyy HH:mm") ?? "Neznámý";
    
    /// <summary>
    /// Type of the last test.
    /// </summary>
    public string LastTestType => LatestTest?.TestType.ToString() ?? "N/A";
    
    /// <summary>
    /// Result/grade of the last test.
    /// </summary>
    public string LastTestGrade => LatestTest?.Grade ?? "?";
}
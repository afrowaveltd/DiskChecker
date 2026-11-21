using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Implementation of selected disk service for sharing between views.
/// </summary>
public class SelectedDiskService : ISelectedDiskService
{
    private CoreDriveInfo? _selectedDisk;
    public CoreDriveInfo? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            // Reset contextual selections only when the physical drive changes.
            // Recreating CoreDriveInfo for the SAME card during navigation must
            // not silently erase the card/session/certificate identity.
            bool sameDrive = _selectedDisk != null && value != null &&
                (!string.IsNullOrWhiteSpace(_selectedDisk.SerialNumber) &&
                 !string.IsNullOrWhiteSpace(value.SerialNumber)
                    ? string.Equals(_selectedDisk.SerialNumber, value.SerialNumber,
                        System.StringComparison.OrdinalIgnoreCase)
                    : string.Equals(_selectedDisk.Path, value.Path,
                        System.StringComparison.OrdinalIgnoreCase) &&
                      _selectedDisk.TotalSize == value.TotalSize &&
                      string.Equals(_selectedDisk.Name, value.Name,
                        System.StringComparison.OrdinalIgnoreCase));
            if (!sameDrive)
            {
                SelectedDiskCardId = null;
                SelectedTestSessionId = null;
                SelectedCertificateId = null;
            }

            _selectedDisk = value;
        }
    }
    public int? SelectedDiskCardId { get; set; }
    public string? SelectedDiskDisplayName { get; set; }
    public bool IsSelectedDiskLocked { get; set; }
    public int? SelectedTestSessionId { get; set; }
    public int? SelectedCertificateId { get; set; }
}
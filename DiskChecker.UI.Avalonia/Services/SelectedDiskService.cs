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
            _selectedDisk = value;
            // A different physical-disk selection must not inherit a card-list identity.
            SelectedDiskCardId = null;
        }
    }
    public int? SelectedDiskCardId { get; set; }
    public string? SelectedDiskDisplayName { get; set; }
    public bool IsSelectedDiskLocked { get; set; }
    public int? SelectedTestSessionId { get; set; }
    public int? SelectedCertificateId { get; set; }
}
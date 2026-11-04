using DiskChecker.Core.Models;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Shares the selected disk between screens (terminal counterpart of the desktop
/// <c>ISelectedDiskService</c>). Selecting a different physical disk clears the
/// persisted card identity, exactly like the desktop implementation.
/// </summary>
public interface ITuiSelection
{
    CoreDriveInfo? SelectedDisk { get; set; }

    string? SelectedDiskDisplayName { get; set; }

    bool IsSelectedDiskLocked { get; set; }

    int? SelectedDiskCardId { get; set; }

    int? SelectedTestSessionId { get; set; }

    int? SelectedCertificateId { get; set; }
}

/// <inheritdoc />
public sealed class TuiSelection : ITuiSelection
{
    private CoreDriveInfo? _selectedDisk;

    public CoreDriveInfo? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            _selectedDisk = value;
            SelectedDiskCardId = null;
        }
    }

    public string? SelectedDiskDisplayName { get; set; }

    public bool IsSelectedDiskLocked { get; set; }

    public int? SelectedDiskCardId { get; set; }

    public int? SelectedTestSessionId { get; set; }

    public int? SelectedCertificateId { get; set; }
}

using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.Services.Interfaces;

/// <summary>
/// Service for sharing selected disk between views.
/// </summary>
public interface ISelectedDiskService
{
    /// <summary>
    /// Gets or sets the currently selected disk.
    /// </summary>
    CoreDriveInfo? SelectedDisk { get; set; }
    
    /// <summary>
    /// Gets or sets the display name for the selected disk.
    /// </summary>
    string? SelectedDiskDisplayName { get; set; }
    
    /// <summary>
    /// Gets or sets whether the selected disk is locked.
    /// </summary>
    bool IsSelectedDiskLocked { get; set; }
}
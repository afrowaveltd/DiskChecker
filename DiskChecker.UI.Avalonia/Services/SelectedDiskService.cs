using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Implementation of selected disk service for sharing between views.
/// </summary>
public class SelectedDiskService : ISelectedDiskService
{
    public CoreDriveInfo? SelectedDisk { get; set; }
    public string? SelectedDiskDisplayName { get; set; }
    public bool IsSelectedDiskLocked { get; set; }
}
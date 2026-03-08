using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.Services.Interfaces;

/// <summary>
/// Service for managing test history.
/// </summary>
public interface IHistoryService
{
    /// <summary>
    /// Get all test history records.
    /// </summary>
    Task<IEnumerable<HistoricalTest>> GetHistoryAsync();
    
    /// <summary>
    /// Get history for a specific disk.
    /// </summary>
    Task<IEnumerable<HistoricalTest>> GetHistoryForDiskAsync(string serialNumber);
    
    /// <summary>
    /// Delete a history record.
    /// </summary>
    Task DeleteHistoryAsync(Guid testId);
    
    /// <summary>
    /// Clear all history.
    /// </summary>
    Task ClearHistoryAsync();
}

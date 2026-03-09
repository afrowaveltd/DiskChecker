using System.ComponentModel.DataAnnotations;

namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// Database record for replication queue.
/// </summary>
public class ReplicationQueueRecord
{
    public Guid Id { get; set; }
    
    public Guid TestId { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? ProcessedAt { get; set; }
    
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";
    
    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }
}
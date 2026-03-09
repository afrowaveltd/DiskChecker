namespace DiskChecker.Core.Models;

/// <summary>
/// SMART self-test report with status and history.
/// </summary>
public class SmartaSelfTestReport
{
    /// <summary>
    /// Test type that was requested.
    /// </summary>
    public SmartaSelfTestType RequestedTestType { get; set; }
    
    /// <summary>
    /// Whether the test has completed.
    /// </summary>
    public bool Completed { get; set; }
    
    /// <summary>
    /// Whether the test passed.
    /// </summary>
    public bool Passed { get; set; }
    
    /// <summary>
    /// Human-readable summary of the test result.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    
    /// <summary>
    /// When the test was started.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }
    
    /// <summary>
    /// When the test finished.
    /// </summary>
    public DateTime FinishedAtUtc { get; set; }
    
    /// <summary>
    /// Recent self-test log entries.
    /// </summary>
    public IReadOnlyList<SmartaSelfTestEntry> RecentEntries { get; set; } = new List<SmartaSelfTestEntry>();
}
namespace DiskChecker.TUI.Models;

/// <summary>
/// Represents a speed measurement sample during disk testing.
/// </summary>
public sealed class SpeedSample
{
    public double PositionPercent { get; init; }
    public double SpeedMBps { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

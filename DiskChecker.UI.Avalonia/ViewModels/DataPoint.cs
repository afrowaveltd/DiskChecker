using System;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Represents a data point for graph plotting.
/// </summary>
public class DataPoint
{
    /// <summary>
    /// Initializes a new instance of the DataPoint class.
    /// </summary>
    /// <param name="timestamp">The timestamp of the data point.</param>
    /// <param name="value">The value of the data point.</param>
    public DataPoint(DateTime timestamp, double value)
    {
        Timestamp = timestamp;
        Value = value;
    }

    /// <summary>
    /// Gets the timestamp of the data point.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets the value of the data point.
    /// </summary>
    public double Value { get; }
}
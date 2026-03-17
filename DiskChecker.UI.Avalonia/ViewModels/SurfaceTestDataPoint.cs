using System;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Represents a data point for surface test graph.
/// </summary>
public class SurfaceTestDataPoint
{
    /// <summary>
    /// Gets the timestamp of the data point.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets the elapsed time from test start.
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Gets the elapsed time in seconds.
    /// </summary>
    public double ElapsedSeconds => Elapsed.TotalSeconds;

    /// <summary>
    /// Gets the elapsed time formatted as mm:ss.
    /// </summary>
    public string ElapsedFormatted => $"{(int)Elapsed.TotalMinutes:D2}:{Elapsed.Seconds:D2}";

    /// <summary>
    /// Gets the speed value (MB/s).
    /// </summary>
    public double Speed { get; }

    /// <summary>
    /// Gets the disk temperature at this point.
    /// </summary>
    public int? Temperature { get; }

    /// <summary>
    /// Gets the test phase (0 = Write, 1 = Read).
    /// </summary>
    public int Phase { get; }

    /// <summary>
    /// Gets the phase name.
    /// </summary>
    public string PhaseName => Phase == 0 ? "Zápis" : "Čtení";

    /// <summary>
    /// Gets the data progress percentage (0-100).
    /// </summary>
    public double DataPercent { get; }

    /// <summary>
    /// Gets the height for display (normalized to MaxSpeed).
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public SurfaceTestDataPoint(DateTime timestamp, TimeSpan elapsed, double speed, int? temperature, int phase, double dataPercent = 0)
    {
        Timestamp = timestamp;
        Elapsed = elapsed;
        Speed = speed;
        Temperature = temperature;
        Phase = phase;
        DataPercent = dataPercent;
    }
}

/// <summary>
/// Represents temperature data point for graph.
/// </summary>
public class TemperatureDataPoint
{
    public DateTime Timestamp { get; }
    public TimeSpan Elapsed { get; }
    public double ElapsedSeconds => Elapsed.TotalSeconds;
    public string ElapsedFormatted => $"{(int)Elapsed.TotalMinutes:D2}:{Elapsed.Seconds:D2}";
    public int Temperature { get; }
    public double Height { get; set; }

    public TemperatureDataPoint(DateTime timestamp, TimeSpan elapsed, int temperature)
    {
        Timestamp = timestamp;
        Elapsed = elapsed;
        Temperature = temperature;
    }
}

/// <summary>
/// Zoom level for the graph time axis.
/// </summary>
public class GraphZoomLevel
{
    public string Name { get; }
    public TimeSpan Duration { get; }

    public GraphZoomLevel(string name, TimeSpan duration)
    {
        Name = name;
        Duration = duration;
    }

    public override string ToString() => Name;

    public static GraphZoomLevel[] DefaultZoomLevels => new[]
    {
        new GraphZoomLevel("1 min", TimeSpan.FromMinutes(1)),
        new GraphZoomLevel("5 min", TimeSpan.FromMinutes(5)),
        new GraphZoomLevel("15 min", TimeSpan.FromMinutes(15)),
        new GraphZoomLevel("30 min", TimeSpan.FromMinutes(30)),
        new GraphZoomLevel("1 hod", TimeSpan.FromHours(1)),
        new GraphZoomLevel("Veškerý čas", TimeSpan.MaxValue)
    };
}
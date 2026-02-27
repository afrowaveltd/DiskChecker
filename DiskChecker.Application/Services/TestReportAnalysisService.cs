using DiskChecker.Core.Models;
using System.Text;

namespace DiskChecker.Application.Services;

/// <summary>
/// Analyzes test results and provides comprehensive reporting with anomaly detection and grading.
/// </summary>
public class TestReportAnalysisService
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Detailed analytics for a test result.
    /// </summary>
    public class TestAnalytics
    {
        /// <summary>
        /// Overall health grade (A+, A, B+, B, C, D, F).
        /// </summary>
        public string Grade { get; set; } = "F";

        /// <summary>
        /// Numeric score (0-100).
        /// </summary>
        public int Score { get; set; }

        /// <summary>
        /// Realistic min speed (after filtering anomalies).
        /// </summary>
        public double FilteredMinSpeedMbps { get; set; }

        /// <summary>
        /// Realistic max speed (after filtering anomalies).
        /// </summary>
        public double FilteredMaxSpeedMbps { get; set; }

        /// <summary>
        /// Standard deviation of samples.
        /// </summary>
        public double SpeedStdDev { get; set; }

        /// <summary>
        /// Percentage of samples that are anomalies.
        /// </summary>
        public double AnomalyPercentage { get; set; }

        /// <summary>
        /// Health assessment text.
        /// </summary>
        public string HealthAssessment { get; set; } = "";

        /// <summary>
        /// Test duration.
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Identification of anomalies detected.
        /// </summary>
        public List<string> DetectedAnomalies { get; set; } = new();
    }

    /// <summary>
    /// Analyzes test result and produces detailed analytics.
    /// </summary>
    public TestAnalytics AnalyzeResult(SurfaceTestResult result)
    {
        var analytics = new TestAnalytics
        {
            Duration = result.CompletedAtUtc - result.StartedAtUtc
        };

        // Detect and filter anomalies
        var (validSamples, anomalies) = FilterAnomalies(result.Samples);
        analytics.DetectedAnomalies = anomalies;

        // Calculate statistics on valid samples
        if (validSamples.Any())
        {
            analytics.FilteredMinSpeedMbps = validSamples.Min(s => s.ThroughputMbps);
            analytics.FilteredMaxSpeedMbps = validSamples.Max(s => s.ThroughputMbps);
            analytics.SpeedStdDev = CalculateStdDev(validSamples.Select(s => s.ThroughputMbps).ToList());
            analytics.AnomalyPercentage = result.Samples.Any() 
                ? (result.Samples.Count - validSamples.Count) * 100.0 / result.Samples.Count 
                : 0;
        }

        // Calculate grade
        analytics.Score = CalculateScore(result, analytics);
        analytics.Grade = ScoreToGrade(analytics.Score);
        analytics.HealthAssessment = GenerateHealthAssessment(result, analytics);

        return analytics;
    }

    /// <summary>
    /// Filters out anomalies like zero speeds, cache spikes, and outliers.
    /// Uses statistical methods to identify unrealistic values.
    /// </summary>
    private (List<SurfaceTestSample> valid, List<string> anomalies) FilterAnomalies(List<SurfaceTestSample> samples)
    {
        var anomalies = new List<string>();
        var valid = new List<SurfaceTestSample>();

        if (!samples.Any())
            return (valid, anomalies);

        // Remove zero samples
        var nonZero = samples.Where(s => s.ThroughputMbps > 0).ToList();
        if (nonZero.Count < samples.Count)
        {
            anomalies.Add($"Removed {samples.Count - nonZero.Count} zero-speed samples (likely I/O delays)");
        }

        if (!nonZero.Any())
            return (valid, anomalies);

        // Calculate statistics for outlier detection
        var speeds = nonZero.Select(s => s.ThroughputMbps).ToList();
        var mean = speeds.Average();
        var stdDev = CalculateStdDev(speeds);

        // Use 2 standard deviations + IQR for outlier detection
        var q1 = speeds.OrderBy(x => x).ElementAt(speeds.Count / 4);
        var q3 = speeds.OrderBy(x => x).ElementAt((speeds.Count * 3) / 4);
        var iqr = q3 - q1;
        var upperBound = q3 + (1.5 * iqr);
        var lowerBound = Math.Max(0, q1 - (1.5 * iqr));

        var cacheSpikes = 0;
        var outliers = 0;

        foreach (var sample in nonZero)
        {
            // Detect cache spikes (>50% faster than mean)
            if (sample.ThroughputMbps > mean * 1.5)
            {
                cacheSpikes++;
                continue;
            }

            // Detect outliers outside IQR bounds
            if (sample.ThroughputMbps > upperBound || sample.ThroughputMbps < lowerBound)
            {
                outliers++;
                continue;
            }

            valid.Add(sample);
        }

        if (cacheSpikes > 0)
            anomalies.Add($"Detected {cacheSpikes} cache spike samples (excluded from min/max)");
        if (outliers > 0)
            anomalies.Add($"Detected {outliers} statistical outliers (excluded from analysis)");

        return (valid, anomalies);
    }

    /// <summary>
    /// Calculates standard deviation of speed samples.
    /// </summary>
    private double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumOfSquares / values.Count);
    }

    /// <summary>
    /// Calculates overall score (0-100) based on test results.
    /// </summary>
    private int CalculateScore(SurfaceTestResult result, TestAnalytics analytics)
    {
        int score = 100;

        // Penalize for errors
        if (result.ErrorCount > 0)
            score = Math.Max(0, score - (result.ErrorCount * 10));

        // Penalize for high anomaly percentage
        if (analytics.AnomalyPercentage > 20)
            score -= 15;
        else if (analytics.AnomalyPercentage > 10)
            score -= 5;

        // Penalize for high speed variance
        if (analytics.SpeedStdDev > 0 && analytics.FilteredMaxSpeedMbps > 0)
        {
            var variance = (analytics.SpeedStdDev / analytics.FilteredMaxSpeedMbps) * 100;
            if (variance > 50)
                score -= 20;
            else if (variance > 30)
                score -= 10;
            else if (variance > 15)
                score -= 5;
        }

        // Bonus for zero errors and stable performance
        if (result.ErrorCount == 0 && analytics.AnomalyPercentage < 5)
            score = Math.Min(100, score + 5);

        return Math.Max(0, score);
    }

    /// <summary>
    /// Converts numeric score to letter grade.
    /// </summary>
    private string ScoreToGrade(int score) => score switch
    {
        >= 95 => "A+",
        >= 90 => "A",
        >= 85 => "B+",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        _ => "F"
    };

    /// <summary>
    /// Generates human-readable health assessment.
    /// </summary>
    private string GenerateHealthAssessment(SurfaceTestResult result, TestAnalytics analytics) => analytics.Score switch
    {
        >= 95 => "✅ EXCELLENT: Disk performs perfectly with no anomalies detected. Ready for production.",
        >= 90 => "✅ VERY GOOD: Disk performs well with minimal issues. Safe to use.",
        >= 85 => "✅ GOOD: Disk is healthy with minor speed variations. Generally acceptable.",
        >= 80 => "⚠️ ACCEPTABLE: Some issues detected, but disk is still usable. Monitor closely.",
        >= 70 => "⚠️ QUESTIONABLE: Significant performance issues or errors detected. Use caution.",
        >= 60 => "❌ POOR: Serious issues detected. Not recommended for critical data.",
        _ => "❌ CRITICAL: Disk has critical issues. Replacement strongly recommended."
    };

    /// <summary>
    /// Generates a detailed terminal-friendly report.
    /// </summary>
    public string GenerateTerminalReport(SurfaceTestResult result, TestAnalytics analytics)
    {
        var sb = new StringBuilder();

        sb.AppendLine("╔════════════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║                            DISK TEST REPORT                                   ║");
        sb.AppendLine("╚════════════════════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();

        // Overall Grade
        sb.AppendLine($"📊 OVERALL GRADE: [{analytics.Grade}]  Score: {analytics.Score}/100");
        sb.AppendLine($"   {analytics.HealthAssessment}");
        sb.AppendLine();

        // Disk Information
        sb.AppendLine("╔─ DISK INFORMATION ─────────────────────────────────────────────────────────────╗");
        sb.AppendLine($"║ Model:              {result.DriveModel,-65}║");
        sb.AppendLine($"║ Serial Number:      {result.DriveSerialNumber ?? "N/A",-65}║");
        sb.AppendLine($"║ Manufacturer:       {result.DriveManufacturer ?? "N/A",-65}║");
        sb.AppendLine($"║ Interface:          {result.DriveInterface ?? "N/A",-65}║");
        sb.AppendLine($"║ Capacity:           {FormatBytes(result.DriveTotalBytes),-65}║");
        sb.AppendLine($"║ Temperature:        {(result.CurrentTemperatureCelsius.HasValue ? $"{result.CurrentTemperatureCelsius}°C" : "N/A"),-65}║");
        sb.AppendLine($"║ Power-On Hours:     {(result.PowerOnHours.HasValue ? $"{result.PowerOnHours} hours" : "N/A"),-65}║");
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();

        // Performance Metrics
        sb.AppendLine("╔─ PERFORMANCE METRICS ──────────────────────────────────────────────────────────╗");
        sb.AppendLine($"║ Test Duration:      {analytics.Duration.ToString("hh\\:mm\\:ss"),-65}║");
        sb.AppendLine($"║ Total Tested:       {FormatBytes(result.TotalBytesTested),-65}║");
        sb.AppendLine($"║ Average Speed:      {result.AverageSpeedMbps:F2} MB/s {GetSpeedBar(result.AverageSpeedMbps, analytics.FilteredMaxSpeedMbps),-35}║");
        sb.AppendLine($"║ Peak Speed:         {result.PeakSpeedMbps:F2} MB/s (may include cache)");
        sb.AppendLine($"║ Realistic Peak:     {analytics.FilteredMaxSpeedMbps:F2} MB/s (cache filtered)");
        sb.AppendLine($"║ Realistic Min:      {analytics.FilteredMinSpeedMbps:F2} MB/s {GetSpeedBar(analytics.FilteredMinSpeedMbps, analytics.FilteredMaxSpeedMbps),-35}║");
        sb.AppendLine($"║ Speed Variance:     ±{analytics.SpeedStdDev:F2} MB/s (σ)");
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();

        // Reliability
        sb.AppendLine("╔─ RELIABILITY ──────────────────────────────────────────────────────────────────╗");
        sb.AppendLine($"║ Errors Found:       {result.ErrorCount} {(result.ErrorCount == 0 ? "✅" : "❌"),-60}║");
        sb.AppendLine($"║ Anomalies:          {analytics.AnomalyPercentage:F1}% of samples filtered");
        sb.AppendLine($"║ Test Quality:       {(analytics.AnomalyPercentage < 5 ? "✅ Excellent" : analytics.AnomalyPercentage < 15 ? "⚠️ Good" : "❌ Poor"),-65}║");
        
        if (result.ReallocatedSectors.HasValue && result.ReallocatedSectors > 0)
            sb.AppendLine($"║ ⚠️  Reallocated:    {result.ReallocatedSectors} sectors (disk wear detected)");
        
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();

        // Detected Issues
        if (analytics.DetectedAnomalies.Any())
        {
            sb.AppendLine("╔─ ANALYSIS NOTES ───────────────────────────────────────────────────────────────╗");
            foreach (var anomaly in analytics.DetectedAnomalies)
            {
                sb.AppendLine($"║ • {anomaly,-74}║");
            }
            sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
            sb.AppendLine();
        }

        // Recommendations
        sb.AppendLine("╔─ RECOMMENDATIONS ──────────────────────────────────────────────────────────────╗");
        if (analytics.Score >= 90)
        {
            sb.AppendLine("║ ✅ Disk is suitable for production use and data storage.                       ║");
        }
        else if (analytics.Score >= 80)
        {
            sb.AppendLine("║ ⚠️  Disk is usable but monitor for issues. Keep regular backups.               ║");
        }
        else if (analytics.Score >= 70)
        {
            sb.AppendLine("║ ⚠️  Disk has issues. Consider replacing soon. Avoid critical data.            ║");
        }
        else
        {
            sb.AppendLine("║ ❌ Disk is unreliable. Replacement strongly recommended immediately.         ║");
        }
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();

        // Footer
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Test ID: {result.TestId})");

        return sb.ToString();
    }

    /// <summary>
    /// Generates exportable JSON report.
    /// </summary>
    public string GenerateJsonReport(SurfaceTestResult result, TestAnalytics analytics)
    {
        var report = new
        {
            testId = result.TestId,
            timestamp = DateTime.Now,
            testDuration = analytics.Duration.ToString("hh\\:mm\\:ss"),
            grade = analytics.Grade,
            score = analytics.Score,
            disk = new
            {
                model = result.DriveModel,
                serialNumber = result.DriveSerialNumber,
                manufacturer = result.DriveManufacturer,
                interface_ = result.DriveInterface,
                capacityBytes = result.DriveTotalBytes,
                temperatureCelsius = result.CurrentTemperatureCelsius,
                powerOnHours = result.PowerOnHours,
                reallocatedSectors = result.ReallocatedSectors
            },
            performance = new
            {
                totalBytesTested = result.TotalBytesTested,
                averageSpeedMbps = Math.Round(result.AverageSpeedMbps, 2),
                peakSpeedMbps = Math.Round(result.PeakSpeedMbps, 2),
                minSpeedMbps = Math.Round(result.MinSpeedMbps, 2),
                filteredMaxSpeedMbps = Math.Round(analytics.FilteredMaxSpeedMbps, 2),
                filteredMinSpeedMbps = Math.Round(analytics.FilteredMinSpeedMbps, 2),
                speedVarianceMbps = Math.Round(analytics.SpeedStdDev, 2)
            },
            reliability = new
            {
                errorCount = result.ErrorCount,
                anomalyPercentage = Math.Round(analytics.AnomalyPercentage, 2),
                detectedAnomalies = analytics.DetectedAnomalies
            },
            healthAssessment = analytics.HealthAssessment,
            samples = result.Samples.Select(s => new
            {
                offsetBytes = s.OffsetBytes,
                blockSizeBytes = s.BlockSizeBytes,
                throughputMbps = Math.Round(s.ThroughputMbps, 2),
                timestampUtc = s.TimestampUtc,
                errorCount = s.ErrorCount
            }).ToList()
        };

        return System.Text.Json.JsonSerializer.Serialize(report, JsonOptions);
    }

    /// <summary>
    /// Generates CSV export for graphing and analysis.
    /// </summary>
    public string GenerateCsvReport(SurfaceTestResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TimeSeconds,OffsetBytes,ThroughputMbps,ErrorCount");

        var startTime = result.Samples.FirstOrDefault()?.TimestampUtc ?? result.StartedAtUtc;

        foreach (var sample in result.Samples)
        {
            var timeSeconds = (sample.TimestampUtc - startTime).TotalSeconds;
            sb.AppendLine($"{timeSeconds:F2},{sample.OffsetBytes},{sample.ThroughputMbps:F2},{sample.ErrorCount}");
        }

        return sb.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static string GetSpeedBar(double current, double max, int width = 15)
    {
        if (max <= 0) return "";
        int filled = (int)((current / max) * width);
        return "[" + new string('█', filled) + new string('░', width - filled) + "]";
    }
}

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
        >= 95 => "A",  // Changed from "A+"
        >= 90 => "A",
        >= 85 => "B",  // Changed from "B+"
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
        >= 95 => "✅ VYNIKAJÍCÍ: Disk funguje dokonale bez anomálií. Připraven na produkci.",
        >= 90 => "✅ VELMI DOBRÝ: Disk funguje dobře s minimálními problémy. Bezpečný k použití.",
        >= 85 => "✅ DOBRÝ: Disk je zdravý s malými variacemi rychlosti. Obecně přijatelný.",
        >= 80 => "⚠️ PŘIJATELNÝ: Zjištěny některé problémy, disk je stále použitelný. Monitorujte.",
        >= 70 => "⚠️ POCHYBNÝ: Zjištěny významné problémy s výkonem. Opatrně.",
        >= 60 => "❌ ŠPATNÝ: Zjištěny vážné problémy. Nedoporučuje se pro kritická data.",
        _ => "❌ KRITICKÝ: Disk má kritické problémy. Okamžitá výměna."
    };

    /// <summary>
    /// Generates a detailed terminal-friendly report.
    /// </summary>
    public string GenerateTerminalReport(SurfaceTestResult result, TestAnalytics analytics)
    {
        var sb = new StringBuilder();

        sb.AppendLine("╔════════════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║                      ZPRÁVA O TESTU DISKU                                    ║");
        sb.AppendLine("╚════════════════════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();

        // Overall Grade
        sb.AppendLine($"📊 CELKOVÉ HODNOCENÍ: [{analytics.Grade}]  Skóre: {analytics.Score}/100");
        sb.AppendLine($"   {analytics.HealthAssessment}");
        sb.AppendLine();

        // Disk Information
        sb.AppendLine("╔─ INFORMACE O DISKU ────────────────────────────────────────────────────────────╗");
        sb.AppendLine($"║ Model:              {SafeFormat(result.DriveModel, 65)}║");
        sb.AppendLine($"║ Sériové číslo:      {SafeFormat(result.DriveSerialNumber ?? "N/A", 65)}║");
        sb.AppendLine($"║ Výrobce:            {SafeFormat(result.DriveManufacturer ?? "N/A", 65)}║");
        sb.AppendLine($"║ Rozhraní:           {SafeFormat(result.DriveInterface ?? "N/A", 65)}║");
        sb.AppendLine($"║ Kapacita:           {SafeFormat(FormatBytes(result.DriveTotalBytes), 65)}║");
        sb.AppendLine($"║ Teplota:            {SafeFormat(result.CurrentTemperatureCelsius.HasValue ? $"{result.CurrentTemperatureCelsius}°C" : "N/A", 65)}║");
        sb.AppendLine($"║ Provozní hodiny:    {SafeFormat(result.PowerOnHours.HasValue ? $"{result.PowerOnHours} h" : "N/A", 65)}║");
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();

        // Performance Metrics
        sb.AppendLine("╔─ METRIKY VÝKONU ──────────────────────────────────────────────────────────────╗");
        sb.AppendLine($"║ Doba testu:         {SafeFormat(analytics.Duration.ToString("hh\\:mm\\:ss"), 65)}║");
        sb.AppendLine($"║ Celkem testováno:   {SafeFormat(FormatBytes(result.TotalBytesTested), 65)}║");
        sb.AppendLine($"║ Průměrná rychlost:  {SafeFormat($"{result.AverageSpeedMbps:F2} MB/s", 65)}║");
        sb.AppendLine($"║ Maximální rychlost: {SafeFormat($"{result.PeakSpeedMbps:F2} MB/s (včetně cache)", 65)}║");
        sb.AppendLine($"║ Reálná max:         {SafeFormat($"{analytics.FilteredMaxSpeedMbps:F2} MB/s (bez cache)", 65)}║");
        sb.AppendLine($"║ Reálná min:         {SafeFormat($"{analytics.FilteredMinSpeedMbps:F2} MB/s", 65)}║");
        sb.AppendLine($"║ Variance rychlosti: {SafeFormat($"±{analytics.SpeedStdDev:F2} MB/s", 65)}║");
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();

        // Reliability
        sb.AppendLine("╔─ SPOLEHLIVOST ─────────────────────────────────────────────────────────────────╗");
        sb.AppendLine($"║ Nalezené chyby:     {SafeFormat(result.ErrorCount == 0 ? "0 ✅" : $"{result.ErrorCount} ❌", 65)}║");
        sb.AppendLine($"║ Anomálie:           {SafeFormat($"{analytics.AnomalyPercentage:F1}% vzorků filtrováno", 65)}║");
        sb.AppendLine($"║ Kvalita testu:      {SafeFormat(analytics.AnomalyPercentage < 5 ? "✅ Vynikající" : analytics.AnomalyPercentage < 15 ? "⚠️ Dobrá" : "❌ Slabá", 65)}║");
        
        if (result.ReallocatedSectors.HasValue && result.ReallocatedSectors > 0)
            sb.AppendLine($"║ ⚠️ PŘEMÍSTĚNÉ SEKTORY: {result.ReallocatedSectors} (detekováno opotřebení)");
        
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();

        // Detected Issues
        if (analytics.DetectedAnomalies.Any())
        {
            sb.AppendLine("╔─ POZNAMENANÁ ZJIŠTĚNÍ ────────────────────────────────────────────────────────╗");
            foreach (var anomaly in analytics.DetectedAnomalies)
            {
                sb.AppendLine($"║ • {SafeFormat(anomaly, 74)}║");
            }
            sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
            sb.AppendLine();
        }

        // Recommendations
        sb.AppendLine("╔─ DOPORUČENÍ ───────────────────────────────────────────────────────────────────╗");
        if (analytics.Score >= 90)
        {
            sb.AppendLine("║ ✅ Disk je vhodný pro produkční použití a ukládání dat.                       ║");
        }
        else if (analytics.Score >= 80)
        {
            sb.AppendLine("║ ⚠️ Disk je použitelný, ale monitorujte problémy. Pravidelně zálohujte.      ║");
        }
        else if (analytics.Score >= 70)
        {
            sb.AppendLine("║ ⚠️ Disk má problémy. Zvažte výměnu. Nepoužívejte pro kritická data.         ║");
        }
        else
        {
            sb.AppendLine("║ ❌ Disk je nespolehlivý. Doporučuje se okamžitá výměna.                    ║");
        }
        sb.AppendLine("╚──────────────────────────────────────────────────────────────────────────────────╝");
        sb.AppendLine();

        // Footer
        sb.AppendLine($"Vygenerováno: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (ID testu: {result.TestId})");

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

    /// <summary>
    /// Helper to safely format strings for fixed-width report columns.
    /// </summary>
    private static string SafeFormat(string? text, int width)
    {
        if (string.IsNullOrEmpty(text))
            text = "N/A";
        
        if (text.Length > width)
            return text[..(width - 3)] + "...";
        
        return text.PadRight(width);
    }
}

using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Calculates quality rating based on SMART data.
/// </summary>
public class QualityCalculator : IQualityCalculator
{
    /// <summary>
    /// Calculates a quality rating from SMART data.
    /// </summary>
    /// <param name="smartaData">SMART data to evaluate.</param>
    /// <returns>Quality rating with grade and score.</returns>
    public QualityRating CalculateQuality(SmartaData smartaData)
    {
        ArgumentNullException.ThrowIfNull(smartaData);

        var warnings = new List<string>();
        double score = 100.0;

        // Penalize for reallocated sectors
        if (smartaData.ReallocatedSectorCount.HasValue && smartaData.ReallocatedSectorCount.Value > 0)
        {
            var penalty = Math.Min(30, smartaData.ReallocatedSectorCount.Value * 0.5);
            score -= penalty;
            warnings.Add($"Reallocated sectors: {smartaData.ReallocatedSectorCount.Value}");
        }

        // Penalize for pending sectors
        if (smartaData.PendingSectorCount.HasValue && smartaData.PendingSectorCount.Value > 0)
        {
            var penalty = Math.Min(25, smartaData.PendingSectorCount.Value * 0.3);
            score -= penalty;
            warnings.Add($"Pending sectors: {smartaData.PendingSectorCount.Value}");
        }

        // Penalize for uncorrectable errors
        if (smartaData.UncorrectableErrorCount.HasValue && smartaData.UncorrectableErrorCount.Value > 0)
        {
            var penalty = Math.Min(20, smartaData.UncorrectableErrorCount.Value * 0.2);
            score -= penalty;
            warnings.Add($"Uncorrectable errors: {smartaData.UncorrectableErrorCount.Value}");
        }

        // Penalize for high temperature
        if (smartaData.Temperature.HasValue && smartaData.Temperature.Value > 50)
        {
            var penalty = Math.Min(10, (smartaData.Temperature.Value - 50) * 0.5);
            score -= penalty;
            warnings.Add($"High temperature: {smartaData.Temperature.Value}°C");
        }

        // Penalize for low wear leveling count (SSD)
        if (smartaData.WearLevelingCount.HasValue && smartaData.WearLevelingCount.Value < 20)
        {
            var penalty = (20 - smartaData.WearLevelingCount.Value) * 2;
            score -= penalty;
            warnings.Add($"Low wear leveling: {smartaData.WearLevelingCount.Value}%");
        }

        // Clamp score to valid range
        score = Math.Max(0, Math.Min(100, score));

        // Determine grade
        var grade = score switch
        {
            >= 90 => QualityGrade.A,
            >= 80 => QualityGrade.B,
            >= 70 => QualityGrade.C,
            >= 60 => QualityGrade.D,
            _ => QualityGrade.F
        };

        return new QualityRating(grade, score) { Warnings = warnings };
    }
}
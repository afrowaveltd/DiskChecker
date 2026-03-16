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
        var score = 100.0;

        if (smartaData.ReallocatedSectorCount is > 0)
        {
            var penalty = Math.Min(35, smartaData.ReallocatedSectorCount.Value * 0.8);
            score -= penalty;
            warnings.Add($"Reallocated sectors: {smartaData.ReallocatedSectorCount.Value}");
        }

        if (smartaData.PendingSectorCount is > 0)
        {
            var penalty = Math.Min(30, smartaData.PendingSectorCount.Value * 1.2);
            score -= penalty;
            warnings.Add($"Pending sectors: {smartaData.PendingSectorCount.Value}");
        }

        if (smartaData.UncorrectableErrorCount is > 0)
        {
            var penalty = Math.Min(24, smartaData.UncorrectableErrorCount.Value * 0.8);
            score -= penalty;
            warnings.Add($"Uncorrectable errors: {smartaData.UncorrectableErrorCount.Value}");
        }

        if (smartaData.Temperature is > 50)
        {
            var over = smartaData.Temperature.Value - 50;
            var penalty = over <= 10
                ? over * 1.0
                : 10 + ((over - 10) * 1.8);

            score -= Math.Min(22, penalty);
            warnings.Add($"High temperature: {smartaData.Temperature.Value}°C");
        }

        if (smartaData.WearLevelingCount is < 20)
        {
            var penalty = (20 - smartaData.WearLevelingCount.Value) * 1.8;
            score -= Math.Min(18, penalty);
            warnings.Add($"Low wear leveling: {smartaData.WearLevelingCount.Value}%");
        }

        if (smartaData.AvailableSpare is < 10)
        {
            var penalty = (10 - smartaData.AvailableSpare.Value) * 2.0;
            score -= Math.Min(20, penalty);
            warnings.Add($"NVMe available spare low: {smartaData.AvailableSpare.Value}%");
        }

        if (smartaData.PercentageUsed is > 90)
        {
            var penalty = (smartaData.PercentageUsed.Value - 90) * 1.8;
            score -= Math.Min(18, penalty);
            warnings.Add($"NVMe wear high: {smartaData.PercentageUsed.Value}% used");
        }

        if (smartaData.MediaErrors is > 0)
        {
            var penalty = Math.Min(16, smartaData.MediaErrors.Value * 0.5);
            score -= penalty;
            warnings.Add($"NVMe media errors: {smartaData.MediaErrors.Value}");
        }

        if (smartaData.UnsafeShutdowns is > 200)
        {
            score -= 4;
            warnings.Add($"Frequent unsafe shutdowns: {smartaData.UnsafeShutdowns.Value}");
        }

        if (smartaData.PowerOnHours is > 40000)
        {
            score -= 6;
            warnings.Add($"High power-on time: {smartaData.PowerOnHours.Value} h");
        }

        if (!smartaData.IsHealthy)
        {
            score -= 12;
            warnings.Add("Drive self-reported SMART health issue");
        }

        score = Math.Clamp(score, 0, 100);

        var grade = score switch
        {
            >= 92 => QualityGrade.A,
            >= 84 => QualityGrade.B,
            >= 74 => QualityGrade.C,
            >= 62 => QualityGrade.D,
            >= 50 => QualityGrade.E,
            _ => QualityGrade.F
        };

        return new QualityRating(grade, score) { Warnings = warnings };
    }
}
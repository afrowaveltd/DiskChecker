using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

public class QualityCalculator : IQualityCalculator
{
    public QualityRating CalculateQuality(SmartaData smartaData)
    {
        double score = 100;
        var warnings = new List<string>();

        score -= CalculateReallocatedSectorsScore(smartaData.ReallocatedSectorCount, warnings);
        score -= CalculatePendingSectorsScore(smartaData.PendingSectorCount, warnings);
        score -= CalculatePowerOnHoursScore(smartaData.PowerOnHours, warnings);
        score -= CalculateTemperatureScore(smartaData.Temperature, warnings);
        
        if (smartaData.UncorrectableErrorCount > 0)
        {
            score -= Math.Min(20, smartaData.UncorrectableErrorCount * 5);
            warnings.Add($"Počet neopravitelných chyb: {smartaData.UncorrectableErrorCount}");
        }

        score = Math.Max(0, Math.Min(100, Math.Round(score, 2)));

        // Return enum based on score
        if (score >= 95) return QualityRating.APlus;
        if (score >= 85) return QualityRating.A;
        if (score >= 75) return QualityRating.B;
        if (score >= 65) return QualityRating.C;
        if (score >= 55) return QualityRating.D;
        if (score >= 40) return QualityRating.E;
        return QualityRating.F;
    }

    private double CalculateReallocatedSectorsScore(long count, List<string> warnings)
    {
        if (count == 0) return 0;
        
        var penalty = Math.Min(40, count * 2);
        warnings.Add($"Reallocated sectors: {count}");
        return penalty;
    }

    private double CalculatePendingSectorsScore(long count, List<string> warnings)
    {
        if (count == 0) return 0;
        
        var penalty = Math.Min(30, count * 3);
        warnings.Add($"Pending sectors: {count}");
        return penalty;
    }

    private double CalculatePowerOnHoursScore(int hours, List<string> warnings)
    {
        if (hours <= 1000) return 0;
        
        double penalty;
        if (hours < 20000) penalty = (hours - 1000) * 0.01;
        else if (hours < 40000) penalty = 190 + (hours - 20000) * 0.02;
        else penalty = 190 + 400 + (hours - 40000) * 0.03;

        warnings.Add($"Power on hours: {hours}");
        return Math.Min(30, penalty);
    }

    private double CalculateTemperatureScore(double temp, List<string> warnings)
    {
        if (temp <= 40) return 0;
        
        double penalty;
        if (temp < 60) penalty = (temp - 40) * 0.5;
        else penalty = 10 + (temp - 60) * 1.0;

        warnings.Add($"Temperature: {temp}°C");
        return Math.Min(15, penalty);
    }
}

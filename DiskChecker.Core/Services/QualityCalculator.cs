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

        QualityGrade grade;
        if (score >= 90) grade = QualityGrade.A;
        else if (score >= 80) grade = QualityGrade.B;
        else if (score >= 70) grade = QualityGrade.C;
        else if (score >= 60) grade = QualityGrade.D;
        else if (score >= 50) grade = QualityGrade.E;
        else grade = QualityGrade.F;

        return new QualityRating
        {
            Grade = grade,
            Score = score,
            Warnings = warnings
        };
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

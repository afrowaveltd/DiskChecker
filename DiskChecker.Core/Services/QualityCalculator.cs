using System.Collections.Generic;
using System.Linq;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Calculates quality rating based on SMART data with media-specific weighting.
/// </summary>
public class QualityCalculator : IQualityCalculator
{
    private enum MediaCategory
    {
        Hdd,
        SataSsd,
        Nvme
    }

    public QualityRating CalculateQuality(SmartaData? smartaData)
    {
        if (smartaData == null)
        {
            // SMART data unavailable - return neutral rating
            // This is informative, not an error (e.g., USB adapters often don't pass SMART)
            return new QualityRating(QualityGrade.C, 50)
            {
                Warnings = new List<string> { "SMART data nedostupná – disk může být v pořádku (např. přes USB adaptér)" }
            };
        }

        var category = GetMediaCategory(smartaData);
        var warnings = new List<string>();
        var score = 100.0;

        ApplyAgePenalty(smartaData, category, warnings, ref score);
        ApplyPowerCyclePenalty(smartaData, category, warnings, ref score);
        ApplyTemperaturePenalty(smartaData, category, warnings, ref score);
        ApplyMagneticMediaPenalty(smartaData, category, warnings, ref score);
        ApplyFlashWearPenalty(smartaData, category, warnings, ref score);

        if (!smartaData.IsHealthy)
        {
            score -= category == MediaCategory.Hdd ? 18 : 14;
            warnings.Add("Drive self-reported SMART health issue");
        }

        score = Math.Clamp(score, 0, 100);

        if (HasCriticalSmartFailure(smartaData))
        {
            score = Math.Min(score, 34);
            warnings.Add("Critical SMART failure detected");
            return new QualityRating(QualityGrade.F, score) { Warnings = warnings };
        }

        var grade = score switch
        {
            >= 94 => QualityGrade.A,
            >= 86 => QualityGrade.B,
            >= 76 => QualityGrade.C,
            >= 64 => QualityGrade.D,
            >= 50 => QualityGrade.E,
            _ => QualityGrade.F
        };

        grade = ApplyGradeCeilings(smartaData, category, grade);
        return new QualityRating(grade, score) { Warnings = warnings };
    }

    private static MediaCategory GetMediaCategory(SmartaData smartaData)
    {
        if (!string.IsNullOrWhiteSpace(smartaData.DeviceType) &&
            smartaData.DeviceType.Contains("nvme", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCategory.Nvme;
        }

        if (smartaData.AvailableSpare.HasValue || smartaData.PercentageUsed.HasValue || smartaData.WearLevelingCount.HasValue)
        {
            return MediaCategory.SataSsd;
        }

        return MediaCategory.Hdd;
    }

    private static void ApplyAgePenalty(SmartaData smartaData, MediaCategory category, List<string> warnings, ref double score)
    {
        if (smartaData.PowerOnHours is not > 0)
        {
            return;
        }

        var hours = smartaData.PowerOnHours.Value;
        var penalty = category switch
        {
            MediaCategory.Hdd => hours switch
            {
                <= 8_000 => 0,
                <= 15_000 => 5,
                <= 25_000 => 11,
                <= 35_000 => 19,
                <= 45_000 => 29,
                _ => 39
            },
            MediaCategory.SataSsd => hours switch
            {
                <= 10_000 => 0,
                <= 20_000 => 3,
                <= 30_000 => 8,
                <= 45_000 => 15,
                <= 60_000 => 24,
                _ => 34
            },
            _ => hours switch
            {
                <= 12_000 => 0,
                <= 25_000 => 2,
                <= 40_000 => 6,
                <= 55_000 => 12,
                <= 70_000 => 20,
                _ => 28
            }
        };

        score -= penalty;
        if (penalty > 0)
        {
            warnings.Add($"Power-on time: {hours:N0} h");
        }
    }

    private static void ApplyPowerCyclePenalty(SmartaData smartaData, MediaCategory category, List<string> warnings, ref double score)
    {
        if (smartaData.PowerCycleCount <= 0)
        {
            return;
        }

        var cycles = smartaData.PowerCycleCount;
        var penalty = category switch
        {
            MediaCategory.Hdd => cycles switch
            {
                <= 2_000 => 0,
                <= 5_000 => 2,
                <= 10_000 => 5,
                <= 20_000 => 9,
                _ => 13
            },
            MediaCategory.SataSsd => cycles switch
            {
                <= 3_000 => 0,
                <= 8_000 => 1,
                <= 15_000 => 4,
                <= 25_000 => 7,
                _ => 11
            },
            _ => cycles switch
            {
                <= 5_000 => 0,
                <= 10_000 => 1,
                <= 20_000 => 3,
                <= 40_000 => 6,
                _ => 10
            }
        };

        score -= penalty;
        if (penalty > 0)
        {
            warnings.Add($"Power cycles: {cycles:N0}");
        }
    }

    private static void ApplyTemperaturePenalty(SmartaData smartaData, MediaCategory category, List<string> warnings, ref double score)
    {
        if (smartaData.Temperature is not > 0)
        {
            return;
        }

        var temp = smartaData.Temperature.Value;
        double penalty = category switch
        {
            MediaCategory.Hdd => temp switch
            {
                <= 40 => 0,
                <= 45 => (temp - 40) * 1.0,
                <= 50 => 5 + ((temp - 45) * 1.8),
                <= 55 => 14 + ((temp - 50) * 2.4),
                _ => 26 + ((temp - 55) * 2.8)
            },
            MediaCategory.SataSsd => temp switch
            {
                <= 45 => 0,
                <= 50 => (temp - 45) * 0.8,
                <= 55 => 4 + ((temp - 50) * 1.4),
                <= 60 => 11 + ((temp - 55) * 2.0),
                _ => 21 + ((temp - 60) * 2.4)
            },
            _ => temp switch
            {
                <= 50 => 0,
                <= 60 => (temp - 50) * 0.9,
                <= 70 => 9 + ((temp - 60) * 1.6),
                _ => 25 + ((temp - 70) * 2.0)
            }
        };

        penalty = Math.Min(36, penalty);
        score -= penalty;
        if (penalty > 0)
        {
            warnings.Add($"Temperature: {temp}°C");
        }
    }

    private static void ApplyMagneticMediaPenalty(SmartaData smartaData, MediaCategory category, List<string> warnings, ref double score)
    {
        if (smartaData.ReallocatedSectorCount is > 0)
        {
            var value = smartaData.ReallocatedSectorCount.Value;
            var penalty = value switch
            {
                <= 4 => category == MediaCategory.Hdd ? 10 : 8,
                <= 19 => category == MediaCategory.Hdd ? 18 : 14,
                <= 99 => category == MediaCategory.Hdd ? 30 : 24,
                _ => 40
            };
            score -= penalty;
            warnings.Add($"Reallocated sectors: {value}");
        }

        if (smartaData.PendingSectorCount is > 0)
        {
            var value = smartaData.PendingSectorCount.Value;
            var penalty = value switch
            {
                <= 2 => 20,
                <= 10 => 32,
                _ => 42
            };
            score -= penalty;
            warnings.Add($"Pending sectors: {value}");
        }

        if (smartaData.UncorrectableErrorCount is > 0)
        {
            var value = smartaData.UncorrectableErrorCount.Value;
            var penalty = value switch
            {
                <= 2 => 16,
                <= 10 => 26,
                _ => 38
            };
            score -= penalty;
            warnings.Add($"Uncorrectable errors: {value}");
        }
    }

    private static void ApplyFlashWearPenalty(SmartaData smartaData, MediaCategory category, List<string> warnings, ref double score)
    {
        if (category == MediaCategory.Hdd)
        {
            return;
        }

        if (smartaData.WearLevelingCount is > 0 and < 95)
        {
            var wearLevel = smartaData.WearLevelingCount.Value;
            var penalty = wearLevel switch
            {
                >= 80 => 2,
                >= 60 => 7,
                >= 40 => 14,
                >= 20 => 24,
                _ => 34
            };
            score -= penalty;
            warnings.Add($"SSD health / wear indicator: {wearLevel}%");
        }

        if (smartaData.PercentageUsed is > 0)
        {
            var used = smartaData.PercentageUsed.Value;
            var penalty = used switch
            {
                <= 10 => 0,
                <= 20 => 2,
                <= 40 => 7,
                <= 60 => 14,
                <= 80 => 22,
                <= 90 => 30,
                _ => 38
            };
            score -= penalty;
            if (penalty > 0)
            {
                warnings.Add($"SSD/NVMe life used: {used}%");
            }
        }

        if (smartaData.AvailableSpare is < 100)
        {
            var spare = smartaData.AvailableSpare.Value;
            var penalty = spare switch
            {
                >= 98 => 0,
                >= 95 => 2,
                >= 90 => 6,
                >= 85 => 12,
                >= 80 => 20,
                >= 70 => 30,
                _ => 40
            };
            score -= penalty;
            if (penalty > 0)
            {
                warnings.Add($"Available spare: {spare}%");
            }
        }

        if (smartaData.MediaErrors is > 0)
        {
            var errors = smartaData.MediaErrors.Value;
            var penalty = errors switch
            {
                <= 2 => 12,
                <= 10 => 22,
                _ => 36
            };
            score -= penalty;
            warnings.Add($"Media errors: {errors}");
        }

        if (smartaData.UnsafeShutdowns is > 0)
        {
            var value = smartaData.UnsafeShutdowns.Value;
            var penalty = value switch
            {
                <= 100 => 0,
                <= 500 => 2,
                <= 2_000 => 5,
                _ => 9
            };
            score -= penalty;
            if (penalty > 0)
            {
                warnings.Add($"Unsafe shutdowns: {value}");
            }
        }
    }

    private static QualityGrade ApplyGradeCeilings(SmartaData smartaData, MediaCategory category, QualityGrade grade)
    {
        if (!smartaData.IsHealthy)
        {
            grade = MaxGrade(grade, QualityGrade.E);
        }

        if (smartaData.PendingSectorCount is > 0 || smartaData.UncorrectableErrorCount is > 0 || smartaData.MediaErrors is > 0)
        {
            grade = MaxGrade(grade, QualityGrade.E);
        }

        if (category == MediaCategory.Hdd)
        {
            if (smartaData.ReallocatedSectorCount is > 0)
            {
                grade = MaxGrade(grade, QualityGrade.C);
            }
            if (smartaData.PowerOnHours is > 45_000)
            {
                grade = MaxGrade(grade, QualityGrade.D);
            }
        }
        else
        {
            if (smartaData.PercentageUsed is > 50 || smartaData.AvailableSpare is < 90)
            {
                grade = MaxGrade(grade, QualityGrade.C);
            }
            if (smartaData.PercentageUsed is > 70 || smartaData.AvailableSpare is < 85)
            {
                grade = MaxGrade(grade, QualityGrade.D);
            }
            if (smartaData.PercentageUsed is > 85 || smartaData.AvailableSpare is < 80)
            {
                grade = MaxGrade(grade, QualityGrade.E);
            }
        }

        return grade;
    }

    private static bool HasCriticalSmartFailure(SmartaData smartaData)
    {
        return smartaData.Attributes.Any(a => !a.IsOk && !string.IsNullOrWhiteSpace(a.WhenFailed)) ||
               smartaData.PendingSectorCount is > 8 ||
               smartaData.UncorrectableErrorCount is > 8 ||
               smartaData.MediaErrors is > 8 ||
               smartaData.ReallocatedSectorCount is > 200 ||
               smartaData.PercentageUsed is >= 100 ||
               smartaData.AvailableSpare is <= 1;
    }

    private static QualityGrade MaxGrade(QualityGrade current, QualityGrade minimum)
    {
        return (QualityGrade)Math.Max((int)current, (int)minimum);
    }
}

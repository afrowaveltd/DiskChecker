using System;
using System.Collections.Generic;
using System.Linq;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Xunit;

namespace DiskChecker.Tests;

public class HistoricalThermalFailureTests
{
    private static SmartaData HealthyDiskWithHistoricalThermalFailure()
    {
        return new SmartaData
        {
            DeviceModel = "Test HDD",
            SerialNumber = "THERMAL-190",
            DeviceType = "ata",
            IsHealthy = true,
            // The parser sets IsFailing = true for any failing attribute, including
            // In_the_past. This mirrors the real-world edge case.
            IsFailing = true,
            Temperature = 38,
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            UncorrectableErrorCount = 0,
            Attributes = new List<SmartaAttributeItem>
            {
                new SmartaAttributeItem
                {
                    Id = 190,
                    Name = "Airflow_Temperature_Cel",
                    Value = 60,
                    Worst = 40,
                    Threshold = 45,
                    IsOk = false,
                    WhenFailed = "In_the_past"
                }
            }
        };
    }

    [Fact]
    public void HistoricalThermalFailure_Alone_DoesNotForceGradeF()
    {
        var smarta = HealthyDiskWithHistoricalThermalFailure();
        var calculator = new QualityCalculator();

        var rating = calculator.CalculateQuality(smarta);

        Assert.NotEqual(QualityGrade.F, rating.Grade);
        Assert.True(rating.Score > 34, "Score should not be clamped to the critical-F band.");
        Assert.Contains(
            rating.Warnings,
            w => w.Contains("překročení povoleného teplotního limitu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HistoricalThermalFailure_WithPendingSectors_StillForcesCritical()
    {
        var smarta = HealthyDiskWithHistoricalThermalFailure();
        smarta.PendingSectorCount = 3;

        var rating = new QualityCalculator().CalculateQuality(smarta);

        Assert.Equal(QualityGrade.F, rating.Grade);
    }

    [Fact]
    public void HistoricalThermalFailure_WithUncorrectableErrors_StillForcesCritical()
    {
        var smarta = HealthyDiskWithHistoricalThermalFailure();
        smarta.UncorrectableErrorCount = 2;

        var rating = new QualityCalculator().CalculateQuality(smarta);

        Assert.Equal(QualityGrade.F, rating.Grade);
    }

    [Fact]
    public void HistoricalThermalFailure_WithReallocatedGrowth_StillForcesCritical()
    {
        var smarta = HealthyDiskWithHistoricalThermalFailure();
        smarta.ReallocatedSectorCount = 60;

        var rating = new QualityCalculator().CalculateQuality(smarta);

        Assert.Equal(QualityGrade.F, rating.Grade);
    }

    [Fact]
    public void CurrentlyExceededThermalThreshold_DoesNotUseExemption()
    {
        var smarta = HealthyDiskWithHistoricalThermalFailure();
        // Attribute is currently below threshold -> active failure, not historical.
        smarta.Attributes[0].Value = 30;
        smarta.Attributes[0].Threshold = 45;
        smarta.Attributes[0].WhenFailed = "FAILING_NOW";

        var rating = new QualityCalculator().CalculateQuality(smarta);

        Assert.Equal(QualityGrade.F, rating.Grade);
    }

    [Fact]
    public void InThePast_ButStillBelowThreshold_DoesNotUseExemption()
    {
        var smarta = HealthyDiskWithHistoricalThermalFailure();
        // WHEN_FAILED says In_the_past, but the current normalized value is still
        // below threshold -> treat as active, not historical.
        smarta.Attributes[0].Value = 30;
        smarta.Attributes[0].Threshold = 45;
        smarta.Attributes[0].WhenFailed = "In_the_past";

        var rating = new QualityCalculator().CalculateQuality(smarta);

        Assert.Equal(QualityGrade.F, rating.Grade);
    }

    [Fact]
    public void HistoricalFailure_OnNonThermalAttribute_DoesNotUseExemption()
    {
        var smarta = HealthyDiskWithHistoricalThermalFailure();
        smarta.Attributes = new List<SmartaAttributeItem>
        {
            new SmartaAttributeItem
            {
                Id = 5,
                Name = "Reallocated_Sector_Ct",
                Value = 90,
                Worst = 90,
                Threshold = 36,
                IsOk = false,
                WhenFailed = "In_the_past"
            }
        };

        var rating = new QualityCalculator().CalculateQuality(smarta);

        Assert.Equal(QualityGrade.F, rating.Grade);
    }

    [Fact]
    public void HistoricalThermalFailure_IsStillReportedAsWarning()
    {
        var smarta = HealthyDiskWithHistoricalThermalFailure();
        var rating = new QualityCalculator().CalculateQuality(smarta);

        // The original SMART warning must remain visible (never deleted/hidden).
        Assert.Contains(
            rating.Warnings,
            w => w.Contains("překročení povoleného teplotního limitu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Certificate_ContainsOriginalSmartWarning_AndExplanation()
    {
        var smarta = HealthyDiskWithHistoricalThermalFailure();
        // Simulate the parser's output: the historical thermal failure is recorded
        // in FailingAttributes / FailurePrediction and must remain visible.
        smarta.FailingAttributes = new List<string>
        {
            "Airflow_Temperature_Cel (ID 190): In_the_past — value=60, threshold=45"
        };
        smarta.FailurePrediction =
            "⚠️ Disk měl kritické atributy v minulosti — 1 atributů pod thresholdem. Zvažte výměnu.";

        var rating = new QualityCalculator().CalculateQuality(smarta);
        var certificate = rating.GenerateCertificate(smarta, DateTime.UtcNow);

        // The original SMART warning is preserved on the SmartaData (never deleted/hidden).
        Assert.Contains(
            smarta.FailingAttributes,
            a => a.Contains("In_the_past", StringComparison.OrdinalIgnoreCase));

        // The certificate text carries the explanation of why it was not treated
        // as an active physical failure.
        Assert.Contains("překročení povoleného teplotního limitu", certificate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nikoli klasifikováno jako aktuální selhání", certificate, StringComparison.OrdinalIgnoreCase);
    }
}

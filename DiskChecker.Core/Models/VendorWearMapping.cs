namespace DiskChecker.Core.Models;

/// <summary>
/// Known SSD manufacturers and their Wear_Leveling_Count (ID 177) interpretation.
/// Normalized value semantics vary by vendor:
/// - Some start at 100 and count down (remaining life %)
/// - Some start at 0 and count up (erase cycles)
/// - Some use raw value as percentage used
/// </summary>
public static class VendorWearMapping
{
    /// <summary>
    /// Known vendor prefixes extracted from model names.
    /// </summary>
    public static readonly Dictionary<string, VendorWearInterpretation> KnownVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Samsung"] = new()
        {
            VendorName = "Samsung",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10, // Normalized <= 10 means critical
            WarningThresholdPercent = 30, // Normalized <= 30 means warning
            Notes = "Samsung normalized value starts at 100 and decreases. Raw value = erase cycles. SSD life = normalized value %."
        },
        ["Intel"] = new()
        {
            VendorName = "Intel",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Intel normalized value starts at 100 and decreases. Raw value = average erase count."
        },
        ["Seagate"] = new()
        {
            VendorName = "Seagate",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Seagate SSD normalized value starts at 100 and decreases."
        },
        ["WDC"] = new()
        {
            VendorName = "Western Digital",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "WD/SanDisk SSD normalized value starts at 100 and decreases."
        },
        ["SanDisk"] = new()
        {
            VendorName = "SanDisk",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "SanDisk (WD) SSD normalized value starts at 100 and decreases."
        },
        ["Crucial"] = new()
        {
            VendorName = "Crucial/Micron",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Crucial/Micron normalized value starts at 100 and decreases."
        },
        ["Micron"] = new()
        {
            VendorName = "Crucial/Micron",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Micron/Crucial normalized value starts at 100 and decreases."
        },
        ["Kingston"] = new()
        {
            VendorName = "Kingston",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Kingston SSD normalized value starts at 100 and decreases."
        },
        ["Toshiba"] = new()
        {
            VendorName = "Toshiba/Kioxia",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Toshiba/Kioxia normalized value starts at 100 and decreases."
        },
        ["Kioxia"] = new()
        {
            VendorName = "Toshiba/Kioxia",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Kioxia (ex-Toshiba) normalized value starts at 100 and decreases."
        },
        ["SK"] = new()
        {
            VendorName = "SK Hynix",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "SK Hynix SSD normalized value starts at 100 and decreases."
        },
        ["Hynix"] = new()
        {
            VendorName = "SK Hynix",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Hynix SSD normalized value starts at 100 and decreases."
        },
        ["ADATA"] = new()
        {
            VendorName = "ADATA",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "ADATA SSD normalized value starts at 100 and decreases."
        },
        ["Corsair"] = new()
        {
            VendorName = "Corsair",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Corsair SSD normalized value starts at 100 and decreases."
        },
        ["Patriot"] = new()
        {
            VendorName = "Patriot",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Patriot SSD normalized value starts at 100 and decreases."
        },
        ["Plextor"] = new()
        {
            VendorName = "Plextor/Lite-On",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Plextor SSD normalized value starts at 100 and decreases."
        },
        ["Lite-On"] = new()
        {
            VendorName = "Plextor/Lite-On",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Lite-On SSD normalized value starts at 100 and decreases."
        },
        ["OCZ"] = new()
        {
            VendorName = "OCZ/Toshiba",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "OCZ SSD normalized value starts at 100 and decreases."
        },
        ["Transcend"] = new()
        {
            VendorName = "Transcend",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Transcend SSD normalized value starts at 100 and decreases."
        },
        ["Goodram"] = new()
        {
            VendorName = "Goodram",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Goodram SSD normalized value starts at 100 and decreases."
        },
        ["Lexar"] = new()
        {
            VendorName = "Lexar",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Lexar SSD normalized value starts at 100 and decreases."
        },
        ["Netac"] = new()
        {
            VendorName = "Netac",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Netac SSD normalized value starts at 100 and decreases."
        },
        ["Fanxiang"] = new()
        {
            VendorName = "Fanxiang",
            NormalizedSemantics = WearNormalizedSemantics.RemainingLife,
            NormalizedStart = 100,
            NormalizedEnd = 0,
            RawSemantics = "Average erase count of NAND blocks",
            WearThresholdPercent = 10,
            WarningThresholdPercent = 30,
            Notes = "Fanxiang SSD normalized value starts at 100 and decreases."
        }
    };

    /// <summary>
    /// Gets the vendor interpretation for a given model name.
    /// </summary>
    public static VendorWearInterpretation? GetInterpretation(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return null;

        var firstToken = modelName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstToken == null)
            return null;

        return KnownVendors.TryGetValue(firstToken, out var interpretation) ? interpretation : null;
    }

    /// <summary>
    /// Interprets the wear leveling count value for a given model.
    /// Returns a human-readable wear assessment.
    /// </summary>
    public static WearAssessment InterpretWearLeveling(SmartaData smartaData)
    {
        var interpretation = GetInterpretation(smartaData.DeviceModel);
        var isNvme = !string.IsNullOrWhiteSpace(smartaData.DeviceType) &&
                     smartaData.DeviceType.Contains("nvme", StringComparison.OrdinalIgnoreCase);

        // NVMe drives use PercentageUsed as standardized wear indicator
        if (isNvme && smartaData.PercentageUsed.HasValue)
        {
            var used = smartaData.PercentageUsed.Value;
            return new WearAssessment
            {
                VendorName = interpretation?.VendorName ?? "NVMe",
                WearPercent = used,
                IsNvmeStandardized = true,
                Severity = used switch
                {
                    >= 100 => SmartAnalysisSeverity.Critical,
                    >= 80 => SmartAnalysisSeverity.Critical,
                    >= 50 => SmartAnalysisSeverity.Warning,
                    >= 20 => SmartAnalysisSeverity.Info,
                    _ => SmartAnalysisSeverity.Info
                },
                Description = used switch
                {
                    >= 100 => "💀 SSD je zcela opotřebeno – hrozí selhání!",
                    >= 80 => "🔴 Kritické opotřebení – doporučujeme okamžitou výměnu.",
                    >= 50 => "🟡 Značné opotřebení – plánujte výměnu.",
                    >= 20 => "🟢 Mírné opotřebení – SSD je v dobrém stavu.",
                    _ => "✅ SSD je téměř nové."
                },
                RawValue = used,
                NormalizedValue = 100 - used
            };
        }

        // SATA SSD with WearLevelingCount
        if (smartaData.WearLevelingCount.HasValue)
        {
            var wearValue = smartaData.WearLevelingCount.Value;

            if (interpretation != null)
            {
                // Known vendor - interpret according to their semantics
                if (interpretation.NormalizedSemantics == WearNormalizedSemantics.RemainingLife)
                {
                    // Normalized value = remaining life percentage (100 = new, 0 = dead)
                    return new WearAssessment
                    {
                        VendorName = interpretation.VendorName,
                        WearPercent = 100 - wearValue,
                        IsNvmeStandardized = false,
                        Severity = wearValue switch
                        {
                            <= 10 => SmartAnalysisSeverity.Critical,
                            <= 30 => SmartAnalysisSeverity.Warning,
                            <= 50 => SmartAnalysisSeverity.Info,
                            _ => SmartAnalysisSeverity.Info
                        },
                        Description = wearValue switch
                        {
                            <= 0 => "💀 SSD je zcela opotřebeno!",
                            <= 10 => "🔴 Kritické opotřebení – doporučujeme výměnu.",
                            <= 30 => "🟡 Značné opotřebení – plánujte výměnu.",
                            <= 50 => "🟢 Střední opotřebení.",
                            _ => "✅ SSD je v dobrém stavu."
                        },
                        RawValue = wearValue,
                        NormalizedValue = wearValue,
                        VendorInterpretation = interpretation
                    };
                }
            }

            // Unknown vendor - use generic interpretation
            // Most SSDs use normalized value decreasing from 100
            if (wearValue <= 100)
            {
                return new WearAssessment
                {
                    VendorName = interpretation?.VendorName ?? "Neznámý výrobce",
                    WearPercent = 100 - wearValue,
                    IsNvmeStandardized = false,
                    Severity = wearValue switch
                    {
                        <= 10 => SmartAnalysisSeverity.Critical,
                        <= 30 => SmartAnalysisSeverity.Warning,
                        <= 50 => SmartAnalysisSeverity.Info,
                        _ => SmartAnalysisSeverity.Info
                    },
                    Description = wearValue switch
                    {
                        <= 0 => "💀 SSD je zcela opotřebeno!",
                        <= 10 => "🔴 Kritické opotřebení – doporučujeme výměnu.",
                        <= 30 => "🟡 Značné opotřebení – plánujte výměnu.",
                        <= 50 => "🟢 Střední opotřebení.",
                        _ => "✅ SSD je v dobrém stavu."
                    },
                    RawValue = wearValue,
                    NormalizedValue = wearValue,
                    VendorInterpretation = interpretation
                };
            }

            // Raw value is erase count (high numbers)
            return new WearAssessment
            {
                VendorName = interpretation?.VendorName ?? "Neznámý výrobce",
                WearPercent = null,
                IsNvmeStandardized = false,
                Severity = SmartAnalysisSeverity.Info,
                Description = $"Počet mazacích cyklů: {wearValue:N0} (bez normalizace)",
                RawValue = wearValue,
                NormalizedValue = null,
                VendorInterpretation = interpretation
            };
        }

        // No wear data available
        return new WearAssessment
        {
            VendorName = interpretation?.VendorName ?? "Neznámý",
            WearPercent = null,
            IsNvmeStandardized = false,
            Severity = SmartAnalysisSeverity.Info,
            Description = "Data o opotřebení nejsou k dispozici.",
            RawValue = null,
            NormalizedValue = null,
            VendorInterpretation = interpretation
        };
    }
}

/// <summary>
/// Interpretation of Wear_Leveling_Count normalized value semantics.
/// </summary>
public enum WearNormalizedSemantics
{
    /// <summary>Normalized value = remaining life % (100 = new, 0 = dead). Used by most vendors.</summary>
    RemainingLife,
    /// <summary>Normalized value = used life % (0 = new, 100 = dead). Rare.</summary>
    UsedLife,
    /// <summary>Normalized value = erase cycles (0 = new, increasing). Some older SSDs.</summary>
    EraseCycles
}

/// <summary>
/// Vendor-specific interpretation of wear indicators.
/// </summary>
public class VendorWearInterpretation
{
    public string VendorName { get; set; } = string.Empty;
    public WearNormalizedSemantics NormalizedSemantics { get; set; } = WearNormalizedSemantics.RemainingLife;
    public int NormalizedStart { get; set; } = 100;
    public int NormalizedEnd { get; set; }
    public string RawSemantics { get; set; } = string.Empty;
    public int WearThresholdPercent { get; set; } = 10;
    public int WarningThresholdPercent { get; set; } = 30;
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// Human-readable wear assessment result.
/// </summary>
public class WearAssessment
{
    public string VendorName { get; set; } = string.Empty;
    public int? WearPercent { get; set; }
    public bool IsNvmeStandardized { get; set; }
    public SmartAnalysisSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public int? RawValue { get; set; }
    public int? NormalizedValue { get; set; }
    public VendorWearInterpretation? VendorInterpretation { get; set; }

    /// <summary>
    /// Returns a compact display string for UI.
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (WearPercent.HasValue)
                return $"{VendorName}: {WearPercent}% opotřebení";
            if (RawValue.HasValue)
                return $"{VendorName}: {RawValue} cyklů";
            return $"{VendorName}: N/A";
        }
    }
}

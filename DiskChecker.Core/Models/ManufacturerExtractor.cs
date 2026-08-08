using System;
using System.Collections.Generic;
using System.Linq;

namespace DiskChecker.Core.Models;

/// <summary>
/// Utility for extracting manufacturer name from SMART device model strings.
/// Uses the known vendor list from VendorWearMapping plus additional heuristics.
/// </summary>
public static class ManufacturerExtractor
{
    /// <summary>
    /// Known manufacturer prefixes mapped to display names.
    /// Extends VendorWearMapping.KnownVendors with additional HDD manufacturers.
    /// Dictionary is case-insensitive.
    /// </summary>
    private static readonly Dictionary<string, string> KnownManufacturers = new(StringComparer.OrdinalIgnoreCase)
    {
        // SSD vendors (from VendorWearMapping)
        ["Samsung"] = "Samsung",
        ["Intel"] = "Intel",
        ["Seagate"] = "Seagate",
        ["WDC"] = "Western Digital",
        ["SanDisk"] = "SanDisk",
        ["Crucial"] = "Crucial",
        ["Micron"] = "Micron",
        ["Kingston"] = "Kingston",
        ["Toshiba"] = "Toshiba",
        ["Kioxia"] = "Kioxia",
        ["SK"] = "SK Hynix",
        ["Hynix"] = "SK Hynix",
        ["ADATA"] = "ADATA",
        ["Corsair"] = "Corsair",
        ["Patriot"] = "Patriot",
        ["Plextor"] = "Plextor",
        ["Lite-On"] = "Lite-On",
        ["OCZ"] = "OCZ",
        ["Transcend"] = "Transcend",
        ["Goodram"] = "Goodram",
        ["Lexar"] = "Lexar",
        ["Netac"] = "Netac",
        ["Fanxiang"] = "Fanxiang",

        // HDD vendors
        ["ST"] = "Seagate",           // Seagate HDD models start with ST
        ["CT"] = "Crucial",           // Crucial NVMe models start with CT
        ["WD"] = "Western Digital",   // Western Digital HDD
        ["HGST"] = "HGST",            // HGST (now part of WD)
        ["Hitachi"] = "Hitachi",      // Hitachi
        ["Maxtor"] = "Maxtor",        // Maxtor
        ["Fujitsu"] = "Fujitsu",      // Fujitsu
        ["IBM"] = "IBM",              // IBM (historical)
        ["Quantum"] = "Quantum",      // Quantum (historical)

        // NVMe / other vendors
        ["Sabrent"] = "Sabrent",
        ["TEAM"] = "TeamGroup",
        ["SP"] = "Silicon Power",
        ["PNY"] = "PNY",
        ["Gigabyte"] = "Gigabyte",
        ["MSI"] = "MSI",
        ["ASUS"] = "ASUS",
        ["HP"] = "HP",
        ["Dell"] = "Dell",
        ["Lenovo"] = "Lenovo",
        ["Apple"] = "Apple",
        ["Western"] = "Western Digital",
        ["TeamGroup"] = "TeamGroup",
        ["Silicon"] = "Silicon Power",
    };

    /// <summary>
    /// Extracts the manufacturer name from a SMART device model string.
    /// Returns empty string if no known manufacturer is detected.
    /// </summary>
    public static string ExtractManufacturer(string? deviceModel)
    {
        if (string.IsNullOrWhiteSpace(deviceModel))
        {
            return string.Empty;
        }

        var model = deviceModel.Trim();

        // Try exact prefix match first (case-insensitive)
        foreach (var kvp in KnownManufacturers)
        {
            if (model.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                // For "ST" prefix, ensure it's a Seagate model (ST followed by digits)
                if (kvp.Key.Equals("ST", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.Length > 2 && char.IsDigit(model[2]))
                    {
                        return kvp.Value;
                    }
                    continue;
                }

                // For "CT" prefix, ensure it's a Crucial model (CT followed by digits)
                if (kvp.Key.Equals("CT", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.Length > 2 && char.IsDigit(model[2]))
                    {
                        return kvp.Value;
                    }
                    continue;
                }

                // For "SK" prefix, ensure it's SK Hynix (not just "SK" as a word)
                if (kvp.Key.Equals("SK", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.Length > 2 && (model[2] == ' ' || model[2] == 'h' || model[2] == 'H'))
                    {
                        return kvp.Value;
                    }
                    continue;
                }

                // For "SP" prefix, ensure it's Silicon Power
                if (kvp.Key.Equals("SP", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.Length > 2 && (model[2] == ' ' || model[2] == 'C' || model[2] == '0'))
                    {
                        return kvp.Value;
                    }
                    continue;
                }

                // For "WD" prefix, ensure it's Western Digital (not WDC which is handled separately)
                if (kvp.Key.Equals("WD", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.Length > 2 && model[2] != 'C')
                    {
                        return kvp.Value;
                    }
                    continue;
                }

                return kvp.Value;
            }
        }

        // Try first token match
        var firstToken = model.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstToken))
        {
            foreach (var kvp in KnownManufacturers)
            {
                if (string.Equals(firstToken, kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Extracts the manufacturer from a SmartaData object.
    /// Tries DeviceModel first, then ModelFamily.
    /// </summary>
    public static string ExtractManufacturer(SmartaData? smartaData)
    {
        if (smartaData == null)
        {
            return string.Empty;
        }

        // Try DeviceModel first
        var manufacturer = ExtractManufacturer(smartaData.DeviceModel);
        if (!string.IsNullOrWhiteSpace(manufacturer))
        {
            return manufacturer;
        }

        // Try ModelFamily as fallback
        if (!string.IsNullOrWhiteSpace(smartaData.ModelFamily))
        {
            manufacturer = ExtractManufacturer(smartaData.ModelFamily);
            if (!string.IsNullOrWhiteSpace(manufacturer))
            {
                return manufacturer;
            }
        }

        return string.Empty;
    }
}

using System.Text.Json;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Parses SMART data from smartctl JSON output.
/// </summary>
public static class SmartctlJsonParser
{
    /// <summary>
    /// Parses smartctl JSON output into <see cref="SmartaData"/>.
    /// </summary>
    /// <param name="json">Raw smartctl JSON output.</param>
    /// <returns>Parsed SMART data or <c>null</c> when parsing fails.</returns>
    public static SmartaData? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            
            // Try multiple ways to get temperature - it can be nested differently
            double? temperature = null;
            
            // Method 1: Direct temperature property
            if (root.TryGetProperty("temperature", out var tempElement))
            {
                if (tempElement.TryGetProperty("current", out var currentTemp))
                    temperature = GetDouble(currentTemp, null) ?? GetDouble(tempElement, "current");
                else
                    temperature = GetDouble(tempElement, null);
            }
            
            // Method 2: ata_smart_attributes might have temperature in SMART table
            if (!temperature.HasValue && root.TryGetProperty("ata_smart_attributes", out var ataAttribs))
            {
                if (ataAttribs.TryGetProperty("table", out var table) && table.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in table.EnumerateArray())
                    {
                        var id = GetInt(entry, "id");
                        if ((id == 194 || id == 190) && entry.TryGetProperty("raw", out var raw))
                        {
                            if (raw.TryGetProperty("value", out var val) && val.TryGetInt32(out var tempVal))
                            {
                                temperature = tempVal;
                                break;
                            }
                        }
                    }
                }
            }

            // Method 3: nvme temperature
            if (!temperature.HasValue && root.TryGetProperty("nvme_smart_health_information_log", out var nvmeLog))
            {
                temperature = GetDouble(nvmeLog, "temperature");
            }

            // Try multiple ways to get PowerOnHours
            int? powerOnHours = null;
            
            // Method 1: ATA attributes
            if (root.TryGetProperty("ata_smart_attributes", out var ataAttributes))
            {
                if (ataAttributes.TryGetProperty("table", out var table) && table.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in table.EnumerateArray())
                    {
                        var id = GetInt(entry, "id");
                        if (id == 9) // Power On Hours attribute ID
                        {
                            if (entry.TryGetProperty("raw", out var raw))
                            {
                                if (raw.TryGetProperty("value", out var val) && val.TryGetInt32(out var hours))
                                {
                                    powerOnHours = hours;
                                    break;
                                }
                                else if (raw.ValueKind == JsonValueKind.Number)
                                {
                                    powerOnHours = (int)raw.GetInt64();
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            
            // Method 2: NVMe Power on hours
            if (!powerOnHours.HasValue && root.TryGetProperty("nvme_smart_health_information_log", out var nvmeLog2))
            {
                powerOnHours = GetInt(nvmeLog2, "power_on_hours");
            }

            // Parse model name and clean up USB device suffixes
            var deviceModel = GetString(root, "model_name") ?? GetString(root, "model_number") ?? GetString(root, "device_model");
            if (deviceModel != null)
            {
                // Remove common USB device suffixes
                deviceModel = System.Text.RegularExpressions.Regex.Replace(deviceModel, @"\s+(USB\s+)?Device\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                deviceModel = System.Text.RegularExpressions.Regex.Replace(deviceModel, @"\s+USB\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            // Parse serial number - prefer actual disk SN over USB controller ID
            // Smartctl returns it in "serial_number" field
            string? serialNumber = GetString(root, "serial_number");
            
            var smartaData = new SmartaData
            {
                ModelFamily = GetString(root, "model_family"),
                DeviceModel = deviceModel,
                SerialNumber = serialNumber,
                FirmwareVersion = GetString(root, "firmware_version"),
                Temperature = temperature ?? 0,
                PowerOnHours = powerOnHours ?? 0
            };

            if (root.TryGetProperty("ata_smart_attributes", out var ataAttributes2))
            {
                PopulateAtaAttributes(ataAttributes2, smartaData);
            }

            if (root.TryGetProperty("nvme_smart_health_information_log", out var nvmeAttributes))
            {
                PopulateNvmeAttributes(nvmeAttributes, smartaData);
            }

            return smartaData;
        }
        catch
        {
            return null;
        }
    }

    private static void PopulateAtaAttributes(JsonElement ataAttributes, SmartaData smartaData)
    {
        if (!ataAttributes.TryGetProperty("table", out var table) || table.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in table.EnumerateArray())
        {
            var id = GetInt(entry, "id");
            if (!id.HasValue) continue;

            var rawValue = GetLong(entry, "raw", "value") ?? GetLong(entry, "raw", "string") ?? 0;

            switch (id.Value)
            {
                case 5:
                    smartaData.ReallocatedSectorCount = rawValue;
                    break;
                case 9:
                    smartaData.PowerOnHours = (int)rawValue;
                    break;
                case 197:
                    smartaData.PendingSectorCount = rawValue;
                    break;
                case 198:
                    smartaData.UncorrectableErrorCount = rawValue;
                    break;
                case 190:
                case 194:
                    if (smartaData.Temperature <= 0)
                    {
                        smartaData.Temperature = rawValue;
                    }
                    break;
            }
        }
    }

    private static void PopulateNvmeAttributes(JsonElement nvmeAttributes, SmartaData smartaData)
    {
        smartaData.PowerOnHours = GetInt(nvmeAttributes, "power_on_hours") ?? smartaData.PowerOnHours;
        smartaData.UncorrectableErrorCount = GetLong(nvmeAttributes, "media_errors") ?? smartaData.UncorrectableErrorCount;
        var temperature = GetDouble(nvmeAttributes, "temperature");
        if (temperature.HasValue)
        {
            smartaData.Temperature = temperature.Value > 200
                ? Math.Round(temperature.Value - 273.15, 1)
                : temperature.Value;
        }

        var wear = GetInt(nvmeAttributes, "percentage_used");
        if (wear.HasValue)
        {
            smartaData.WearLevelingCount = wear.Value;
        }
    }

    private static string? GetString(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
            
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString();
        }
        
        return null;
    }

    private static long? GetLong(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number) return value.GetInt64();
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed)) return parsed;
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number) return prop.Value.GetInt64();
                if (prop.Value.ValueKind == JsonValueKind.String && long.TryParse(prop.Value.GetString(), out var parsed)) return parsed;
            }
        }
        
        return null;
    }

    private static long? GetLong(JsonElement root, string property, string nestedProperty)
    {
        if (!root.TryGetProperty(property, out var nested))
        {
            return null;
        }

        if (nested.ValueKind == JsonValueKind.Number)
        {
            return nested.GetInt64();
        }

        if (nested.ValueKind == JsonValueKind.String && long.TryParse(nested.GetString(), out var parsed))
        {
            return parsed;
        }

        if (nested.ValueKind == JsonValueKind.Object)
        {
            return GetLong(nested, nestedProperty);
        }

        return null;
    }

    private static int? GetInt(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number) return value.GetInt32();
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number) return prop.Value.GetInt32();
                if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out var parsed)) return parsed;
            }
        }
        
        return null;
    }

    private static double? GetDouble(JsonElement root, string? property)
    {
        // If no property specified, try to parse the element itself as double
        if (string.IsNullOrEmpty(property))
        {
            if (root.ValueKind == JsonValueKind.Number) return root.GetDouble();
            if (root.ValueKind == JsonValueKind.String && double.TryParse(root.GetString(), out var parsed)) return parsed;
            return null;
        }

        if (root.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed)) return parsed;
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number) return prop.Value.GetDouble();
                if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), out var parsed)) return parsed;
            }
        }
        
        return null;
    }
}

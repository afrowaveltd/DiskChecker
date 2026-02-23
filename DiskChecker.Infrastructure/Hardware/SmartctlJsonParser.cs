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
            var smartaData = new SmartaData
            {
                ModelFamily = GetString(root, "model_family"),
                DeviceModel = GetString(root, "model_name") ?? GetString(root, "model_number") ?? GetString(root, "device_model"),
                SerialNumber = GetString(root, "serial_number"),
                FirmwareVersion = GetString(root, "firmware_version")
            };

            if (root.TryGetProperty("ata_smart_attributes", out var ataAttributes))
            {
                PopulateAtaAttributes(ataAttributes, smartaData);
            }

            if (root.TryGetProperty("nvme_smart_health_information_log", out var nvmeAttributes))
            {
                PopulateNvmeAttributes(nvmeAttributes, smartaData);
            }

            if (root.TryGetProperty("temperature", out var temperature))
            {
                var tempValue = GetDouble(temperature, "current");
                if (tempValue.HasValue)
                {
                    smartaData.Temperature = tempValue.Value;
                }
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

    private static double? GetDouble(JsonElement root, string property)
    {
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

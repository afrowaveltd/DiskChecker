using System.Text.Json;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Parses SMART data from Windows PowerShell JSON output.
/// </summary>
public static class WindowsSmartJsonParser
{
    /// <summary>
    /// Parses PowerShell JSON output into <see cref="SmartaData"/>.
    /// </summary>
    /// <param name="diskInfoJson">JSON with basic disk information.</param>
    /// <param name="attributesJson">JSON with SMART attributes.</param>
    /// <returns>Parsed SMART data or <c>null</c> when parsing fails.</returns>
    public static SmartaData? Parse(string diskInfoJson, string attributesJson)
    {
        if (string.IsNullOrWhiteSpace(diskInfoJson))
        {
            return null;
        }

        try
        {
            var smartaData = new SmartaData();

            using (var diskDocument = JsonDocument.Parse(diskInfoJson))
            {
                var root = diskDocument.RootElement;
                smartaData.DeviceModel = GetString(root, "Model");
                smartaData.SerialNumber = GetString(root, "SerialNumber");
                smartaData.FirmwareVersion = GetString(root, "FirmwareVersion");
                
                // Fallback for PowerOnHours and Temperature if they are in basic info
                smartaData.PowerOnHours = GetInt(root, "PowerOnHours") ?? smartaData.PowerOnHours;
                smartaData.Temperature = GetDouble(root, "Temperature") ?? smartaData.Temperature;
            }

            if (!string.IsNullOrWhiteSpace(attributesJson))
            {
                PopulateAttributes(attributesJson, smartaData);
            }

            return smartaData;
        }
        catch
        {
            return null;
        }
    }

    private static void PopulateAttributes(string attributesJson, SmartaData smartaData)
    {
        using var attributesDocument = JsonDocument.Parse(attributesJson);
        if (attributesDocument.RootElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var attribute in attributesDocument.RootElement.EnumerateArray())
        {
            var id = GetInt(attribute, "Id");
            var name = GetString(attribute, "Name") ?? string.Empty;
            var value = GetLong(attribute, "RawValue") ?? GetLong(attribute, "Value");

            if (value == null)
            {
                continue;
            }

            if (id == 5 || name.Contains("Reallocated", StringComparison.OrdinalIgnoreCase))
            {
                smartaData.ReallocatedSectorCount = value.Value;
            }
            else if (id == 9 || name.Contains("PowerOn", StringComparison.OrdinalIgnoreCase))
            {
                smartaData.PowerOnHours = (int)value.Value;
            }
            else if (id == 197 || name.Contains("Pending", StringComparison.OrdinalIgnoreCase))
            {
                smartaData.PendingSectorCount = value.Value;
            }
            else if (id == 198 || name.Contains("Uncorrectable", StringComparison.OrdinalIgnoreCase))
            {
                smartaData.UncorrectableErrorCount = value.Value;
            }
            else if (id == 194 || id == 190 || name.Contains("Temperature", StringComparison.OrdinalIgnoreCase))
            {
                smartaData.Temperature = value.Value;
            }
            else if (name.Contains("Wear", StringComparison.OrdinalIgnoreCase))
            {
                smartaData.WearLevelingCount = (int)value.Value;
            }
        }
    }

    private static string? GetString(JsonElement root, string property)
    {
        // Try exact match
        if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
            
        // Try case-insensitive match
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString();
        }
        
        return null;
    }

    private static long? GetLong(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number)
            return value.GetInt64();

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Number)
                return prop.Value.GetInt64();
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

    private static int? GetInt(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number)
            return value.GetInt32();

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Number)
                return prop.Value.GetInt32();
        }
        
        return null;
    }
}

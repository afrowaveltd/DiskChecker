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

            double? temperature = null;
            if (root.TryGetProperty("temperature", out var tempElement))
            {
                temperature = tempElement.TryGetProperty("current", out var currentTemp)
                    ? GetDouble(currentTemp, null) ?? GetDouble(tempElement, "current")
                    : GetDouble(tempElement, null);
            }

            if (!temperature.HasValue && TryGetNestedProperty(root, out var ataTable, "ata_smart_attributes", "table") && ataTable.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in ataTable.EnumerateArray())
                {
                    var id = GetInt(entry, "id");
                    if ((id == 194 || id == 190) && entry.TryGetProperty("raw", out var raw) && raw.TryGetProperty("value", out var valueElement) && valueElement.TryGetInt32(out var tempValue))
                    {
                        temperature = tempValue;
                        break;
                    }
                }
            }

            if (!temperature.HasValue && TryGetNestedProperty(root, out var nvmeTemperatureLog, "nvme_smart_health_information_log"))
            {
                temperature = GetDouble(nvmeTemperatureLog, "temperature");
            }

            int? powerOnHours = null;
            if (TryGetNestedProperty(root, out var ataAttributes, "ata_smart_attributes", "table") && ataAttributes.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in ataAttributes.EnumerateArray())
                {
                    if (GetInt(entry, "id") == 9)
                    {
                        powerOnHours = (int?)(GetLong(entry, "raw", "value") ?? GetLong(entry, "raw", "string"));
                        break;
                    }
                }
            }

            if (!powerOnHours.HasValue && TryGetNestedProperty(root, out var nvmeLog, "nvme_smart_health_information_log"))
            {
                powerOnHours = GetInt(nvmeLog, "power_on_hours");
            }

            var data = new SmartaData
            {
                ModelFamily = GetString(root, "model_family"),
                DeviceModel = CleanModelName(GetString(root, "model_name") ?? GetString(root, "model_number") ?? GetString(root, "device_model")),
                SerialNumber = GetString(root, "serial_number"),
                FirmwareVersion = GetString(root, "firmware_version"),
                Temperature = temperature ?? 0,
                PowerOnHours = powerOnHours ?? 0
            };

            if (TryGetNestedProperty(root, out var ataAttributesTable, "ata_smart_attributes"))
            {
                PopulateAtaAttributes(ataAttributesTable, data);
            }

            if (TryGetNestedProperty(root, out var nvmeAttributesLog, "nvme_smart_health_information_log"))
            {
                PopulateNvmeAttributes(nvmeAttributesLog, data);
            }

            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses detailed SMART attributes from smartctl JSON output.
    /// </summary>
    public static IReadOnlyList<SmartaAttributeItem> ParseAttributes(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SmartaAttributeItem>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var attributes = new List<SmartaAttributeItem>();

            if (TryGetNestedProperty(root, out var ataTable, "ata_smart_attributes", "table") && ataTable.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in ataTable.EnumerateArray())
                {
                    var id = GetInt(entry, "id");
                    if (!id.HasValue)
                    {
                        continue;
                    }

                    attributes.Add(new SmartaAttributeItem
                    {
                        Id = id.Value,
                        Name = GetString(entry, "name") ?? $"Attribute {id.Value}",
                        Current = GetInt(entry, "value"),
                        Worst = GetInt(entry, "worst"),
                        Threshold = GetInt(entry, "thresh"),
                        RawValue = GetLong(entry, "raw", "value") ?? 0,
                        WhenFailed = GetString(entry, "when_failed")
                    });
                }

                return attributes;
            }

            if (TryGetNestedProperty(root, out var nvmeLog, "nvme_smart_health_information_log"))
            {
                AppendNvmeSyntheticAttributes(nvmeLog, attributes);
            }

            return attributes;
        }
        catch (JsonException)
        {
            return Array.Empty<SmartaAttributeItem>();
        }
    }

    /// <summary>
    /// Parses current SMART self-test execution status.
    /// </summary>
    public static SmartaSelfTestStatus? ParseSelfTestStatus(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (TryGetNestedProperty(root, out var ataStatus, "ata_smart_data", "self_test", "status"))
            {
                var statusText = GetString(ataStatus, "string") ?? "Neznámý stav";
                var remaining = GetInt(ataStatus, "remaining_percent");

                return new SmartaSelfTestStatus
                {
                    IsRunning = remaining.GetValueOrDefault() > 0 || statusText.Contains("progress", StringComparison.OrdinalIgnoreCase),
                    StatusText = statusText,
                    RemainingPercent = remaining,
                    CheckedAtUtc = DateTime.UtcNow
                };
            }

            if (TryGetNestedProperty(root, out var nvmeSelfTest, "nvme_self_test_log"))
            {
                var currentOperation = GetInt(nvmeSelfTest, "current_self_test_operation") ?? 0;
                var completion = GetInt(nvmeSelfTest, "current_self_test_completion_percent") ?? 0;

                return new SmartaSelfTestStatus
                {
                    IsRunning = currentOperation != 0,
                    StatusText = currentOperation == 0 ? "Neběží žádný self-test" : "SMART self-test probíhá",
                    RemainingPercent = currentOperation == 0 ? 0 : Math.Max(0, 100 - completion),
                    CheckedAtUtc = DateTime.UtcNow
                };
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses SMART self-test log entries.
    /// </summary>
    public static IReadOnlyList<SmartaSelfTestEntry> ParseSelfTestLog(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SmartaSelfTestEntry>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var entries = new List<SmartaSelfTestEntry>();

            if (TryGetNestedProperty(root, out var ataLogTable, "ata_smart_self_test_log", "standard", "table") && ataLogTable.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in ataLogTable.EnumerateArray())
                {
                    entries.Add(new SmartaSelfTestEntry
                    {
                        Number = GetInt(item, "num"),
                        TestType = GetNestedString(item, "type", "string") ?? GetString(item, "type") ?? "N/A",
                        Status = GetNestedString(item, "status", "string") ?? GetString(item, "status") ?? "N/A",
                        RemainingPercent = GetNestedInt(item, "status", "remaining_percent"),
                        LifeTimeHours = GetInt(item, "lifetime_hours"),
                        LbaOfFirstError = GetLong(item, "lba_of_first_error")
                    });
                }

                return entries;
            }

            if (TryGetNestedProperty(root, out var nvmeLogTable, "nvme_self_test_log", "table") && nvmeLogTable.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in nvmeLogTable.EnumerateArray())
                {
                    entries.Add(new SmartaSelfTestEntry
                    {
                        Number = GetInt(item, "self_test_code"),
                        TestType = GetString(item, "self_test_code_description") ?? "NVMe self-test",
                        Status = GetString(item, "self_test_result") ?? "N/A",
                        LifeTimeHours = GetInt(item, "power_on_hours"),
                        LbaOfFirstError = GetLong(item, "lba_of_first_error")
                    });
                }
            }

            return entries;
        }
        catch (JsonException)
        {
            return Array.Empty<SmartaSelfTestEntry>();
        }
    }

    private static string? CleanModelName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var cleaned = System.Text.RegularExpressions.Regex.Replace(value, @"\s+(USB\s+)?Device\s*$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+USB\s*$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool TryGetNestedProperty(JsonElement root, out JsonElement result, params string[] propertyPath)
    {
        result = root;
        foreach (var part in propertyPath)
        {
            if (!result.TryGetProperty(part, out result))
            {
                return false;
            }
        }

        return true;
    }

    private static string? GetNestedString(JsonElement root, string property, string nestedProperty)
    {
        if (!root.TryGetProperty(property, out var nested) || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(nested, nestedProperty);
    }

    private static int? GetNestedInt(JsonElement root, string property, string nestedProperty)
    {
        if (!root.TryGetProperty(property, out var nested) || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetInt(nested, nestedProperty);
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
            if (!id.HasValue)
            {
                continue;
            }

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

    private static void AppendNvmeSyntheticAttributes(JsonElement nvmeLog, ICollection<SmartaAttributeItem> attributes)
    {
        AddNvmeAttribute(attributes, 9001, "NVMe Temperature", GetLong(nvmeLog, "temperature") ?? 0);
        AddNvmeAttribute(attributes, 9002, "NVMe Power-On Hours", GetLong(nvmeLog, "power_on_hours") ?? 0);
        AddNvmeAttribute(attributes, 9003, "NVMe Media Errors", GetLong(nvmeLog, "media_errors") ?? 0);
        AddNvmeAttribute(attributes, 9004, "NVMe Percentage Used", GetLong(nvmeLog, "percentage_used") ?? 0);
        AddNvmeAttribute(attributes, 9005, "NVMe Data Units Read", GetLong(nvmeLog, "data_units_read") ?? 0);
        AddNvmeAttribute(attributes, 9006, "NVMe Data Units Written", GetLong(nvmeLog, "data_units_written") ?? 0);
    }

    private static void AddNvmeAttribute(ICollection<SmartaAttributeItem> attributes, int id, string name, long raw)
    {
        attributes.Add(new SmartaAttributeItem
        {
            Id = id,
            Name = name,
            RawValue = raw
        });
    }

    private static string? GetString(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
            {
                return prop.Value.GetString();
            }
        }

        return null;
    }

    private static long? GetLong(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt64();
            }

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    return prop.Value.GetInt64();
                }

                if (prop.Value.ValueKind == JsonValueKind.String && long.TryParse(prop.Value.GetString(), out var parsed))
                {
                    return parsed;
                }
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

        return nested.ValueKind == JsonValueKind.Object ? GetLong(nested, nestedProperty) : null;
    }

    private static int? GetInt(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt32();
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    return prop.Value.GetInt32();
                }

                if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static double? GetDouble(JsonElement root, string? property)
    {
        if (string.IsNullOrWhiteSpace(property))
        {
            if (root.ValueKind == JsonValueKind.Number)
            {
                return root.GetDouble();
            }

            if (root.ValueKind == JsonValueKind.String && double.TryParse(root.GetString(), out var parsedValue))
            {
                return parsedValue;
            }

            return null;
        }

        if (root.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDouble();
            }

            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsedValue))
            {
                return parsedValue;
            }
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    return prop.Value.GetDouble();
                }

                if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), out var parsedValue))
                {
                    return parsedValue;
                }
            }
        }

        return null;
    }
}

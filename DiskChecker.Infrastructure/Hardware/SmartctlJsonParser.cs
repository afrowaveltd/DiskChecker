using DiskChecker.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Parser for smartctl JSON output supporting ATA/SATA, NVMe, and SCSI/SAS drives.
/// </summary>
public static class SmartctlJsonParser
{
    public static SmartCheckResult? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }
    
    public static SmartCheckResult? Parse(JsonElement root)
    {
        try
        {
            var result = new SmartCheckResult();
            
            // Detect device type
            var deviceType = GetDeviceType(root);
            result.DeviceType = deviceType switch
            {
                DeviceType.NVMe => "NVMe",
                DeviceType.SCSI => "SCSI/SAS",
                _ => "SATA/ATA"
            };
            
            // Parse SMART status
            if (root.TryGetProperty("smart_status", out var smartStatus) &&
                smartStatus.TryGetProperty("passed", out var passed))
            {
                result.IsHealthy = passed.GetBoolean();
                result.TestPassed = result.IsHealthy;
            }
            
            // Parse device info
            if (root.TryGetProperty("model_name", out var modelName))
                result.DeviceModel = modelName.GetString()?.Trim();
            if (root.TryGetProperty("serial_number", out var serialNum))
                result.SerialNumber = serialNum.GetString()?.Trim();
            if (root.TryGetProperty("firmware_version", out var firmware))
                result.FirmwareVersion = firmware.GetString()?.Trim();
            
            // Parse SMART support
            if (root.TryGetProperty("smart_support", out var smartSupport))
            {
                result.IsEnabled = smartSupport.TryGetProperty("enabled", out var en) && en.GetBoolean();
            }
            
            // Parse common metrics
            if (root.TryGetProperty("power_on_time", out var powerOn) &&
                powerOn.TryGetProperty("hours", out var hours))
            {
                result.PowerOnHours = hours.GetInt32();
            }
            
            if (root.TryGetProperty("power_cycle_count", out var cycleCount))
            {
                result.PowerCycleCount = cycleCount.GetInt32();
            }
            
            // Parse temperature (common location)
            if (root.TryGetProperty("temperature", out var temp))
            {
                if (temp.TryGetProperty("current", out var curr))
                    result.Temperature = curr.GetInt32();
                else if (temp.ValueKind == JsonValueKind.Number)
                    result.Temperature = temp.GetInt32();
            }
            
            // Parse capacity
            if (root.TryGetProperty("user_capacity", out var cap) &&
                cap.TryGetProperty("bytes", out var bytes))
            {
                result.TotalSize = bytes.GetInt64();
            }
            
            // Parse device-specific data
            switch (deviceType)
            {
                case DeviceType.NVMe:
                    ParseNvmeData(root, result);
                    break;
                case DeviceType.SCSI:
                    ParseScsiData(root, result);
                    break;
                default:
                    ParseAtaData(root, result);
                    break;
            }
            
            return result;
        }
        catch
        {
            return null;
        }
    }
    
    private static DeviceType GetDeviceType(JsonElement root)
    {
        if (root.TryGetProperty("device", out var device) &&
            device.TryGetProperty("type", out var type))
        {
            var t = type.GetString()?.ToLowerInvariant() ?? "";
            if (t.Contains("nvme")) return DeviceType.NVMe;
            if (t.Contains("scsi")) return DeviceType.SCSI;
        }
        if (root.TryGetProperty("nvme_smart_health_information_log", out _)) return DeviceType.NVMe;
        if (root.TryGetProperty("ata_smart_attributes", out _)) return DeviceType.ATA;
        return DeviceType.ATA;
    }
    
    private static void ParseAtaData(JsonElement root, SmartCheckResult result)
    {
        // Parse ATA attributes
        if (root.TryGetProperty("ata_smart_attributes", out var attrs) &&
            attrs.TryGetProperty("table", out var table))
        {
            var attrList = new List<SmartaAttributeItem>();
            foreach (var item in table.EnumerateArray())
            {
                var attr = new SmartaAttributeItem
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    Name = item.TryGetProperty("name", out var name) ? (name.GetString() ?? "") : "",
                    Value = item.TryGetProperty("value", out var val) ? val.GetInt32() : 0,
                    Worst = item.TryGetProperty("worst", out var worst) ? worst.GetInt32() : 0,
                    Threshold = item.TryGetProperty("thresh", out var thresh) ? thresh.GetInt32() : 0,
                    RawValue = GetRawValue(item),
                    IsOk = !item.TryGetProperty("when_failed", out var wf) || wf.ValueKind == JsonValueKind.Null,
                    Current = (byte)(item.TryGetProperty("value", out var v) ? v.GetInt32() : 0),
                    WhenFailed = ""
                };
                attrList.Add(attr);
                
                // Extract key metrics
                switch (attr.Id)
                {
                    case 5: result.ReallocatedSectorCount = (int)attr.RawValue; break;
                    case 9: if (result.PowerOnHours == 0) result.PowerOnHours = (int)attr.RawValue; break;
                    case 12: if (result.PowerCycleCount == 0) result.PowerCycleCount = (int)attr.RawValue; break;
                    case 177: result.WearLevelingCount = (int)attr.RawValue; break;
                    case 194: if (result.Temperature == 0) result.Temperature = (int)(attr.RawValue & 0xFF); break;
                    case 197: result.PendingSectorCount = (int)attr.RawValue; break;
                    case 198: result.UncorrectableErrorCount = (int)attr.RawValue; break;
                    case 231: if (result.WearLevelingCount == 0) result.WearLevelingCount = (int)attr.RawValue; break;
                }
            }
            result.Attributes = attrList;
        }
        
        // Parse self-test log
        result.SelfTests = ParseAtaSelfTestLog(root);
    }
    
    private static uint GetRawValue(JsonElement item)
    {
        if (item.TryGetProperty("raw", out var raw) &&
            raw.TryGetProperty("value", out var val))
        {
            if (val.TryGetInt64(out var l)) return (uint)Math.Min(l, uint.MaxValue);
            if (val.TryGetUInt64(out var u)) return (uint)Math.Min(u, uint.MaxValue);
        }
        return 0;
    }
    
    private static List<SmartaSelfTestEntry> ParseAtaSelfTestLog(JsonElement root)
    {
        var tests = new List<SmartaSelfTestEntry>();
        if (!root.TryGetProperty("ata_smart_self_test_log", out var log) ||
            !log.TryGetProperty("table", out var table)) return tests;
        
        foreach (var t in table.EnumerateArray())
        {
            var entry = new SmartaSelfTestEntry
            {
                Type = ParseTestType(t.TryGetProperty("type", out var type) ? type.GetString() : null),
                Status = ParseTestStatus(t.TryGetProperty("status", out var status) ? status.GetString() : null)
            };
            if (t.TryGetProperty("num", out var num)) entry.Number = num.GetInt32();
            if (t.TryGetProperty("lifetime_hours", out var hrs)) entry.LifeTimeHours = hrs.GetInt32();
            tests.Add(entry);
        }
        return tests;
    }
    
    private static SmartaSelfTestType ParseTestType(string? s)
    {
        if (s == null) return SmartaSelfTestType.Unknown;
        var lower = s.ToLowerInvariant();
        if (lower.Contains("short")) return SmartaSelfTestType.ShortTest;
        if (lower.Contains("extended") || lower.Contains("long")) return SmartaSelfTestType.Extended;
        if (lower.Contains("conveyance")) return SmartaSelfTestType.Conveyance;
        return SmartaSelfTestType.Unknown;
    }
    
    private static SmartaSelfTestStatus ParseTestStatus(string? s)
    {
        if (s == null) return SmartaSelfTestStatus.Unknown;
        var lower = s.ToLowerInvariant();
        if (lower.Contains("completed without error") || lower.Contains("passed")) return SmartaSelfTestStatus.CompletedWithoutError;
        if (lower.Contains("aborted")) return SmartaSelfTestStatus.AbortedByUser;
        if (lower.Contains("in progress")) return SmartaSelfTestStatus.InProgress;
        return SmartaSelfTestStatus.Unknown;
    }
    
    private static void ParseNvmeData(JsonElement root, SmartCheckResult result)
    {
        if (!root.TryGetProperty("nvme_smart_health_information_log", out var nvme)) return;
        
        // Temperature
        if (nvme.TryGetProperty("temperature", out var temp))
            result.Temperature = temp.GetInt32();
        
        // Power on hours
        if (nvme.TryGetProperty("power_on_hours", out var hrs))
            result.PowerOnHours = hrs.GetInt32();
        
        // Power cycles
        if (nvme.TryGetProperty("power_cycles", out var cycles))
            result.PowerCycleCount = cycles.GetInt32();
        
        // Percentage used
        if (nvme.TryGetProperty("percentage_used", out var pct))
            result.WearLevelingCount = 100 - pct.GetInt32();
        
        // Media errors
        if (nvme.TryGetProperty("media_errors", out var errors))
            result.UncorrectableErrorCount = (int)errors.GetInt64();
        
        // Build attributes
        var attrs = new List<SmartaAttributeItem>();
        AddAttr(attrs, 194, "Temperature", GetInt(nvme, "temperature"));
        AddAttr(attrs, 9, "Power On Hours", GetInt(nvme, "power_on_hours"));
        AddAttr(attrs, 12, "Power Cycles", GetInt(nvme, "power_cycles"));
        AddAttr(attrs, 177, "Wear Leveling", GetInt(nvme, "percentage_used"));
        AddAttr(attrs, 198, "Media Errors", GetInt(nvme, "media_errors"));
        result.Attributes = attrs;
        
        result.SelfTests = ParseNvmeSelfTestLog(root);
    }
    
    private static void ParseScsiData(JsonElement root, SmartCheckResult result)
    {
        if (root.TryGetProperty("temperature", out var temp) && temp.ValueKind == JsonValueKind.Number)
            result.Temperature = temp.GetInt32();
        
        if (root.TryGetProperty("scsi_grown_defect_list", out var defects) && defects.ValueKind == JsonValueKind.Number)
            result.ReallocatedSectorCount = defects.GetInt32();
        
        var attrs = new List<SmartaAttributeItem>();
        AddAttr(attrs, 194, "Temperature", result.Temperature);
        AddAttr(attrs, 5, "Grown Defects", result.ReallocatedSectorCount);
        AddAttr(attrs, 9, "Power On Hours", result.PowerOnHours);
        result.Attributes = attrs;
    }
    
    private static void AddAttr(List<SmartaAttributeItem> attrs, int id, string name, int? val)
    {
        attrs.Add(new SmartaAttributeItem
        {
            Id = id,
            Name = name,
            RawValue = (uint)(val ?? 0),
            Value = 100,
            Worst = 100,
            Threshold = 0,
            IsOk = true,
            Current = 100,
            WhenFailed = ""
        });
    }
    
    private static int? GetInt(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number)
            return p.GetInt32();
        return null;
    }
    
    private static List<SmartaSelfTestEntry> ParseNvmeSelfTestLog(JsonElement root)
    {
        var tests = new List<SmartaSelfTestEntry>();
        if (!root.TryGetProperty("nvme_self_test_log", out var log)) return tests;
        
        if (log.TryGetProperty("table", out var table))
        {
            foreach (var t in table.EnumerateArray())
            {
                var entry = new SmartaSelfTestEntry();
                if (t.TryGetProperty("self_test_code", out var code) && 
                    code.TryGetProperty("value", out var cv))
                {
                    entry.Type = cv.GetInt32() switch
                    {
                        1 => SmartaSelfTestType.ShortTest,
                        2 => SmartaSelfTestType.Extended,
                        _ => SmartaSelfTestType.Unknown
                    };
                }
                if (t.TryGetProperty("self_test_result", out var res) && 
                    res.TryGetProperty("value", out var rv))
                {
                    entry.Status = rv.GetInt32() switch
                    {
                        0 => SmartaSelfTestStatus.CompletedWithoutError,
                        _ => SmartaSelfTestStatus.Unknown
                    };
                }
                if (t.TryGetProperty("power_on_hours", out var hrs)) entry.LifeTimeHours = hrs.GetInt32();
                tests.Add(entry);
            }
        }
        return tests;
    }
    
    public static List<SmartaAttributeItem> ParseAttributes(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var deviceType = GetDeviceType(root);
            
            System.Diagnostics.Debug.WriteLine($"ParseAttributes: DeviceType={deviceType}");
            
            var result = new SmartCheckResult();
            switch (deviceType)
            {
                case DeviceType.NVMe: ParseNvmeData(root, result); break;
                case DeviceType.SCSI: ParseScsiData(root, result); break;
                default: ParseAtaData(root, result); break;
            }
            
            var count = result.Attributes?.Count ?? 0;
            System.Diagnostics.Debug.WriteLine($"ParseAttributes: Parsed {count} attributes");
            
            // Debug: Check if ata_smart_attributes exists
            if (deviceType == DeviceType.ATA)
            {
                if (root.TryGetProperty("ata_smart_attributes", out var attrs))
                {
                    System.Diagnostics.Debug.WriteLine("ParseAttributes: ata_smart_attributes FOUND");
                    if (attrs.TryGetProperty("table", out var table))
                    {
                        System.Diagnostics.Debug.WriteLine($"ParseAttributes: table has {table.GetArrayLength()} elements");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("ParseAttributes: table NOT FOUND in ata_smart_attributes");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ParseAttributes: ata_smart_attributes NOT FOUND");
                }
            }
            
            return result.Attributes ?? new List<SmartaAttributeItem>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ParseAttributes ERROR: {ex.Message}");
            return new List<SmartaAttributeItem>();
        }
    }
    
    public static List<SmartaSelfTestEntry> ParseSelfTestLog(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var deviceType = GetDeviceType(root);
            
            return deviceType switch
            {
                DeviceType.NVMe => ParseNvmeSelfTestLog(root),
                _ => ParseAtaSelfTestLog(root)
            };
        }
        catch { return new List<SmartaSelfTestEntry>(); }
    }
    
    public static SmartaData ToSmartaData(SmartCheckResult result)
    {
        return new SmartaData
        {
            DeviceModel = result.DeviceModel ?? "",
            SerialNumber = result.SerialNumber ?? "",
            FirmwareVersion = result.FirmwareVersion ?? "",
            DeviceType = result.DeviceType ?? "Unknown",
            IsHealthy = result.IsHealthy,
            SmartEnabled = result.IsEnabled,
            Temperature = result.Temperature,
            PowerOnHours = result.PowerOnHours,
            PowerCycleCount = result.PowerCycleCount,
            ReallocatedSectorCount = result.ReallocatedSectorCount,
            PendingSectorCount = result.PendingSectorCount,
            UncorrectableErrorCount = result.UncorrectableErrorCount,
            WearLevelingCount = result.WearLevelingCount,
            Attributes = result.Attributes ?? new List<SmartaAttributeItem>(),
            SelfTests = result.SelfTests ?? new List<SmartaSelfTestEntry>()
        };
    }
    
    private enum DeviceType { ATA, NVMe, SCSI }
}
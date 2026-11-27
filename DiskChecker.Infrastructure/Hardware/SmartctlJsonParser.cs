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
        catch (Exception)
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
            
            // Parse SMART status - try multiple locations
            // Standard location: smart_status.passed
            if (root.TryGetProperty("smart_status", out var smartStatus) &&
                smartStatus.ValueKind == JsonValueKind.Object &&
                smartStatus.TryGetProperty("passed", out var passed))
            {
                result.SmartStatusKnown = true;
                result.IsHealthy = passed.GetBoolean();
                result.TestPassed = result.IsHealthy;
            }
            // Fallback: some older smartctl versions use "smart_status" as a direct boolean
            else if (root.TryGetProperty("smart_status", out var smartStatusDirect) &&
                     (smartStatusDirect.ValueKind == JsonValueKind.True || smartStatusDirect.ValueKind == JsonValueKind.False))
            {
                result.SmartStatusKnown = true;
                result.IsHealthy = smartStatusDirect.GetBoolean();
                result.TestPassed = result.IsHealthy;
            }
            
            // Parse device info - try multiple field names for cross-version compatibility
            if (root.TryGetProperty("model_name", out var modelName))
                result.DeviceModel = modelName.GetString()?.Trim();
            else if (root.TryGetProperty("scsi_model_name", out var scsiModel))
                result.DeviceModel = scsiModel.GetString()?.Trim();
            else if (root.TryGetProperty("device", out var device) && device.TryGetProperty("model_name", out var devModel))
                result.DeviceModel = devModel.GetString()?.Trim();
            
            if (root.TryGetProperty("serial_number", out var serialNum))
                result.SerialNumber = serialNum.GetString()?.Trim();
            else if (root.TryGetProperty("scsi_serial_number", out var scsiSerial))
                result.SerialNumber = scsiSerial.GetString()?.Trim();
            else if (root.TryGetProperty("device", out var device2) && device2.TryGetProperty("serial_number", out var devSerial))
                result.SerialNumber = devSerial.GetString()?.Trim();
            
            if (root.TryGetProperty("firmware_version", out var firmware))
                result.FirmwareVersion = firmware.GetString()?.Trim();
            else if (root.TryGetProperty("scsi_firmware_version", out var scsiFw))
                result.FirmwareVersion = scsiFw.GetString()?.Trim();
            else if (root.TryGetProperty("device", out var device3) && device3.TryGetProperty("firmware_version", out var devFw))
                result.FirmwareVersion = devFw.GetString()?.Trim();
            
            // Parse SMART support
            if (root.TryGetProperty("smart_support", out var smartSupport))
            {
                result.IsEnabled = smartSupport.TryGetProperty("enabled", out var en) && en.GetBoolean();

                // "available" tells us whether the device exposes SMART at all.
                // When it is false there is no SMART data to grade (USB flash drives,
                // USB bridges without passthrough, RAID controllers, ...).
                if (smartSupport.TryGetProperty("available", out var available))
                {
                    result.SmartAvailable = available.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => null
                    };
                }
            }
            
            // Parse common metrics
            if (root.TryGetProperty("power_on_time", out var powerOn) &&
                powerOn.ValueKind == JsonValueKind.Object &&
                powerOn.TryGetProperty("hours", out var hours))
            {
                result.PowerOnHours = hours.GetInt32();
            }
            else if (root.TryGetProperty("power_on_time", out var powerOnDirect) &&
                     powerOnDirect.ValueKind == JsonValueKind.Number)
            {
                // Some versions report power_on_time as direct number of hours
                result.PowerOnHours = powerOnDirect.GetInt32();
            }
            
            if (root.TryGetProperty("power_cycle_count", out var cycleCount))
            {
                result.PowerCycleCount = cycleCount.GetInt32();
            }
            
            // Parse temperature (multiple locations for cross-version compatibility)
            if (root.TryGetProperty("temperature", out var temp))
            {
                if (temp.TryGetProperty("current", out var curr))
                    result.Temperature = curr.GetInt32();
                else if (temp.ValueKind == JsonValueKind.Number)
                    result.Temperature = temp.GetInt32();
            }
            // NVMe temperature may be in nvme_smart_health_information_log (Kelvin)
            else if (root.TryGetProperty("nvme_smart_health_information_log", out var nvmeTemp) &&
                     nvmeTemp.TryGetProperty("temperature", out var nvmeTempVal))
            {
                var kelvin = nvmeTempVal.GetInt32();
                result.Temperature = kelvin > 273 ? kelvin - 273 : kelvin;
            }
            
            // Parse capacity
            if (root.TryGetProperty("user_capacity", out var cap) &&
                cap.TryGetProperty("bytes", out var bytes))
            {
                result.TotalSize = bytes.GetInt64();
            }
            else if (root.TryGetProperty("user_capacity", out var capDirect) &&
                     capDirect.ValueKind == JsonValueKind.Number)
            {
                result.TotalSize = capDirect.GetInt64();
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
            
            if (deviceType == DeviceType.ATA)
                ValidateAtaDiagnostics(root, result);

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartctlJsonParser] Parse failed: {ex.Message}");
            // Return a minimal result with whatever we could parse before the exception
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
                    IsOk = !item.TryGetProperty("when_failed", out var wf) || wf.ValueKind == JsonValueKind.Null || string.IsNullOrWhiteSpace(wf.GetString()),
                    Current = (byte)(item.TryGetProperty("value", out var v) ? v.GetInt32() : 0),
                    WhenFailed = item.TryGetProperty("when_failed", out var failed) && failed.ValueKind == JsonValueKind.String
                        ? failed.GetString() ?? string.Empty
                        : string.Empty
                };
                attrList.Add(attr);
                
                // Extract key metrics
                switch (attr.Id)
                {
                    case 5: result.ReallocatedSectorCount = (int)attr.RawValue; break;
                    case 9: if (result.PowerOnHours is null or 0) result.PowerOnHours = (int)attr.RawValue; break;
                    case 12: if (result.PowerCycleCount == 0) result.PowerCycleCount = (int)attr.RawValue; break;
                    case 177: result.WearLevelingCount = (int)attr.RawValue; break;
                    case 194: if (result.Temperature is null or 0) result.Temperature = (int)(attr.RawValue & 0xFF); break;
                    case 197: result.PendingSectorCount = (int)attr.RawValue; break;
                    case 198: result.UncorrectableErrorCount = (int)attr.RawValue; break;
                    case 231: if (result.WearLevelingCount == 0) result.WearLevelingCount = (int)attr.RawValue; break;
                }
            }
            result.Attributes = attrList;
        }
        
        // Parse self-test log
        result.SelfTests = ParseAtaSelfTestLog(root);
        
        
        // Check for currently running self-test in ata_smart_data.self_test.status
        if (root.TryGetProperty("ata_smart_data", out var smartData) &&
            smartData.TryGetProperty("self_test", out var selfTest))
        {
            
            if (selfTest.TryGetProperty("status", out var status))
            {
                // ATA self-test status: high nibble is the status code,
                // low nibble is remaining tens of percent (0..10).
                var statusVal = status.TryGetProperty("value", out var sv) ? sv.GetInt32() : -1;
                var statusStr = status.TryGetProperty("string", out var ss) ? (ss.GetString() ?? "") : "";
                bool isInProgress = statusVal >= 0
                    ? statusVal is >= 0xF0 and <= 0xFF
                    : statusStr.Contains("in progress", StringComparison.OrdinalIgnoreCase);
                int? remainingPercent = statusVal is >= 0xF0 and <= 0xFA
                    ? (statusVal & 0x0F) * 10
                    : null;

                if (isInProgress)
                {
                    
                    // Test is running - find it in the log or create entry
                    if (result.SelfTests.Count > 0)
                    {
                        result.CurrentSelfTest = result.SelfTests[0];
                        result.CurrentSelfTest.Status = SmartaSelfTestStatus.InProgress;
                        if (remainingPercent.HasValue)
                        {
                            result.CurrentSelfTest.RemainingPercent = remainingPercent;
                        }
                    }
                    else
                    {
                        result.CurrentSelfTest = new SmartaSelfTestEntry
                        {
                            Status = SmartaSelfTestStatus.InProgress,
                            Type = SmartaSelfTestType.Unknown,
                            RemainingPercent = remainingPercent
                        };
                    }
                }
            }
            
            // Get polling time estimates for progress calculation
            int? shortMinutes = null;
            int? extendedMinutes = null;
            if (selfTest.TryGetProperty("polling_minutes", out var polling))
            {
                if (polling.TryGetProperty("short", out var sm)) shortMinutes = sm.GetInt32();
                if (polling.TryGetProperty("extended", out var em)) extendedMinutes = em.GetInt32();
            }
        }
        else
        {
        }
        
        // Fallback: check first entry in log for in-progress test
        if (result.CurrentSelfTest == null && result.SelfTests.Count > 0 && 
            result.SelfTests[0].Status == SmartaSelfTestStatus.InProgress)
        {
            result.CurrentSelfTest = result.SelfTests[0];
        }
        
        // Overall SMART failure is not evidence that a self-test is running.
    }

    private static void ValidateAtaDiagnostics(JsonElement root, SmartCheckResult result)
    {
        foreach (var group in result.Attributes.Where(a => a.Id != 0).GroupBy(a => a.Id))
        {
            if (group.Count() > 1)
                result.DiagnosticIssues.Add($"Duplicate ATA SMART attribute ID {group.Key}.");
        }

        foreach (var attr in result.Attributes.Where(a => a.Id is 5 or 197 or 198))
        {
            if (attr.RawValue > int.MaxValue)
                result.DiagnosticIssues.Add($"ATA SMART attribute {attr.Id} exceeds the supported counter range.");
        }

        if (root.TryGetProperty("ata_smart_data", out var data) &&
            data.TryGetProperty("self_test", out var selfTest) &&
            selfTest.TryGetProperty("status", out var current))
            ValidateAtaTestStatus(current, result);

        long? blocks = null;
        if (root.TryGetProperty("user_capacity", out var capacity) &&
            capacity.ValueKind == JsonValueKind.Object &&
            capacity.TryGetProperty("blocks", out var blockCount) &&
            blockCount.TryGetInt64(out var count) && count > 0)
            blocks = count;
        else if (result.TotalSize > 0 && root.TryGetProperty("logical_block_size", out var sectorSize) &&
                 sectorSize.TryGetInt64(out var size) && size > 0)
            blocks = result.TotalSize / size;

        if (root.TryGetProperty("ata_smart_self_test_log", out var log))
        {
            ValidateAtaLog(log, blocks, result);
            foreach (var name in new[] { "standard", "extended" })
                if (log.TryGetProperty(name, out var section))
                    ValidateAtaLog(section, blocks, result);
        }
    }

    private static void ValidateAtaLog(JsonElement log, long? blocks, SmartCheckResult result)
    {
        if (!log.TryGetProperty("table", out var table) || table.ValueKind != JsonValueKind.Array)
            return;
        foreach (var entry in table.EnumerateArray())
        {
            if (entry.TryGetProperty("status", out var status))
                ValidateAtaTestStatus(status, result);
            if (entry.TryGetProperty("remaining_percent", out var remaining) &&
                remaining.TryGetInt32(out var percent) && (percent < 0 || percent > 100))
                result.DiagnosticIssues.Add("ATA SMART self-test remaining percentage is outside 0–100.");
            if (blocks.HasValue && entry.TryGetProperty("lba", out var lba) &&
                lba.TryGetInt64(out var address) && address >= blocks.Value &&
                address != uint.MaxValue && address != 0xFFFFFFFFFFFFL)
                result.DiagnosticIssues.Add("ATA SMART self-test error LBA exceeds device capacity.");
        }
    }

    private static void ValidateAtaTestStatus(JsonElement status, SmartCheckResult result)
    {
        if (status.ValueKind != JsonValueKind.Object)
            return;
        if (status.TryGetProperty("value", out var raw) && raw.TryGetInt32(out var value) &&
            (value < 0 || value > 255 || (value & 0x0F) > 10))
            result.DiagnosticIssues.Add("Invalid ATA SMART self-test status or remaining percentage.");
        if (status.TryGetProperty("remaining_percent", out var remaining) &&
            remaining.TryGetInt32(out var percent) && (percent < 0 || percent > 100))
            result.DiagnosticIssues.Add("ATA SMART self-test remaining percentage is outside 0–100.");
    }

    private static uint GetRawValue(JsonElement item)
    {
        if (!item.TryGetProperty("raw", out var raw))
            return 0;

        // smartctl JSON exposes two different views of the ATA raw bytes:
        //   raw.value  = numeric interpretation of the vendor raw bytes
        //   raw.string = the human SMART RAW_VALUE column, e.g. "3 (0 1)"
        // For counters such as Reallocated_Sector_Ct (5), Current_Pending_Sector
        // (197) and Offline_Uncorrectable (198), the UI must use the displayed
        // counter, not the packed/vendor bytes.  Otherwise "3 (0 1)" becomes
        // 0x00010003 = 65539.  Prefer the leading integer from raw.string when
        // available and fall back to raw.value for older smartctl output.
        if (raw.TryGetProperty("string", out var strEl) && strEl.ValueKind == JsonValueKind.String)
        {
            var text = strEl.GetString()?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                var i = 0;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                var start = i;
                while (i < text.Length && char.IsDigit(text[i])) i++;
                if (i > start && ulong.TryParse(text[start..i], out var parsed))
                    return (uint)Math.Min(parsed, uint.MaxValue);
            }
        }

        if (raw.TryGetProperty("value", out var val))
        {
            if (val.TryGetInt64(out var l)) return (uint)Math.Clamp(l, 0, uint.MaxValue);
            if (val.TryGetUInt64(out var u)) return (uint)Math.Min(u, uint.MaxValue);
        }
        return 0;
    }
    
    private static List<SmartaSelfTestEntry> ParseAtaSelfTestLog(JsonElement root)
    {
        var tests = new List<SmartaSelfTestEntry>();
        
        // Try standard structure first: ata_smart_self_test_log.standard.table
        if (root.TryGetProperty("ata_smart_self_test_log", out var log))
        {
            JsonElement? tableElement = null;
            
            // Check for standard.table (most common)
            if (log.TryGetProperty("standard", out var standard) &&
                standard.TryGetProperty("table", out var stdTable))
            {
                tableElement = stdTable;
            }
            // Fallback: direct table (older format)
            else if (log.TryGetProperty("table", out var directTable))
            {
                tableElement = directTable;
            }
            
            if (tableElement.HasValue)
            {
                foreach (var t in tableElement.Value.EnumerateArray())
                {
                    // Type can be either a string or an object with "string" property
                    string? typeStr = null;
                    if (t.TryGetProperty("type", out var typeEl))
                    {
                        if (typeEl.ValueKind == JsonValueKind.String)
                            typeStr = typeEl.GetString();
                        else if (typeEl.ValueKind == JsonValueKind.Object && typeEl.TryGetProperty("string", out var typeStrEl))
                            typeStr = typeStrEl.GetString();
                    }
                    
                    // Status can be either a string or an object with "string" property
                    string? statusStr = null;
                    if (t.TryGetProperty("status", out var statusEl))
                    {
                        if (statusEl.ValueKind == JsonValueKind.String)
                            statusStr = statusEl.GetString();
                        else if (statusEl.ValueKind == JsonValueKind.Object && statusEl.TryGetProperty("string", out var statusStrEl))
                            statusStr = statusStrEl.GetString();
                    }
                    
                    var entry = new SmartaSelfTestEntry
                    {
                        Type = ParseTestType(typeStr),
                        Status = ParseTestStatus(statusStr)
                    };
                    if (t.TryGetProperty("num", out var num)) entry.Number = num.GetInt32();
                    if (t.TryGetProperty("lifetime_hours", out var hrs)) entry.LifeTimeHours = hrs.GetInt32();
                    
                    // Extract remaining percent from status value if present
                    // ATA self-test log can have status.value with % remaining info
                    if (t.TryGetProperty("status", out var statusObj) && 
                        statusObj.ValueKind == JsonValueKind.Object &&
                        statusObj.TryGetProperty("value", out var statusVal))
                    {
                        var val = statusVal.GetInt32();
                        // Low nibble encodes remaining tens of percent.
                        if (val > 0 && val <= 255)
                        {
                            entry.RemainingPercent = (val & 0x0F) <= 10 ? (val & 0x0F) * 10 : null;
                        }
                    }
                    
                    if (t.TryGetProperty("lba", out var lba) && lba.TryGetInt64(out var address))
                        entry.LbaOfFirstError = address;
                    tests.Add(entry);
                }
            }
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
        
        // Completed successfully - various formats
        if (lower.Contains("completed without error") || 
            lower.Contains("passed") || 
            lower.Contains("success"))
            return SmartaSelfTestStatus.CompletedWithoutError;
        
        // In progress - various formats
        if (lower.Contains("in progress") || 
            lower.Contains("running") || 
            lower.Contains("incomplete") ||
            lower.Contains("being executed"))
            return SmartaSelfTestStatus.InProgress;
        
        // Aborted
        if (lower.Contains("aborted") || lower.Contains("interrupted"))
        {
            if (lower.Contains("host")) return SmartaSelfTestStatus.AbortedByHost;
            return SmartaSelfTestStatus.AbortedByUser;
        }
        
        // Error conditions
        if (lower.Contains("fatal error") || lower.Contains("error fatal") || lower.Contains("fatal or unknown error"))
            return SmartaSelfTestStatus.FatalError;
        if (lower.Contains("electrical"))
            return SmartaSelfTestStatus.ErrorElectrical;
        if (lower.Contains("servo") || lower.Contains("mechanical"))
            return SmartaSelfTestStatus.ErrorServo;
        if (lower.Contains("read error") || lower.Contains("error reading") || lower.Contains("read failure"))
            return SmartaSelfTestStatus.ErrorRead;
        if (lower.Contains("handling"))
            return SmartaSelfTestStatus.ErrorHandling;
        
        return SmartaSelfTestStatus.Unknown;
    }
    
    private static void ParseNvmeData(JsonElement root, SmartCheckResult result)
    {
        if (!root.TryGetProperty("nvme_smart_health_information_log", out var nvme)) return;
        
        
        // Temperature - only use NVMe health log temperature if not already set from top-level
        // NVMe health log reports Kelvin, convert to Celsius
        if (result.Temperature == null && nvme.TryGetProperty("temperature", out var temp))
        {
            var kelvin = temp.GetInt32();
            result.Temperature = kelvin > 273 ? kelvin - 273 : kelvin;
        }
        
        // Power on hours
        if (nvme.TryGetProperty("power_on_hours", out var hrs))
            result.PowerOnHours = hrs.GetInt32();
        
        // Power cycles
        if (nvme.TryGetProperty("power_cycles", out var cycles))
            result.PowerCycleCount = cycles.GetInt32();
        
        // NVMe health counters
        if (nvme.TryGetProperty("available_spare", out var spare))
            result.AvailableSparePercent = spare.GetInt32();

        // Percentage used (NVMe endurance consumed)
        if (nvme.TryGetProperty("percentage_used", out var pctUsed))
        {
            result.EnduranceUsedPercent = pctUsed.GetInt32();
            result.WearLevelingCount = Math.Max(0, 100 - pctUsed.GetInt32());
        }
        
        // Media/Data Integrity errors
        if (nvme.TryGetProperty("media_errors", out var errors))
        {
            var value = (int)Math.Min(int.MaxValue, errors.GetInt64());
            result.MediaErrors = value;
            // Keep compatibility with legacy UI/certificate fields.
            result.UncorrectableErrorCount = value;
        }

        if (nvme.TryGetProperty("unsafe_shutdowns", out var unsafeShutdowns))
            result.UnsafeShutdowns = (int)Math.Min(int.MaxValue, unsafeShutdowns.GetInt64());
        
        // Build pseudo attributes for NVMe so the critical-attributes grid has the same data source.
        var attrs = new List<SmartaAttributeItem>();
        AddAttr(attrs, 194, "Temperature", result.Temperature);
        AddAttr(attrs, 9, "Power On Hours", result.PowerOnHours);
        AddAttr(attrs, 12, "Power Cycles", result.PowerCycleCount);
        AddAttr(attrs, 170, "Available Spare", result.AvailableSparePercent);
        AddAttr(attrs, 177, "Percentage Used", result.EnduranceUsedPercent);
        AddAttr(attrs, 198, "Media/Data Integrity Errors", result.MediaErrors);
        AddAttr(attrs, 174, "Unsafe Shutdowns", result.UnsafeShutdowns);
        result.Attributes = attrs;
        
        result.SelfTests = ParseNvmeSelfTestLog(root);
        
        // Check for currently running self-test in NVMe log
        // The structure from smartctl is:
        // "nvme_self_test_log": { "current_self_test_operation": { "value": 0, "string": "No self-test in progress" } }
        if (root.TryGetProperty("nvme_self_test_log", out var log))
        {
            
            // Try new format first: current_self_test_operation
            if (log.TryGetProperty("current_self_test_operation", out var currentOp))
            {
                
                var entry = new SmartaSelfTestEntry { Number = 0 };
                
                if (currentOp.TryGetProperty("value", out var valueEl))
                {
                    var value = valueEl.GetInt32();
                    
                    // NVMe spec: value 0 = No test running, 1 = Short test in progress, 2 = Extended test in progress
                    // When value > 0, test is in progress
                    if (value > 0)
                    {
                        entry.Status = SmartaSelfTestStatus.InProgress;
                        
                        // Try to get completion percentage (some smartctl versions report this)
                        if (currentOp.TryGetProperty("completion_percent", out var compPct))
                        {
                            var completion = compPct.GetInt32();
                            entry.RemainingPercent = 100 - completion;
                        }
                        
                        // Get test type from value
                        entry.Type = value switch
                        {
                            1 => SmartaSelfTestType.ShortTest,
                            2 => SmartaSelfTestType.Extended,
                            _ => SmartaSelfTestType.Unknown
                        };
                        
                        result.CurrentSelfTest = entry;
                        
                        // Add to self-tests list as first entry
                        if (result.SelfTests.Count == 0 || result.SelfTests[0].Status != SmartaSelfTestStatus.InProgress)
                        {
                            result.SelfTests.Insert(0, entry);
                        }
                    }
                    // value = 0 means no test running, check string for more info
                    else if (currentOp.TryGetProperty("string", out var stringEl))
                    {
                        var statusString = stringEl.GetString() ?? "";
                        
                        // If string contains "in progress", test is running
                        if (statusString.ToLowerInvariant().Contains("in progress"))
                        {
                            entry.Status = SmartaSelfTestStatus.InProgress;
                            
                            // Try to extract percentage from string like "Self-test in progress, 50% complete"
                            var match = System.Text.RegularExpressions.Regex.Match(statusString, @"(\d+)\s*%");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
                            {
                                entry.RemainingPercent = 100 - pct;
                            }
                            
                            result.CurrentSelfTest = entry;
                        }
                    }
                }
            }
            // Legacy format: current_self_test (older smartctl versions)
            else if (log.TryGetProperty("current_self_test", out var currentTest))
            {
                
                var entry = new SmartaSelfTestEntry { Number = 0 };
                
                if (currentTest.TryGetProperty("self_test_status", out var status) &&
                    status.TryGetProperty("value", out var statusVal))
                {
                    var statusCode = statusVal.GetInt32();
                    
                    // CRITICAL: status.value=0 means NO TEST RUNNING, not "completed without error"
                    entry.Status = statusCode switch
                    {
                        0 => SmartaSelfTestStatus.NoTest,  // No test running
                        1 => SmartaSelfTestStatus.InProgress,
                        2 => SmartaSelfTestStatus.AbortedByUser,
                        3 => SmartaSelfTestStatus.AbortedByHost,
                        _ => SmartaSelfTestStatus.Unknown
                    };
                }
                
                if (currentTest.TryGetProperty("self_test_completion", out var completion))
                {
                    var completionPct = completion.GetInt32();
                    entry.RemainingPercent = 100 - completionPct;
                }
                
                if (entry.Status == SmartaSelfTestStatus.InProgress)
                {
                    result.CurrentSelfTest = entry;
                }
            }
        }
        
        // Also check first entry in log for in-progress test (older smartctl versions)
        if (result.CurrentSelfTest == null && result.SelfTests.Count > 0)
        {
            var firstEntry = result.SelfTests[0];
            if (firstEntry.Status == SmartaSelfTestStatus.InProgress)
            {
                result.CurrentSelfTest = firstEntry;
            }
        }
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
        
        
        // Check for current self-test operation (new format: current_self_test_operation)
        if (log.TryGetProperty("current_self_test_operation", out var currentOp))
        {
            
            if (currentOp.TryGetProperty("value", out var valueEl))
            {
                var value = valueEl.GetInt32();
                
                // value > 0 means test is in progress (1=Short, 2=Extended, etc.)
                if (value > 0)
                {
                    var currentEntry = new SmartaSelfTestEntry { Number = 0, Status = SmartaSelfTestStatus.InProgress };
                    
                    // Get test type from value
                    currentEntry.Type = value switch
                    {
                        1 => SmartaSelfTestType.ShortTest,
                        2 => SmartaSelfTestType.Extended,
                        _ => SmartaSelfTestType.Unknown
                    };
                    
                    // Try to get completion percentage
                    if (currentOp.TryGetProperty("completion_percent", out var compPct))
                    {
                        var completion = compPct.GetInt32();
                        currentEntry.RemainingPercent = 100 - completion;
                    }
                    
                    tests.Insert(0, currentEntry);
                }
            }
            
            // Also check string field for "No self-test in progress" or "Self-test in progress"
            if (currentOp.TryGetProperty("string", out var stringEl))
            {
                var statusString = stringEl.GetString() ?? "";
                
                // If text contains "in progress" but we haven't added a test yet
                if (statusString.ToLowerInvariant().Contains("in progress") && tests.Count == 0)
                {
                    var currentEntry = new SmartaSelfTestEntry { Number = 0, Status = SmartaSelfTestStatus.InProgress };
                    
                    // Extract percentage from string like "Self-test in progress, 50% complete"
                    var match = System.Text.RegularExpressions.Regex.Match(statusString, @"(\d+)\s*%");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
                    {
                        currentEntry.RemainingPercent = 100 - pct;
                    }
                    
                    tests.Insert(0, currentEntry);
                }
            }
        }
        // Legacy format: current_self_test (older smartctl versions)
        else if (log.TryGetProperty("current_self_test", out var currentTest))
        {
            
            var currentEntry = new SmartaSelfTestEntry { Number = 0 };
            bool isInProgress = false;
            
            if (currentTest.TryGetProperty("self_test_status", out var status) &&
                status.TryGetProperty("value", out var statusVal))
            {
                var statusCode = statusVal.GetInt32();
                
                // 0 = No test running, 1 = In progress, 2 = Aborted by user, etc.
                currentEntry.Status = statusCode switch
                {
                    0 => SmartaSelfTestStatus.NoTest,
                    1 => SmartaSelfTestStatus.InProgress,
                    2 => SmartaSelfTestStatus.AbortedByUser,
                    3 => SmartaSelfTestStatus.AbortedByHost,
                    _ => SmartaSelfTestStatus.Unknown
                };
                
                isInProgress = statusCode > 0;
            }
            
            if (currentTest.TryGetProperty("self_test_completion", out var completion))
            {
                var completionPct = completion.GetInt32();
                currentEntry.RemainingPercent = Math.Max(0, 100 - completionPct);
            }
            
            if (currentTest.TryGetProperty("self_test_code", out var code) &&
                code.TryGetProperty("value", out var codeVal))
            {
                currentEntry.Type = codeVal.GetInt32() switch
                {
                    1 => SmartaSelfTestType.ShortTest,
                    2 => SmartaSelfTestType.Extended,
                    _ => SmartaSelfTestType.Unknown
                };
            }
            
            if (isInProgress)
            {
                tests.Insert(0, currentEntry);
            }
        }
        
        // Parse historical self-tests from table
        if (log.TryGetProperty("table", out var table))
        {
            
            foreach (var t in table.EnumerateArray())
            {
                var entry = new SmartaSelfTestEntry();
                
                // Test type
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
                
                // Test result (completed, aborted, etc.)
                if (t.TryGetProperty("self_test_result", out var res) && 
                    res.TryGetProperty("value", out var rv))
                {
                    // NVMe result codes: 0=Completed, 1=Aborted by self-test command, 2=Aborted by reset
                    entry.Status = rv.GetInt32() switch
                    {
                        0 => SmartaSelfTestStatus.CompletedWithoutError,
                        1 => SmartaSelfTestStatus.AbortedByUser,
                        2 => SmartaSelfTestStatus.AbortedByHost,
                        _ => SmartaSelfTestStatus.Unknown
                    };
                }
                
                if (t.TryGetProperty("power_on_hours", out var hrs)) 
                    entry.LifeTimeHours = hrs.GetInt32();
                
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
        // Detect failing attributes
        var failingAttrs = new List<string>();
        var smartOverallFailed = result.SmartStatusKnown && !result.IsHealthy;
        bool isFailing = smartOverallFailed;
        string failurePrediction = string.Empty;

        if (result.Attributes != null)
        {
            foreach (var attr in result.Attributes)
            {
                if (!attr.IsOk && !string.IsNullOrWhiteSpace(attr.WhenFailed))
                {
                    isFailing = true;
                    var failType = attr.WhenFailed.Contains("FAILING_NOW", StringComparison.OrdinalIgnoreCase)
                        ? "FAILING_NOW"
                        : attr.WhenFailed.Contains("In_the_past", StringComparison.OrdinalIgnoreCase)
                            ? "In_the_past"
                            : attr.WhenFailed;
                    failingAttrs.Add($"{attr.Name} (ID {attr.Id}): {failType} — value={attr.Value}, threshold={attr.Threshold}");
                }
            }
        }

        // Build failure prediction message
        if (isFailing)
        {
            var failingNowCount = failingAttrs.Count(a => a.Contains("FAILING_NOW"));
            if (failingNowCount > 0)
            {
                failurePrediction = failingNowCount == 1
                    ? $"⚠️ SMART hlásí kritické varování — atribut {failingAttrs.First(a => a.Contains("FAILING_NOW")).Split(':')[0]} je v kritickém stavu. ZÁLOHUJTE NYNÍ!"
                    : $"⚠️ Disk selže do 24 hodin — {failingNowCount} atributů v kritickém stavu. ZÁLOHUJTE NYNÍ!";
            }
            else if (failingAttrs.Any(a => a.Contains("In_the_past")))
            {
                failurePrediction = $"⚠️ Disk měl kritické atributy v minulosti — {failingAttrs.Count} atributů pod thresholdem. Zvažte výměnu.";
            }
            else if (smartOverallFailed)
            {
                failurePrediction = "⚠️ SMART overall-health: FAILED! Disk je v kritickém stavu. Zálohujte data a vyměňte disk.";
            }
        }

        var invalid = result.DiagnosticIssues.Count > 0;
        if (invalid)
            failurePrediction = "SMART diagnostic data is inconsistent. Drive health is undetermined; test independently and do not rely on this drive for important data.";

        return new SmartaData
        {
            DeviceModel = result.DeviceModel ?? "",
            SerialNumber = result.SerialNumber ?? "",
            FirmwareVersion = result.FirmwareVersion ?? "",
            DeviceType = result.DeviceType ?? "Unknown",
            IsDiagnosticInvalid = invalid,
            DiagnosticIssues = new List<string>(result.DiagnosticIssues),
            IsHealthy = !invalid && (result.SmartStatusKnown ? result.IsHealthy : true),
            IsSmartAvailable = result.SmartAvailable ?? true,
            IsFailing = invalid || isFailing,
            FailurePrediction = failurePrediction,
            FailingAttributes = failingAttrs,
            SmartEnabled = result.IsEnabled,
            Temperature = invalid ? null : result.Temperature,
            PowerOnHours = invalid ? null : result.PowerOnHours,
            PowerCycleCount = invalid ? 0 : result.PowerCycleCount,
            ReallocatedSectorCount = invalid ? null : result.ReallocatedSectorCount,
            PendingSectorCount = invalid ? null : result.PendingSectorCount,
            UncorrectableErrorCount = invalid ? null : result.UncorrectableErrorCount,
            WearLevelingCount = invalid ? null : result.WearLevelingCount,
            AvailableSpare = result.AvailableSparePercent,
            PercentageUsed = result.EnduranceUsedPercent,
            MediaErrors = result.MediaErrors,
            UnsafeShutdowns = result.UnsafeShutdowns,
            Attributes = result.Attributes ?? new List<SmartaAttributeItem>(),
            SelfTests = result.SelfTests ?? new List<SmartaSelfTestEntry>()
        };
    }
    
    private enum DeviceType { ATA, NVMe, SCSI }
}

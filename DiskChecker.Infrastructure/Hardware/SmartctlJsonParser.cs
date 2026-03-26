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
        catch (Exception ex)
        {
            Console.WriteLine($"[SmartctlJsonParser] Parse ERROR: {ex.Message}");
            Console.WriteLine($"[SmartctlJsonParser] JSON preview: {(json.Length > 500 ? json.Substring(0, 500) : json)}");
            return null;
        }
    }
    
    public static SmartCheckResult? Parse(JsonElement root)
    {
        try
        {
            var result = new SmartCheckResult();
            
            // Debug: Log JSON structure keys
            var rootKeys = string.Join(", ", root.EnumerateObject().Select(p => p.Name).Take(20));
            Console.WriteLine($"[SmartctlJsonParser] JSON root properties: {rootKeys}");
            
            // Detect device type
            var deviceType = GetDeviceType(root);
            result.DeviceType = deviceType switch
            {
                DeviceType.NVMe => "NVMe",
                DeviceType.SCSI => "SCSI/SAS",
                _ => "SATA/ATA"
            };
            
            Console.WriteLine($"[SmartctlJsonParser] DeviceType detected: {result.DeviceType}");
            
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
        catch (Exception ex)
        {
            Console.WriteLine($"[SmartctlJsonParser] Parse(JsonElement) ERROR: {ex.Message}");
            Console.WriteLine($"[SmartctlJsonParser] Stack: {ex.StackTrace}");
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
                    WhenFailed = item.TryGetProperty("when_failed", out var failed) && failed.ValueKind != JsonValueKind.Null
                        ? failed.GetString() ?? string.Empty
                        : string.Empty
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
        
        Console.WriteLine($"[SmartctlJsonParser] ParseAtaData: SelfTests.Count = {result.SelfTests.Count}");
        
        // Check for currently running self-test in ata_smart_data.self_test.status
        if (root.TryGetProperty("ata_smart_data", out var smartData) &&
            smartData.TryGetProperty("self_test", out var selfTest))
        {
            Console.WriteLine("[SmartctlJsonParser] Found ata_smart_data.self_test");
            
            if (selfTest.TryGetProperty("status", out var status))
            {
                // status.value tells us if test is running
                // According to ATA spec:
                // 0x00 = No error, test completed or no test running
                // Non-zero values indicate test in progress or error conditions
                // Values 240-255 indicate % remaining (lower 7 bits = %)
                // Bits 7-8 indicate test type (short/extended/conveyance)
                
                var statusVal = status.TryGetProperty("value", out var sv) ? sv.GetInt32() : 0;
                var statusStr = status.TryGetProperty("string", out var ss) ? (ss.GetString() ?? "") : "";
                
                Console.WriteLine($"[SmartctlJsonParser] SELF_TEST: value={statusVal}, string='{statusStr}'");
                
                // NON-ZERO value means something is happening
                // According to smartctl source code and ATA spec:
                // - Value 0 = no test running, no error
                // - Value > 0 = test in progress OR error from last test
                // The lower 7 bits indicate % remaining for in-progress tests
                // String contains "in progress" for running tests
                
                bool isInProgress = false;
                int remainingPercent = 0;
                
                // Method 1: String contains "in progress"
                if (!string.IsNullOrEmpty(statusStr) && 
                    (statusStr.ToLowerInvariant().Contains("in progress") ||
                     statusStr.ToLowerInvariant().Contains("running") ||
                     statusStr.ToLowerInvariant().Contains("incomplete")))
                {
                    isInProgress = true;
                    Console.WriteLine("[SmartctlJsonParser] Method 1: String indicates in-progress");
                }
                // Method 2: Value is non-zero (test running OR previous test error)
                // For in-progress tests, value is typically 240-255 where lower bits = % remaining
                else if (statusVal > 0)
                {
                    // Check if this is an in-progress test
                    // Values 240-255 with % remaining in lower bits
                    if (statusVal >= 240 && statusVal <= 255)
                    {
                        remainingPercent = statusVal & 0x7F; // Lower 7 bits = % remaining (0-100)
                        isInProgress = true;
                        Console.WriteLine($"[SmartctlJsonParser] Method 2: Value {statusVal} indicates in-progress with {remainingPercent}% remaining");
                    }
                    // Any non-zero value could indicate in-progress or error
                    // If string exists, it's describing the status
                    else if (!string.IsNullOrEmpty(statusStr))
                    {
                        // Non-zero with string = probably in-progress or error
                        // String will tell us which
                        if (!statusStr.ToLowerInvariant().Contains("error") &&
                            !statusStr.ToLowerInvariant().Contains("abort") &&
                            !statusStr.ToLowerInvariant().Contains("fail"))
                        {
                            isInProgress = true;
                            Console.WriteLine($"[SmartctlJsonParser] Method 2b: Non-zero value {statusVal} with non-error string, treating as in-progress");
                        }
                    }
                }
                
                if (isInProgress)
                {
                    Console.WriteLine($"[SmartctlJsonParser] Setting CurrentSelfTest to InProgress, remaining={remainingPercent}%");
                    
                    // Test is running - find it in the log or create entry
                    if (result.SelfTests.Count > 0)
                    {
                        result.CurrentSelfTest = result.SelfTests[0];
                        result.CurrentSelfTest.Status = SmartaSelfTestStatus.InProgress;
                        if (remainingPercent > 0)
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
            Console.WriteLine("[SmartctlJsonParser] NO ata_smart_data.self_test found");
        }
        
        // Fallback: check first entry in log for in-progress test
        if (result.CurrentSelfTest == null && result.SelfTests.Count > 0 && 
            result.SelfTests[0].Status == SmartaSelfTestStatus.InProgress)
        {
            Console.WriteLine("[SmartctlJsonParser] Fallback: Found in-progress test in self-test log");
            result.CurrentSelfTest = result.SelfTests[0];
        }
        
        // Additional fallback: check if passed=false (test running) 
        // smartctl sets smart_status.passed=false during test execution
        if (result.CurrentSelfTest == null && 
            root.TryGetProperty("smart_status", out var smartStatus) &&
            smartStatus.TryGetProperty("passed", out var passed) &&
            !passed.GetBoolean())
        {
            Console.WriteLine("[SmartctlJsonParser] Fallback 2: smart_status.passed=false, creating InProgress entry");
            result.CurrentSelfTest = new SmartaSelfTestEntry
            {
                Status = SmartaSelfTestStatus.InProgress,
                Type = SmartaSelfTestType.Unknown
            };
        }
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
                        // Lower 7 bits indicate % remaining for in-progress tests
                        if (val > 0 && val <= 255)
                        {
                            entry.RemainingPercent = val & 0x7F;
                            Console.WriteLine($"[SmartctlJsonParser] ATA self-test entry: status.value={val}, remaining={entry.RemainingPercent}%");
                        }
                    }
                    
                    tests.Add(entry);
                }
            }
        }
        
        Console.WriteLine($"[SmartctlJsonParser] ParseAtaSelfTestLog: Found {tests.Count} entries");
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
            lower.Contains("completed") && !lower.Contains("in progress") ||
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
        if (lower.Contains("fatal error") || lower.Contains("error fatal"))
            return SmartaSelfTestStatus.FatalError;
        if (lower.Contains("electrical"))
            return SmartaSelfTestStatus.ErrorElectrical;
        if (lower.Contains("servo") || lower.Contains("mechanical"))
            return SmartaSelfTestStatus.ErrorServo;
        if (lower.Contains("read error") || lower.Contains("error reading"))
            return SmartaSelfTestStatus.ErrorRead;
        if (lower.Contains("handling"))
            return SmartaSelfTestStatus.ErrorHandling;
        
        return SmartaSelfTestStatus.Unknown;
    }
    
    private static void ParseNvmeData(JsonElement root, SmartCheckResult result)
    {
        if (!root.TryGetProperty("nvme_smart_health_information_log", out var nvme)) return;
        
        Console.WriteLine("[SmartctlJsonParser] ParseNvmeData: Found nvme_smart_health_information_log");
        
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
        if (nvme.TryGetProperty("percentage_used", out var pctUsed))
            result.WearLevelingCount = 100 - pctUsed.GetInt32();
        
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
        
        // Check for currently running self-test in NVMe log
        // The structure from smartctl is:
        // "nvme_self_test_log": { "current_self_test_operation": { "value": 0, "string": "No self-test in progress" } }
        if (root.TryGetProperty("nvme_self_test_log", out var log))
        {
            Console.WriteLine("[SmartctlJsonParser] ParseNvmeData: Found nvme_self_test_log");
            
            // Try new format first: current_self_test_operation
            if (log.TryGetProperty("current_self_test_operation", out var currentOp))
            {
                Console.WriteLine("[SmartctlJsonParser] ParseNvmeData: Found current_self_test_operation");
                
                var entry = new SmartaSelfTestEntry { Number = 0 };
                
                if (currentOp.TryGetProperty("value", out var valueEl))
                {
                    var value = valueEl.GetInt32();
                    Console.WriteLine($"[SmartctlJsonParser] ParseNvmeData: current_self_test_operation.value = {value}");
                    
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
                            Console.WriteLine($"[SmartctlJsonParser] ParseNvmeData: completion = {completion}%, remaining = {entry.RemainingPercent}%");
                        }
                        
                        // Get test type from value
                        entry.Type = value switch
                        {
                            1 => SmartaSelfTestType.ShortTest,
                            2 => SmartaSelfTestType.Extended,
                            _ => SmartaSelfTestType.Unknown
                        };
                        
                        Console.WriteLine($"[SmartctlJsonParser] ParseNvmeData: Test IN PROGRESS, type = {entry.Type}");
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
                        Console.WriteLine($"[SmartctlJsonParser] ParseNvmeData: current_self_test_operation.string = '{statusString}'");
                        
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
                            
                            Console.WriteLine($"[SmartctlJsonParser] ParseNvmeData: Found 'in progress' in string, setting InProgress");
                            result.CurrentSelfTest = entry;
                        }
                    }
                }
            }
            // Legacy format: current_self_test (older smartctl versions)
            else if (log.TryGetProperty("current_self_test", out var currentTest))
            {
                Console.WriteLine("[SmartctlJsonParser] ParseNvmeData: Found current_self_test (legacy format)");
                
                var entry = new SmartaSelfTestEntry { Number = 0 };
                
                if (currentTest.TryGetProperty("self_test_status", out var status) &&
                    status.TryGetProperty("value", out var statusVal))
                {
                    var statusCode = statusVal.GetInt32();
                    Console.WriteLine($"[SmartctlJsonParser] ParseNvmeData: self_test_status.value = {statusCode}");
                    
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
        
        Console.WriteLine("[SmartctlJsonParser] ParseNvmeSelfTestLog: Found nvme_self_test_log");
        
        // Check for current self-test operation (new format: current_self_test_operation)
        if (log.TryGetProperty("current_self_test_operation", out var currentOp))
        {
            Console.WriteLine("[SmartctlJsonParser] ParseNvmeSelfTestLog: Found current_self_test_operation");
            
            if (currentOp.TryGetProperty("value", out var valueEl))
            {
                var value = valueEl.GetInt32();
                Console.WriteLine($"[SmartctlJsonParser] ParseNvmeSelfTestLog: current_self_test_operation.value = {value}");
                
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
                    
                    Console.WriteLine($"[SmartctlJsonParser] ParseNvmeSelfTestLog: Test IN PROGRESS, type={currentEntry.Type}, remaining={currentEntry.RemainingPercent}%");
                    tests.Insert(0, currentEntry);
                }
            }
            
            // Also check string field for "No self-test in progress" or "Self-test in progress"
            if (currentOp.TryGetProperty("string", out var stringEl))
            {
                var statusString = stringEl.GetString() ?? "";
                Console.WriteLine($"[SmartctlJsonParser] ParseNvmeSelfTestLog: current_self_test_operation.string = '{statusString}'");
                
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
            Console.WriteLine("[SmartctlJsonParser] ParseNvmeSelfTestLog: Found current_self_test (legacy format)");
            
            var currentEntry = new SmartaSelfTestEntry { Number = 0 };
            bool isInProgress = false;
            
            if (currentTest.TryGetProperty("self_test_status", out var status) &&
                status.TryGetProperty("value", out var statusVal))
            {
                var statusCode = statusVal.GetInt32();
                Console.WriteLine($"[SmartctlJsonParser] ParseNvmeSelfTestLog: self_test_status.value = {statusCode}");
                
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
                Console.WriteLine($"[SmartctlJsonParser] ParseNvmeSelfTestLog: Test IN PROGRESS (legacy), type={currentEntry.Type}");
                tests.Insert(0, currentEntry);
            }
        }
        
        // Parse historical self-tests from table
        if (log.TryGetProperty("table", out var table))
        {
            Console.WriteLine("[SmartctlJsonParser] ParseNvmeSelfTestLog: Parsing self_test_log table");
            
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
        
        Console.WriteLine($"[SmartctlJsonParser] ParseNvmeSelfTestLog: Found {tests.Count} entries total");
        
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
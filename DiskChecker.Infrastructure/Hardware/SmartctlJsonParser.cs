using DiskChecker.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

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
            if (!root.TryGetProperty("smartctl", out var smartctl) || 
                !smartctl.TryGetProperty("smart_status", out var smartStatus))
            {
                return null;
            }
            
            var result = new SmartCheckResult
            {
                IsEnabled = smartctl.TryGetProperty("smart_enabled", out var enabled) && enabled.GetBoolean(),
                IsHealthy = smartStatus.TryGetProperty("passed", out var passed) && passed.GetBoolean(),
                TestPassed = smartStatus.TryGetProperty("passed", out var passed2) && passed2.GetBoolean()
            };
            
            // Parse power on hours
            if (root.TryGetProperty("power_on_time", out var powerOn) && 
                powerOn.TryGetProperty("hours", out var hours))
            {
                result.PowerOnHours = hours.ValueKind == JsonValueKind.Number ? hours.GetInt32() : (int?)null;
            }
            
            // Parse attributes
            result.Attributes = ParseAttributes(root);
            
            // Extract key metrics from attributes
            foreach (var attr in result.Attributes)
            {
                if (attr.Id == 5) result.ReallocatedSectorCount = (int?)(attr.RawValue ?? 0);
                if (attr.Id == 197) result.PendingSectorCount = (int?)(attr.RawValue ?? 0);
                if (attr.Id == 198) result.UncorrectableErrorCount = (int?)(attr.RawValue ?? 0);
                if (attr.Id == 194) result.Temperature = (int?)(attr.RawValue.HasValue ? attr.RawValue.Value & 0xFF : 0);
                if (attr.Id == 231) result.WearLevelingCount = (int?)(attr.RawValue ?? 0);
            }
            
            // Parse self tests
            result.SelfTests = ParseSelfTestLog(root);
            
            // Parse current self test status
            if (root.TryGetProperty("ata_smart_self_test_log", out var selfTest2) &&
                selfTest2.TryGetProperty("current_test", out var currentTest))
            {
                var statusValue = "";
                var remainingPercent = 0;
                
                if (currentTest.TryGetProperty("status", out var status) && 
                    status.TryGetProperty("string", out var str))
                {
                    statusValue = str.GetString().ToSafeString();
                }
                
                if (currentTest.TryGetProperty("percent", out var pct) && pct.ValueKind == JsonValueKind.Number)
                {
                    remainingPercent = pct.GetInt32();
                }
                
                var isRunning = statusValue.Contains("Self-test routine in progress");
                result.CurrentSelfTest = new SmartaSelfTestEntry
                {
                    Status = isRunning ? SmartaSelfTestStatus.InProgress : 
                        ParseSelfTestStatus(statusValue),
                    RemainingPercent = remainingPercent,
                    CompletedAt = DateTime.UtcNow.ToString("o")
                };
            }
            
            return result;
        }
        catch (Exception)
        {
            // Handle exception properly
            return null;
        }
    }
    
    public static List<SmartaAttributeItem> ParseAttributes(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseAttributes(doc.RootElement);
        }
        catch
        {
            return new List<SmartaAttributeItem>();
        }
    }
    
    public static List<SmartaSelfTestEntry> ParseSelfTestLog(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseSelfTestLog(doc.RootElement);
        }
        catch
        {
            return new List<SmartaSelfTestEntry>();
        }
    }
    
    public static List<SmartaAttributeItem> ParseAttributes(JsonElement root)
    {
        var attrs = new List<SmartaAttributeItem>();
        
        if (root.TryGetProperty("ata_smart_attributes", out var attrsElement) &&
            attrsElement.TryGetProperty("table", out var table))
        {
            foreach (var item in table.EnumerateArray())
            {
                var attribute = item; // For clarity
                var attr = new SmartaAttributeItem
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    Name = item.TryGetProperty("name", out var name) ? name.GetString().ToSafeString() : string.Empty,
                    Value = item.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number ? val.GetInt32() : 0,
                    Worst = item.TryGetProperty("worst", out var worst) && worst.ValueKind == JsonValueKind.Number ? worst.GetInt32() : 0,
                    Threshold = item.TryGetProperty("thresh", out var thresh) && thresh.ValueKind == JsonValueKind.Number ? thresh.GetInt32() : 0,
                    RawValue = (uint)(item.TryGetProperty("raw", out var raw) && raw.TryGetInt64(out var r) ? r : 0),
                    IsOk = item.TryGetProperty("when_failed", out var whenFailed) && whenFailed.ValueKind != JsonValueKind.Null 
                        ? false 
                        : true,
                    Current = (byte)(item.TryGetProperty("value", out var curVal) && curVal.TryGetInt32(out var cv) ? cv : 0),
                    WhenFailed = item.TryGetProperty("when_failed", out var wf) && wf.ValueKind != JsonValueKind.Null 
                        ? wf.GetString().ToSafeString() 
                        : string.Empty
                };
                attrs.Add(attr);
            }
        }
        
        return attrs;
    }
    
    public static List<SmartaSelfTestEntry> ParseSelfTestLog(JsonElement root)
    {
        var tests = new List<SmartaSelfTestEntry>();
        
        if (root.TryGetProperty("ata_smart_self_test_log", out var selfTest) &&
            selfTest.TryGetProperty("table", out var testTable))
        {
            foreach (var test in testTable.EnumerateArray())
            {
                var typeStr = test.TryGetProperty("type", out var type) ? type.GetString().ToSafeString() : null;
                var statusStr = test.TryGetProperty("status", out var status) ? status.GetString().ToSafeString() : null;
                var entry = new SmartaSelfTestEntry
                {
                    Number = test.TryGetProperty("num", out var num) && num.ValueKind == JsonValueKind.Number ? num.GetInt32() : 0,
                    Type = ParseSelfTestType(typeStr),
                    Status = ParseSelfTestStatus(statusStr),
                    RemainingPercent = test.TryGetProperty("remaining", out var rem) && rem.ValueKind == JsonValueKind.Number ? rem.GetInt32() : 0,
                    LifeTimeHours = test.TryGetProperty("lifetime_hours", out var lifetime) && lifetime.ValueKind == JsonValueKind.Number ? lifetime.GetInt32() : 0,
                    LbaOfFirstError = test.TryGetProperty("lba_of_first_error", out var lba) && lba.ValueKind == JsonValueKind.Number ? lba.GetInt64() : 0,
                    CompletedAt = test.TryGetProperty("timestamp", out var timestamp) ? timestamp.GetString().ToSafeString() : string.Empty
                };
                tests.Add(entry);
            }
        }
        
        return tests;
    }
    
    public static SmartaSelfTestType ParseSelfTestType(string? type)
    {
        if (string.IsNullOrEmpty(type)) return SmartaSelfTestType.Offline;
        
        return type.ToLowerInvariant() switch
        {
            "short" => SmartaSelfTestType.ShortTest,
            "extended" => SmartaSelfTestType.Extended,
            "conveyance" => SmartaSelfTestType.Conveyance,
            "selective" => SmartaSelfTestType.Selective,
            "offline" => SmartaSelfTestType.Offline,
            "abort" => SmartaSelfTestType.Abort,
            _ => SmartaSelfTestType.Quick
        };
    }
    
    public static SmartaSelfTestStatus ParseSelfTestStatus(string? status)
    {
        if (string.IsNullOrEmpty(status)) 
        {
            return SmartaSelfTestStatus.ErrorUnknown;
        }
        
        var s = status.ToLowerInvariant();
        if (s.Contains("completed without error") || s.Contains("passed")) 
        {
            return SmartaSelfTestStatus.CompletedWithoutError;
        }
        if (s.Contains("aborted")) 
        {
            return SmartaSelfTestStatus.AbortedByUser;
        }
        if (s.Contains("interrupted")) 
        {
            return SmartaSelfTestStatus.AbortedByHost;
        }
        if (s.Contains("fatal")) 
        {
            return SmartaSelfTestStatus.FatalError;
        }
        if (s.Contains("electrical")) 
        {
            return SmartaSelfTestStatus.ErrorElectrical;
        }
        if (s.Contains("servo")) 
        {
            return SmartaSelfTestStatus.ErrorServo;
        }
        if (s.Contains("read")) 
        {
            return SmartaSelfTestStatus.ErrorRead;
        }
        if (s.Contains("handling")) 
        {
            return SmartaSelfTestStatus.ErrorHandling;
        }
        if (s.Contains("in progress") || s.Contains("running")) 
        {
            return SmartaSelfTestStatus.InProgress;
        }
        
        return SmartaSelfTestStatus.ErrorUnknown;
    }
    
    public static SmartaData ToSmartaData(SmartCheckResult result, string? deviceModel = null, string? serialNumber = null)
    {
        return new SmartaData
        {
            DeviceModel = deviceModel.ToSafeString(),
            SerialNumber = serialNumber.ToSafeString(),
            PowerOnHours = result.PowerOnHours ?? 0,
            ReallocatedSectorCount = result.ReallocatedSectorCount ?? 0,
            PendingSectorCount = result.PendingSectorCount ?? 0,
            UncorrectableErrorCount = result.UncorrectableErrorCount ?? 0,
            Temperature = result.Temperature ?? 0,
            WearLevelingCount = result.WearLevelingCount,
            Attributes = result.Attributes,
            SelfTests = result.SelfTests ?? new List<SmartaSelfTestEntry>()
        };
    }
}
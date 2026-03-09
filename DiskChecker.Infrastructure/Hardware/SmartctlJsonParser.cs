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
                TestPassed = passed.GetBoolean()
            };
            
            // Parse power on hours
            if (root.TryGetProperty("power_on_time", out var powerOn) && 
                powerOn.TryGetProperty("hours", out var hours))
            {
                result.PowerOnHours = hours.TryGetInt32(out var h) ? h : 0;
            }
            
            // Parse attributes - call with JsonElement
            result.Attributes = ParseAttributes(root);
            
            // Extract key metrics from attributes
            foreach (var attr in result.Attributes)
            {
                if (attr.Id == 5) result.ReallocatedSectorCount = attr.RawValue;
                if (attr.Id == 197) result.PendingSectorCount = attr.RawValue;
                if (attr.Id == 198) result.UncorrectableErrorCount = attr.RawValue;
                if (attr.Id == 194) result.Temperature = (attr.RawValue & 0xFF);
                if (attr.Id == 231) result.WearLevelingCount = (int)attr.RawValue;
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
                    statusValue = str.GetString() ?? "";
                }
                
                if (currentTest.TryGetProperty("percent", out var pct))
                {
                    remainingPercent = pct.TryGetInt32(out var p) ? p : 0;
                }
                
                var isRunning = statusValue.Contains("Self-test routine in progress");
                result.CurrentSelfTest = new SmartaSelfTestEntry
                {
                    Status = isRunning ? SmartaSelfTestStatus.InProgress : 
                        ParseSelfTestStatus(statusValue),
                    RemainingPercent = remainingPercent,
                    CompletedAt = DateTime.UtcNow
                };
            }
            
            return result;
        }
        catch
        {
            return null;
        }
    }
    
    // String overloads for backward compatibility
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
                var attr = new SmartaAttributeItem
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Value = (byte)(item.TryGetProperty("value", out var val) && val.TryGetInt32(out var v) ? v : 0),
                    Worst = (byte)(item.TryGetProperty("worst", out var worst) && worst.TryGetInt32(out var w) ? w : 0),
                    Threshold = item.TryGetProperty("thresh", out var thresh) && thresh.TryGetInt32(out var t) ? t : 0,
                    RawValue = (uint)(item.TryGetProperty("raw", out var raw) && raw.TryGetInt64(out var r) ? r : 0),
                    IsOk = item.TryGetProperty("when_failed", out var whenFailed) && whenFailed.ValueKind != JsonValueKind.Null 
                        ? false 
                        : true,
                    Current = (byte)(item.TryGetProperty("value", out var curVal) && curVal.TryGetInt32(out var cv) ? cv : 0),
                    WhenFailed = item.TryGetProperty("when_failed", out var wf) && wf.ValueKind != JsonValueKind.Null 
                        ? wf.GetString() ?? string.Empty 
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
                var typeStr = test.TryGetProperty("type", out var type) ? type.GetString() : null;
                var statusStr = test.TryGetProperty("status", out var status) ? status.GetString() : null;
                var entry = new SmartaSelfTestEntry
                {
                    Number = test.TryGetProperty("num", out var num) && num.TryGetInt32(out var n) ? n : 0,
                    Type = ParseSelfTestType(typeStr),
                    Status = ParseSelfTestStatus(statusStr),
                    RemainingPercent = test.TryGetProperty("remaining", out var rem) && rem.TryGetInt32(out var r) ? r : 0,
                    LifeTimeHours = test.TryGetProperty("lifetime_hours", out var lifetime) && lifetime.TryGetInt32(out var l) ? l : 0,
                    LbaOfFirstError = test.TryGetProperty("lba_of_first_error", out var lba) && lba.TryGetInt64(out var lb) ? (long)lb : 0
                };
                tests.Add(entry);
            }
        }
        
        return tests;
    }
    
    public static SmartaSelfTestType ParseSelfTestType(string? type)
    {
        return type?.ToLowerInvariant() switch
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
    
    // Helper method to convert SmartCheckResult to SmartaData
    public static SmartaData ToSmartaData(SmartCheckResult result, string? deviceModel = null, string? serialNumber = null)
    {
        return new SmartaData
        {
            DeviceModel = deviceModel?.ToIntOrNull() ?? 0,
            SerialNumber = serialNumber ?? string.Empty,
            SmartEnabled = result.IsEnabled,
            SmartHealthy = result.IsHealthy,
            PowerOnHours = result.PowerOnHours,
            ReallocatedSectorCount = result.ReallocatedSectorCount,
            PendingSectorCount = result.PendingSectorCount,
            UncorrectableErrorCount = result.UncorrectableErrorCount,
            Temperature = result.Temperature,
            WearLevelingCount = result.WearLevelingCount,
            Attributes = result.Attributes,
            SelfTests = result.SelfTests ?? new List<SmartaSelfTestEntry>()
        };
    }
}

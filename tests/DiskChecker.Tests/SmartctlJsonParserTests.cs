using System;
using System.Text.Json;
using DiskChecker.Infrastructure.Hardware;
using Xunit;

namespace DiskChecker.Tests;

public class SmartctlJsonParserTests
{
    [Fact]
    public void Parse_MinimalJson_ReturnsResult()
    {
        var json = @"{""smart_status"": {""passed"": true}}";
        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);
        Assert.True(result.IsHealthy);
    }

    [Fact]
    public void Parse_WithModelAndSerial_ExtractsCorrectly()
    {
        var json = @"{
            ""smart_status"": { ""passed"": true },
            ""model_name"": ""Test Model"",
            ""serial_number"": ""ABC123"",
            ""firmware_version"": ""1.0""
        }";
        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);
        Assert.Equal("Test Model", result.DeviceModel);
        Assert.Equal("ABC123", result.SerialNumber);
        Assert.Equal("1.0", result.FirmwareVersion);
    }

    [Fact]
    public void Parse_WithPowerOnAndTemperature_ExtractsCorrectly()
    {
        var json = @"{
            ""smart_status"": { ""passed"": true },
            ""model_name"": ""Test"",
            ""serial_number"": ""SN"",
            ""power_on_time"": { ""hours"": 12345 },
            ""temperature"": { ""current"": 35 }
        }";
        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);
        Assert.Equal(12345, result.PowerOnHours);
        Assert.Equal(35, result.Temperature);
    }

    [Fact]
    public void Parse_StandardAtaOutput_ExtractsAllFields()
    {
        var json = @"{
            ""smart_status"": { ""passed"": true },
            ""device"": { ""name"": ""/dev/sda"", ""type"": ""sat"", ""protocol"": ""ATA"" },
            ""model_name"": ""Samsung SSD 860 EVO 500GB"",
            ""serial_number"": ""S3Z8NB0M123456"",
            ""firmware_version"": ""RVT04B6Q"",
            ""user_capacity"": { ""bytes"": 500107862016, ""blocks"": 976773168, ""human_readable"": ""500 GB"" },
            ""power_on_time"": { ""hours"": 12345 },
            ""temperature"": { ""current"": 35 },
            ""ata_smart_attributes"": {
                ""table"": [
                    { ""id"": 5, ""name"": ""Reallocated_Sector_Ct"", ""value"": 100, ""worst"": 100, ""thresh"": 10, ""raw"": { ""value"": 0, ""string"": ""0"" } },
                    { ""id"": 9, ""name"": ""Power_On_Hours"", ""value"": 99, ""worst"": 99, ""thresh"": 0, ""raw"": { ""value"": 12345, ""string"": ""12345"" } },
                    { ""id"": 197, ""name"": ""Current_Pending_Sector"", ""value"": 100, ""worst"": 100, ""thresh"": 0, ""raw"": { ""value"": 0, ""string"": ""0"" } }
                ]
            }
        }";

        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);

        Assert.True(result.IsHealthy);
        Assert.Equal("Samsung SSD 860 EVO 500GB", result.DeviceModel);
        Assert.Equal("S3Z8NB0M123456", result.SerialNumber);
        Assert.Equal("RVT04B6Q", result.FirmwareVersion);
        Assert.Equal(12345, result.PowerOnHours);
        Assert.Equal(35, result.Temperature);
        Assert.Equal(3, result.Attributes.Count);
        Assert.Equal(0, result.ReallocatedSectorCount);
        Assert.Equal(0, result.PendingSectorCount);
    }

    [Fact]
    public void Parse_NvmeOutput_ExtractsFields()
    {
        var json = @"{
            ""smart_status"": { ""passed"": true },
            ""device"": { ""name"": ""/dev/nvme0"", ""type"": ""nvme"", ""protocol"": ""NVMe"" },
            ""model_name"": ""Samsung SSD 980 PRO 1TB"",
            ""serial_number"": ""S5GXNF0R123456"",
            ""firmware_version"": ""5B2QGXA7"",
            ""user_capacity"": { ""bytes"": 1000204886016 },
            ""power_on_time"": { ""hours"": 5000 },
            ""temperature"": { ""current"": 42 },
            ""nvme_smart_health_information_log"": {
                ""percentage_used"": 2,
                ""temperature"": 315
            }
        }";

        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);

        Assert.True(result.IsHealthy);
        Assert.Equal("Samsung SSD 980 PRO 1TB", result.DeviceModel);
        Assert.Equal("S5GXNF0R123456", result.SerialNumber);
        Assert.Equal("5B2QGXA7", result.FirmwareVersion);
        Assert.Equal(5000, result.PowerOnHours);
        Assert.Equal(42, result.Temperature);
        Assert.Equal(2, result.EnduranceUsedPercent);
    }

    [Fact]
    public void Parse_DeviceObjectFallback_ExtractsModelAndSerial()
    {
        var json = @"{
            ""smart_status"": { ""passed"": true },
            ""device"": {
                ""name"": ""/dev/sdb"",
                ""type"": ""sat"",
                ""protocol"": ""ATA"",
                ""model_name"": ""WDC WD40EFRX-68N32N0"",
                ""serial_number"": ""WD-WCC7K1234567"",
                ""firmware_version"": ""82.00A82""
            },
            ""power_on_time"": { ""hours"": 25000 },
            ""temperature"": { ""current"": 38 }
        }";

        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);

        Assert.Equal("WDC WD40EFRX-68N32N0", result.DeviceModel);
        Assert.Equal("WD-WCC7K1234567", result.SerialNumber);
        Assert.Equal("82.00A82", result.FirmwareVersion);
        Assert.Equal(25000, result.PowerOnHours);
        Assert.Equal(38, result.Temperature);
    }

    [Fact]
    public void Parse_PowerOnTimeAsDirectNumber_ExtractsCorrectly()
    {
        var json = @"{
            ""smart_status"": { ""passed"": true },
            ""model_name"": ""Test Drive"",
            ""serial_number"": ""TEST123"",
            ""firmware_version"": ""1.0"",
            ""power_on_time"": 8760,
            ""temperature"": { ""current"": 30 }
        }";

        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);

        Assert.Equal(8760, result.PowerOnHours);
    }

    [Fact]
    public void Parse_SmartStatusAsDirectBoolean_HandlesCorrectly()
    {
        var json = @"{
            ""smart_status"": true,
            ""model_name"": ""Test Drive"",
            ""serial_number"": ""TEST456"",
            ""firmware_version"": ""2.0""
        }";

        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);

        Assert.True(result.IsHealthy);
    }

    [Fact]
    public void Parse_SmartStatusFailed_ReturnsNotHealthy()
    {
        var json = @"{
            ""smart_status"": { ""passed"": false },
            ""model_name"": ""Failing Drive"",
            ""serial_number"": ""FAIL001"",
            ""firmware_version"": ""1.0""
        }";

        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);

        Assert.False(result.IsHealthy);
    }

    [Fact]
    public void Parse_NvmeTemperatureFromHealthLog_ExtractsCorrectly()
    {
        var json = @"{
            ""smart_status"": { ""passed"": true },
            ""model_name"": ""NVMe Test Drive"",
            ""serial_number"": ""NVME001"",
            ""firmware_version"": ""1.0"",
            ""nvme_smart_health_information_log"": {
                ""temperature"": 320,
                ""percentage_used"": 5
            }
        }";

        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);

        // 320 Kelvin - 273 = 47°C
        Assert.Equal(47, result.Temperature);
        Assert.Equal(5, result.EnduranceUsedPercent);
    }

    [Fact]
    public void Parse_EmptyJson_ReturnsResultWithDefaults()
    {
        var result = SmartctlJsonParser.Parse("{}");
        // Parser returns a result with defaults rather than null for empty JSON
        Assert.NotNull(result);
        Assert.False(result.IsHealthy);
        Assert.Null(result.DeviceModel);
        Assert.Null(result.SerialNumber);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        var result = SmartctlJsonParser.Parse("not valid json at all");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_ScsiOutput_ExtractsFields()
    {
        var json = @"{
            ""smart_status"": { ""passed"": true },
            ""device"": { ""name"": ""/dev/sdc"", ""type"": ""scsi"", ""protocol"": ""SCSI"" },
            ""scsi_model_name"": ""SEAGATE ST1000NM0033"",
            ""scsi_serial_number"": ""Z1Z123450000C123ABCD"",
            ""scsi_firmware_version"": ""SN04"",
            ""user_capacity"": { ""bytes"": 1000204886016 },
            ""temperature"": { ""current"": 40 }
        }";

        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);

        Assert.True(result.IsHealthy);
        Assert.Equal("SEAGATE ST1000NM0033", result.DeviceModel);
        Assert.Equal("Z1Z123450000C123ABCD", result.SerialNumber);
        Assert.Equal("SN04", result.FirmwareVersion);
        Assert.Equal(40, result.Temperature);
    }

    [Fact]
    public void Parse_ScsiModelFallbackToDeviceModel_Works()
    {
        var json = @"{
            ""smart_status"": { ""passed"": true },
            ""device"": { ""name"": ""/dev/sdd"", ""type"": ""scsi"", ""protocol"": ""SCSI"", ""model_name"": ""SAS Drive Model"" },
            ""scsi_serial_number"": ""SAS12345"",
            ""user_capacity"": { ""bytes"": 500107862016 }
        }";

        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);

        Assert.Equal("SAS Drive Model", result.DeviceModel);
        Assert.Equal("SAS12345", result.SerialNumber);
    }

    [Fact]
    public void Parse_ReallocatedSectorsFromAttributes_CalculatesCorrectly()
    {
        var json = @"{
            ""smart_status"": { ""passed"": true },
            ""model_name"": ""Aging Drive"",
            ""serial_number"": ""AGE001"",
            ""firmware_version"": ""1.0"",
            ""ata_smart_attributes"": {
                ""table"": [
                    { ""id"": 5, ""name"": ""Reallocated_Sector_Ct"", ""raw"": { ""value"": 15, ""string"": ""15"" } },
                    { ""id"": 197, ""name"": ""Current_Pending_Sector"", ""raw"": { ""value"": 3, ""string"": ""3"" } },
                    { ""id"": 198, ""name"": ""Offline_Uncorrectable"", ""raw"": { ""value"": 2, ""string"": ""2"" } }
                ]
            }
        }";

        var result = SmartctlJsonParser.Parse(json);
        Assert.NotNull(result);

        Assert.Equal(15, result.ReallocatedSectorCount);
        Assert.Equal(3, result.PendingSectorCount);
        Assert.Equal(2, result.UncorrectableErrorCount);
    }
}

using DiskChecker.Infrastructure.Hardware;
using Xunit;

namespace DiskChecker.Tests;

public class SmartJsonParserTests
{
    [Fact]
    public void SmartctlJsonParser_ParsesAtaAttributes()
    {
        var json = """
        {
          \"model_name\": \"Samsung SSD\",
          \"serial_number\": \"SN123\",
          \"firmware_version\": \"1.0\",
          \"ata_smart_attributes\": {
            \"table\": [
              { \"id\": 9, \"raw\": { \"value\": 1234 } },
              { \"id\": 5, \"raw\": { \"value\": 2 } },
              { \"id\": 197, \"raw\": { \"value\": 1 } },
              { \"id\": 198, \"raw\": { \"value\": 3 } },
              { \"id\": 194, \"raw\": { \"value\": 35 } }
            ]
          }
        }
        """;

        var result = SmartctlJsonParser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal("Samsung SSD", result!.DeviceModel);
        Assert.Equal("SN123", result.SerialNumber);
        Assert.Equal(1234, result.PowerOnHours);
        Assert.Equal(2, result.ReallocatedSectorCount);
        Assert.Equal(1, result.PendingSectorCount);
        Assert.Equal(3, result.UncorrectableErrorCount);
        Assert.Equal(35, result.Temperature);
    }

    [Fact]
    public void SmartctlJsonParser_ParsesNvmeAttributes()
    {
        var json = """
        {
          \"model_number\": \"NVMe Drive\",
          \"serial_number\": \"NVME123\",
          \"firmware_version\": \"2.0\",
          \"nvme_smart_health_information_log\": {
            \"power_on_hours\": 500,
            \"media_errors\": 4,
            \"temperature\": 303,
            \"percentage_used\": 7
          }
        }
        """;

        var result = SmartctlJsonParser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal("NVMe Drive", result!.DeviceModel);
        Assert.Equal(500, result.PowerOnHours);
        Assert.Equal(4, result.UncorrectableErrorCount);
        Assert.Equal(29.9, result.Temperature, 1);
        Assert.Equal(7, result.WearLevelingCount);
    }

    [Fact]
    public void WindowsSmartJsonParser_ParsesAttributes()
    {
        var diskInfo = "{\"Model\":\"Windows Disk\",\"SerialNumber\":\"WIN123\",\"FirmwareVersion\":\"FW\"}";
        var attributes = """
        [
          { \"Id\": 9, \"Name\": \"PowerOnHours\", \"Value\": 111 },
          { \"Id\": 5, \"Name\": \"ReallocatedSectorsCount\", \"Value\": 2 },
          { \"Id\": 197, \"Name\": \"CurrentPendingSectorCount\", \"Value\": 1 },
          { \"Id\": 198, \"Name\": \"UncorrectableSectorCount\", \"Value\": 3 },
          { \"Id\": 194, \"Name\": \"Temperature\", \"Value\": 34 },
          { \"Id\": 202, \"Name\": \"WearLevelingCount\", \"Value\": 95 }
        ]
        """;

        var result = WindowsSmartJsonParser.Parse(diskInfo, attributes);

        Assert.NotNull(result);
        Assert.Equal("Windows Disk", result!.DeviceModel);
        Assert.Equal("WIN123", result.SerialNumber);
        Assert.Equal(111, result.PowerOnHours);
        Assert.Equal(2, result.ReallocatedSectorCount);
        Assert.Equal(1, result.PendingSectorCount);
        Assert.Equal(3, result.UncorrectableErrorCount);
        Assert.Equal(34, result.Temperature);
        Assert.Equal(95, result.WearLevelingCount);
    }
}

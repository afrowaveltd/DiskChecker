using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>Conservative full-pass performance expectations, not manufacturer specifications or health diagnoses.</summary>
public readonly record struct DrivePerformanceProfile(double WriteFloor, double ReadFloor, double NormalMinPeakDrop)
{
    /// <summary>Resolve media from positive evidence, never from capacity or observed throughput alone.</summary>
    public static string ResolveMedia(string? declared, string? model, string? connection, SmartaData? smart = null)
    {
        static bool Has(string? value, string token) => value?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;
        if (Has(declared, "HDD") || Has(declared, "rotational")) return "HDD";
        if (Has(declared, "SSD") || Has(declared, "NVMe") || Has(connection, "NVMe")) return "SSD";
        if (Has(smart?.DeviceType, "NVMe") || Has(smart?.DeviceType, "SSD") ||
            smart?.AvailableSpare.HasValue == true || smart?.PercentageUsed.HasValue == true ||
            smart?.WearLevelingCount.HasValue == true) return "SSD";
        if (Has(model, "SSD") || Has(model, "NVMe") || Has(model, "flash")) return "SSD";
        if (Has(model, "HDD") || Has(smart?.ModelFamily, "Caviar") ||
            smart?.Attributes.Any(a => Has(a.Name, "Spin_Up_Time") || Has(a.Name, "Spin_Retry_Count")) == true)
            return "HDD";
        return "Unknown";
    }

    /// <summary>Approximate conservative thresholds in the caller's throughput units (MB/s or MiB/s).
    /// Unknown media or transport receive no absolute speed penalty.</summary>
    public static DrivePerformanceProfile For(string? media, string? connection, long capacityBytes)
    {
        var bus = (connection ?? "").ToUpperInvariant();
        var usb = bus.Contains("USB");
        var legacy = bus.Contains("IDE") || bus.Contains("PATA");
        var sata = bus.Contains("SATA") || bus == "ATA";
        var sas = bus.Contains("SAS") || bus.Contains("SCSI");
        var nvme = bus.Contains("NVME");
        if (string.Equals(media, "HDD", StringComparison.OrdinalIgnoreCase))
        {
            // Decimal capacity bands are a generation proxy, not a manufacturing date.
            var floor = capacityBytes <= 0 ? 0d :
                capacityBytes < 100_000_000_000L ? 25d :
                capacityBytes < 500_000_000_000L ? 35d :
                capacityBytes < 1_000_000_000_000L ? 45d : 55d;
            if (legacy) floor = Math.Min(floor, 30);
            else if (usb) floor = Math.Min(floor, 20);
            else if (!sata && !sas) floor = 0;
            return new(floor, floor, 0.55d);
        }
        if (string.Equals(media, "SSD", StringComparison.OrdinalIgnoreCase))
        {
            if (usb) return new(0, 0, 0.35d);
            if (legacy) return new(15, 20, 0.35d);
            if (sata || sas) return new(40, 80, 0.35d);
            if (nvme) return new(80, 160, 0.35d);
        }
        return new(0, 0, 0.55d);
    }
}

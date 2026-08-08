using DiskChecker.Core.Models;
using Xunit;

namespace DiskChecker.Tests;

public class ManufacturerExtractorTests
{
    [Theory]
    [InlineData("Samsung SSD 870 EVO 500GB", "Samsung")]
    [InlineData("SAMSUNG MZ7LN256HCHP-000L7", "Samsung")]
    [InlineData("Seagate BarraCuda 120 SSD ZA500CM10003", "Seagate")]
    [InlineData("ST1000DM003-1CH162", "Seagate")]
    [InlineData("ST2000DM008-2FR102", "Seagate")]
    [InlineData("WDC WD10EZEX-08WN4A0", "Western Digital")]
    [InlineData("WDC WDS500G2B0A-00SM50", "Western Digital")]
    [InlineData("WD40EZRZ-00GXCB0", "Western Digital")]
    [InlineData("Kingston SA400S37480G", "Kingston")]
    [InlineData("KINGSTON SV300S37A120G", "Kingston")]
    [InlineData("Crucial CT500MX500SSD1", "Crucial")]
    [InlineData("Intel SSDSC2KW256G8", "Intel")]
    [InlineData("SanDisk SDSSDH3 500G", "SanDisk")]
    [InlineData("Toshiba DT01ACA100", "Toshiba")]
    [InlineData("HGST HTS721010A9E630", "HGST")]
    [InlineData("Hitachi HDS721050CLA362", "Hitachi")]
    [InlineData("ADATA SU650", "ADATA")]
    [InlineData("Corsair Force LE SSD", "Corsair")]
    [InlineData("SK hynix SC311 SATA 256GB", "SK Hynix")]
    [InlineData("Micron 1100 SATA 256GB", "Micron")]
    [InlineData("Kioxia EXCERIA SATA SSD", "Kioxia")]
    [InlineData("Transcend TS128GSSD370S", "Transcend")]
    [InlineData("Patriot Burst", "Patriot")]
    [InlineData("Sabrent Rocket 4.0 1TB", "Sabrent")]
    [InlineData("PNY CS900 500GB SSD", "PNY")]
    [InlineData("Samsung SSD 980 PRO 1TB", "Samsung")]
    [InlineData("CT1000P3SSD8", "Crucial")]
    [InlineData("Maxtor 6Y080L0", "Maxtor")]
    [InlineData("FUJITSU MHZ2160BH G2", "Fujitsu")]
    public void ExtractManufacturer_FromModel_ReturnsCorrectManufacturer(string model, string expected)
    {
        var result = ManufacturerExtractor.ExtractManufacturer(model);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown Device")]
    [InlineData("Generic Flash Disk")]
    [InlineData("USB DISK 3.0")]
    public void ExtractManufacturer_UnknownModel_ReturnsEmpty(string? model)
    {
        var result = ManufacturerExtractor.ExtractManufacturer(model);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractManufacturer_FromSmartaData_UsesDeviceModel()
    {
        var smarta = new SmartaData
        {
            DeviceModel = "Samsung SSD 870 EVO 500GB",
            ModelFamily = "Samsung based SSDs"
        };

        var result = ManufacturerExtractor.ExtractManufacturer(smarta);
        Assert.Equal("Samsung", result);
    }

    [Fact]
    public void ExtractManufacturer_FromSmartaData_FallsBackToModelFamily()
    {
        var smarta = new SmartaData
        {
            DeviceModel = "Unknown SSD",
            ModelFamily = "Seagate BarraCuda"
        };

        var result = ManufacturerExtractor.ExtractManufacturer(smarta);
        Assert.Equal("Seagate", result);
    }

    [Fact]
    public void ExtractManufacturer_FromNullSmartaData_ReturnsEmpty()
    {
        var result = ManufacturerExtractor.ExtractManufacturer((SmartaData?)null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractManufacturer_FromSmartaData_WithEmptyModel_ReturnsEmpty()
    {
        var smarta = new SmartaData
        {
            DeviceModel = "",
            ModelFamily = null
        };

        var result = ManufacturerExtractor.ExtractManufacturer(smarta);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractManufacturer_ST_NotFollowedByDigit_ReturnsEmpty()
    {
        // "ST" alone or followed by non-digit should not match
        var result = ManufacturerExtractor.ExtractManufacturer("ST-ATA");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractManufacturer_WD_NotFollowedByC_ReturnsWesternDigital()
    {
        // "WD" followed by non-C character should match Western Digital
        var result = ManufacturerExtractor.ExtractManufacturer("WD40EZRZ-00GXCB0");
        Assert.Equal("Western Digital", result);
    }

    [Fact]
    public void ExtractManufacturer_WDC_ReturnsWesternDigital()
    {
        // "WDC" is a separate key
        var result = ManufacturerExtractor.ExtractManufacturer("WDC WD10EZEX-08WN4A0");
        Assert.Equal("Western Digital", result);
    }

    [Fact]
    public void ExtractManufacturer_SK_FollowedBySpace_ReturnsSKHynix()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("SK hynix SC311 SATA 256GB");
        Assert.Equal("SK Hynix", result);
    }

    [Fact]
    public void ExtractManufacturer_SK_Alone_ReturnsEmpty()
    {
        // "SK" alone without hynix context should not match
        var result = ManufacturerExtractor.ExtractManufacturer("SK-12345");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractManufacturer_SP_FollowedBySpace_ReturnsSiliconPower()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("SP CC550 512GB");
        Assert.Equal("Silicon Power", result);
    }

    [Fact]
    public void ExtractManufacturer_SP_Alone_ReturnsEmpty()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("SP-12345");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractManufacturer_NVMe_Model_ReturnsCorrectManufacturer()
    {
        // NVMe drives often have different model naming
        var result = ManufacturerExtractor.ExtractManufacturer("Samsung SSD 980 PRO 1TB");
        Assert.Equal("Samsung", result);
    }

    [Fact]
    public void ExtractManufacturer_TeamGroup_ReturnsTeamGroup()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("TEAM TM8FP6001T");
        Assert.Equal("TeamGroup", result);
    }

    [Fact]
    public void ExtractManufacturer_Gigabyte_ReturnsGigabyte()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Gigabyte GP-GSTFS31256GTND");
        Assert.Equal("Gigabyte", result);
    }

    [Fact]
    public void ExtractManufacturer_MSI_ReturnsMSI()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("MSI M390 500GB");
        Assert.Equal("MSI", result);
    }

    [Fact]
    public void ExtractManufacturer_ASUS_ReturnsASUS()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("ASUS ROG STRIX SQ7 1TB");
        Assert.Equal("ASUS", result);
    }

    [Fact]
    public void ExtractManufacturer_HP_ReturnsHP()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("HP SSD EX900 500GB");
        Assert.Equal("HP", result);
    }

    [Fact]
    public void ExtractManufacturer_Dell_ReturnsDell()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Dell Express Flash NVMe PM1725b 1.6TB");
        Assert.Equal("Dell", result);
    }

    [Fact]
    public void ExtractManufacturer_Lenovo_ReturnsLenovo()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Lenovo LENSE20512GMSP34MEAT2TA");
        Assert.Equal("Lenovo", result);
    }

    [Fact]
    public void ExtractManufacturer_Apple_ReturnsApple()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Apple SSD SM0512L");
        Assert.Equal("Apple", result);
    }

    [Fact]
    public void ExtractManufacturer_Netac_ReturnsNetac()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Netac SSD 256GB");
        Assert.Equal("Netac", result);
    }

    [Fact]
    public void ExtractManufacturer_Fanxiang_ReturnsFanxiang()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Fanxiang S101 512GB");
        Assert.Equal("Fanxiang", result);
    }

    [Fact]
    public void ExtractManufacturer_Goodram_ReturnsGoodram()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Goodram CX400 256GB");
        Assert.Equal("Goodram", result);
    }

    [Fact]
    public void ExtractManufacturer_Lexar_ReturnsLexar()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Lexar 256GB SSD");
        Assert.Equal("Lexar", result);
    }

    [Fact]
    public void ExtractManufacturer_Plextor_ReturnsPlextor()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Plextor PX-256M8VC");
        Assert.Equal("Plextor", result);
    }

    [Fact]
    public void ExtractManufacturer_LiteOn_ReturnsLiteOn()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Lite-On CV3-8D256-11 SATA 256GB");
        Assert.Equal("Lite-On", result);
    }

    [Fact]
    public void ExtractManufacturer_OCZ_ReturnsOCZ()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("OCZ-VERTEX3");
        Assert.Equal("OCZ", result);
    }

    [Fact]
    public void ExtractManufacturer_IBM_ReturnsIBM()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("IBM-DTLA-307030");
        Assert.Equal("IBM", result);
    }

    [Fact]
    public void ExtractManufacturer_Quantum_ReturnsQuantum()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("QUANTUM FIREBALLP AS30.0");
        Assert.Equal("Quantum", result);
    }

    [Fact]
    public void ExtractManufacturer_Hynix_ReturnsSKHynix()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Hynix HFS256G39TND-N210A");
        Assert.Equal("SK Hynix", result);
    }

    [Fact]
    public void ExtractManufacturer_Silicon_ReturnsSiliconPower()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Silicon Power A55 512GB");
        Assert.Equal("Silicon Power", result);
    }

    [Fact]
    public void ExtractManufacturer_Western_ReturnsWesternDigital()
    {
        var result = ManufacturerExtractor.ExtractManufacturer("Western Digital WD Blue SN550 1TB");
        Assert.Equal("Western Digital", result);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Regression tests for the "non-SMART disk shows grade A" bug.
/// A device that does not expose SMART (USB flash drive, USB-SATA bridge without
/// passthrough, RAID controller, ...) must never be graded — the grade has to stay
/// empty / N/A instead of a misleading "A".
/// </summary>
public class NonSmartGradeTests
{
    // ──────────────────────────────────────────────
    //  SmartctlJsonParser — smart_support.available
    // ──────────────────────────────────────────────

    [Fact]
    public void Parse_SmartSupportAvailableFalse_ExposesSmartAvailableFalse()
    {
        const string json = """
            {
              "device": { "name": "/dev/sdb", "type": "scsi" },
              "model_name": "USB DISK 3.0",
              "smart_support": { "available": false, "enabled": false }
            }
            """;

        var result = SmartctlJsonParser.Parse(json);

        Assert.NotNull(result);
        Assert.False(result.SmartAvailable);
    }

    [Fact]
    public void Parse_SmartSupportAvailableTrue_ExposesSmartAvailableTrue()
    {
        const string json = """
            {
              "model_name": "Samsung SSD 860 EVO 500GB",
              "smart_support": { "available": true, "enabled": true }
            }
            """;

        var result = SmartctlJsonParser.Parse(json);

        Assert.NotNull(result);
        Assert.True(result.SmartAvailable);
    }

    [Fact]
    public void Parse_WithoutSmartSupportSection_LeavesSmartAvailableUnknown()
    {
        const string json = """
            { "model_name": "NVMe Device", "smart_status": { "passed": true } }
            """;

        var result = SmartctlJsonParser.Parse(json);

        Assert.NotNull(result);
        Assert.Null(result.SmartAvailable);
    }

    [Fact]
    public void ToSmartaData_SmartUnavailable_SetsIsSmartAvailableFalse()
    {
        var result = SmartctlJsonParser.Parse("""
            {
              "model_name": "USB DISK 3.0",
              "smart_support": { "available": false, "enabled": false }
            }
            """);

        Assert.NotNull(result);
        var smarta = SmartctlJsonParser.ToSmartaData(result);

        Assert.False(smarta.IsSmartAvailable);
    }

    [Fact]
    public void ToSmartaData_WithoutSmartSupportFlag_DefaultsToAvailable()
    {
        var result = SmartctlJsonParser.Parse("""
            {
              "model_name": "Legacy Device",
              "smart_status": { "passed": true }
            }
            """);

        Assert.NotNull(result);
        var smarta = SmartctlJsonParser.ToSmartaData(result);

        // Unknown/legacy data keeps the previous behaviour.
        Assert.True(smarta.IsSmartAvailable);
    }

    // ──────────────────────────────────────────────
    //  QualityCalculator — no data means no grade
    // ──────────────────────────────────────────────

    [Fact]
    public void CalculateQuality_WithNullData_ReturnsNull()
    {
        var calculator = new QualityCalculator();

        var rating = calculator.CalculateQuality(null);

        Assert.Null(rating);
    }

    [Fact]
    public void CalculateQuality_WithUnavailableSmart_ReturnsNull()
    {
        var calculator = new QualityCalculator();
        var smarta = new SmartaData
        {
            DeviceModel = "USB DISK 3.0",
            IsSmartAvailable = false,
            IsHealthy = true
        };

        var rating = calculator.CalculateQuality(smarta);

        Assert.Null(rating);
    }

    [Fact]
    public void CalculateQuality_WithParsedUnavailableSmart_ReturnsNull()
    {
        var parsed = SmartctlJsonParser.Parse("""
            {
              "model_name": "USB DISK 3.0",
              "smart_support": { "available": false, "enabled": false }
            }
            """);
        Assert.NotNull(parsed);
        var smarta = SmartctlJsonParser.ToSmartaData(parsed);

        var rating = new QualityCalculator().CalculateQuality(smarta);

        Assert.Null(rating);
    }

    [Fact]
    public void CalculateQuality_WithHealthySmartData_StillReturnsGradeA()
    {
        // Guard: normal disks must keep being graded exactly as before.
        var calculator = new QualityCalculator();
        var smarta = new SmartaData
        {
            DeviceModel = "Samsung SSD 860 EVO 500GB",
            DeviceType = "SATA/ATA",
            IsSmartAvailable = true,
            IsHealthy = true,
            Temperature = 30,
            PowerOnHours = 1_000,
            PowerCycleCount = 100
        };

        var rating = calculator.CalculateQuality(smarta);

        Assert.NotNull(rating);
        Assert.Equal(QualityGrade.A, rating.Grade);
    }

    [Fact]
    public void CalculateQuality_WithFailingSmartData_StillReturnsGradeF()
    {
        var calculator = new QualityCalculator();
        var smarta = new SmartaData
        {
            DeviceModel = "Failing Disk",
            IsHealthy = false,
            PendingSectorCount = 8
        };

        var rating = calculator.CalculateQuality(smarta);

        Assert.NotNull(rating);
        Assert.Equal(QualityGrade.F, rating.Grade);
    }

    // ──────────────────────────────────────────────
    //  SmartCheckService — no persistence of a fake grade
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SmartCheckService_WithUnavailableSmart_ReturnsNullAndPersistsNothing()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        smartaProvider.GetSmartaDataAsync("/dev/nosmart", Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult<SmartaData?>(new SmartaData
            {
                DeviceModel = "USB DISK 3.0",
                IsSmartAvailable = false,
                IsHealthy = true
            }));

        var qualityCalculator = new QualityCalculator();
        var dbContext = new DiskCheckerDbContext(
            new DbContextOptionsBuilder<DiskCheckerDbContext>()
                .UseInMemoryDatabase("NonSmartGrade_" + Guid.NewGuid())
                .Options);
        var cardTestService = Substitute.For<DiskCardTestService>(
            dbContext,
            qualityCalculator,
            NullLogger<DiskCardTestService>.Instance,
            Substitute.For<ICertificateGenerator>());

        var service = new SmartCheckService(
            smartaProvider,
            qualityCalculator,
            dbContext,
            cardTestService,
            NullLogger<SmartCheckService>.Instance);

        var drive = new CoreDriveInfo
        {
            Path = "/dev/nosmart",
            Name = "USB DISK 3.0",
            SupportsSmart = true
        };

        var result = await service.RunAsync(drive, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(dbContext.Tests);
        Assert.Empty(dbContext.Drives);
    }

    // ──────────────────────────────────────────────
    //  DiskSelectionViewModel — disk list shows N/A
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DiskList_NonSmartDrive_ShowsNA()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var viewModel = CreateDiskSelectionViewModel(smartaProvider);

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sdb",
            Name = "USB Flash Drive",
            TotalSize = 16_000_000_000,
            SupportsSmart = false
        };

        var card = await InvokeLoadDriveAsync(viewModel, drive);

        Assert.Equal("N/A", card.GradeText);
        Assert.Null(card.Quality);
        Assert.Equal("#6C757D", card.GradeColor);
        Assert.Equal("Unknown", card.HealthStatus);
        await smartaProvider.DidNotReceive()
            .GetSmartaDataAsync(Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task DiskList_DriveReportingSmartUnavailable_ShowsNA()
    {
        // Defensive: even if a provider still returns data flagged as unavailable,
        // the list must not display a grade.
        var smartaProvider = Substitute.For<ISmartaProvider>();
        smartaProvider.GetSmartaDataAsync("/dev/sdc", Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult<SmartaData?>(new SmartaData
            {
                DeviceModel = "USB DISK 3.0",
                IsSmartAvailable = false,
                IsHealthy = true
            }));

        var viewModel = CreateDiskSelectionViewModel(smartaProvider);

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sdc",
            Name = "USB Flash Drive",
            TotalSize = 32_000_000_000,
            SupportsSmart = true
        };

        var card = await InvokeLoadDriveAsync(viewModel, drive);

        Assert.Equal("N/A", card.GradeText);
        Assert.Null(card.Quality);
    }

    [Fact]
    public async Task DiskList_HealthyDrive_StillShowsItsGrade()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        smartaProvider.GetSmartaDataAsync("/dev/sda", Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult<SmartaData?>(new SmartaData
            {
                DeviceModel = "Samsung SSD 860 EVO 500GB",
                SerialNumber = "S3Z8NB0M123456",
                DeviceType = "SATA/ATA",
                IsSmartAvailable = true,
                IsHealthy = true,
                Temperature = 30,
                PowerOnHours = 1_000,
                PowerCycleCount = 100
            }));

        var viewModel = CreateDiskSelectionViewModel(smartaProvider);

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sda",
            Name = "Samsung SSD",
            TotalSize = 500_000_000_000,
            SupportsSmart = true,
            IsSystemDisk = true
        };

        var card = await InvokeLoadDriveAsync(viewModel, drive);

        Assert.Equal("A", card.GradeText);
        Assert.NotNull(card.Quality);
        Assert.Equal("#27AE60", card.GradeColor);
    }

    private static DiskSelectionViewModel CreateDiskSelectionViewModel(ISmartaProvider smartaProvider)
    {
        return new DiskSelectionViewModel(
            Substitute.For<INavigationService>(),
            Substitute.For<IDiskCacheService>(),
            smartaProvider,
            new QualityCalculator(),
            Substitute.For<ISettingsService>(),
            Substitute.For<ISelectedDiskService>(),
            Substitute.For<IDiskCardRepository>(),
            Substitute.For<IDialogService>());
    }

    private static async Task<DiskStatusCardItem> InvokeLoadDriveAsync(
        DiskSelectionViewModel viewModel,
        CoreDriveInfo drive)
    {
        var method = typeof(DiskSelectionViewModel).GetMethod(
            "LoadDriveAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<DiskStatusCardItem>)method.Invoke(viewModel, new object[] { drive, new List<string>() })!;
        return await task;
    }

    // ──────────────────────────────────────────────
    //  LinuxSmartaProvider — real smartctl invocation against a fake binary
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LinuxProvider_WhenSmartUnavailable_ReturnsNullAndMarksDeviceUnsupported()
    {
        await WithFakeSmartctlAsync(
            """
            {
              "device": { "name": "/dev/sdb", "type": "scsi" },
              "model_name": "USB DISK 3.0",
              "smart_support": { "available": false, "enabled": false }
            }
            """,
            async provider =>
            {
                var data = await provider.GetSmartaDataAsync("/dev/fake-nosmart", TestContext.Current.CancellationToken);
                Assert.Null(data);

                var supported = await provider.IsSmartSupportedAsync("/dev/fake-nosmart", TestContext.Current.CancellationToken);
                Assert.False(supported);

                var rating = new QualityCalculator().CalculateQuality(data);
                Assert.Null(rating);
            });
    }

    [Fact]
    public async Task LinuxProvider_WhenSmartUnavailableWithModelName_ProbeReportsUnsupported()
    {
        await WithFakeSmartctlAsync(
            """
            {
              "model_name": "USB DISK 3.0",
              "smart_support": { "available": false, "enabled": false }
            }
            """,
            async provider =>
            {
                // model_name is present, but smart_support.available=false wins.
                var supported = await provider.IsSmartSupportedAsync("/dev/fake-usb", TestContext.Current.CancellationToken);
                Assert.False(supported);
            });
    }

    [Fact]
    public async Task LinuxProvider_WhenSmartAvailable_StillReturnsGradableData()
    {
        await WithFakeSmartctlAsync(
            """
            {
              "model_name": "Samsung SSD 860 EVO 500GB",
              "serial_number": "S3Z8NB0M123456",
              "smart_status": { "passed": true },
              "smart_support": { "available": true, "enabled": true },
              "power_on_time": { "hours": 1000 },
              "power_cycle_count": 100,
              "temperature": { "current": 30 }
            }
            """,
            async provider =>
            {
                var data = await provider.GetSmartaDataAsync("/dev/fake-smart", TestContext.Current.CancellationToken);

                Assert.NotNull(data);
                Assert.True(data.IsSmartAvailable);
                Assert.Equal("Samsung SSD 860 EVO 500GB", data.DeviceModel);

                var rating = new QualityCalculator().CalculateQuality(data);
                Assert.NotNull(rating);
                Assert.Equal(QualityGrade.A, rating.Grade);
            });
    }

    private static async Task WithFakeSmartctlAsync(string json, Func<LinuxSmartaProvider, Task> test)
    {
        if (!OperatingSystem.IsLinux())
        {
            // The Linux provider only makes sense on Linux; other platforms skip this test.
            return;
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"fake-smartctl-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(
            scriptPath,
            "#!/bin/sh\ncat <<'SMARTCTL_JSON'\n" + json + "\nSMARTCTL_JSON\nexit 0\n",
            TestContext.Current.CancellationToken);
        File.SetUnixFileMode(
            scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var pathField = typeof(LinuxSmartaProvider).GetField(
            "s_cachedSmartctlPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(pathField);
        var previous = (string?)pathField.GetValue(null);

        try
        {
            pathField.SetValue(null, scriptPath);
            var provider = new LinuxSmartaProvider(NullLogger<LinuxSmartaProvider>.Instance);
            await test(provider);
        }
        finally
        {
            pathField.SetValue(null, previous);
            try { File.Delete(scriptPath); } catch { /* best effort */ }
        }
    }
}

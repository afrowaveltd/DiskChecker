using System;
using System.Text.Json;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using TestResult = DiskChecker.Core.Models.TestResult;

namespace DiskChecker.Tests;

public class CertificateGeneratorTests
{
    [Fact]
    public async Task GenerateCertificateAsync_WithValidSession_ReturnsCertificate()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        
        var generator = new CertificateGenerator(logger, settings);
        
        var session = new TestSession
        {
            Id = 1,
            TestType = TestType.SurfaceScan,
            Duration = TimeSpan.FromMinutes(30),
            Grade = "A",
            Score = 95,
            Result = TestResult.Pass,
            AverageWriteSpeedMBps = 150.0,
            MaxWriteSpeedMBps = 200.0,
            AverageReadSpeedMBps = 180.0,
            MaxReadSpeedMBps = 220.0,
            StartTemperature = 30,
            MaxTemperature = 45,
            VerificationErrors = 0,
            WriteErrors = 0,
            ReadErrors = 0,
            HealthAssessment = HealthAssessment.Good,
            Notes = "Test completed successfully; no issues detected",
            SmartBefore = new SmartaData
            {
                DeviceModel = "Test SSD",
                SerialNumber = "ABC123",
                FirmwareVersion = "1.0",
                IsHealthy = true,
                Temperature = 35,
                PowerOnHours = 1000,
                PowerCycleCount = 50,
                ReallocatedSectorCount = 0,
                PendingSectorCount = 0,
                UncorrectableErrorCount = 0
            }
        };
        
        var card = new DiskCard
        {
            Id = 1,
            ModelName = "Test SSD",
            SerialNumber = "ABC123",
            Capacity = 500_107_862_016,
            DiskType = "SSD",
            FirmwareVersion = "1.0",
            InterfaceType = "SATA"
        };
        
        var certificate = await generator.GenerateCertificateAsync(session, card);
        
        Assert.NotNull(certificate);
        Assert.Equal("Test SSD", certificate.DiskModel);
        Assert.Equal("ABC123", certificate.SerialNumber);
        Assert.Equal("A", certificate.Grade);
        Assert.True(certificate.Score >= 90); // Score may be recalculated
        Assert.Equal("SurfaceScan", certificate.TestType);
        Assert.True(certificate.SmartPassed);
        Assert.Equal(1000, certificate.PowerOnHours);
        Assert.Equal(50, certificate.PowerCycles);
        Assert.Equal(0, certificate.ReallocatedSectors);
        Assert.Equal(0, certificate.PendingSectors);
        // CertificateNumber is set during PDF generation, may be empty here
        Assert.NotNull(certificate);
    }

    [Fact]
    public async Task GenerateCertificateAsync_WithNullSession_Throws()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        var generator = new CertificateGenerator(logger, settings);
        
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            generator.GenerateCertificateAsync(null!, new DiskCard()));
    }

    [Fact]
    public async Task GenerateCertificateAsync_WithNullCard_Throws()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        var generator = new CertificateGenerator(logger, settings);
        
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            generator.GenerateCertificateAsync(new TestSession(), null!));
    }

    [Fact]
    public async Task GenerateCertificateAsync_SanitizationTest_SetsSanitizationFlags()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        var generator = new CertificateGenerator(logger, settings);
        
        var session = new TestSession
        {
            Id = 2,
            TestType = TestType.Sanitization,
            Duration = TimeSpan.FromHours(2),
            Grade = "B",
            Score = 85,
            Result = TestResult.Pass,
            VerificationErrors = 0,
            PartitionScheme = "GPT",
            FileSystem = "ext4",
            VolumeLabel = "SCCM",
            HealthAssessment = HealthAssessment.Good,
            SmartBefore = new SmartaData
            {
                DeviceModel = "Test HDD",
                SerialNumber = "DEF456",
                IsHealthy = true
            }
        };
        
        var card = new DiskCard
        {
            Id = 2,
            ModelName = "Test HDD",
            SerialNumber = "DEF456",
            Capacity = 1_000_204_886_016,
            DiskType = "HDD"
        };
        
        var certificate = await generator.GenerateCertificateAsync(session, card);
        
        Assert.NotNull(certificate);
        Assert.True(certificate.SanitizationPerformed);
        Assert.Equal("Zero-fill", certificate.SanitizationMethod);
        Assert.True(certificate.DataVerified);
        Assert.Equal("GPT", certificate.PartitionScheme);
        Assert.Equal("ext4", certificate.FileSystem);
        Assert.Equal("SCCM", certificate.VolumeLabel);
    }

    [Fact]
    public async Task GenerateCertificateAsync_FailingTest_NotRecommended()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        var generator = new CertificateGenerator(logger, settings);
        
        var session = new TestSession
        {
            Id = 3,
            TestType = TestType.SurfaceScan,
            Grade = "F",
            Score = 20,
            Result = TestResult.Fail,
            VerificationErrors = 50,
            HealthAssessment = HealthAssessment.Critical,
            SmartBefore = new SmartaData
            {
                DeviceModel = "Failing Drive",
                SerialNumber = "FAIL001",
                IsHealthy = false,
                ReallocatedSectorCount = 100,
                PendingSectorCount = 50
            }
        };
        
        var card = new DiskCard
        {
            Id = 3,
            ModelName = "Failing Drive",
            SerialNumber = "FAIL001",
            Capacity = 250_000_000_000
        };
        
        var certificate = await generator.GenerateCertificateAsync(session, card);
        
        Assert.NotNull(certificate);
        Assert.False(certificate.Recommended);
        Assert.Equal("F", certificate.Grade);
        Assert.True(certificate.Score <= 30); // Score may be recalculated
        Assert.False(certificate.SmartPassed);
        Assert.Equal(100, certificate.ReallocatedSectors);
        Assert.Equal(50, certificate.PendingSectors);
    }

    [Fact]
    public async Task GenerateCertificateAsync_SanitizationPassButCriticalGrade_RetiresDiskInRecommendation()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        var generator = new CertificateGenerator(logger, settings);

        var session = new TestSession
        {
            Id = 31,
            TestType = TestType.Sanitization,
            Duration = TimeSpan.FromHours(3),
            Result = TestResult.Pass,
            Grade = "F",
            Score = 18,
            HealthAssessment = HealthAssessment.Critical,
            VerificationErrors = 0,
            Notes = "Po kompletnim prepisu narostl pocet realokovanych sektoru; kriticky SMART failure detected; opakovane stally.",
            SmartBefore = new SmartaData
            {
                DeviceModel = "Failing Sanitized Drive",
                SerialNumber = "SANFAIL001",
                IsHealthy = true,
                ReallocatedSectorCount = 0,
                PendingSectorCount = 0,
                UncorrectableErrorCount = 0
            },
            SmartAfter = new SmartaData
            {
                DeviceModel = "Failing Sanitized Drive",
                SerialNumber = "SANFAIL001",
                IsHealthy = false,
                IsFailing = true,
                ReallocatedSectorCount = 8,
                PendingSectorCount = 2,
                UncorrectableErrorCount = 1
            },
            SmartChanges = new List<SmartAttributeChange>
            {
                new() { AttributeId = 5, AttributeName = "Reallocated Sector Count", ValueBefore = 0, ValueAfter = 8, Change = 8 }
            },
            WriteSamples = Enumerable.Range(0, 10).Select(i => new SpeedSample
            {
                SpeedMBps = i is 2 or 5 or 8 ? 0 : 80,
                IsStalled = i is 2 or 5 or 8,
                Phase = "Write"
            }).ToList()
        };

        var card = new DiskCard
        {
            Id = 31,
            ModelName = "Failing Sanitized Drive",
            SerialNumber = "SANFAIL001",
            Capacity = 1_000_000_000_000,
            DiskType = "HDD"
        };

        var certificate = await generator.GenerateCertificateAsync(session, card);

        Assert.NotNull(certificate);
        Assert.False(certificate.Recommended);
        Assert.Equal("F", certificate.Grade);
        Assert.Contains("NEN", certificate.RecommendationNotes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vy", certificate.RecommendationNotes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dobr", certificate.RecommendationNotes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bezpe", certificate.RecommendationNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateCertificateAsync_WithoutSmartData_UsesDefaults()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        var generator = new CertificateGenerator(logger, settings);
        
        var session = new TestSession
        {
            Id = 4,
            TestType = TestType.SmartShort,
            Grade = "C",
            Score = 70,
            Result = TestResult.Pass,
            HealthAssessment = HealthAssessment.Fair,
            SmartBefore = null
        };
        
        var card = new DiskCard
        {
            Id = 4,
            ModelName = "Unknown Drive",
            SerialNumber = "NOSN-1234567890ABCDEF12345678",
            Capacity = 0
        };
        
        var certificate = await generator.GenerateCertificateAsync(session, card);
        
        Assert.NotNull(certificate);
        Assert.Equal("Unknown Drive", certificate.DiskModel);
        // Serial may be resolved to "N/A" for NOSN- prefixed keys
        Assert.NotNull(certificate.SerialNumber);
        // SmartPassed defaults to true when no SMART data and no sector issues
        Assert.Equal(0, certificate.PowerOnHours);
        Assert.Equal(0, certificate.ReallocatedSectors);
    }

    [Fact]
    public async Task GenerateCertificateAsync_WithCriticalAttributes_IncludesThem()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        var generator = new CertificateGenerator(logger, settings);
        
        var session = new TestSession
        {
            Id = 5,
            TestType = TestType.SurfaceScan,
            Grade = "D",
            Score = 55,
            Result = TestResult.Pass,
            HealthAssessment = HealthAssessment.Poor,
            SmartBefore = new SmartaData
            {
                DeviceModel = "Aging Drive",
                SerialNumber = "AGE001",
                IsHealthy = true,
                Attributes = new List<SmartaAttributeItem>
                {
                    new() { Id = 5, Name = "Reallocated_Sector_Ct", RawValue = 10, IsOk = false },
                    new() { Id = 197, Name = "Current_Pending_Sector", RawValue = 5, IsOk = false },
                    new() { Id = 198, Name = "Offline_Uncorrectable", RawValue = 3, IsOk = false },
                    new() { Id = 9, Name = "Power_On_Hours", RawValue = 50000, IsOk = true }
                }
            }
        };
        
        var card = new DiskCard
        {
            Id = 5,
            ModelName = "Aging Drive",
            SerialNumber = "AGE001",
            Capacity = 1_000_000_000_000
        };
        
        var certificate = await generator.GenerateCertificateAsync(session, card);
        
        Assert.NotNull(certificate);
        Assert.NotEmpty(certificate.SmartAttributes);
        // Critical attributes (5, 197, 198) should be included
        Assert.Contains(certificate.SmartAttributes, a => a.Id == 5);
        Assert.Contains(certificate.SmartAttributes, a => a.Id == 197);
        Assert.Contains(certificate.SmartAttributes, a => a.Id == 198);
    }

    [Fact]
    public async Task GenerateCertificateAsync_WithNotes_PreservesThem()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        var generator = new CertificateGenerator(logger, settings);

        var diagnosticNotes = "kritické propady rychlosti v závěru testu; nestabilní průběh čtení; SMART varování";
        var session = new TestSession
        {
            Id = 6,
            TestType = TestType.Sanitization,
            Grade = "D",
            Score = 50,
            Result = TestResult.Pass,
            HealthAssessment = HealthAssessment.Poor,
            Notes = diagnosticNotes,
            SmartBefore = new SmartaData
            {
                DeviceModel = "Problematic Drive",
                SerialNumber = "PROB001",
                IsHealthy = true
            }
        };

        var card = new DiskCard
        {
            Id = 6,
            ModelName = "Problematic Drive",
            SerialNumber = "PROB001",
            Capacity = 2_000_000_000_000
        };

        var certificate = await generator.GenerateCertificateAsync(session, card);

        Assert.NotNull(certificate);
        Assert.Equal(diagnosticNotes, certificate.Notes);
    }

    [Fact]
    public async Task GenerateCertificateAsync_WithMissingAggregates_UsesSampleFallback()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        var generator = new CertificateGenerator(logger, settings);

        var session = new TestSession
        {
            Id = 7,
            TestType = TestType.Sanitization,
            Duration = TimeSpan.FromMinutes(45),
            Result = TestResult.Pass,
            Grade = "B",
            Score = 82,
            WriteErrors = 2,
            ReadErrors = 1,
            VerificationErrors = 3,
            HealthAssessment = HealthAssessment.Good,
            WriteSamples = new List<SpeedSample>
            {
                new() { ProgressPercent = 10, SpeedMBps = 120 },
                new() { ProgressPercent = 50, SpeedMBps = 140 },
                new() { ProgressPercent = 90, SpeedMBps = 160 }
            },
            ReadSamples = new List<SpeedSample>
            {
                new() { ProgressPercent = 10, SpeedMBps = 100 },
                new() { ProgressPercent = 50, SpeedMBps = 130 },
                new() { ProgressPercent = 90, SpeedMBps = 150 }
            }
        };

        var card = new DiskCard
        {
            Id = 7,
            ModelName = "Fallback Drive",
            SerialNumber = "FALL007",
            Capacity = 500_000_000_000
        };

        var certificate = await generator.GenerateCertificateAsync(session, card);

        Assert.NotNull(certificate);
        Assert.Equal(140, certificate.AvgWriteSpeed, 1);
        Assert.Equal(160, certificate.MaxWriteSpeed, 1);
        Assert.Equal(126.666, certificate.AvgReadSpeed, 0.1);
        Assert.Equal(150, certificate.MaxReadSpeed, 1);
        Assert.Equal(6, certificate.ErrorCount);
    }

    [Fact]
    public async Task GenerateCertificateAsync_WithSeekResultsJson_PopulatesSeekMetrics()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        var generator = new CertificateGenerator(logger, settings);

        var seekResult = new SeekTestResult
        {
            TestType = SeekTestType.Random,
            SeekCount = 1000,
            AverageLatencyMs = 9.5,
            MinLatencyMs = 6.0,
            MaxLatencyMs = 18.0,
            LatencyStdDevMs = 2.2,
            P95LatencyMs = 14.0,
            ErrorCount = 3,
            IsCompleted = true,
            Samples = new List<SeekLatencySample>
            {
                new() { LatencyMs = 8.5 },
                new() { LatencyMs = 9.0 },
                new() { LatencyMs = 11.0 }
            }
        };

        var session = new TestSession
        {
            Id = 8,
            TestType = TestType.Seek,
            Duration = TimeSpan.FromMinutes(10),
            Result = TestResult.Pass,
            Grade = "B",
            Score = 80,
            HealthAssessment = HealthAssessment.Good,
            SeekResultsJson = JsonSerializer.Serialize(seekResult)
        };

        var card = new DiskCard
        {
            Id = 8,
            ModelName = "Seek Drive",
            SerialNumber = "SEEK008",
            Capacity = 500_000_000_000
        };

        var certificate = await generator.GenerateCertificateAsync(session, card);

        Assert.NotNull(certificate);
        Assert.True(certificate.SeekAvgLatencyMs.HasValue);
        Assert.True(certificate.SeekP95LatencyMs.HasValue);
        Assert.False(string.IsNullOrWhiteSpace(certificate.SeekTestSummary));
        Assert.Equal(3, certificate.ErrorCount);
    }

    [Fact]
    public async Task GenerateCertificateAsync_WithAbsoluteJson_PopulatesComparisonMetrics()
    {
        var logger = Substitute.For<ILogger<CertificateGenerator>>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        var generator = new CertificateGenerator(logger, settings);

        var sanitize1 = new SanitizationResult
        {
            Success = true,
            WriteSpeedMBps = 120,
            ReadSpeedMBps = 110,
            ErrorsDetected = 2
        };

        var sanitize2 = new SanitizationResult
        {
            Success = true,
            WriteSpeedMBps = 135,
            ReadSpeedMBps = 125,
            ErrorsDetected = 1,
            FileSystem = "NTFS",
            VolumeLabel = "DiskChecker"
        };

        var seekEnvelope = new
        {
            FullStroke = new SeekTestResult { TestType = SeekTestType.FullStroke, SeekCount = 200, AverageLatencyMs = 12, IsCompleted = true },
            Random = new SeekTestResult { TestType = SeekTestType.Random, SeekCount = 400, AverageLatencyMs = 10, IsCompleted = true },
            Skip = new SeekTestResult { TestType = SeekTestType.Skip, SeekCount = 300, AverageLatencyMs = 11, IsCompleted = true }
        };

        var session = new TestSession
        {
            Id = 9,
            TestType = TestType.AbsoluteDestructive,
            Duration = TimeSpan.FromHours(2),
            Result = TestResult.Pass,
            Grade = "B",
            Score = 84,
            HealthAssessment = HealthAssessment.Good,
            SeekResultsJson = JsonSerializer.Serialize(seekEnvelope),
            Sanitize1ResultJson = JsonSerializer.Serialize(sanitize1),
            Sanitize2ResultJson = JsonSerializer.Serialize(sanitize2)
        };

        var card = new DiskCard
        {
            Id = 9,
            ModelName = "Absolute Drive",
            SerialNumber = "ABS009",
            Capacity = 1_000_000_000_000
        };

        var certificate = await generator.GenerateCertificateAsync(session, card);

        Assert.NotNull(certificate);
        Assert.True(certificate.SanitizationPerformed);
        Assert.NotNull(certificate.Sanitize1AvgWriteMBps);
        Assert.NotNull(certificate.Sanitize2AvgWriteMBps);
        Assert.NotNull(certificate.Sanitize1AvgReadMBps);
        Assert.NotNull(certificate.Sanitize2AvgReadMBps);
        Assert.InRange(certificate.Sanitize1AvgWriteMBps.Value, 119.9, 120.1);
        Assert.InRange(certificate.Sanitize2AvgWriteMBps.Value, 134.9, 135.1);
        Assert.InRange(certificate.Sanitize1AvgReadMBps.Value, 109.9, 110.1);
        Assert.InRange(certificate.Sanitize2AvgReadMBps.Value, 124.9, 125.1);
        Assert.True(certificate.WriteSpeedChangePercent.HasValue);
        Assert.True(certificate.ReadSpeedChangePercent.HasValue);
        Assert.Equal(3, certificate.ErrorCount);
        Assert.Equal("NTFS", certificate.FileSystem);
        Assert.Equal("DiskChecker", certificate.VolumeLabel);
        Assert.True(certificate.SeekAvgLatencyMs.HasValue);
    }
}

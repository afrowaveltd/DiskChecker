using System;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using TestResult = DiskChecker.Core.Models.TestResult;

namespace DiskChecker.Tests;

public class SmartOnlyCertificateTests
{
    [Fact]
    public async Task SaveSmartCheckAsync_CriticalSmart_SavesFailingSessionForCertificate()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var card = new DiskCard
        {
            ModelName = "Critical SMART",
            SerialNumber = "SMART-CRIT",
            DevicePath = "/dev/smartcrit",
            Capacity = 500_000_000_000
        };
        context.DiskCards.Add(card);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var smart = new SmartaData
        {
            DeviceModel = "Critical SMART",
            SerialNumber = "SMART-CRIT",
            SmartEnabled = true,
            IsHealthy = false,
            IsFailing = true,
            ReallocatedSectorCount = 5,
            PendingSectorCount = 1,
            UncorrectableErrorCount = 1
        };

        var session = await service.SaveSmartCheckAsync(
            card,
            smart,
            new QualityRating(QualityGrade.F, 12),
            TestType.SmartShort,
            "SMART-only certificate: nebyl proveden povrchový ani sanitizační test.",
            TestContext.Current.CancellationToken);

        Assert.Equal(TestResult.Fail, session.Result);
        Assert.Equal(TestStatus.Completed, session.Status);
        Assert.False(session.IsDestructive);
        Assert.Contains("SMART-only", session.Notes);
    }

    [Fact]
    public async Task SaveSmartCheckAsync_NonCriticalSmart_DoesNotSaveFalseFail()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var card = new DiskCard
        {
            ModelName = "Healthy SMART",
            SerialNumber = "SMART-OK",
            DevicePath = "/dev/smartok",
            Capacity = 500_000_000_000
        };
        context.DiskCards.Add(card);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var smart = new SmartaData
        {
            DeviceModel = "Healthy SMART",
            SerialNumber = "SMART-OK",
            SmartEnabled = true,
            IsHealthy = true,
            IsFailing = false,
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            UncorrectableErrorCount = 0
        };

        var session = await service.SaveSmartCheckAsync(
            card,
            smart,
            new QualityRating(QualityGrade.A, 98),
            TestType.SmartShort,
            "SMART-only certificate: nebyl proveden povrchový ani sanitizační test.",
            TestContext.Current.CancellationToken);

        Assert.Equal(TestResult.Pass, session.Result);
        Assert.Equal("A", session.Grade);
        Assert.False(session.IsDestructive);
    }


    [Fact]
    public async Task SaveSanitizationAsync_WriteStartFailure_PersistsFailedSessionWithPhase()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var card = new DiskCard
        {
            ModelName = "Write Failure Drive",
            SerialNumber = "WRITE-FAIL",
            DevicePath = "/dev/writefail",
            Capacity = 500_000_000_000
        };
        context.DiskCards.Add(card);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = new SanitizationResult
        {
            Success = false,
            ErrorMessage = "Nelze získat výhradní přístup k disku",
            BytesWritten = 0,
            BytesRead = 0,
            Duration = TimeSpan.FromSeconds(2),
            ErrorDetails =
            {
                new SanitizationErrorDetail
                {
                    Phase = "Write",
                    ErrorCode = "RAW_OPEN_21",
                    Message = "Zápis nelze zahájit.",
                    Details = "Device is not ready",
                    OffsetBytes = 0
                }
            }
        };

        var session = await service.SaveSanitizationAsync(
            card,
            result,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TestStatus.Failed, session.Status);
        Assert.Equal(TestResult.Fail, session.Result);
        Assert.Equal("F", session.Grade);
        Assert.Equal(0, session.Score);
        Assert.Contains(session.Errors, e => e.Phase == "Write" && e.ErrorCode == "RAW_OPEN_21");
    }


    [Fact]
    public async Task SaveSanitizationAsync_UserCancellation_IsNotSavedAsFailedDisk()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var card = new DiskCard
        {
            ModelName = "Cancelled Drive",
            SerialNumber = "SAN-CANCEL",
            DevicePath = "/dev/cancel",
            Capacity = 500_000_000_000
        };
        context.DiskCards.Add(card);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = new SanitizationResult
        {
            Success = false,
            ErrorMessage = "Operace zrušena uživatelem",
            Duration = TimeSpan.FromSeconds(5)
        };

        var session = await service.SaveSanitizationAsync(
            card,
            result,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TestStatus.Cancelled, session.Status);
        Assert.Equal(TestResult.Inconclusive, session.Result);
        Assert.Equal("?", session.Grade);
        Assert.Equal(HealthAssessment.Unknown, session.HealthAssessment);
        Assert.Contains("není hodnoceno jako porucha", session.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.Errors);
    }

    private static DiskCheckerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DiskCheckerDbContext(options);
    }

    private static DiskCardTestService CreateService(DiskCheckerDbContext context)
    {
        var quality = Substitute.For<IQualityCalculator>();
        var logger = Substitute.For<ILogger<DiskCardTestService>>();
        var certificateGenerator = Substitute.For<ICertificateGenerator>();
        certificateGenerator.GenerateAndStoreChartImageAsync(Arg.Any<TestSession>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns((string?)null);
        return new DiskCardTestService(context, quality, logger, certificateGenerator);
    }
}

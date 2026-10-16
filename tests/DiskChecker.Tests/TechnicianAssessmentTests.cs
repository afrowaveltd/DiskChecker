using System;
using System.Linq;
using System.Threading.Tasks;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>Technician interpretation is stored separately from objective test data.</summary>
public class TechnicianAssessmentTests
{
    [Fact]
    public async Task UpdateAssessment_PreservesAutomaticGradeAndRecommendation_AndPersists()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseSqlite(connection).Options;
        int certId;
        await using (var context = new DiskCheckerDbContext(options))
        {
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var disk = new DiskCard { ModelName = "IDE HDD", SerialNumber = "IDE-ABC123",
                DevicePath = "/dev/sdd", Capacity = 160_041_885_696 };
            context.DiskCards.Add(disk);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var session = new TestSession { DiskCardId = disk.Id,
                TestType = TestType.SmartShort, Status = TestStatus.Completed,
                StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow };
            context.TestSessions.Add(session);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var cert = new DiskCertificate
            {
                DiskCardId = disk.Id, TestSessionId = session.Id,
                CertificateNumber = "TEST-TECHNICIAN-01",
                GeneratedAt = DateTime.UtcNow,
                Grade = "F", Score = 11, HealthStatus = "Critical",
                RecommendationNotes = "Automatic assessment: critical SMART warning",
                Notes = "SMART recorded excessive previous temperature",
                PdfGenerated = true, PdfPath = "/tmp/old-cert.pdf"
            };
            context.DiskCertificates.Add(cert);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            certId = cert.Id;
        }

        const string technicianText = "Disk has an old thermal SMART record. Current surface read test completed without errors; use only for noncritical data.";
        await using (var updateContext = new DiskCheckerDbContext(options))
        {
            var repo = new DiskCardRepository(updateContext);
            Assert.True(await repo.UpdateCertificateTechnicianAssessmentAsync(certId, technicianText));
        }

        await using (var verifyContext = new DiskCheckerDbContext(options))
        {
            var cert = await verifyContext.DiskCertificates.AsNoTracking()
                .SingleAsync(c => c.Id == certId, TestContext.Current.CancellationToken);
            Assert.Equal("F", cert.Grade);
            Assert.Equal(11, cert.Score);
            Assert.Equal("Automatic assessment: critical SMART warning", cert.RecommendationNotes);
            Assert.Equal("SMART recorded excessive previous temperature", cert.Notes);
            Assert.Equal(technicianText, cert.TechnicianAssessment);
            Assert.NotNull(cert.TechnicianAssessmentUpdatedAtUtc);
            Assert.False(cert.PdfGenerated);
            Assert.Null(cert.PdfPath);
        }
    }

    [Fact]
    public async Task AssessmentRejectsOverlongText_WithoutTouchingDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>().UseSqlite(connection).Options;
        await using var context = new DiskCheckerDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var repo = new DiskCardRepository(context);
        await Assert.ThrowsAsync<ArgumentException>(
            () => repo.UpdateCertificateTechnicianAssessmentAsync(1, new string('X', 2001)));
    }

    [Fact]
    public void SchemaPatcher_AddsAssessmentColumnsToExistingCertificates()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE DiskCertificates (Id INTEGER NOT NULL PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseSqlite(connection).Options;
        using var context = new DiskCheckerDbContext(options);
        SchemaCompatibilityPatcher.Apply(context);
        SchemaCompatibilityPatcher.Apply(context); // safe to run at every startup

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM pragma_table_info('DiskCertificates')";
        using var reader = cmd.ExecuteReader();
        var columns = new System.Collections.Generic.List<string>();
        while (reader.Read()) columns.Add(reader.GetString(0));
        Assert.Contains("TechnicianAssessment", columns);
        Assert.Contains("TechnicianAssessmentUpdatedAtUtc", columns);
    }
}

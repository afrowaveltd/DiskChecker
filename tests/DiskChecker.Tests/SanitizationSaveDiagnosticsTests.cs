using System;
using System.IO;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiskChecker.Tests;

public sealed class SanitizationSaveDiagnosticsTests
{
    [Theory]
    [InlineData(false, "SurfaceTest.Status.SanitizeSaveFailed")]
    [InlineData(true, "SurfaceTest.Status.SanitizeArtifactsFailed")]
    public void FailedCertificateIsNotConfusedWithFailedSession(
        bool sessionPersisted, string expected)
    {
        Assert.Equal(expected, SanitizationSaveDiagnostics.FailureStatusKey(sessionPersisted));
    }

    [Fact]
    public void OtherFailuresKeepOriginalErrorWithoutLeakingDatabaseCredentials()
    {
        var storage = new DatabaseStorageSettings
        {
            Provider = DatabaseProviderKind.Sqlite,
            ConnectionString = "Data Source=test.db;Password=private-secret"
        };
        var error = new DbUpdateException("save failed", new IOException("I/O failure"));

        var explanation = SanitizationSaveDiagnostics.Describe(error, storage);

        Assert.Contains("I/O failure", explanation);
        Assert.DoesNotContain("private-secret", explanation);
    }
}

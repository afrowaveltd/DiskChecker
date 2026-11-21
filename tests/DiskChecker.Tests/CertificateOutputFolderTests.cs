using System;
using System.IO;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Services;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>PDF destination must follow the persisted operator choice, not the elevated profile.</summary>
public sealed class CertificateOutputFolderTests
{
    [Fact]
    public async Task PdfGeneratorReadsChangedDirectoryOnEveryExportAndRejectsMissingDestination()
    {
        var root = Directory.CreateTempSubdirectory("diskchecker-certificate-location-").FullName;
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(root, "tester")).FullName;
            var second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
            string current = first;
            var settings = Substitute.For<ISettingsService>();
            settings.GetCertificatePathAsync().Returns(_ => current);
            var generator = new CertificateGenerator(settingsService: settings);

            DiskCertificate CreateCertificate() => new()
            {
                CertificateNumber = "LOCATION-CHECK",
                SerialNumber = "TEST-DRIVE-001",
                DiskModel = "Virtual test drive",
                Grade = "A",
                Score = 100,
                GeneratedAt = DateTime.UtcNow
            };

            var firstPdf = await generator.GeneratePdfAsync(CreateCertificate());
            Assert.Equal(Path.Combine(first, "Certificates"), Path.GetDirectoryName(firstPdf));
            Assert.True(File.Exists(firstPdf));

            current = second; // Same service instance: no application restart.
            var secondPdf = await generator.GeneratePdfAsync(CreateCertificate());
            Assert.Equal(Path.Combine(second, "Certificates"), Path.GetDirectoryName(secondPdf));
            Assert.True(File.Exists(secondPdf));
            Assert.True(File.Exists(firstPdf)); // Old PDFs are not moved/deleted.

            Directory.Delete(second, recursive: true);
            await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => generator.GeneratePdfAsync(CreateCertificate()));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CustomCertificatePathSurvivesSettingsRestartAndReset()
    {
        var root = Directory.CreateTempSubdirectory("diskchecker-settings-cert-").FullName;
        try
        {
            var first = new SettingsService(root);
            var destination = Path.Combine(root, "reports");
            await first.SetCertificatePathAsync(destination);
            Assert.Equal(destination, await new SettingsService(root).GetCertificatePathAsync());

            await first.SetCertificatePathAsync(null);
            Assert.Null(await new SettingsService(root).GetCertificatePathAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

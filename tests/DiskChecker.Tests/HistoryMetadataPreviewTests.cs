using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public sealed class HistoryMetadataPreviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewReadsMetadataAcrossBuffersAndIgnoresNestedDecoys(bool metadataFirst)
    {
        var folder = Directory.CreateTempSubdirectory("dc-preview-").FullName;
        try
        {
            var path = Path.Combine(folder, "large.json");
            var metadata = "\"metadata\":{\"version\":\"1.0.0\",\"diskCount\":2,\"applicationVersion\":\"" + new string('a', 70000) + "\"}";
            var records = "\"diskCards\":[{\"metadata\":{\"version\":\"wrong\"},\"payload\":\"" + new string('b', 200000) + "\"}]";
            var json = metadataFirst ? "{" + metadata + "," + records + "}" : "{" + records + "," + metadata + "}";
            await File.WriteAllTextAsync(path, json, Encoding.UTF8, TestContext.Current.CancellationToken);

            var service = new DataExportImportService(Substitute.For<IDiskCardRepository>());
            var preview = await service.PeekMetadataAsync(path, TestContext.Current.CancellationToken);

            Assert.NotNull(preview);
            Assert.Equal("1.0.0", preview.Version);
            Assert.Equal(2, preview.DiskCount);
            Assert.Equal(70000, preview.ApplicationVersion.Length);
            Assert.Equal(new FileInfo(path).Length, preview.TotalSizeBytes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"metadata\":{}}")]
    [InlineData("{\"metadata\":{\"version\":\"1.0.0\"},\"testSessions\":[")]
    [InlineData("{\"metadata\":{\"version\":\"1.0.0\"}} trailing")]
    [InlineData("[]")]
    public async Task PreviewRejectsMissingOrMalformedMetadata(string json)
    {
        var folder = Directory.CreateTempSubdirectory("dc-invalid-preview-").FullName;
        try
        {
            var path = Path.Combine(folder, "invalid.json");
            await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
            var service = new DataExportImportService(Substitute.For<IDiskCardRepository>());
            Assert.Null(await service.PeekMetadataAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"metadata\":{\"version\":\"1.0.0\"}}")]
    [InlineData("{\"metadata\":{},\"diskCards\":[],\"testSessions\":[],\"certificates\":[]}")]
    public async Task ImportRejectsIncompletePackageBeforeAccessingRepository(string json)
    {
        var folder = Directory.CreateTempSubdirectory("dc-invalid-package-").FullName;
        try
        {
            var path = Path.Combine(folder, "invalid.json");
            await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
            var repository = Substitute.For<IDiskCardRepository>();
            var result = await new DataExportImportService(repository).ImportAsync(path, ImportMode.Add, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Empty(repository.ReceivedCalls());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledPreviewPropagatesCancellation()
    {
        var folder = Directory.CreateTempSubdirectory("dc-cancel-preview-").FullName;
        try
        {
            var path = Path.Combine(folder, "history.json");
            await File.WriteAllTextAsync(path, "{\"metadata\":{\"version\":\"1.0.0\"}}", TestContext.Current.CancellationToken);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancellation.Cancel();
            var service = new DataExportImportService(Substitute.For<IDiskCardRepository>());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PeekMetadataAsync(path, cancellation.Token));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

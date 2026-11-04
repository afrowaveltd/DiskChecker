using System.Security.Cryptography;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;

namespace DiskChecker.Tui.Tests.Support;

/// <summary>
/// Deterministic fixtures for the backup, restore and settings screens: throw-away source
/// devices, target volumes and backup folders created under the test harness directory.
/// </summary>
internal static class BackupTestFixtures
{
    /// <summary>3 MiB – a multiple of the 512 B VHDx requirement and of the 1 MiB block size.</summary>
    public const long SmallImageSize = 3L * 1024 * 1024;

    public static string NewDirectory(string root, params string[] segments)
    {
        string path = root;
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Creates a deterministic fake device image and returns its path.</summary>
    public static string CreateSourceImage(string directory, long sizeBytes = SmallImageSize, byte seed = 17)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "source-disk.img");
        var buffer = new byte[64 * 1024];
        long written = 0;
        int counter = seed;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        while (written < sizeBytes)
        {
            int chunk = (int)Math.Min(buffer.Length, sizeBytes - written);
            for (int index = 0; index < chunk; index++)
            {
                buffer[index] = (byte)(counter + index);
            }

            stream.Write(buffer, 0, chunk);
            written += chunk;
            counter = (counter + chunk) & 0xFF;
        }

        return path;
    }

    /// <summary>Creates a small directory tree used by the file level backup tests.</summary>
    public static string CreateSourceFolder(string directory, int fileCount = 3)
    {
        Directory.CreateDirectory(directory);
        for (int index = 0; index < fileCount; index++)
        {
            string path = Path.Combine(directory, "file-" + index + ".txt");
            File.WriteAllText(path, new string((char)('a' + index), 512 + index));
        }

        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        File.WriteAllText(Path.Combine(directory, "nested", "deep.txt"), "hluboký obsah");
        return directory;
    }

    /// <summary>Builds the disk description the selection service would hold.</summary>
    public static CoreDriveInfo CreateDisk(string path, long totalSize, string model = "Testovací disk X1") => new()
    {
        Path = path,
        Name = model,
        Model = model,
        TotalSize = totalSize,
        IsPhysical = true,
        IsReady = true,
        SerialNumber = "SN-TEST-0001",
    };

    /// <summary>Runs a raw image backup so restore tests have a real backup folder.</summary>
    public static async Task<TuiBackupResult> CreateRawBackupAsync(string targetParent, string sourcePath, long totalSize)
    {
        Directory.CreateDirectory(targetParent);
        var service = new TuiImageBackupService();
        return await service.RunAsync(
            new TuiBackupRequest
            {
                SourcePath = sourcePath,
                SourceModel = "Testovací disk X1",
                TotalSize = totalSize,
                Mode = TuiBackupMode.RawImage,
                TargetRoot = targetParent,
            },
            progress: null,
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Runs a fixed VHDx backup.</summary>
    public static async Task<TuiBackupResult> CreateVhdxBackupAsync(string targetParent, string sourcePath, long totalSize)
    {
        Directory.CreateDirectory(targetParent);
        var service = new TuiImageBackupService();
        return await service.RunAsync(
            new TuiBackupRequest
            {
                SourcePath = sourcePath,
                SourceModel = "Testovací disk X1",
                TotalSize = totalSize,
                Mode = TuiBackupMode.VhdxImage,
                TargetRoot = targetParent,
            },
            progress: null,
            CancellationToken.None).ConfigureAwait(false);
    }

    public static string Sha256OfFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static byte[] ReadRegion(string path, long offset, int length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = offset;
        var buffer = new byte[length];
        int read = 0;
        while (read < buffer.Length)
        {
            int chunk = stream.Read(buffer, read, buffer.Length - read);
            if (chunk == 0)
            {
                break;
            }

            read += chunk;
        }

        return buffer;
    }

    public static string? FindChildDirectory(string parent, string pattern)
    {
        if (!Directory.Exists(parent))
        {
            return null;
        }

        return Directory.GetDirectories(parent, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

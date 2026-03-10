using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

public interface IFileTestExecutor
{
    Task<FileTestResult> ExecuteSequentialTestAsync(string filePath, FileTestOptions options);
}

public class FileTestOptions
{
    public bool VerifyWrites { get; set; } = true;
    public int BufferSize { get; set; } = 1024 * 1024;
    public long MaxFileSize { get; set; } = 100 * 1024 * 1024; // 100MB default
}

public class FileTestResult
{
    public string TestId { get; set; } = Guid.NewGuid().ToString();
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime EndTime { get; set; } = DateTime.UtcNow;
    public TestStatus Status { get; set; } = TestStatus.Pending;
    public string? FilePath { get; set; }
    public string? Message { get; set; }
    public long BytesProcessed { get; set; }
    public double AverageSpeedMbps { get; set; }
    public int ErrorCount { get; set; }
}
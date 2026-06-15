using System.Reflection;
using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware.Sanitization;
using Xunit;

namespace DiskChecker.Tests;

public class SanitizationProgressTests
{
    [Theory]
    [InlineData("Zápis nul")]
    [InlineData("Zápis nul (O_DIRECT)")]
    [InlineData("Zápis nul (FileStream)")]
    public void IsWritePhase_RecognizesCompatibleDisplayNames(string phase)
    {
        var progress = new SanitizationProgress { Phase = phase };

        Assert.True(progress.IsWritePhase);
        Assert.False(progress.IsReadVerifyPhase);
    }

    [Theory]
    [InlineData("Čtení a ověření")]
    [InlineData("Čtení a ověření (FileStream)")]
    public void IsReadVerifyPhase_RecognizesCompatibleDisplayNames(string phase)
    {
        var progress = new SanitizationProgress { Phase = phase };

        Assert.True(progress.IsReadVerifyPhase);
        Assert.False(progress.IsWritePhase);
    }

    [Fact]
    public void PhaseKind_RecognizesPhaseIndependentlyOfDisplayText()
    {
        var progress = new SanitizationProgress
        {
            PhaseKind = SanitizationProgressPhase.ReadVerify,
            Phase = "Platform-specific read detail"
        };

        Assert.True(progress.IsReadVerifyPhase);
    }

    [Fact]
    public async Task LinuxWriteAndReadVerify_ReportProgressAndVerifyZeros_OnTemporaryFile()
    {
        var path = Path.GetTempFileName();
        const int fileSize = 1024 * 1024;

        try
        {
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)0xA5, fileSize).ToArray());
            var service = new LinuxDiskSanitizationService();
            var writeProgress = new List<SanitizationProgress>();
            var readProgress = new List<SanitizationProgress>();

            var writeResult = await InvokePhaseAsync(
                service,
                "WriteZerosAsync",
                path,
                fileSize,
                new CallbackProgress<SanitizationProgress>(writeProgress.Add));
            var readResult = await InvokePhaseAsync(
                service,
                "ReadAndVerifyAsync",
                path,
                fileSize,
                new CallbackProgress<SanitizationProgress>(readProgress.Add));

            Assert.True(GetProperty<bool>(writeResult, "Success"));
            Assert.True(GetProperty<bool>(readResult, "Success"));
            Assert.All(writeProgress, item => Assert.True(item.IsWritePhase));
            Assert.All(readProgress, item => Assert.True(item.IsReadVerifyPhase));
            Assert.Equal(100, writeProgress[^1].ProgressPercent);
            Assert.Equal(100, readProgress[^1].ProgressPercent);
            Assert.All(await File.ReadAllBytesAsync(path), value => Assert.Equal(0, value));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LinuxReadVerify_RetriesWhenWriteHandleIsStillBeingReleased()
    {
        var path = Path.GetTempFileName();
        const int fileSize = 1024 * 1024;

        try
        {
            await File.WriteAllBytesAsync(path, new byte[fileSize]);
            var service = new LinuxDiskSanitizationService();
            using var writeLock = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);

            var readTask = InvokePhaseAsync(
                service,
                "ReadAndVerifyAsync",
                path,
                fileSize,
                new CallbackProgress<SanitizationProgress>(_ => { }));

            await Task.Delay(100);
            Assert.False(readTask.IsCompleted);
            writeLock.Dispose();

            var readResult = await readTask;

            Assert.True(GetProperty<bool>(readResult, "Success"));
            Assert.Equal(fileSize, GetProperty<long>(readResult, "BytesRead"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<object> InvokePhaseAsync(
        LinuxDiskSanitizationService service,
        string methodName,
        string path,
        long size,
        IProgress<SanitizationProgress> progress)
    {
        var method = typeof(LinuxDiskSanitizationService).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = Assert.IsAssignableFrom<Task>(
            method.Invoke(service, new object[] { path, size, progress, CancellationToken.None }));
        await task;

        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        Assert.NotNull(result);
        Assert.Equal(method.ReturnType.GenericTypeArguments[0], result.GetType());
        return result;
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        var value = instance.GetType().GetProperty(propertyName)?.GetValue(instance);
        return Assert.IsType<T>(value);
    }

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public CallbackProgress(Action<T> callback)
        {
            _callback = callback;
        }

        public void Report(T value) => _callback(value);
    }
}

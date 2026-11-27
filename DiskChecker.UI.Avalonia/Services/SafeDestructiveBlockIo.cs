using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>Full-length zero write/read verification for safe destructive passes.</summary>
public static class SafeDestructiveBlockIo
{
    public static long GetBlockCount(long length, int blockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        return 1 + (length - 1) / blockSize;
    }

    public static async Task TransferAsync(Stream device, byte[] buffer, int count,
        bool write, long offset, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length);
        cancellationToken.ThrowIfCancellationRequested();
        if (write)
        {
            Array.Clear(buffer, 0, count);
            await device.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        else
        {
            await device.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken);
            int firstNonZero = buffer.AsSpan(0, count).IndexOfAnyExcept((byte)0);
            if (firstNonZero >= 0)
                throw new IOException($"Sanitization verification found nonzero data at byte {offset + firstNonZero}.");
        }
    }
}

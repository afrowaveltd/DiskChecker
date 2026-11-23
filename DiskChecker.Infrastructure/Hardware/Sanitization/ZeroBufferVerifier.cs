using System;

namespace DiskChecker.Infrastructure.Hardware.Sanitization;

/// <summary>
/// Checks zero-filled read-back blocks without walking every byte on the
/// overwhelmingly common successful path. The vectorized span search keeps
/// verification overhead from being mistaken for slow disk I/O.
/// </summary>
internal static class ZeroBufferVerifier
{
    /// <summary>
    /// Returns the exact number of nonzero bytes and the first offending
    /// position, or (0, -1) when the entire buffer is zero.
    /// </summary>
    internal static (int NonZeroCount, int FirstNonZeroIndex) Inspect(ReadOnlySpan<byte> data)
    {
        // IndexOfAnyExcept uses the runtime's optimized span search.
        // Avoid a scalar per-byte loop for the typical all-zero block.
        int first = data.IndexOfAnyExcept((byte)0);
        if (first < 0)
            return (0, -1);

        // Preserve existing diagnostic detail when corruption is encountered:
        // we still count every offending byte and retain the first offset.
        int count = 0;
        for (int i = first; i < data.Length; i++)
        {
            if (data[i] != 0)
                count++;
        }

        return (count, first);
    }
}

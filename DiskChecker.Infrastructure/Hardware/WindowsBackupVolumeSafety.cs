using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using DiskChecker.Infrastructure.Hardware.Sanitization;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>Resolves Windows drive-letter volumes to every backing physical disk.</summary>
public static class WindowsBackupVolumeSafety
{
    private const string PhysicalPrefix = @"\\.\PhysicalDrive";
    private const uint GetVolumeDiskExtents = 0x00560000;

    public static bool IsIndependentVolume(string? sourcePath, IReadOnlyCollection<uint>? diskNumbers)
    {
        if (sourcePath == null || !sourcePath.StartsWith(PhysicalPrefix, StringComparison.OrdinalIgnoreCase) ||
            !uint.TryParse(sourcePath.AsSpan(PhysicalPrefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var sourceNumber) ||
            diskNumbers == null || diskNumbers.Count == 0)
            return false;

        foreach (var diskNumber in diskNumbers)
            if (diskNumber == sourceNumber)
                return false;
        return true;
    }

    /// <summary>Returns null when the backing extents cannot be established. Never opens a volume for writing.</summary>
    public static IReadOnlyCollection<uint>? GetDiskNumbers(string root)
    {
        if (!OperatingSystem.IsWindows() || root.Length != 3 ||
            !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
            return null;

        using var handle = Win32DiskInterop.CreateFile(@"\\.\" + root[..2], 0,
            Win32DiskInterop.FILE_SHARE_READ | Win32DiskInterop.FILE_SHARE_WRITE,
            IntPtr.Zero, Win32DiskInterop.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return null;

        const int bufferLength = 65536;
        IntPtr buffer = Marshal.AllocHGlobal(bufferLength);
        try
        {
            if (!Win32DiskInterop.DeviceIoControl(handle, GetVolumeDiskExtents,
                IntPtr.Zero, 0, buffer, bufferLength, out uint returned, IntPtr.Zero))
                return null;

            int offset = Marshal.OffsetOf<VolumeExtents>(nameof(VolumeExtents.First)).ToInt32();
            int stride = Marshal.SizeOf<DiskExtent>();
            if (returned < offset)
                return null;
            int count = Marshal.ReadInt32(buffer);
            if (count <= 0 || count > (returned - offset) / stride)
                return null;

            var numbers = new HashSet<uint>();
            for (int i = 0; i < count; i++)
                numbers.Add(unchecked((uint)Marshal.ReadInt32(buffer, offset + i * stride)));
            return numbers;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DiskExtent
    {
        public uint DiskNumber;
        public long StartingOffset;
        public long ExtentLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VolumeExtents
    {
        public uint NumberOfDiskExtents;
        public DiskExtent First;
    }
}

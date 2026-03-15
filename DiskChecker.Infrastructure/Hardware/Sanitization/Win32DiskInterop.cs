using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DiskChecker.Infrastructure.Hardware.Sanitization;

/// <summary>
/// Win32 P/Invoke declarations for raw disk access
/// </summary>
internal static class Win32DiskInterop
{
    // Win32 API imports for raw disk access
#pragma warning disable CA2101 // Specify marshaling for P/Invoke string arguments
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);
#pragma warning restore CA2101

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetFilePointerEx(
        IntPtr hFile,
        long liDistanceToMove,
        out long lpNewFilePointer,
        uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool WriteFile(
        IntPtr hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadFile(
        IntPtr hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    internal static extern bool DeviceIoControlRaw(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    // ===== NEW: Volume management functions for exclusive disk access =====

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool GetVolumeNameForVolumeMountPoint(
        string lpszVolumeMountPoint,
        [Out] char[] lpszVolumeName,
        uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool GetVolumePathNamesForVolumeName(
        string lpszVolumeName,
        [Out] char[] lpszVolumePathNames,
        uint cchBufferLength,
        out uint lpcchReturnLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool DeleteVolumeMountPoint(
        string lpszVolumeMountPoint);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    // Constants
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    internal const uint FILE_ATTRIBUTE_NO_BUFFERING = 0x20000000;
    internal const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    internal const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
    internal const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x700A0;
    internal const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x70140;
    internal const uint IOCTL_DISK_REASSIGN_BLOCKS = 0x7405C;
    internal const uint FILE_DEVICE_DISK = 0x7;
    internal const uint METHOD_BUFFERED = 0;
    internal const uint FILE_ANY_ACCESS = 0;
    internal const uint FILE_SPECIAL_ACCESS = 0;

    // ===== NEW: Volume lock/dismount control codes =====
    internal const uint FSCTL_LOCK_VOLUME = 0x00090018;
    internal const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
    internal const uint FSCTL_UNLOCK_VOLUME = 0x00090024;
    internal const uint IOCTL_VOLUME_SET_GPT_ATTRIBUTES = 0x000CC040;
}

/// <summary>
/// Security attributes structure for advanced file operations
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SECURITY_ATTRIBUTES
{
    public uint nLength;
    public IntPtr lpSecurityDescriptor;
    public bool bInheritHandle;
}

/// <summary>
/// Disk geometry structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DISK_GEOMETRY_EX
{
    public DISK_GEOMETRY Geometry;
    public long DiskSize;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
    public byte[] Data;
}

/// <summary>
/// Disk geometry basic info
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DISK_GEOMETRY
{
    public long Cylinders;
    public uint MediaType;
    public uint TracksPerCylinder;
    public uint SectorsPerTrack;
    public uint BytesPerSector;
}
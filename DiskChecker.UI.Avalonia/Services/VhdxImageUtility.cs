using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Minimal fixed VHDX writer/reader used by backup, restore and safe destructive workflows.
/// The payload block size is 1 MiB; images are pre-extended to include the full rounded
/// payload area so Windows' VHDX validator does not reject the file as too small.
/// </summary>
public sealed record VhdxImageInfo(long VirtualDiskSizeBytes, long DataStartOffset, long RequiredFileBytes);

public static class VhdxImageUtility
{
    public const int LogicalBlockSize = 1024 * 1024;
    public const int PhysicalSectorSize = 4096;
    private const long BatRegionOffset = 2L * LogicalBlockSize; // header + reserved log
    private static readonly byte[] BatRegionGuid = { 0x66, 0x77, 0xC2, 0x2D, 0x23, 0xF6, 0x00, 0x42, 0x9D, 0x64, 0x11, 0x5E, 0x9B, 0xFD, 0x4A, 0x08 };
    private static readonly byte[] MetadataRegionGuid = { 0x06, 0xA2, 0x7C, 0x8B, 0x90, 0x47, 0x9A, 0x4B, 0xB8, 0xFE, 0x57, 0x5F, 0x05, 0x0F, 0x88, 0x6E };

    public static long RoundUp(long value, long alignment)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alignment);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return ((value + alignment - 1) / alignment) * alignment;
    }

    public static long CalculateDataStartOffset(long diskSizeBytes)
    {
        var diskSizeRounded = RoundUp(diskSizeBytes, LogicalBlockSize);
        var chunkCount = diskSizeRounded / LogicalBlockSize;
        var batSize = RoundUp(checked(chunkCount * 8), LogicalBlockSize);
        // The 1 MiB header is followed by the 1 MiB empty log, a BAT
        // aligned to 1 MiB, and the 1 MiB metadata region.
        return checked(BatRegionOffset + batSize + LogicalBlockSize);
    }

    public static long CalculateRequiredFileBytes(long diskSizeBytes)
        => CalculateDataStartOffset(diskSizeBytes) + RoundUp(diskSizeBytes, LogicalBlockSize);

    public static void ValidateFixedImageLength(string path, long diskSizeBytes)
    {
        var required = CalculateRequiredFileBytes(diskSizeBytes);
        var actual = new FileInfo(path).Length;
        if (actual < required)
        {
            throw new InvalidOperationException(
                $"VHDx soubor je příliš malý: má {actual} B, ale pro virtuální disk {diskSizeBytes} B musí mít alespoň {required} B.");
        }
    }

    public static bool TryReadInfo(string path, out VhdxImageInfo info)
    {
        info = new VhdxImageInfo(0, 0, 0);
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 2L * 1024 * 1024) return false;

            Span<byte> sig = stackalloc byte[8];
            fs.ReadExactly(sig);
            if (!sig.SequenceEqual(new byte[] { 0x76, 0x68, 0x64, 0x78, 0x66, 0x69, 0x6C, 0x65 }))
                return false;

            // This writer stores BAT at 256 KiB and metadata immediately after the BAT.
            // Parse region table enough to find metadata offset/length. Fall back to the
            // current writer layout if any optional field is missing.
            // Validate the active header CRC-32C and the region-table CRC-32C,
            // rather than accepting signatures alone.
            fs.Position = 64L * 1024;
            Span<byte> header = stackalloc byte[4096];
            fs.ReadExactly(header);
            if (!header[..4].SequenceEqual(new byte[] { 0x68, 0x65, 0x61, 0x64 })) return false;
            var headerCrc = BitConverter.ToUInt32(header.Slice(4, 4));
            header.Slice(4, 4).Clear();
            if (Crc32(header.ToArray()) != headerCrc) return false;

            fs.Position = 192L * 1024;
            Span<byte> region = stackalloc byte[64 * 1024];
            fs.ReadExactly(region);
            if (!region[..4].SequenceEqual(new byte[] { 0x72, 0x65, 0x67, 0x69 })) return false;
            var regionCrc = BitConverter.ToUInt32(region.Slice(4, 4));
            region.Slice(4, 4).Clear();
            if (Crc32(region.ToArray()) != regionCrc) return false;

            var entryCount = BitConverter.ToUInt32(region.Slice(8, 4));
            ulong metadataOffset = 0;
            ulong batOffset = 0;
            uint batRegionLength = 0;
            for (int i = 0; i < Math.Min(entryCount, 2047); i++)
            {
                var entry = region.Slice(16 + i * 32, 32);
                if (entry[..16].SequenceEqual(MetadataRegionGuid))
                    metadataOffset = BitConverter.ToUInt64(entry.Slice(16, 8));
                else if (entry[..16].SequenceEqual(BatRegionGuid))
                {
                    batOffset = BitConverter.ToUInt64(entry.Slice(16, 8));
                    batRegionLength = BitConverter.ToUInt32(entry.Slice(24, 4));
                }
            }
            if (batOffset != (ulong)BatRegionOffset || batRegionLength == 0 ||
                batRegionLength % LogicalBlockSize != 0 ||
                metadataOffset != batOffset + batRegionLength ||
                metadataOffset + LogicalBlockSize > (ulong)fs.Length) return false;

            fs.Position = (long)metadataOffset;
            Span<byte> metadataHeader = stackalloc byte[32];
            fs.ReadExactly(metadataHeader);
            if (!metadataHeader[..8].SequenceEqual(new byte[] { 0x6D, 0x65, 0x74, 0x61, 0x64, 0x61, 0x74, 0x61 }))
                return false;

            var metadataEntryCount = BitConverter.ToUInt16(metadataHeader.Slice(10, 2));
            if (metadataEntryCount > 2047) return false;
            Span<byte> entries = stackalloc byte[metadataEntryCount * 32];
            fs.ReadExactly(entries);

            long virtualDiskSize = 0;
            bool fixedFileParameters = false;
            Span<byte> sizeBytes = stackalloc byte[8];
            Span<byte> paramBytes = stackalloc byte[8];
            for (int i = 0; i < metadataEntryCount; i++)
            {
                var entry = entries.Slice(i * 32, 32);
                var itemOffset = BitConverter.ToUInt32(entry.Slice(16, 4));
                var itemLength = BitConverter.ToUInt32(entry.Slice(20, 4));
                if (itemOffset < 64 * 1024 || (ulong)itemOffset + itemLength > LogicalBlockSize)
                    return false;
                if (entry[..16].SequenceEqual(new byte[] { 0x24, 0x42, 0xA5, 0x2F, 0x1B, 0xCD, 0x76, 0x48, 0xB2, 0x11, 0x5D, 0xBE, 0xD8, 0x3B, 0xF4, 0xB8 }))
                {
                    if (itemLength != 8) return false;
                    fs.Position = (long)metadataOffset + itemOffset;
                    fs.ReadExactly(sizeBytes);
                    virtualDiskSize = checked((long)BitConverter.ToUInt64(sizeBytes));
                }
                else if (entry[..16].SequenceEqual(new byte[] { 0x37, 0x67, 0xA1, 0xCA, 0x36, 0xFA, 0x43, 0x4D, 0xB3, 0xB6, 0x33, 0xF0, 0xAA, 0x44, 0xE7, 0x6B }))
                {
                    if (itemLength != 8) return false;
                    fs.Position = (long)metadataOffset + itemOffset;
                    fs.ReadExactly(paramBytes);
                    fixedFileParameters = BitConverter.ToUInt32(paramBytes[..4]) == LogicalBlockSize &&
                        BitConverter.ToUInt32(paramBytes.Slice(4, 4)) == 1;
                }
            }

            if (virtualDiskSize <= 0 || virtualDiskSize % 512 != 0 || !fixedFileParameters) return false;
            var dataStart = CalculateDataStartOffset(virtualDiskSize);
            var required = CalculateRequiredFileBytes(virtualDiskSize);
            if (dataStart != (long)metadataOffset + LogicalBlockSize || fs.Length < required ||
                batRegionLength != RoundUp((RoundUp(virtualDiskSize, LogicalBlockSize) / LogicalBlockSize) * 8, LogicalBlockSize))
                return false;

            // Only contiguous, fully-present fixed images are restorable by a raw copy.
            fs.Position = (long)batOffset;
            var count = RoundUp(virtualDiskSize, LogicalBlockSize) / LogicalBlockSize;
            Span<byte> batEntryBytes = stackalloc byte[8];
            for (long i = 0; i < count; i++)
            {
                fs.ReadExactly(batEntryBytes);
                var expected = (((ulong)(dataStart / LogicalBlockSize + i)) << 20) | 6UL;
                if (BitConverter.ToUInt64(batEntryBytes) != expected) return false;
            }
            info = new VhdxImageInfo(virtualDiskSize, dataStart, required);
            return true;
        }
        catch
        {
            info = new VhdxImageInfo(0, 0, 0);
            return false;
        }
    }

    public static async Task WriteFixedHeaderAsync(string path, long diskSizeBytes, CancellationToken ct)
    {
        var diskSizeRounded = RoundUp(diskSizeBytes, LogicalBlockSize);
        var chunkCount = diskSizeRounded / LogicalBlockSize;
        if (diskSizeBytes <= 0 || diskSizeBytes % 512 != 0)
            throw new ArgumentOutOfRangeException(nameof(diskSizeBytes), "VHDX disk size must be a positive multiple of 512 bytes.");
        var batSize = RoundUp(checked(chunkCount * 8), LogicalBlockSize);
        var dataStartOffset = CalculateDataStartOffset(diskSizeBytes);
        var requiredFileBytes = CalculateRequiredFileBytes(diskSizeBytes);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan);
        using var writer = new BinaryWriter(fs);

        writer.Write(new byte[] { 0x76, 0x68, 0x64, 0x78, 0x66, 0x69, 0x6C, 0x65 }); // vhdxfile
        writer.Write((uint)0);
        writer.Write(new byte[65536 - 12]);

        WriteHeaderAt(writer, 0);
        while (fs.Position < 128L * 1024) writer.Write((byte)0);
        WriteHeaderAt(writer, 1);
        while (fs.Position < 192L * 1024) writer.Write((byte)0);

        WriteRegionTable(writer, batSize); // 192 KiB region table
        WriteRegionTable(writer, batSize); // 256 KiB redundant region table

        while (fs.Position < BatRegionOffset) writer.Write((byte)0);

        var fileOffsetInSectors = dataStartOffset / LogicalBlockSize;
        for (long i = 0; i < chunkCount; i++)
        {
            var batEntry = (6UL << 0) | ((ulong)(fileOffsetInSectors + i) << 20);
            writer.Write(batEntry);
        }

        var batEnd = BatRegionOffset + batSize;
        while (fs.Position < batEnd) writer.Write((byte)0);

        WriteMetadataAt(writer, diskSizeBytes);
        while (fs.Position < dataStartOffset) writer.Write((byte)0);

        // Critical for Windows validation: the file must contain the complete rounded
        // payload range referenced by the BAT, even when the source disk size is not
        // an exact multiple of 1 MiB.
        fs.SetLength(requiredFileBytes);
        await fs.FlushAsync(ct);
    }

    private static void WriteRegionTable(BinaryWriter writer, long batSize)
    {
        var table = new byte[64 * 1024];
        table[0] = 0x72; table[1] = 0x65; table[2] = 0x67; table[3] = 0x69; // regi
        BitConverter.GetBytes((uint)2).CopyTo(table, 8);

        Array.Copy(BatRegionGuid, 0, table, 16, 16);
        BitConverter.GetBytes((ulong)BatRegionOffset).CopyTo(table, 32);
        BitConverter.GetBytes(checked((uint)batSize)).CopyTo(table, 40);
        BitConverter.GetBytes((uint)1).CopyTo(table, 44);

        Array.Copy(MetadataRegionGuid, 0, table, 48, 16);
        BitConverter.GetBytes((ulong)(BatRegionOffset + batSize)).CopyTo(table, 64);
        BitConverter.GetBytes((uint)(1024L * 1024)).CopyTo(table, 72);
        BitConverter.GetBytes((uint)1).CopyTo(table, 76);

        var crc = Crc32(table);
        BitConverter.GetBytes(crc).CopyTo(table, 4);
        writer.Write(table);
    }

    private static void WriteHeaderAt(BinaryWriter writer, ulong sequenceNumber)
    {
        using var ms = new MemoryStream(4096);
        using var bw = new BinaryWriter(ms);

        bw.Write(new byte[] { 0x68, 0x65, 0x61, 0x64 }); // head
        bw.Write((uint)0);
        bw.Write(sequenceNumber);
        bw.Write(Guid.NewGuid().ToByteArray());
        bw.Write(Guid.NewGuid().ToByteArray());
        bw.Write(new byte[16]);
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((uint)LogicalBlockSize); // reserved, empty 1 MiB log
        bw.Write((ulong)LogicalBlockSize); // log begins at 1 MiB
        bw.Write(new byte[4016]);

        var header = ms.ToArray();
        BitConverter.GetBytes(Crc32(header)).CopyTo(header, 4);
        writer.Write(header);
    }

    private static uint Crc32(byte[] data)
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0x82F63B78 : crc >> 1;
            table[i] = crc;
        }
        uint result = 0xFFFFFFFF;
        foreach (byte b in data)
            result = table[(result ^ b) & 0xFF] ^ (result >> 8);
        return result ^ 0xFFFFFFFF;
    }

    private static void WriteMetadataAt(BinaryWriter writer, long diskSizeRounded)
    {
        long metadataStart = writer.BaseStream.Position;
        writer.Write(new byte[] { 0x6D, 0x65, 0x74, 0x61, 0x64, 0x61, 0x74, 0x61 }); // metadata
        writer.Write((ushort)0);
        writer.Write((ushort)5);
        writer.Write(new byte[20]);

        long entriesStart = writer.BaseStream.Position;
        byte[] entries = new byte[160];
        writer.Write(entries);

        // Metadata items must start AFTER the reserved 64 KiB metadata table.
        while (writer.BaseStream.Position < metadataStart + 64L * 1024)
            writer.Write((byte)0);

        long item0Pos = writer.BaseStream.Position;
        writer.Write((uint)LogicalBlockSize);
        writer.Write((uint)1); // LeaveBlocksAllocated: fixed image
        WriteMetadataEntry(entries, 0, new byte[] { 0x37, 0x67, 0xA1, 0xCA, 0x36, 0xFA, 0x43, 0x4D, 0xB3, 0xB6, 0x33, 0xF0, 0xAA, 0x44, 0xE7, 0x6B }, (uint)(item0Pos - metadataStart), 8, false, false);

        long item1Pos = writer.BaseStream.Position;
        writer.Write((ulong)diskSizeRounded);
        WriteMetadataEntry(entries, 1, new byte[] { 0x24, 0x42, 0xA5, 0x2F, 0x1B, 0xCD, 0x76, 0x48, 0xB2, 0x11, 0x5D, 0xBE, 0xD8, 0x3B, 0xF4, 0xB8 }, (uint)(item1Pos - metadataStart), 8, false, true);

        long item2Pos = writer.BaseStream.Position;
        writer.Write((uint)512);
        WriteMetadataEntry(entries, 2, new byte[] { 0x1D, 0xBF, 0x41, 0x81, 0x6F, 0xA9, 0x09, 0x47, 0xBA, 0x47, 0xF2, 0x33, 0xA8, 0xFA, 0xAB, 0x5F }, (uint)(item2Pos - metadataStart), 4, false, true);

        long item3Pos = writer.BaseStream.Position;
        writer.Write((uint)PhysicalSectorSize);
        WriteMetadataEntry(entries, 3, new byte[] { 0xC7, 0x48, 0xA3, 0xCD, 0x5D, 0x44, 0x71, 0x44, 0x9C, 0xC9, 0xE9, 0x88, 0x52, 0x51, 0xC5, 0x56 }, (uint)(item3Pos - metadataStart), 4, false, true);

        long item4Pos = writer.BaseStream.Position;
        writer.Write(Guid.NewGuid().ToByteArray());
        WriteMetadataEntry(entries, 4, new byte[] { 0xAB, 0x12, 0xCA, 0xBE, 0xE6, 0xB2, 0x23, 0x45, 0x93, 0xEF, 0xC3, 0x09, 0xE0, 0x00, 0xC7, 0x46 }, (uint)(item4Pos - metadataStart), 16, false, true);

        long currentPos = writer.BaseStream.Position;
        writer.BaseStream.Position = entriesStart;
        writer.Write(entries);
        writer.BaseStream.Position = currentPos;

        while (writer.BaseStream.Position < metadataStart + 1024L * 1024)
            writer.Write((byte)0);
    }

    private static void WriteMetadataEntry(byte[] buffer, int index, byte[] guid, uint offset, uint length, bool isUser, bool isVirtualDisk)
    {
        int baseOffset = index * 32;
        Array.Copy(guid, 0, buffer, baseOffset, 16);
        BitConverter.GetBytes(offset).CopyTo(buffer, baseOffset + 16);
        BitConverter.GetBytes(length).CopyTo(buffer, baseOffset + 20);
        // Metadata entry flags (one little-endian 32-bit field): User=1, VirtualDisk=2, Required=4.
        buffer[baseOffset + 24] = (byte)((isUser ? 1 : 0) | (isVirtualDisk ? 2 : 0) | 4);
    }
}

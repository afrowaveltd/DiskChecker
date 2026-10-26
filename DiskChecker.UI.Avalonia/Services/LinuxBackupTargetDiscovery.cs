using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>Finds writable, disk-backed filesystems, excluding the source disk and virtual mounts.</summary>
public static class LinuxBackupTargetDiscovery
{
    public static IReadOnlyList<string> GetMountPoints(string lsblkJson, string? sourcePath)
    {
        using var document = JsonDocument.Parse(lsblkJson);
        var roots = document.RootElement.GetProperty("blockdevices").EnumerateArray().ToArray();
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots.Where(r => ContainsSource(r, sourcePath)))
            CollectDeviceIds(root, excluded);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var mounts = new List<string>();
        foreach (var root in roots)
        {
            // Loop/Snap, optical and RAM devices are not destination disks. LVM,
            // encryption and RAID are traversed only when backed by a real disk.
            if (Text(root, "type") == "disk") Visit(root);
        }
        return mounts;

        void Visit(JsonElement device)
        {
            var type = Text(device, "type");
            var kernelPath = Text(device, "kname");
            if (type is "loop" or "rom" or "ram" ||
                kernelPath.StartsWith("/dev/zram", StringComparison.Ordinal) ||
                kernelPath.StartsWith("/dev/ram", StringComparison.Ordinal))
                return;

            var id = DeviceId(device);
            var fs = Text(device, "fstype");
            if (!excluded.Contains(id) && !ReadOnly(device) &&
                !string.IsNullOrEmpty(fs) && fs is not ("squashfs" or "erofs" or "iso9660" or "udf" or "swap") &&
                device.TryGetProperty("mountpoints", out var points) && points.ValueKind == JsonValueKind.Array)
            {
                // Bind mounts and btrfs subvolumes share free space. Count a
                // filesystem once, preferring its shortest mount point (e.g. /).
                var mount = points.EnumerateArray()
                    .Where(p => p.ValueKind == JsonValueKind.String)
                    .Select(p => p.GetString()!)
                    .Where(p => p.StartsWith('/') && !p.StartsWith("/snap/", StringComparison.Ordinal) &&
                        !p.StartsWith("/var/lib/snapd/", StringComparison.Ordinal))
                    .OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal).FirstOrDefault();
                if (mount != null && seen.Add(id)) mounts.Add(mount);
            }
            foreach (var child in Children(device)) Visit(child);
        }
    }

    private static string Text(JsonElement device, string name)
        => device.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static string DeviceId(JsonElement device)
        => Text(device, "maj:min") is { Length: > 0 } id ? id : Text(device, "path");

    private static bool ReadOnly(JsonElement device)
        => device.TryGetProperty("ro", out var value) &&
            (value.ValueKind == JsonValueKind.True || value.ToString() == "1");

    private static IEnumerable<JsonElement> Children(JsonElement device)
        => device.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array
            ? children.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static bool ContainsSource(JsonElement device, string? sourcePath)
        => !string.IsNullOrEmpty(sourcePath) &&
            (Text(device, "path") == sourcePath || Text(device, "name") == sourcePath ||
             Text(device, "kname") == sourcePath || Children(device).Any(c => ContainsSource(c, sourcePath)));

    private static void CollectDeviceIds(JsonElement device, HashSet<string> ids)
    {
        ids.Add(DeviceId(device));
        foreach (var child in Children(device)) CollectDeviceIds(child, ids);
    }
}

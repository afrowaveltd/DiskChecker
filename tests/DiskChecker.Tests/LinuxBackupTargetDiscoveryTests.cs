using DiskChecker.UI.Avalonia.Services;
using Xunit;

namespace DiskChecker.Tests;

public class LinuxBackupTargetDiscoveryTests
{
    private const string Topology = """
        {"blockdevices":[
          {"path":"/dev/loop0","type":"loop","fstype":"squashfs","mountpoints":["/snap/core/1"]},
          {"path":"/dev/sr0","type":"rom","fstype":"udf","mountpoints":["/media/cd"]},
          {"path":"/dev/zram0","type":"disk","kname":"/dev/zram0","fstype":"ext4","mountpoints":["/ram"]},
          {"path":"/dev/ram0","type":"disk","kname":"/dev/ram0","fstype":"ext4","mountpoints":["/ramdisk"]},
          {"path":"/dev/sda","type":"disk","children":[
            {"path":"/dev/sda1","type":"part","fstype":"ext4","ro":false,"maj:min":"8:1","mountpoints":["/"]},
            {"path":"/dev/sda2","type":"part","children":[
              {"path":"/dev/mapper/crypt","type":"crypt","children":[
                {"path":"/dev/mapper/home","kname":"/dev/dm-1","type":"lvm","fstype":"ext4","ro":false,"maj:min":"253:1","mountpoints":["/home"]}
              ]}
            ]}
          ]},
          {"path":"/dev/nvme0n1","type":"disk","children":[
            {"path":"/dev/nvme0n1p1","type":"part","fstype":"btrfs","ro":false,"maj:min":"259:1","mountpoints":["/backup/snapshot","/backup",null]},
            {"path":"/dev/nvme0n1p1","type":"part","fstype":"btrfs","ro":false,"maj:min":"259:1","mountpoints":["/bind-backup"]},
            {"path":"/dev/nvme0n1p2","type":"part","fstype":"ext4","ro":true,"mountpoints":["/readonly"]},
            {"path":"/dev/nvme0n1p3","type":"part","fstype":"swap","mountpoints":["[SWAP]"]},
            {"path":"/dev/nvme0n1p4","type":"part","fstype":"squashfs","mountpoints":["/image"]}
          ]},
          {"path":"/dev/sdb","type":"disk","fstype":"xfs","ro":false,"maj:min":"8:16","mountpoints":["/media/USB backup"]}
        ]}
        """;

    [Fact]
    public void OnlyWritableDiskBackedFilesystemsAreReturnedOnce()
    {
        Assert.Equal(new[] { "/", "/home", "/backup", "/media/USB backup" },
            LinuxBackupTargetDiscovery.GetMountPoints(Topology, null));
    }

    [Theory]
    [InlineData("/dev/sda")]
    [InlineData("/dev/sda1")]
    [InlineData("/dev/mapper/home")]
    [InlineData("/dev/dm-1")]
    public void SourceDiskAndNestedVolumesCannotBeDestinations(string source)
    {
        Assert.Equal(new[] { "/backup", "/media/USB backup" },
            LinuxBackupTargetDiscovery.GetMountPoints(Topology, source));
    }

    [Fact]
    public void UnpartitionedSourceDiskIsExcluded()
    {
        Assert.DoesNotContain("/media/USB backup", LinuxBackupTargetDiscovery.GetMountPoints(Topology, "/dev/sdb"));
    }

    [Fact]
    public void RaidMountedUnderMultiplePhysicalDisksIsExcludedIfAnyMemberIsSource()
    {
        const string json = """
            {"blockdevices":[
              {"path":"/dev/sda","type":"disk","children":[
                {"path":"/dev/md0","type":"raid1","fstype":"ext4","maj:min":"9:0","mountpoints":["/raid"]}]},
              {"path":"/dev/sdb","type":"disk","children":[
                {"path":"/dev/md0","type":"raid1","fstype":"ext4","maj:min":"9:0","mountpoints":["/raid"]}]}
            ]}
            """;
        Assert.Equal(new[] { "/raid" }, LinuxBackupTargetDiscovery.GetMountPoints(json, null));
        Assert.Empty(LinuxBackupTargetDiscovery.GetMountPoints(json, "/dev/sda"));
    }
}

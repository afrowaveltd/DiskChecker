using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// OS-level validation of disk images. Attaches the image READ ONLY and optionally
/// exposes known Linux filesystem partitions under a separate mount directory.
/// Never writes to the image, source device, or partition table.
/// </summary>
public static class ReadOnlyImageMountService
{
    public sealed record MountedImage(
        string ImagePath, string AccessPath, string? LinuxDevice,
        IReadOnlyList<string> LinuxMountDirectories, bool OwnedByDiskChecker);

    public sealed record MountAttempt(bool Verified, string Message, MountedImage? Mount);

    public static async Task<MountAttempt> TryAttachAsync(string imagePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        if (!File.Exists(imagePath))
            return new MountAttempt(false, "Soubor obrazu neexistuje.", null);

        try
        {
            if (OperatingSystem.IsWindows())
                return await AttachWindowsAsync(imagePath, ct);
            if (OperatingSystem.IsLinux())
                return await AttachLinuxAsync(imagePath, ct);
            return new MountAttempt(false, "Ověření systémovým připojením není na této platformě implementováno.", null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new MountAttempt(false,
                $"Systém odmítl obraz připojit jen pro čtení: {ex.Message}", null);
        }
    }

    public static async Task DetachAsync(MountedImage mount, CancellationToken ct = default)
    {
        if (!mount.OwnedByDiskChecker)
            return; // Never detach an image that was already mounted by another application.

        if (OperatingSystem.IsWindows())
        {
            var script = "$ErrorActionPreference='Stop'; Dismount-DiskImage -ImagePath " +
                PsString(mount.ImagePath) + " -ErrorAction Stop | Out-Null";
            await RunPowerShellAsync(script, ct);
            return;
        }

        if (OperatingSystem.IsLinux() && mount.LinuxDevice != null)
        {
            // Do not detach an NBD device while any of its partitions remain
            // mounted. That would turn a live read-only copy into a broken path.
            foreach (var directory in mount.LinuxMountDirectories.Reverse())
            {
                await RunAsync("umount", new[] { directory }, ct);
                try { Directory.Delete(directory); } catch (IOException) { }
            }
            await RunAsync("qemu-nbd", new[] { "--disconnect", mount.LinuxDevice }, ct);
        }
    }

    private static async Task<MountAttempt> AttachWindowsAsync(string imagePath, CancellationToken ct)
    {
        if (!string.Equals(Path.GetExtension(imagePath), ".vhdx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetExtension(imagePath), ".vhd", StringComparison.OrdinalIgnoreCase))
            return new MountAttempt(false,
                "Windows neumí tento RAW .img obraz přímo připojit jako virtuální disk. Pro ověření použij VHDX nebo Debian/qemu-nbd.",
                null);

        var quoted = PsString(Path.GetFullPath(imagePath));
        // Mount-DiskImage invokes the Windows virtual-disk parser, independently
        // of our own VhdxImageUtility header/BAT validator.
        var script = "$ErrorActionPreference='Stop'; $img=" + quoted + "; " +
            "$existing=Get-DiskImage -ImagePath $img -ErrorAction SilentlyContinue; " +
            "$wasAttached=($null -ne $existing -and $existing.Attached); " +
            "if($wasAttached -and [string]$existing.Access -ne 'ReadOnly') " +
                "{throw 'Obraz byl již připojen pro zápis; kontrola vyžaduje read-only režim.'}; " +
            "try { " +
            "if(-not $wasAttached){Mount-DiskImage -ImagePath $img -Access ReadOnly -ErrorAction Stop | Out-Null}; " +
            "$state=Get-DiskImage -ImagePath $img -ErrorAction Stop; " +
            "if(-not $state.Attached -or [string]$state.Access -ne 'ReadOnly') " +
                "{throw 'Obraz nelze potvrdit jako read-only.'}; " +
            "$letters=@(); try { $letters=@($state | Get-Disk -ErrorAction Stop | " +
            "Get-Partition -ErrorAction Stop | Get-Volume -ErrorAction SilentlyContinue | " +
            "Where-Object { $_.DriveLetter } | ForEach-Object { [string]$_.DriveLetter + ':\\' }) } catch {}; " +
            "if($wasAttached){Write-Output ('EXISTING|' + ($letters -join ','))} " +
            "else{Write-Output ('ATTACHED|' + ($letters -join ','))} " +
            "} catch { if(-not $wasAttached){Dismount-DiskImage -ImagePath $img -ErrorAction SilentlyContinue | Out-Null}; throw }";
        var response = await RunPowerShellAsync(script, ct);
        var existing = response.StartsWith("EXISTING|", StringComparison.Ordinal);
        var volumes = response.Contains('|') ? response[(response.IndexOf('|') + 1)..].Trim() : "";
        var access = string.IsNullOrWhiteSpace(volumes)
            ? "Správa disků (obraz je připojen, ale oddíl nemá písmeno)"
            : volumes;

        return new MountAttempt(true,
            $"Windows obraz připojil pouze pro čtení. Přístup: {access}. " +
            "Tím je ověřeno připojení obrazu, nikoli správnost všech zálohovaných souborů.",
            new MountedImage(imagePath, access, null, Array.Empty<string>(), !existing));
    }

    private static async Task<MountAttempt> AttachLinuxAsync(string imagePath, CancellationToken ct)
    {
        if (!File.Exists("/dev/nbd0"))
        {
            try { await RunAsync("modprobe", new[] { "nbd", "max_part=16" }, ct); }
            catch (Exception) { /* The useful error is reported below if no device exists. */ }
        }

        var device = Enumerable.Range(0, 16).Select(i => $"/dev/nbd{i}")
            .FirstOrDefault(IsUnusedNbd);
        if (device == null)
            return new MountAttempt(false, "Není dostupné volné zařízení /dev/nbdX. Ověř instalaci qemu-utils a modul nbd (sudo modprobe nbd max_part=16).", null);

        var format = string.Equals(Path.GetExtension(imagePath), ".vhdx", StringComparison.OrdinalIgnoreCase)
            ? "vhdx" : "raw";
        await RunAsync("qemu-nbd", new[] {
            "--read-only", "--format=" + format, "--connect=" + device, Path.GetFullPath(imagePath) }, ct);

        var directories = new List<string>();
        try
        {
            // Give the kernel an opportunity to discover the new partition table.
            try { await RunAsync("partprobe", new[] { device }, ct); }
            catch (Exception) { /* A valid partitionless image need not have partprobe. */ }

            var sizeText = await RunAsync("blockdev", new[] { "--getsize64", device }, ct);
            if (!long.TryParse(sizeText.Trim(), out var size) || size <= 0)
                throw new InvalidDataException("NBD zařízení nehlásí platnou virtuální kapacitu.");

            // Mount supported filesystems read-only where possible. If they
            // are encrypted or unknown, expose the verified /dev/nbdX instead.
            var access = device;
            try
            {
                var devices = await RunAsync("lsblk", new[] { "-nr", "-o", "PATH,TYPE,FSTYPE", device }, ct);
                var rows = devices.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    .Where(cols => cols.Length >= 3 && cols[2] != "" &&
                                   cols[2] != "swap" && cols[2] != "crypto_LUKS" &&
                                   cols[2] != "LVM2_member").ToArray();
                var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(imagePath))!,
                    Path.GetFileNameWithoutExtension(imagePath) + "_readonly");
                foreach (var row in rows)
                {
                    var directory = Path.Combine(root, "volume" + (directories.Count + 1));
                    Directory.CreateDirectory(directory);
                    var mountOptions = row[2] switch
                    {
                        "ext2" or "ext3" or "ext4" => "ro,noload",
                        "xfs" => "ro,norecovery",
                        "btrfs" => "ro,nologreplay",
                        _ => "ro"
                    };
                    try
                    {
                        await RunAsync("mount", new[] { "-o", mountOptions, row[0], directory }, ct);
                        directories.Add(directory);
                        access = string.Join(", ", directories);
                    }
                    catch (Exception) { try { Directory.Delete(directory); } catch (Exception) { } }
                }
            }
            catch (Exception) { /* A block-device attach is still a valid format check. */ }

            return new MountAttempt(true,
                $"Linux připojil obraz jen pro čtení jako {device} ({size} B). " +
                $"Přístup: {access}. Připojení samo o sobě neověřuje obsah každého souboru.",
                new MountedImage(imagePath, access, device, directories, true));
        }
        catch
        {
            foreach (var directory in directories.AsEnumerable().Reverse())
            {
                try { await RunAsync("umount", new[] { directory }, CancellationToken.None); }
                catch (Exception) { }
            }
            try { await RunAsync("qemu-nbd", new[] { "--disconnect", device }, CancellationToken.None); }
            catch (Exception) { }
            throw;
        }
    }

    private static bool IsUnusedNbd(string device)
    {
        if (!File.Exists(device))
            return false;
        var name = Path.GetFileName(device);
        if (File.Exists("/sys/block/" + name + "/pid"))
            return false;
        try
        {
            var size = File.ReadAllText("/sys/block/" + name + "/size").Trim();
            return size == "0";
        }
        catch (IOException) { return false; }
    }

    private static string PsString(string text) => "'" + text.Replace("'", "''") + "'";

    private static Task<string> RunPowerShellAsync(string script, CancellationToken ct) =>
        RunAsync("powershell.exe", new[] {
            "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }, ct);

    private static async Task<string> RunAsync(string fileName, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ??
            throw new IOException($"Nástroj {fileName} se nepodařilo spustit.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new IOException($"{fileName}: {stderr.Trim()} {stdout.Trim()}".Trim());
            return stdout.Trim();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }
}

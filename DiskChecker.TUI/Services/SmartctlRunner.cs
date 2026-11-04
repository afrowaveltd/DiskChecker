using System.Diagnostics;

namespace DiskChecker.TUI.Services;

/// <summary>Reads the raw <c>smartctl -a</c> dump for a device (Linux/Windows).</summary>
public static class SmartctlRunner
{
    private static readonly string[] LinuxSearchPaths =
    {
        "/usr/sbin/smartctl", "/usr/bin/smartctl", "/usr/local/sbin/smartctl",
        "/usr/local/bin/smartctl", "/sbin/smartctl", "/bin/smartctl", "/snap/bin/smartctl",
    };

    /// <summary>Returns the smartctl output or a message explaining why it is unavailable.</summary>
    public static async Task<string> RunAsync(string? devicePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return "Není vybrán žádný disk.";
        }

        string? smartctl = FindSmartctl();
        if (smartctl is null)
        {
            return "smartctl nebyl nalezen. Nainstalujte smartmontools (balíček smartmontools).";
        }

        string arguments = devicePath.Contains("nvme", StringComparison.OrdinalIgnoreCase)
            ? $"-d nvme -a \"{devicePath}\""
            : $"-a \"{devicePath}\"";

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = smartctl,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(info);
            if (process is null)
            {
                return "smartctl se nepodařilo spustit.";
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            string output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            string error = await process.StandardError.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(error))
            {
                return string.IsNullOrWhiteSpace(output) ? "(bez výstupu)" : output;
            }

            return output + Environment.NewLine + "--- Errors ---" + Environment.NewLine + error;
        }
        catch (OperationCanceledException)
        {
            return "Timeout: smartctl neodpověděl do 20 sekund.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "smartctl selhal: " + ex.Message;
        }
    }

    /// <summary>Locates the smartctl binary; returns null when it is not installed.</summary>
    public static string? FindSmartctl()
    {
        if (OperatingSystem.IsWindows())
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "smartmontools", "bin", "smartctl.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "smartmontools", "bin", "smartctl.exe"),
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        foreach (string candidate in LinuxSearchPaths)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory, "smartctl");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

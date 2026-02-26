using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DiskChecker.Infrastructure.Helpers;

/// <summary>
/// Helper for running specific operations with elevated privileges without running the entire app as admin.
/// </summary>
public static class PrivilegeHelper
{
    /// <summary>
    /// Checks if the current process is running with administrator privileges.
    /// </summary>
    public static bool IsRunningAsAdministrator()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true; // On Linux, assume sufficient privileges or handled via sudo

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Executes a command with elevated privileges (Windows UAC prompt).
    /// </summary>
    /// <param name="fileName">Executable to run (e.g., "diskpart", "cmd")</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="waitForExit">Whether to wait for the process to complete</param>
    /// <returns>Exit code or -1 if failed</returns>
    public static async Task<int> RunElevatedAsync(string fileName, string arguments, bool waitForExit = true)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Linux, use sudo
            fileName = "sudo";
            arguments = $"{fileName} {arguments}";
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas", // Request elevation
                CreateNoWindow = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            using var process = Process.Start(psi);
            if (process == null)
                return -1;

            if (waitForExit)
            {
                await process.WaitForExitAsync();
                return process.ExitCode;
            }

            return 0;
        }
        catch (Exception)
        {
            // User cancelled UAC prompt or elevation failed
            return -1;
        }
    }

    /// <summary>
    /// Prompts user to restart application with administrator privileges.
    /// </summary>
    /// <param name="currentExecutablePath">Path to current executable</param>
    /// <param name="args">Original command line arguments</param>
    /// <returns>True if restart initiated, false if user cancelled</returns>
    public static bool RestartAsAdministrator(string currentExecutablePath, string[] args)
    {
        if (IsRunningAsAdministrator())
            return true; // Already running as admin

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = currentExecutablePath,
                Arguments = string.Join(" ", args),
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a specific disk operation requires elevation.
    /// </summary>
    /// <param name="drivePath">Drive path to check (e.g., "D:\" or "/dev/sdb1")</param>
    /// <returns>True if elevation is likely needed</returns>
    public static bool RequiresElevationForDiskAccess(string drivePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Raw disk access (\\.\PhysicalDriveX) always requires elevation
            if (drivePath.StartsWith(@"\\.\PhysicalDrive", StringComparison.OrdinalIgnoreCase))
                return true;

            // Regular drive letters usually don't require elevation for read
            // But sanitization/formatting does
            return false;
        }
        else
        {
            // On Linux, /dev/ access usually requires sudo
            return drivePath.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase);
        }
    }
}

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Provides robust launching of files in the default OS application.
/// </summary>
internal static class DocumentLauncher
{
    /// <summary>
    /// Opens a file with the system default application and falls back to Windows shell start when needed.
    /// </summary>
    /// <param name="filePath">Full path to the file.</param>
    /// <exception cref="ArgumentException">Thrown when path is empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when file does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when all launch strategies fail.</exception>
    public static void OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Cesta k souboru nesmí být prázdná.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Soubor nebyl nalezen.", filePath);
        }

        var errors = new StringBuilder();

        // When the app runs under sudo on Linux, xdg-open would run as root and
        // cannot reach the desktop user's session. Open the file under the
        // original (logged-in) user instead.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && TryOpenAsSudoUser(filePath, errors))
        {
            return;
        }

        if (TryOpenWithShell(filePath, errors))
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && TryOpenWithWindowsStart(filePath, errors))
        {
            return;
        }

        throw new InvalidOperationException($"Soubor se nepodařilo otevřít výchozí aplikací. {errors}");
    }

    /// <summary>
    /// Opens a file under the original sudo user's desktop session (Linux only).
    /// </summary>
    private static bool TryOpenAsSudoUser(string filePath, StringBuilder errors)
    {
        var sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
        if (string.IsNullOrWhiteSpace(sudoUser) || string.Equals(sudoUser, "root", System.StringComparison.OrdinalIgnoreCase))
            return false;

        var sudoUid = Environment.GetEnvironmentVariable("SUDO_UID");
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var xauthority = ResolveXAuthority(sudoUid, sudoUser);

        // Build the environment prefix for the child process. We only forward the
        // variables that are actually available so we don't pass empty values.
        var env = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(display))
            env.Append("DISPLAY=").Append(display).Append(' ');
        if (!string.IsNullOrWhiteSpace(xauthority))
            env.Append("XAUTHORITY=").Append(xauthority).Append(' ');

        var escapedPath = filePath.Replace("'", "'\\''");

        // Prefer runuser (no password prompt, available on most systemd distros).
        if (TryRun("runuser", BuildRunuserArgs(sudoUser, env, escapedPath), errors))
            return true;

        // Fallback to su.
        if (TryRun("su", BuildSuArgs(sudoUser, env, escapedPath), errors))
            return true;

        return false;
    }

    private static string BuildRunuserArgs(string sudoUser, StringBuilder env, string escapedPath)
    {
        // runuser -u <user> -- env <ENV...> xdg-open '<path>'
        var args = new StringBuilder();
        args.Append("-u ").Append(sudoUser).Append(" -- env ");
        args.Append(env);
        args.Append("xdg-open '").Append(escapedPath).Append('\'');
        return args.ToString();
    }

    private static string BuildSuArgs(string sudoUser, StringBuilder env, string escapedPath)
    {
        // su <user> -c "env <ENV...> xdg-open '<path>'"
        var command = new StringBuilder();
        command.Append("env ").Append(env).Append("xdg-open '").Append(escapedPath).Append('\'');
        return $"{sudoUser} -c \"{command.ToString().Replace("\"", "\\\"")}\"";
    }

    private static bool TryRun(string fileName, string arguments, StringBuilder errors)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null)
                return false;

            // xdg-open returns quickly; give it a moment to hand off to the viewer.
            process.WaitForExit(5000);
            return true;
        }
        catch (Win32Exception ex)
        {
            errors.Append($"{fileName} selhal: {ex.Message}. ");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            errors.Append($"{fileName} selhal: {ex.Message}. ");
            return false;
        }
    }

    /// <summary>
    /// Resolves the X authority file for the original user's desktop session.
    /// </summary>
    private static string? ResolveXAuthority(string? sudoUid, string sudoUser)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(sudoUid))
        {
            var runUser = Path.Combine("/run/user", sudoUid);
            candidates.Add(Path.Combine(runUser, "gdm", "Xauthority"));
            candidates.Add(Path.Combine(runUser, "xauth_0"));
            candidates.Add(Path.Combine(runUser, "xauth_1"));
        }

        var home = GetOriginalUserHome(sudoUser);
        if (!string.IsNullOrWhiteSpace(home))
        {
            candidates.Add(Path.Combine(home, ".Xauthority"));
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? GetOriginalUserHome(string sudoUser)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "getent",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("passwd");
            startInfo.ArgumentList.Add(sudoUser);
            using var process = Process.Start(startInfo);

            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(1000);
                var parts = output.Split(':');
                if (parts.Length >= 6 && Directory.Exists(parts[5]))
                    return parts[5];
            }
        }
        catch
        {
            // Fall back below.
        }

        var fallback = Path.Combine("/home", sudoUser);
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static bool TryOpenWithShell(string filePath, StringBuilder errors)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
                Verb = "open"
            });
            return true;
        }
        catch (Win32Exception ex)
        {
            errors.Append($"Shell open selhal: {ex.Message}. ");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            errors.Append($"Shell open selhal: {ex.Message}. ");
            return false;
        }
    }

    private static bool TryOpenWithWindowsStart(string filePath, StringBuilder errors)
    {
        try
        {
            var escapedPath = filePath.Replace("\"", "\"\"");
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" \"{escapedPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        catch (Win32Exception ex)
        {
            errors.Append($"Windows start selhal: {ex.Message}. ");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            errors.Append($"Windows start selhal: {ex.Message}. ");
            return false;
        }
    }
}

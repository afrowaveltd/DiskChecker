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

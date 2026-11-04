using System.Globalization;
using System.Runtime.InteropServices;

namespace DiskChecker.TUI.Services;

/// <summary>Static host information shown in the terminal header.</summary>
public sealed record TuiSystemInfo(
    string OsName,
    string KernelVersion,
    string CpuInfo,
    string RamInfo,
    string RuntimeInfo,
    string AppVersion)
{
    /// <summary>Collects the same values the desktop status bar shows.</summary>
    public static TuiSystemInfo Collect()
    {
        string os = RuntimeInformation.OSDescription;
        string kernel = OperatingSystem.IsLinux()
            ? ReadFirstLine("/proc/sys/kernel/osrelease") ?? Environment.OSVersion.Version.ToString()
            : Environment.OSVersion.Version.ToString();

        if (OperatingSystem.IsLinux())
        {
            string? pretty = ReadOsPrettyName();
            if (!string.IsNullOrWhiteSpace(pretty))
            {
                os = pretty;
            }
        }

        string cpu = $"{Environment.ProcessorCount} jader";
        if (OperatingSystem.IsLinux())
        {
            string? modelName = ReadCpuModelName();
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                cpu = $"{modelName} ({cpu})";
            }
        }

        string ram = "N/A";
        if (OperatingSystem.IsLinux())
        {
            long? totalKb = ReadMemTotalKb();
            if (totalKb is > 0)
            {
                ram = (totalKb.Value / 1024d / 1024d).ToString("F1", CultureInfo.InvariantCulture) + " GB RAM";
            }
        }
        else
        {
            double gb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024d * 1024d * 1024d);
            if (gb > 0)
            {
                ram = gb.ToString("F1", CultureInfo.InvariantCulture) + " GB RAM";
            }
        }

        string version = typeof(TuiSystemInfo).Assembly.GetName().Version is { } v
            ? $"v{v.Major}.{v.Minor}.{v.Build}"
            : "v1.0.0";

        return new TuiSystemInfo(os, kernel, cpu, ram, $".NET {Environment.Version.Major}", version);
    }

    private static string? ReadFirstLine(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadLines(path).FirstOrDefault()?.Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadOsPrettyName()
    {
        try
        {
            if (!File.Exists("/etc/os-release"))
            {
                return null;
            }

            foreach (string line in File.ReadLines("/etc/os-release"))
            {
                if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                {
                    return line["PRETTY_NAME=".Length..].Trim('"');
                }
            }
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }

    private static string? ReadCpuModelName()
    {
        try
        {
            if (!File.Exists("/proc/cpuinfo"))
            {
                return null;
            }

            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.Ordinal))
                {
                    int separator = line.IndexOf(':');
                    if (separator > 0)
                    {
                        string name = line[(separator + 1)..].Trim();
                        return name.Length > 32 ? name[..30] + "…" : name;
                    }
                }
            }
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }

    private static long? ReadMemTotalKb()
    {
        try
        {
            if (!File.Exists("/proc/meminfo"))
            {
                return null;
            }

            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], CultureInfo.InvariantCulture, out long kb))
                    {
                        return kb;
                    }
                }
            }
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }
}

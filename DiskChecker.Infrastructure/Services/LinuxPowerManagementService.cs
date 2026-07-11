using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// Linux implementation of power management service.
/// Uses systemd-inhibit, cpupower/governor, and process nice.
/// </summary>
public class LinuxPowerManagementService : IPowerManagementService
{
    private readonly ILogger<LinuxPowerManagementService>? _logger;

    public LinuxPowerManagementService(ILogger<LinuxPowerManagementService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsAvailable => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public Task<IPowerManagementSession> BeginTestSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("Linux power management is only available on Linux.");
        }

        var session = new LinuxPowerSession(_logger, cancellationToken);
        return Task.FromResult<IPowerManagementSession>(session);
    }

    private sealed class LinuxPowerSession : IPowerManagementSession
    {
        private readonly ILogger? _logger;
        private bool _disposed;
        private Process? _inhibitProcess;
        private int? _previousNice;
        private string? _previousGovernor;
        private readonly int _currentPid;

        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public bool IsActive { get; private set; }

        public LinuxPowerSession(ILogger? logger, CancellationToken cancellationToken)
        {
            _logger = logger;
            _currentPid = Environment.ProcessId;

            try
            {
                // 1. Start systemd-inhibit to prevent sleep, idle, shutdown
                StartSystemdInhibit(cancellationToken);

                // 2. Save and set CPU governor to performance
                SetPerformanceGovernor();

                // 3. Save and set process priority (nice)
                SetProcessPriority();

                // 4. Disable disk power management
                DisableDiskPowerManagement();

                // 5. Prevent screen blanking
                PreventScreenBlanking();

                IsActive = true;
                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("[PowerMgmt] Linux power management session started (session {SessionId})", SessionId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[PowerMgmt] Failed to initialize Linux power management session");
                // Clean up partial initialization
                RestoreAsync().GetAwaiter().GetResult();
                throw;
            }
        }

        private void StartSystemdInhibit(CancellationToken cancellationToken)
        {
            // A test must not claim that sleep is inhibited unless systemd actually
            // accepted and keeps the inhibitor lock. Failing closed is safer than a
            // multi-hour destructive operation being interrupted by suspend.
            if (!IsCommandAvailable("systemd-inhibit"))
            {
                throw new InvalidOperationException(
                    "systemd-inhibit is not available; system sleep cannot be safely suppressed.");
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "systemd-inhibit",
                    Arguments = $"--what=idle:sleep:shutdown:handle-lid-switch " +
                                $"--who=\"DiskChecker\" " +
                                $"--why=\"Disk testing in progress\" " +
                                $"--mode=block " +
                                $"sleep infinity",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _inhibitProcess = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start systemd-inhibit.");

                // Detect command-line/DBus failures before reporting an active session.
                if (_inhibitProcess.WaitForExit(250))
                {
                    var error = _inhibitProcess.StandardError.ReadToEnd().Trim();
                    var exitCode = _inhibitProcess.ExitCode;
                    _inhibitProcess.Dispose();
                    _inhibitProcess = null;
                    throw new InvalidOperationException(
                        $"systemd-inhibit exited with code {exitCode}: {error}");
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("[PowerMgmt] systemd-inhibit started (PID: {Pid})", _inhibitProcess?.Id);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[PowerMgmt] Failed to start mandatory systemd inhibitor");
                throw;
            }
        }

        private void SetPerformanceGovernor()
        {
            try
            {
                // Save current governor
                _previousGovernor = ExecuteCommand("cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_governor");

                if (string.IsNullOrWhiteSpace(_previousGovernor))
                {
                    _logger?.LogWarning("[PowerMgmt] Could not read current CPU governor");
                    return;
                }

                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("[PowerMgmt] Current CPU governor: {Governor}", _previousGovernor.Trim());
                }

                // Try to set performance governor
                // This requires root, so it may fail
                var cpuCount = Environment.ProcessorCount;
                for (int i = 0; i < cpuCount; i++)
                {
                    try
                    {
                        ExecuteCommand($"echo performance | tee /sys/devices/system/cpu/cpu{i}/cpufreq/scaling_governor");
                    }
                    catch
                    {
                        // Ignore errors for individual CPUs
                    }
                }

                _logger?.LogInformation("[PowerMgmt] Set CPU governor to performance (may require root)");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[PowerMgmt] Failed to set performance governor (may require root)");
            }
        }

        private void SetProcessPriority()
        {
            try
            {
                // Get current nice value
                var niceOutput = ExecuteCommand($"ps -o nice= -p {_currentPid}");
                if (int.TryParse(niceOutput?.Trim(), out var nice))
                {
                    _previousNice = nice;
                    if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("[PowerMgmt] Current process nice value: {Nice}", nice);
                    }
                }

                // Set high priority (nice -10, requires root for negative values)
                ExecuteCommand($"renice -n -10 -p {_currentPid}");
                _logger?.LogInformation("[PowerMgmt] Set process nice to -10 (high priority)");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[PowerMgmt] Failed to set process priority (may require root)");
            }
        }

        private void DisableDiskPowerManagement()
        {
            try
            {
                // Find all /dev/sd* and /dev/nvme* devices and disable APM
                var devices = Directory.GetFiles("/dev", "sd?")
                    .Concat(Directory.GetFiles("/dev", "nvme?n?"))
                    .ToArray();

                foreach (var device in devices)
                {
                    try
                    {
                        // Disable APM (Advanced Power Management) for SATA/IDE drives
                        ExecuteCommand($"hdparm -B 255 {device}");

                        // Disable spindown timeout
                        ExecuteCommand($"hdparm -S 0 {device}");
                    }
                    catch
                    {
                        // Ignore errors for individual devices
                    }
                }

                _logger?.LogInformation("[PowerMgmt] Disabled disk power management");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[PowerMgmt] Failed to disable disk power management");
            }
        }

        private void PreventScreenBlanking()
        {
            try
            {
                // Try xset if X11 is available
                if (IsCommandAvailable("xset") && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                {
                    ExecuteCommand("xset s off");
                    ExecuteCommand("xset -dpms");
                    _logger?.LogInformation("[PowerMgmt] Disabled screen blanking via xset");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[PowerMgmt] Failed to disable screen blanking");
            }
        }

        public async Task RestoreAsync()
        {
            if (!IsActive)
            {
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    // Stop systemd-inhibit
                    if (_inhibitProcess != null && !_inhibitProcess.HasExited)
                    {
                        _inhibitProcess.Kill(true);
                        _inhibitProcess.WaitForExit(5000);
                        _inhibitProcess.Dispose();
                        _logger?.LogInformation("[PowerMgmt] Stopped systemd-inhibit");
                    }

                    // Restore CPU governor
                    if (!string.IsNullOrWhiteSpace(_previousGovernor))
                    {
                        var cpuCount = Environment.ProcessorCount;
                        for (int i = 0; i < cpuCount; i++)
                        {
                            try
                            {
                                ExecuteCommand($"echo {_previousGovernor.Trim()} | tee /sys/devices/system/cpu/cpu{i}/cpufreq/scaling_governor");
                            }
                            catch
                            {
                                // Ignore errors
                            }
                        }
                        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation("[PowerMgmt] Restored CPU governor to {Governor}", _previousGovernor.Trim());
                        }
                    }

                    // Restore process priority
                    if (_previousNice.HasValue)
                    {
                        try
                        {
                            ExecuteCommand($"renice -n {_previousNice.Value} -p {_currentPid}");
                            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                            {
                                _logger.LogInformation("[PowerMgmt] Restored process nice to {Nice}", _previousNice.Value);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "[PowerMgmt] Failed to restore process priority");
                        }
                    }

                    // Re-enable screen blanking
                    try
                    {
                        if (IsCommandAvailable("xset") && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                        {
                            ExecuteCommand("xset s on");
                            ExecuteCommand("xset +dpms");
                            _logger?.LogInformation("[PowerMgmt] Re-enabled screen blanking");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[PowerMgmt] Failed to re-enable screen blanking");
                    }

                    IsActive = false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[PowerMgmt] Error restoring power settings");
                }
            });
        }

        private static bool IsCommandAvailable(string command)
        {
            try
            {
                var result = ExecuteCommand($"which {command}");
                return !string.IsNullOrWhiteSpace(result);
            }
            catch
            {
                return false;
            }
        }

        private static string? ExecuteCommand(string command)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            RestoreAsync().GetAwaiter().GetResult();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        ~LinuxPowerSession()
        {
            Dispose();
        }
    }
}

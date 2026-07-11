using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// Windows implementation of power management service.
/// Uses SetThreadExecutionState and Power Management APIs.
/// </summary>
public class WindowsPowerManagementService : IPowerManagementService
{
    private readonly ILogger<WindowsPowerManagementService>? _logger;

    public WindowsPowerManagementService(ILogger<WindowsPowerManagementService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsAvailable => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public Task<IPowerManagementSession> BeginTestSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("Windows power management is only available on Windows.");
        }

        var session = new WindowsPowerSession(_logger);
        return Task.FromResult<IPowerManagementSession>(session);
    }

    #region Windows API

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    [Flags]
    private enum EXECUTION_STATE : uint
    {
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_USER_PRESENT = 0x00000004
    }

    [DllImport("powrprof.dll", CharSet = CharSet.Auto)]
    private static extern bool PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid ActivePolicyGuid);

    [DllImport("powrprof.dll", CharSet = CharSet.Auto)]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    private static readonly Guid GUID_HIGH_PERFORMANCE = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    #endregion

    private sealed class WindowsPowerSession : IPowerManagementSession
    {
        private readonly ILogger? _logger;
        private bool _disposed;
        private EXECUTION_STATE _previousState;
        private Guid? _previousPowerScheme;
        private ProcessPriorityClass _previousPriority;
        private readonly Process _currentProcess;

        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public bool IsActive { get; private set; }

        public WindowsPowerSession(ILogger? logger)
        {
            _logger = logger;
            _currentProcess = Process.GetCurrentProcess();

            try
            {
                // Save original state
                _previousPriority = _currentProcess.PriorityClass;

                // Get current power scheme
                if (PowerGetActiveScheme(IntPtr.Zero, out var schemePtr) == 0)
                {
                    _previousPowerScheme = Marshal.PtrToStructure<Guid>(schemePtr);
                    Marshal.FreeHGlobal(schemePtr);
                }

                // Prevent system sleep, display off, and idle-to-sleep
                _previousState = SetThreadExecutionState(
                    EXECUTION_STATE.ES_CONTINUOUS |
                    EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                    EXECUTION_STATE.ES_DISPLAY_REQUIRED |
                    EXECUTION_STATE.ES_AWAYMODE_REQUIRED);

                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("[PowerMgmt] Prevented sleep and display off (session {SessionId})", SessionId);
                }

                // Set high performance power scheme
                var highPerfGuid = GUID_HIGH_PERFORMANCE;
                if (PowerSetActiveScheme(IntPtr.Zero, ref highPerfGuid))
                {
                    _logger?.LogInformation("[PowerMgmt] Switched to High Performance power scheme");
                }
                else
                {
                    _logger?.LogWarning("[PowerMgmt] Failed to set High Performance power scheme");
                }

                // Increase process priority
                try
                {
                    _currentProcess.PriorityClass = ProcessPriorityClass.High;
                    _logger?.LogInformation("[PowerMgmt] Set process priority to High");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[PowerMgmt] Failed to set process priority");
                }

                // Disable disk timeout via registry (requires admin)
                try
                {
                    DisableDiskTimeout();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[PowerMgmt] Failed to disable disk timeout (may require admin)");
                }

                IsActive = true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[PowerMgmt] Failed to initialize power management session");
                throw;
            }
        }

        private void DisableDiskTimeout()
        {
            // Set disk timeout to 0 (never) via powercfg
            var startInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/change disk-timeout-ac 0",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
        }

        public async Task RestoreAsync()
        {
            if (_disposed || !IsActive)
            {
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    // Restore execution state (allow sleep/display off again)
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                    if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("[PowerMgmt] Restored execution state (session {SessionId})", SessionId);
                    }

                    // Restore previous power scheme
                    if (_previousPowerScheme.HasValue)
                    {
                        var guid = _previousPowerScheme.Value;
                        PowerSetActiveScheme(IntPtr.Zero, ref guid);
                        _logger?.LogInformation("[PowerMgmt] Restored previous power scheme");
                    }

                    // Restore process priority
                    try
                    {
                        _currentProcess.PriorityClass = _previousPriority;
                        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation("[PowerMgmt] Restored process priority to {Priority}", _previousPriority);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[PowerMgmt] Failed to restore process priority");
                    }

                    // Restore disk timeout (re-enable power management)
                    try
                    {
                        RestoreDiskTimeout();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[PowerMgmt] Failed to restore disk timeout");
                    }

                    IsActive = false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[PowerMgmt] Error restoring power settings");
                }
            });
        }

        private void RestoreDiskTimeout()
        {
            // Restore disk timeout to default (20 minutes for AC)
            var startInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/change disk-timeout-ac 20",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            RestoreAsync().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        ~WindowsPowerSession()
        {
            Dispose();
        }
    }
}

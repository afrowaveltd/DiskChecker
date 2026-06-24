using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Service for managing system power and performance during disk tests.
/// Prevents sleep, display off, disk power down, and ensures high performance.
/// </summary>
public interface IPowerManagementService
{
    /// <summary>
    /// Begin power management session for disk testing.
    /// Prevents sleep, locks high performance mode, keeps display active.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Session handle that must be disposed to restore original settings</returns>
    Task<IPowerManagementSession> BeginTestSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the service is available on current platform.
    /// </summary>
    bool IsAvailable { get; }
}

/// <summary>
/// Represents an active power management session.
/// Must be disposed to restore original power settings.
/// </summary>
public interface IPowerManagementSession : IDisposable
{
    /// <summary>
    /// Session ID for tracking
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// Whether the session is currently active
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Restore original power settings before test
    /// </summary>
    Task RestoreAsync();
}

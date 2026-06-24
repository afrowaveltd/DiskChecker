using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// Cross-platform power management service that delegates to platform-specific implementations.
/// </summary>
public class PowerManagementService : IPowerManagementService
{
    private readonly IPowerManagementService _implementation;
    private readonly ILogger<PowerManagementService>? _logger;

    public PowerManagementService(ILogger<PowerManagementService>? logger = null)
    {
        _logger = logger;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _implementation = new WindowsPowerManagementService(
                logger != null ? (ILogger<WindowsPowerManagementService>)LoggerFactory.Create(builder => builder.AddProvider(new DelegatingLoggerProvider(logger))).CreateLogger<WindowsPowerManagementService>() : null);
            _logger?.LogInformation("[PowerMgmt] Using Windows power management implementation");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _implementation = new LinuxPowerManagementService(
                logger != null ? (ILogger<LinuxPowerManagementService>)LoggerFactory.Create(builder => builder.AddProvider(new DelegatingLoggerProvider(logger))).CreateLogger<LinuxPowerManagementService>() : null);
            _logger?.LogInformation("[PowerMgmt] Using Linux power management implementation");
        }
        else
        {
            _implementation = new NullPowerManagementService();
            _logger?.LogWarning("[PowerMgmt] Platform not supported, using null implementation");
        }
    }

    public bool IsAvailable => _implementation.IsAvailable;

    public Task<IPowerManagementSession> BeginTestSessionAsync(CancellationToken cancellationToken = default)
    {
        return _implementation.BeginTestSessionAsync(cancellationToken);
    }

    /// <summary>
    /// Null implementation for unsupported platforms
    /// </summary>
    private class NullPowerManagementService : IPowerManagementService
    {
        public bool IsAvailable => false;

        public Task<IPowerManagementSession> BeginTestSessionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IPowerManagementSession>(new NullSession());
        }

        private class NullSession : IPowerManagementSession
        {
            public string SessionId { get; } = "null";
            public bool IsActive => false;
            public Task RestoreAsync() => Task.CompletedTask;
            public void Dispose() { }
        }
    }

    private class DelegatingLoggerProvider : ILoggerProvider
    {
        private readonly ILogger _logger;

        public DelegatingLoggerProvider(ILogger logger)
        {
            _logger = logger;
        }

        public ILogger CreateLogger(string categoryName) => _logger;
        public void Dispose() { }
    }
}

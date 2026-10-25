using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces
{
    /// <summary>
    /// Background sampler that polls disk temperature (via SMART) on a timer while a
    /// destructive test is running, without interrupting the test's I/O stream.
    /// </summary>
    public interface IDiskTemperatureMonitor : IDisposable
    {
        /// <summary>Most recent temperature in °C, or null when no reading is available yet.</summary>
        int? CurrentTemperature { get; }

        /// <summary>Lowest temperature observed since the monitor started.</summary>
        int? MinTemperature { get; }

        /// <summary>Highest temperature observed since the monitor started.</summary>
        int? MaxTemperature { get; }

        /// <summary>
        /// Safe-temperature threshold in °C. Derived from SMART when available,
        /// otherwise a device-type default. Null when unknown.
        /// </summary>
        int? SafeTemperatureThreshold { get; }

        /// <summary>True when the latest reading is at or above the safe threshold.</summary>
        bool IsOverThreshold { get; }

        /// <summary>Raised (on a background thread) when the temperature crosses the safe threshold.</summary>
        event EventHandler<int>? ThresholdExceeded;

        /// <summary>Raised (on a background thread) whenever a new temperature reading is obtained.</summary>
        event EventHandler<int>? TemperatureChanged;

        /// <summary>
        /// Starts the background sampling loop for the given device.
        /// Safe to call when SMART is unsupported — the loop simply produces no readings.
        /// </summary>
        void Start(string devicePath, bool supportsSmart, string? deviceType = null, int? smartThreshold = null);

        /// <summary>Stops the background sampling loop. Idempotent.</summary>
        void StopMonitoring();

        /// <summary>
        /// Performs a single synchronous temperature read (used to seed the initial value).
        /// Returns null when the read fails or SMART is unsupported.
        /// </summary>
        Task<int?> ReadOnceAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a snapshot of the temperature samples collected so far, in chronological order.
        /// </summary>
        IReadOnlyList<TemperatureSample> GetSamples();
    }
}

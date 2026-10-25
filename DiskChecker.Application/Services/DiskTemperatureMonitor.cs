#pragma warning disable CA1848 // Use LoggerMessage delegates for better performance
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Application.Services
{
    /// <summary>
    /// Background sampler that polls disk temperature via <see cref="ISmartaProvider.GetTemperatureOnlyAsync"/>
    /// on a timer while a destructive test is running. Reading the SMART temperature attribute is a
    /// low-priority firmware query serviced between I/O operations, so it does not block or distort the
    /// sanitization. Any read failure is swallowed and the last known value is kept.
    /// </summary>
    public sealed class DiskTemperatureMonitor : IDiskTemperatureMonitor
    {
        private readonly ISmartaProvider _smartaProvider;
        private readonly ILogger<DiskTemperatureMonitor>? _logger;
        private readonly TimeSpan _interval;

        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private readonly object _lock = new();

        private string _devicePath = string.Empty;
        private bool _supportsSmart;
        private string? _deviceType;
        private int? _smartThreshold;

        private int? _current;
        private int? _min;
        private int? _max;
        private int? _threshold;
        private bool _thresholdExceededRaised;
        private readonly List<TemperatureSample> _samples = new();

        public DiskTemperatureMonitor(
            ISmartaProvider smartaProvider,
            ILogger<DiskTemperatureMonitor>? logger = null,
            TimeSpan? interval = null)
        {
            _smartaProvider = smartaProvider ?? throw new ArgumentNullException(nameof(smartaProvider));
            _logger = logger;
            _interval = interval ?? TimeSpan.FromSeconds(5);
        }

        public int? CurrentTemperature
        {
            get { lock (_lock) return _current; }
        }

        public int? MinTemperature
        {
            get { lock (_lock) return _min; }
        }

        public int? MaxTemperature
        {
            get { lock (_lock) return _max; }
        }

        public int? SafeTemperatureThreshold
        {
            get { lock (_lock) return _threshold; }
        }

        public bool IsOverThreshold
        {
            get
            {
                lock (_lock)
                {
                    return _current.HasValue && _threshold.HasValue && _current.Value >= _threshold.Value;
                }
            }
        }

        public event EventHandler<int>? ThresholdExceeded;
        public event EventHandler<int>? TemperatureChanged;

        public void Start(string devicePath, bool supportsSmart, string? deviceType = null, int? smartThreshold = null)
        {
            lock (_lock)
            {
                StopCore();

                _devicePath = devicePath ?? string.Empty;
                _supportsSmart = supportsSmart;
                _deviceType = deviceType;
                _smartThreshold = smartThreshold;
                _threshold = ResolveThreshold(smartThreshold, deviceType);
                _thresholdExceededRaised = false;

                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _loopTask = Task.Run(() => RunLoopAsync(token), token);
            }
        }

        public void StopMonitoring()
        {
            lock (_lock)
            {
                StopCore();
            }
        }

        public async Task<int?> ReadOnceAsync(CancellationToken cancellationToken = default)
        {
            if (!_supportsSmart || string.IsNullOrEmpty(_devicePath))
                return null;

            try
            {
                var temp = await _smartaProvider.GetTemperatureOnlyAsync(_devicePath, cancellationToken);
                if (temp.HasValue)
                {
                    Record(temp.Value);
                }
                return temp;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Temperature read failed for {DevicePath}: {Error}", _devicePath, ex.Message);
                return null;
            }
        }

        public IReadOnlyList<TemperatureSample> GetSamples()
        {
            lock (_lock)
            {
                return _samples.ToArray();
            }
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(_interval, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) break;

                    await ReadOnceAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Temperature monitor loop terminated unexpectedly: {Error}", ex.Message);
            }
        }

        private void Record(int temperature)
        {
            bool thresholdCrossed = false;
            int? thresholdValue = null;

            lock (_lock)
            {
                _current = temperature;
                if (!_min.HasValue || temperature < _min.Value) _min = temperature;
                if (!_max.HasValue || temperature > _max.Value) _max = temperature;

                _samples.Add(new TemperatureSample
                {
                    Timestamp = DateTime.UtcNow,
                    TemperatureCelsius = temperature,
                    Phase = string.Empty,
                    ProgressPercent = 0
                });

                if (_threshold.HasValue && temperature >= _threshold.Value && !_thresholdExceededRaised)
                {
                    _thresholdExceededRaised = true;
                    thresholdCrossed = true;
                    thresholdValue = _threshold.Value;
                }
            }

            TemperatureChanged?.Invoke(this, temperature);

            if (thresholdCrossed && thresholdValue.HasValue)
            {
                ThresholdExceeded?.Invoke(this, thresholdValue.Value);
            }
        }

        private static int? ResolveThreshold(int? smartThreshold, string? deviceType)
        {
            if (smartThreshold.HasValue && smartThreshold.Value > 0)
                return smartThreshold.Value;

            // Device-type fallback when SMART does not report a threshold.
            var type = deviceType ?? string.Empty;
            if (type.Contains("nvme", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("ssd", StringComparison.OrdinalIgnoreCase))
            {
                return 70;
            }

            // HDD / SATA / SCSI / unknown default.
            return 60;
        }

        private void StopCore()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
        }

        public void Dispose()
        {
            StopMonitoring();
            GC.SuppressFinalize(this);
        }
    }
}

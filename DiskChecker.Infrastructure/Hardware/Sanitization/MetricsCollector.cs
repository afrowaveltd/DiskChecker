using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware.Sanitization;

/// <summary>
/// Service for collecting metrics during disk tests.
/// </summary>
public class MetricsCollector : IMetricsCollector
{
    private TestSession? _currentSession;
    private readonly Stopwatch _stopwatch;
    private readonly object _lock = new();
    private string _currentPhase = "";
    private long _bytesProcessed;
    private long _totalBytes;
    private double _lastWriteSpeed;
    private double _lastReadSpeed;
    private readonly List<TemperatureSample> _temperatureSamples = new();
    private readonly List<SpeedSample> _writeSamples = new();
    private readonly List<SpeedSample> _readSamples = new();
    private readonly List<TestError> _errors = new();

    public MetricsCollector()
    {
        _stopwatch = new Stopwatch();
    }

    public void StartSession(TestSession session)
    {
        lock (_lock)
        {
            _currentSession = session;
            _stopwatch.Restart();
            _currentPhase = "";
            _bytesProcessed = 0;
            _totalBytes = 0;
            _lastWriteSpeed = 0;
            _lastReadSpeed = 0;
            _temperatureSamples.Clear();
            _writeSamples.Clear();
            _readSamples.Clear();
            _errors.Clear();
        }
    }

    public void RecordWriteSpeed(double speedMBps, double progressPercent, long bytesProcessed)
    {
        lock (_lock)
        {
            _lastWriteSpeed = speedMBps;
            
            if (_currentSession != null)
            {
                _writeSamples.Add(new SpeedSample
                {
                    Timestamp = DateTime.UtcNow,
                    SpeedMBps = speedMBps,
                    ProgressPercent = progressPercent,
                    BytesProcessed = bytesProcessed
                });
            }
        }
    }

    public void RecordReadSpeed(double speedMBps, double progressPercent, long bytesProcessed)
    {
        lock (_lock)
        {
            _lastReadSpeed = speedMBps;
            
            if (_currentSession != null)
            {
                _readSamples.Add(new SpeedSample
                {
                    Timestamp = DateTime.UtcNow,
                    SpeedMBps = speedMBps,
                    ProgressPercent = progressPercent,
                    BytesProcessed = bytesProcessed
                });
            }
        }
    }

    public void RecordTemperature(int temperatureCelsius, string phase, double progressPercent)
    {
        lock (_lock)
        {
            if (_currentSession != null)
            {
                _temperatureSamples.Add(new TemperatureSample
                {
                    Timestamp = DateTime.UtcNow,
                    TemperatureCelsius = temperatureCelsius,
                    Phase = phase,
                    ProgressPercent = progressPercent
                });
            }
        }
    }

    public void RecordError(string errorCode, string message, string phase, bool isCritical, string? details = null)
    {
        lock (_lock)
        {
            if (_currentSession != null)
            {
                _errors.Add(new TestError
                {
                    Timestamp = DateTime.UtcNow,
                    ErrorCode = errorCode,
                    Message = message,
                    Phase = phase,
                    IsCritical = isCritical,
                    Details = details
                });
            }
        }
    }

    public async Task<TestSession> CompleteSessionAsync()
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                _stopwatch.Stop();

                if (_currentSession == null)
                {
                    throw new InvalidOperationException("No session started");
                }

                var session = _currentSession;
                session.CompletedAt = DateTime.UtcNow;
                session.Duration = _stopwatch.Elapsed;

                // Calculate write metrics
                if (_writeSamples.Count > 0)
                {
                    session.WriteSamples = new List<SpeedSample>(_writeSamples);
                    session.AverageWriteSpeedMBps = CalculateAverage(_writeSamples);
                    session.MaxWriteSpeedMBps = CalculateMax(_writeSamples);
                    session.MinWriteSpeedMBps = CalculateMin(_writeSamples);
                    session.WriteSpeedStdDev = CalculateStdDev(_writeSamples, session.AverageWriteSpeedMBps);
                    session.WriteErrors = _errors.Count(e => e.Phase.Equals("Write", StringComparison.OrdinalIgnoreCase));
                }

                // Calculate read metrics
                if (_readSamples.Count > 0)
                {
                    session.ReadSamples = new List<SpeedSample>(_readSamples);
                    session.AverageReadSpeedMBps = CalculateAverage(_readSamples);
                    session.MaxReadSpeedMBps = CalculateMax(_readSamples);
                    session.MinReadSpeedMBps = CalculateMin(_readSamples);
                    session.ReadSpeedStdDev = CalculateStdDev(_readSamples, session.AverageReadSpeedMBps);
                    session.ReadErrors = _errors.Count(e => e.Phase.Equals("Read", StringComparison.OrdinalIgnoreCase));
                }

                // Temperature metrics
                if (_temperatureSamples.Count > 0)
                {
                    session.TemperatureSamples = new List<TemperatureSample>(_temperatureSamples);
                    session.StartTemperature = _temperatureSamples[0].TemperatureCelsius;
                    session.MaxTemperature = _temperatureSamples.Max(t => t.TemperatureCelsius);
                    session.AverageTemperature = _temperatureSamples.Average(t => t.TemperatureCelsius);
                }

                // Copy errors
                session.Errors = new List<TestError>(_errors);

                // Calculate result
                var (grade, score) = CalculateGradeAndScore(session);
                session.Grade = grade;
                session.Score = score;
                session.Result = DetermineResult(session);
                session.HealthAssessment = DetermineHealthAssessment(session);

                return session;
            }
        });
    }

    public (double WriteProgress, double ReadProgress, double CurrentSpeed) GetProgress()
    {
        lock (_lock)
        {
            var writeProgress = _totalBytes > 0 ? (_bytesProcessed / (double)_totalBytes) * 100 : 0;
            return (writeProgress, 0, _lastWriteSpeed > 0 ? _lastWriteSpeed : _lastReadSpeed);
        }
    }

    /// <summary>
    /// Set total bytes for progress calculation.
    /// </summary>
    public void SetTotalBytes(long totalBytes)
    {
        _totalBytes = totalBytes;
    }

    /// <summary>
    /// Update bytes processed.
    /// </summary>
    public void UpdateBytesProcessed(long bytesProcessed)
    {
        _bytesProcessed = bytesProcessed;
    }

    /// <summary>
    /// Set current phase (Write, Read, Verify).
    /// </summary>
    public void SetPhase(string phase)
    {
        _currentPhase = phase;
    }

    private static double CalculateAverage(List<SpeedSample> samples)
    {
        if (samples.Count == 0) return 0;
        return samples.Average(s => s.SpeedMBps);
    }

    private static double CalculateMax(List<SpeedSample> samples)
    {
        if (samples.Count == 0) return 0;
        return samples.Max(s => s.SpeedMBps);
    }

    private static double CalculateMin(List<SpeedSample> samples)
    {
        if (samples.Count == 0) return 0;
        return samples.Min(s => s.SpeedMBps);
    }

    private static double CalculateStdDev(List<SpeedSample> samples, double average)
    {
        if (samples.Count < 2) return 0;
        var sumSquaredDiffs = samples.Sum(s => Math.Pow(s.SpeedMBps - average, 2));
        return Math.Sqrt(sumSquaredDiffs / samples.Count);
    }

    private static (string grade, double score) CalculateGradeAndScore(TestSession session)
    {
        double score = 100;
        
        // Deduct points for errors
        score -= session.WriteErrors * 5;
        score -= session.ReadErrors * 5;
        score -= session.VerificationErrors * 10;
        
        // Deduct points for high temperatures
        if (session.MaxTemperature.HasValue && session.MaxTemperature > 60)
        {
            score -= (session.MaxTemperature.Value - 60) * 2;
        }
        
        // Deduct points for slow speeds
        if (session.AverageWriteSpeedMBps < 50)
        {
            score -= (50 - session.AverageWriteSpeedMBps) * 0.5;
        }
        
        if (session.AverageReadSpeedMBps < 50)
        {
            score -= (50 - session.AverageReadSpeedMBps) * 0.5;
        }
        
        // Deduct for high speed variance (instability)
        if (session.WriteSpeedStdDev > 20)
        {
            score -= (session.WriteSpeedStdDev - 20) * 0.5;
        }
        
        if (session.ReadSpeedStdDev > 20)
        {
            score -= (session.ReadSpeedStdDev - 20) * 0.5;
        }
        
        // Ensure score is in range 0-100
        score = Math.Max(0, Math.Min(100, score));
        
        // Determine grade
        string grade = score switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 60 => "D",
            >= 50 => "E",
            _ => "F"
        };
        
        return (grade, score);
    }

    private static TestResult DetermineResult(TestSession session)
    {
        // Critical errors = fail
        if (session.Errors.Any(e => e.IsCritical))
        {
            return TestResult.Fail;
        }
        
        // ANY write/read errors = FAIL (disk failure)
        if (session.WriteErrors > 0 || session.ReadErrors > 0)
        {
            return TestResult.Fail;
        }
        
        // No data integrity errors and good speeds = pass
        if (session.VerificationErrors == 0)
        {
            if (session.AverageWriteSpeedMBps > 30 && session.AverageReadSpeedMBps > 30)
            {
                return TestResult.Pass;
            }
        }
        
        // Verification errors but not too many = warning
        if (session.VerificationErrors <= 5)
        {
            return TestResult.Warning;
        }
        
        return TestResult.Fail;
    }

    private static HealthAssessment DetermineHealthAssessment(TestSession session)
    {
        if (session.Result == TestResult.Fail)
        {
            return HealthAssessment.Critical;
        }
        
        // Write/Read errors = poor health (disk may be failing)
        if (session.WriteErrors > 0 || session.ReadErrors > 0)
        {
            return HealthAssessment.Poor;
        }
        
        if (session.Errors.Count > 0 || session.VerificationErrors > 0)
        {
            return HealthAssessment.Fair;
        }
        
        if (session.MaxTemperature > 60 || session.AverageWriteSpeedMBps < 30)
        {
            return HealthAssessment.Fair;
        }
        
        if (session.Score >= 85)
        {
            return HealthAssessment.Excellent;
        }
        
        if (session.Score >= 70)
        {
            return HealthAssessment.Good;
        }
        
        return HealthAssessment.Fair;
    }
}
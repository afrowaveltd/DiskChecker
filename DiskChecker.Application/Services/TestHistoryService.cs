using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiskChecker.Application.Services;

/// <summary>
/// Service for managing test history and reports across all disk types.
/// </summary>
public class TestHistoryService
{
    private readonly IDiskCardRepository _diskCardRepository;

    public TestHistoryService(IDiskCardRepository diskCardRepository)
    {
        _diskCardRepository = diskCardRepository;
    }

    /// <summary>
    /// Gets all test reports across all disks.
    /// </summary>
    public async Task<List<TestReport>> GetAllTestReportsAsync()
    {
        var reports = new List<TestReport>();
        var cards = await _diskCardRepository.GetAllAsync();

        foreach (var card in cards)
        {
            foreach (var session in card.TestSessions)
            {
                var report = CreateTestReportFromSession(session, card);
                reports.Add(report);
            }
        }

        // Sort by test date descending
        return reports.OrderByDescending(r => r.TestDate).ToList();
    }

    /// <summary>
    /// Gets test reports for a specific disk.
    /// </summary>
    public async Task<List<TestReport>> GetReportsForDiskAsync(string serialNumber)
    {
        var card = await _diskCardRepository.GetBySerialNumberAsync(serialNumber);
        if (card == null)
            return new List<TestReport>();

        return card.TestSessions
            .Select(session => CreateTestReportFromSession(session, card))
            .OrderByDescending(r => r.TestDate)
            .ToList();
    }

    /// <summary>
    /// Gets test reports for a specific time period.
    /// </summary>
    public async Task<List<TestReport>> GetReportsForPeriodAsync(DateTime startDate, DateTime endDate)
    {
        var reports = new List<TestReport>();
        var cards = await _diskCardRepository.GetAllAsync();

        foreach (var card in cards)
        {
            foreach (var session in card.TestSessions.Where(s => s.StartedAt >= startDate && s.StartedAt <= endDate))
            {
                var report = CreateTestReportFromSession(session, card);
                reports.Add(report);
            }
        }

        return reports.OrderByDescending(r => r.TestDate).ToList();
    }

    /// <summary>
    /// Gets test reports filtered by test type.
    /// </summary>
    public async Task<List<TestReport>> GetReportsByTestTypeAsync(TestType testType)
    {
        var reports = new List<TestReport>();
        var cards = await _diskCardRepository.GetAllAsync();

        foreach (var card in cards)
        {
            foreach (var session in card.TestSessions.Where(s => s.TestType == testType))
            {
                var report = CreateTestReportFromSession(session, card);
                reports.Add(report);
            }
        }

        return reports.OrderByDescending(r => r.TestDate).ToList();
    }

    /// <summary>
    /// Gets failed test reports.
    /// </summary>
    public async Task<List<TestReport>> GetFailedTestsAsync()
    {
        var reports = new List<TestReport>();
        var cards = await _diskCardRepository.GetAllAsync();

        foreach (var card in cards)
        {
            foreach (var session in card.TestSessions.Where(s => s.Result == TestResult.Fail))
            {
                var report = CreateTestReportFromSession(session, card);
                reports.Add(report);
            }
        }

        return reports.OrderByDescending(r => r.TestDate).ToList();
    }

    /// <summary>
    /// Deletes a specific test report.
    /// </summary>
    public async Task<bool> DeleteTestReportAsync(int testSessionId)
    {
        try
        {
            await _diskCardRepository.DeleteTestSessionAsync(testSessionId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a TestReport from a TestSession and DiskCard.
    /// </summary>
    private TestReport CreateTestReportFromSession(TestSession session, DiskCard card)
    {
        return new TestReport
        {
            ReportId = Guid.NewGuid(),
            TestDate = session.StartedAt,
            TestType = session.TestType.ToString(),
            Grade = session.Grade,
            Score = (int)session.Score,
            DriveModel = card.ModelName,
            SerialNumber = card.SerialNumber,
            AverageSpeed = session.AverageWriteSpeedMBps,
            PeakSpeed = session.MaxWriteSpeedMBps,
            Errors = session.Errors.Count + session.WriteErrors + session.ReadErrors + session.VerificationErrors,
            IsCompleted = session.Status == TestStatus.Completed
        };
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Xunit;

namespace DiskChecker.Tests;

public sealed class LongTestRecoveryJournalTests
{
    private static CoreDriveInfo FakeDrive() => new()
    {
        Path = OperatingSystem.IsWindows() ? @"\\.\PHYSICALDRIVE101" : "/dev/fictional101",
        Name = "Fictional Test Disk",
        SerialNumber = "RECOVERY-ONLY-101",
        TotalSize = 512L * 1024 * 1024 * 1024,
        BusType = CoreBusType.Usb,
        IsPhysical = true
    };

    private static string NewDirectory()
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "DiskChecker-Recovery-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void RunningCheckpointSurvivesInspectionWithoutRawDevice()
    {
        var folder = NewDirectory();
        try
        {
            var journal = new LongTestRecoveryJournal(FakeDrive(), directoryOverride: folder);
            journal.Checkpoint("Write", 50_000_000, 512L * 1024 * 1024 * 1024, 4.5, 0);

            var entry = Assert.Single(LongTestRecoveryJournal.Inspect(folder));
            Assert.Null(entry.ValidationError);
            Assert.NotNull(entry.Snapshot);
            Assert.Equal(journal.RunId, entry.Snapshot.RunId);
            Assert.Equal(LongTestRecoveryState.Running, entry.Snapshot.State);
            Assert.Equal("Write", entry.Snapshot.Phase);
            Assert.Equal(50_000_000, entry.Snapshot.BytesProcessed);
            Assert.Equal(FakeDrive().TotalSize, entry.Snapshot.DeviceCapacityBytes);
            Assert.True(File.Exists(journal.FilePath));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void ExplicitFinishedOutcomeAndBoundedSamplesSurviveRestart()
    {
        var folder = NewDirectory();
        try
        {
            var journal = new LongTestRecoveryJournal(FakeDrive(), directoryOverride: folder);
            var result = new SanitizationResult
            {
                Success = true,
                BytesWritten = 10_000,
                BytesRead = 10_000,
                WriteSpeedMBps = 109.5,
                ReadSpeedMBps = 105.5,
                Duration = TimeSpan.FromHours(2)
            };
            var write = new[]
            {
                new SpeedSample { Phase = "Write", ProgressPercent = 100, BytesProcessed = 10_000, SpeedMBps = 109.5 }
            };
            var read = new[]
            {
                new SpeedSample { Phase = "Read", ProgressPercent = 100, BytesProcessed = 10_000, SpeedMBps = 105.5 }
            };
            journal.FinishSanitization(result, write, read, null, null);
            var entry = Assert.Single(LongTestRecoveryJournal.Inspect(folder));
            Assert.Equal(LongTestRecoveryState.Completed, entry.Snapshot!.State);
            Assert.True(entry.Snapshot.FinalSanitizationResult!.Success);
            Assert.Single(entry.Snapshot.WriteSamples!);
            Assert.Single(entry.Snapshot.ReadSamples!);
            Assert.Equal(109.5, entry.Snapshot.WriteSamples![0].SpeedMBps);

            journal.MarkDatabaseSaved(journal.RunId);
            entry = Assert.Single(LongTestRecoveryJournal.Inspect(folder));
            Assert.Equal(LongTestRecoveryState.DatabaseSaved, entry.Snapshot!.State);
            Assert.False(journal.DeleteAfterVerification(_ => true));
            journal.MarkArtifactsSaved();
            Assert.False(journal.DeleteAfterVerification(_ => false));
            Assert.True(File.Exists(journal.FilePath));
            Assert.True(journal.DeleteAfterVerification(id => id == journal.RunId));
            Assert.Empty(LongTestRecoveryJournal.Inspect(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void FailedAndCancelledOutcomesAreNotMistakenForSuccess()
    {
        var folder = NewDirectory();
        try
        {
            var failed = new LongTestRecoveryJournal(FakeDrive(), directoryOverride: folder);
            failed.FinishSanitization(new SanitizationResult
            {
                Success = false,
                ErrorMessage = "Simulated read verification fault",
                BytesWritten = 1000,
                BytesRead = 700
            }, null, null, null, null);
            var cancelled = new LongTestRecoveryJournal(FakeDrive(), directoryOverride: folder);
            cancelled.RecordFailure("Operator stopped the test", cancelled: true);
            var found = LongTestRecoveryJournal.Inspect(folder);
            Assert.Equal(2, found.Count);
            Assert.Contains(found, x => x.Snapshot?.State == LongTestRecoveryState.Failed);
            Assert.Contains(found, x => x.Snapshot?.State == LongTestRecoveryState.Cancelled);
            Assert.DoesNotContain(found, x => x.Snapshot?.State == LongTestRecoveryState.Completed);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void DamagedAndUnrecognizedFilesAreReportedButNeverDeleted()
    {
        var folder = NewDirectory();
        try
        {
            var corrupted = Path.Combine(folder, "run_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(corrupted, "{\"FormatVersion\":1,\"RunId\":");
            var inventory = LongTestRecoveryJournal.Inspect(folder);
            var item = Assert.Single(inventory);
            Assert.Null(item.Snapshot);
            Assert.NotNull(item.ValidationError);
            Assert.True(File.Exists(corrupted));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void UnverifiedOrMismatchedDatabaseSessionCannotRemoveJournal()
    {
        var folder = NewDirectory();
        try
        {
            var journal = new LongTestRecoveryJournal(FakeDrive(), directoryOverride: folder);
            Assert.Throws<InvalidOperationException>(() => journal.MarkDatabaseSaved(journal.RunId));
            journal.RecordFailure("Raw device interrupted");
            Assert.Throws<ArgumentException>(() => journal.MarkDatabaseSaved(Guid.NewGuid()));
            Assert.True(File.Exists(journal.FilePath));
            Assert.False(journal.DeleteAfterVerification(_ => true));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void PendingRecoveryJournalHasNoImplicitDestructiveResume()
    {
        var folder = NewDirectory();
        try
        {
            var journal = new LongTestRecoveryJournal(FakeDrive(), directoryOverride: folder);
            journal.Checkpoint("Write", 480_000, 500_000, 96);
            var item = Assert.Single(LongTestRecoveryJournal.Inspect(folder));
            Assert.Equal(LongTestRecoveryState.Running, item.Snapshot!.State);
            Assert.Null(item.Snapshot.FinalSanitizationResult);
            Assert.Null(item.Snapshot.EndedAtUtc);
            Assert.Equal(96, item.Snapshot.PhasePercent);
            // The recovered progress cannot prove that the write/read/verify
            // phase completed; it is diagnostic metadata only.
            Assert.True(File.Exists(journal.FilePath));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
    [Fact]
    public void ReopenedCompletedSnapshotKeepsStableRunId()
    {
        var folder = NewDirectory();
        try
        {
            var journal = new LongTestRecoveryJournal(FakeDrive(), directoryOverride: folder);
            journal.FinishSanitization(new SanitizationResult
            {
                Success = true,
                BytesWritten = 100,
                BytesRead = 100
            }, null, null, null, null);
            var entry = Assert.Single(LongTestRecoveryJournal.Inspect(folder));
            var reopened = LongTestRecoveryJournal.ReopenCompleted(entry);
            Assert.Equal(journal.RunId, reopened.RunId);
            reopened.MarkDatabaseSaved(reopened.RunId);
            Assert.Equal(LongTestRecoveryState.DatabaseSaved,
                Assert.Single(LongTestRecoveryJournal.Inspect(folder)).Snapshot!.State);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void AutomaticImportRequiresTerminalResultAndReliableIdentity()
    {
        var record = new LongTestRecoverySnapshot
        {
            RunId = Guid.NewGuid(),
            TestKind = "Sanitization",
            DevicePath = "/dev/fictional",
            SerialNumber = "RECOVERY-ID-1",
            DeviceCapacityBytes = 1000,
            State = LongTestRecoveryState.Completed,
            EndedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            FinalSanitizationResult = new SanitizationResult
            {
                Success = true,
                BytesWritten = 1000,
                BytesRead = 1000
            }
        };
        Assert.True(DiskChecker.Application.Services.LongTestRecoveryReconciler
            .IsAutomaticImportCandidate(record));
        record.State = LongTestRecoveryState.Running;
        Assert.False(DiskChecker.Application.Services.LongTestRecoveryReconciler
            .IsAutomaticImportCandidate(record));
        record.State = LongTestRecoveryState.Completed;
        record.SerialNumber = "unknown";
        Assert.False(DiskChecker.Application.Services.LongTestRecoveryReconciler
            .IsAutomaticImportCandidate(record));
        record.SerialNumber = "RECOVERY-ID-1";
        record.FinalSanitizationResult = null;
        Assert.False(DiskChecker.Application.Services.LongTestRecoveryReconciler
            .IsAutomaticImportCandidate(record));
    }

}

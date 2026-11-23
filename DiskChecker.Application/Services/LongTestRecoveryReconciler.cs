using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Application.Services;

/// <summary>
/// One bounded, safe startup reconciliation pass. Only explicitly finalized
/// sanitization result snapshots can be imported into the *existing* database.
/// In-progress/checkpoint-only runs are NEVER restarted or treated as passed.
/// </summary>
public sealed class LongTestRecoveryReconciler
{
    private readonly DiskCheckerDbContext _database;
    private readonly DiskCardTestService _cards;
    private readonly ICertificateGenerator _certificateGenerator;
    private readonly IDiskCardRepository _repository;

    /// <summary>Create an application recovery reconciler for one DI scope.</summary>
    public LongTestRecoveryReconciler(DiskCheckerDbContext database,
        DiskCardTestService cards, ICertificateGenerator certificateGenerator,
        IDiskCardRepository repository)
    {
        _database = database;
        _cards = cards;
        _certificateGenerator = certificateGenerator;
        _repository = repository;
    }

    /// <summary>
    /// Reconcile terminal checkpoint files (at most 100 per pass). A valid
    /// captured success result is necessary, but never sufficient, to infer
    /// a completed test from a progress percent. No physical device is opened.
    /// </summary>
    public async Task<LongTestRecoverySummary> ReconcileAsync(
        CancellationToken cancellationToken = default)
    {
        var summary = new LongTestRecoverySummary();
        foreach(var entry in LongTestRecoveryJournal.Inspect(maxFiles: 100))
        {
            cancellationToken.ThrowIfCancellationRequested();
            summary.Found++;

            if(entry.Snapshot == null || entry.ValidationError != null)
            {
                summary.Unresolved++;
                continue; // Malformed evidence is never deleted.
            }

            var snapshot = entry.Snapshot;
            if(!IsAutomaticImportCandidate(snapshot))
            {
                summary.Unresolved++;
                continue;
            }

            try
            {
                var journal = LongTestRecoveryJournal.ReopenCompleted(entry);
                var session = await _database.TestSessions.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.SessionId == snapshot.RunId,
                        cancellationToken);

                if(session != null && session.TestType != TestType.Sanitization)
                {
                    summary.Unresolved++;
                    continue; // ID collision/inconsistent data, never silently merge.
                }

                if(session == null)
                {
                    // Reconstruct the historical disk identity from the exact
                    // checkpoint, not by looking up an arbitrary currently
                    // connected PhysicalDriveN or /dev/sdX.
                    var drive = new CoreDriveInfo
                    {
                        Path = snapshot.DevicePath,
                        Name = snapshot.ModelName,
                        Model = snapshot.ModelName,
                        SerialNumber = snapshot.SerialNumber,
                        FirmwareVersion = snapshot.FirmwareVersion,
                        TotalSize = snapshot.DeviceCapacityBytes,
                        BusType = snapshot.BusType,
                        IsRemovable = snapshot.IsRemovable,
                        IsSolidState = snapshot.IsSolidState,
                        IsRotational = snapshot.IsRotational,
                        IsPhysical = true
                    };
                    var smart = string.IsNullOrWhiteSpace(snapshot.BorrowedSmartProvenance)
                        ? snapshot.SmartBefore : null;
                    var card = await _cards.GetOrCreateCardAsync(
                        drive, smart, cancellationToken);
                    session = await _cards.SaveSanitizationAsync(
                        card, snapshot.FinalSanitizationResult!,
                        snapshot.WriteSamples, snapshot.ReadSamples,
                        snapshot.SmartBefore, snapshot.SmartAfter,
                        snapshot.BorrowedSmartProvenance, snapshot.RunId,
                        cancellationToken);
                    summary.Imported++;
                }
                else
                {
                    summary.AlreadyInDatabase++;
                }

                // This explicit state is written only after a DB commit (or an
                // independent exact-GUID lookup above); it does not modify
                // the original physical operation's result.
                journal.MarkDatabaseSaved(snapshot.RunId);

                // Preserve detailed zoom samples. Move only the exact pending
                // telemetry referenced by this journal, never an arbitrary
                // pending_*.dat from a different test or an active writer.
                if(journal.RequiresTelemetryConfirmation &&
                   !DiskTelemetryArchive.Exists(snapshot.RunId))
                {
                    if(!DiskTelemetryArchive.TryRecoverPending(
                        journal.PendingTelemetryPath!, snapshot.RunId))
                    {
                        summary.Unresolved++;
                        continue;
                    }
                }

                int? certId = null;
                string? pdfPath = null;

                if(snapshot.FinalSanitizationResult!.Success)
                {
                    var existingCert = await _database.DiskCertificates.AsNoTracking()
                        .FirstOrDefaultAsync(c => c.TestSessionId == session.Id,
                            cancellationToken);
                    if(existingCert != null)
                    {
                        // The old PDF path is not always stored in SQLite.
                        // Do not generate duplicate certificate records/PDFs
                        // or erase evidence without a verified output path.
                        if(snapshot.CertificateId != existingCert.Id ||
                           string.IsNullOrWhiteSpace(snapshot.PdfPath) ||
                           !File.Exists(snapshot.PdfPath))
                        {
                            summary.Unresolved++;
                            continue;
                        }
                        certId = existingCert.Id;
                        pdfPath = snapshot.PdfPath;
                    }
                    else
                    {
                        var savedSession = await _database.TestSessions
                            .AsNoTracking()
                            .Include(s => s.WriteSamples)
                            .Include(s => s.ReadSamples)
                            .FirstAsync(s => s.SessionId == snapshot.RunId,
                                cancellationToken);
                        var savedCard = await _database.DiskCards.AsNoTracking()
                            .FirstAsync(c => c.Id == savedSession.DiskCardId,
                                cancellationToken);
                        var certificate = await _certificateGenerator
                            .GenerateCertificateAsync(savedSession, savedCard);
                        certificate.TestSessionId = savedSession.Id;
                        certificate = await _repository.CreateCertificateAsync(certificate);
                        certId = certificate.Id;
                        pdfPath = await _certificateGenerator.GeneratePdfAsync(certificate);
                        if(!File.Exists(pdfPath))
                            throw new IOException("Certificate PDF was not found after generation.");
                    }
                }

                if(!await _cards.IsRecoveryRunDurableAsync(
                     snapshot.RunId, certId, cancellationToken))
                {
                    summary.Unresolved++;
                    continue;
                }

                journal.MarkArtifactsSaved(certId, pdfPath);
                if(journal.DeleteAfterVerification(id => id == snapshot.RunId))
                    summary.Cleaned++;
                else
                    summary.Unresolved++;
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException or DbUpdateException or
                InvalidOperationException or ArgumentException or NotSupportedException)
            {
                // A full DB, inaccessible PDF output or an uncertain old
                // checkpoint must NEVER prevent the app from starting, nor
                // cause evidence deletion or an implicit destructive retry.
                summary.Unresolved++;
                summary.LastIssue = ex.Message;
            }
        }
        return summary;
    }

    /// <summary>
    /// A new-format terminal result and a stable serial are required.
    /// The record must have aged at least two minutes to avoid racing
    /// another live DiskChecker instance finishing its own DB writes.
    /// </summary>
    public static bool IsAutomaticImportCandidate(LongTestRecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if(snapshot.FormatVersion != 1 ||
           snapshot.RunId == Guid.Empty ||
           !string.Equals(snapshot.TestKind, "Sanitization", StringComparison.Ordinal) ||
           snapshot.State == LongTestRecoveryState.Running ||
           snapshot.FinalSanitizationResult == null ||
           snapshot.EndedAtUtc == null ||
           snapshot.UpdatedAtUtc > DateTime.UtcNow.AddMinutes(-2))
            return false;
        var serial = snapshot.SerialNumber?.Trim();
        return !string.IsNullOrWhiteSpace(serial) &&
               serial.Length >= 4 &&
               !new[] { "unknown", "n/a", "none", "null", "nosn", "00000000" }
                  .Contains(serial.ToLowerInvariant(), StringComparer.Ordinal);
    }
}

/// <summary>Outcome of one read-safe startup reconciliation scan.</summary>
public sealed class LongTestRecoverySummary
{
    public int Found { get; set; }
    public int Imported { get; set; }
    public int AlreadyInDatabase { get; set; }
    public int Cleaned { get; set; }
    public int Unresolved { get; set; }
    public string? LastIssue { get; set; }
}

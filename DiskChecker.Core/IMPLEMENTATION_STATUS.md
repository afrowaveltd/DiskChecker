# Core implementation status

## 2026-11-23 — long-test recovery checkpoint foundation

Added `LongTestRecoveryJournal` to Core as a
versioned, atomically replaced and flushed JSON
checkpoint. It is intentionally independent of
the high-resolution binary telemetry archive.
The snapshot records a stable per-test GUID,
device identity/capacity, phase/progress/errors,
and (when available) the actual terminal
`SanitizationResult`, SMART and bounded
write/read chart samples. Persistence is
opt-in from long-test orchestrators.

The checkpoint distinguishes `Running`,
`Completed`, `Failed`, `Cancelled`,
`DatabaseSaved` and `ArtifactsSaved`.
It can be inventoried **read-only**, including
invalid files, and refuses cleanup unless the
caller independently confirms the durable
session/certificate. It never accesses raw
devices, creates a partition, resumes test
writes, migrates a DB or deletes old pending
binary archives.

The service alone does not yet implement
automatic database import or complete
cross-test orchestration; do not present it
as full crash recovery until UI integration,
regression tests, and CI are green.

## 2026-11-23 — crash-resilient sanitization checkpoint, first slice

The GUI now creates a stable, versioned
`LongTestRecoveryJournal` **before raw I/O**
and periodically updates phase/progress/errors.
An explicit terminal `SanitizationResult`,
SMART and bounded chart samples are
flushed to JSON before SQLite writes.
`SaveSanitizationAsync` accepts a new
opt-in stable GUID for idempotent
`TestSession.SessionId` preservation.
The pending binary telemetry path is
recorded so the original detailed data
can be reattached after a crash.

An Avalonia startup reconciler imports
eligible completed/failed sanitization
records with a stable serial and valid
terminal outcome (age >=2min) into the
same DB. It never opens a raw device or
silently retries a physical disk operation.
Existing DB rows are not duplicated.
JSON safety records are deleted only
after exact session/certificate/PDF
and telemetry verification. Unclear,
legacy, incomplete, corrupt or active
records are retained for manual review.

**Not done:** raw block-level resume,
absolute destructive phase recovery,
safe destructive backup/restore recovery,
or retrospective import of old
`pending_*.dat` without result identity.
This code cannot patch an already-running
old DiskChecker process. See
`docs/Long-Test-Recovery/en.md`.
Actual cross-platform CI evidence follows.


## 2026-11-23 — long-test recovery CI verified on both platforms

Production code and replay tests at
`dbbaeba7e104160edbdeb9804b0db66d395b03f9`
have the following **verified successful** CI:

- [Surface/SMART tests](https://github.com/afrowaveltd/DiskChecker/actions/runs/45203442198):
  583 PASS, 4 SKIP, 0 FAIL in main desktop/core xUnit suite
  (587 total), and 288 PASS, 0 FAIL in TUI suite.
- [Windows/Linux safety tests](https://github.com/afrowaveltd/DiskChecker/actions/runs/45203442135):
  both Ubuntu and Windows jobs **success**.

The tests include disk-free, isolated recovery checkpoint
round trips, corrupted/truncated file preservation,
GUID-based deduplication, recovery from an explicit
completed/failing sanitizer result, PDF generation
with a real transient in-memory SQLite database
(rather than EF InMemory's unsupported raw SQL),
and closed binary telemetry reattachment.
The recorded failure of the early synthetic PDF test
was caused by EF InMemory not supporting the repository's
actual SQL UPDATE; the final test verifies real SQLite.

No physical 500-GB or 10-TB disk was written, scanned,
formatted, or resumed in CI. The original running
pre-update DiskChecker cannot acquire these new recovery
features retroactively. Automatic physical test resume,
absolute-destructive multi-phase replay and safe-destructive
backup/restore recovery remain **explicitly not implemented**.

## 2026-11-23 — consistent cancellation classification

Sanitization checkpoint final state now uses the
same Core `SanitizationOutcomeClassification`
as the authoritative DB session service.
An explicit user cancellation with no physical
error details produces `Cancelled`, not
`Failed`; physical faults with actual
error details remain `Failed` even when the
message contains a cancellation word.
Added both cases as synthetic regression tests.

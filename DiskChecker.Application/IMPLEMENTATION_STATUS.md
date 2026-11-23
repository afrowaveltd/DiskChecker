# Application implementation status

## 2026-11-23 — completed sanitization startup reconciliation

Introduced `LongTestRecoveryReconciler` to
reconcile up to 100 validated terminal
`LongTestRecoveryJournal` snapshots per pass.
A completed/failed test **with an explicit
terminal result**, stable serial number and
2-minute age may be imported into the same
configured database, using a stable
`TestSession.SessionId` GUID. Existing rows
are skipped, not duplicated. The import uses
captured historical drive identity only and
never opens raw disks or resumes writes.
After DB insertion, the exact recorded pending
binary telemetry may be moved into the
session archive if it is closed and valid.

Success certificates are added only if no
certificate already exists. If an existing
certificate has no verifiably recorded PDF,
reconciliation preserves the journal rather
than generating duplicate output or silently
erasing evidence. Only verified DB + PDF
+ telemetry permits cleanup; failed/cancelled
test records do not require a success PDF.
Corrupt, still-running, serial-less, uncertain
or very recent records are left untouched.

Startup UI invocation, testing and CI are
still pending at this commit. Do not infer
a safe raw-disk resume is implemented.

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
Automated test results to be recorded
after the pending GitHub CI run finishes.

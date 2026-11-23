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
Automated test results to be recorded
after the pending GitHub CI run finishes.

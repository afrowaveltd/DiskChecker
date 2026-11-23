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

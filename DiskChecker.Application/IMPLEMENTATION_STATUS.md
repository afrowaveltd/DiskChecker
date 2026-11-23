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

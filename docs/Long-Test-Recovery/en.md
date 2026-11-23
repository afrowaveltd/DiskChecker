# Durable recovery checkpoints for long disk tests

**Status (2026-11-23):** the first implementation covers **full-disk
sanitization in Avalonia** and conservative import of explicitly
finished test records. It does **not** yet resume interrupted raw
writes or cover the complete absolute/safe destructive test pipelines.

## Why a second file is necessary

`DiskTelemetryArchive` already writes detailed throughput points to
small binary blocks. The original `pending_*.dat` files lack tested
disk identity, a stable database RunId, terminal result and
acknowledgement of a successful SQL commit. They are useful for
graph recovery but cannot independently prove test completion.

New tests create a versioned `run_<GUID>.json` in
`DiskChecker/Telemetry/Recovery` under the effective Windows/Linux
user's LocalApplicationData. The GUID is also stored in
`TestSession.SessionId`. Recovery entries are never attached to a
drive merely because an OS device index happens to match.

| State | Meaning | Automatic action |
| --- | --- | --- |
| Running | Physical test progress, no terminal result | Preserve, warn; **never restart raw I/O** |
| Completed | Sanitizer returned real success and result was saved to JSON | Look up exact GUID; import if safe |
| Failed | Terminal failed sanitizer outcome | Record as failed if replayable |
| Cancelled | User explicitly cancelled | Record as cancelled, never a media failure |
| DatabaseSaved | Exact session GUID was committed | Retain until other evidence is checked |
| ArtifactsSaved | Expected certificate/PDF was recorded | Clean JSON only after independent verification |

The phase/progress/error checkpoint is replaced after a flushed
temporary write, approximately every 5% or ten seconds. Each
new completed test stores the actual `SanitizationResult`, SMART,
bounded graph points (up to 5,000 per phase), timestamps, disk
identity and the exact path of the binary telemetry archive.
A 100%-complete progress bar **never substitutes** for an explicit
terminal success.

The size-capped JSON is an additional safety record, not a
replacement for the separate binary archive. Successful binary
telemetry is retained under its session-GUID filename because
the application needs it for precise chart zoom.

## Startup reconciliation

After Avalonia opens, a scoped reconciler examines up to 100
checkpoints. Only a valid, terminal, older-than-two-minutes
sanitization with a sufficiently reliable serial number can
be imported automatically. The process:

1. checks for an existing `TestSession.SessionId` using the exact GUID;
2. if absent, reconstructs the **historical** disk/test metadata
   and inserts a new session, without opening the raw device;
3. reattaches only the recorded, valid, closed `pending_*.dat`
   to that session, without overwriting another archive;
4. creates a success certificate only when none exists, and
   requires a real PDF file; an uncertain existing certificate
   does not trigger a duplicate automatically;
5. confirms the exact database session/certificate and archived
   telemetry before deleting the extra JSON file.

If SQLite is full, a PDF is missing, an archive is open by another
process, the serial is unreliable, or a checkpoint is corrupt,
the application retains the evidence and warns about unresolved
records. It never deletes live SQLite WAL/SHM files or repairs a
database implicitly. In this first version, the JSON replacement
is atomic at the application level; full power-loss durability
still depends on the OS and storage hardware.

## Physical resume is a separate feature

To resume **sanitization write/verify** mid-disk, a recorded byte
offset is insufficient. The application would have to verify
stable hardware identity, write-pattern state, block boundaries,
read-back integrity, last flushed positions and exclusive access
before a new user-confirmed destructive operation. There is no
safe automatic physical resume in this release.

**Absolute destructive** tests have multiple write/read/seek
phases requiring their own checkpoint contracts.
**Safe destructive** tests are even more sensitive: the
original backup/image, its hash, restore boundaries and
rollback conditions must be verified before *any* resume.
Their full journal integrations are follow-up milestones.
Short SMART and seek tests are deliberately out of scope.

## Existing failed tests and old binaries

An already-running version of DiskChecker cannot acquire newly
committed code. Legacy `pending_*.dat` files lack the metadata
needed for automatic reconstruction: **do not delete them** or
assume an old SQLite error 13 means the sanitized HDD filled up.
Preserve the current GUI, database and pending telemetry for
manual assessment, as described in
[SQLITE_FULL incident recovery](../Sqlite-Sanitization-Recovery/en.md).

## Validation

xUnit tests use fake disk descriptors, disposable temporary
folders and EF Core InMemory databases. They cover atomic
checkpoints, explicit terminal state, corrupted-file retention,
stable GUIDs, idempotent session backfill and refusal to treat
interrupted work as completed. No actual physical disk is
overwritten by these tests.

The first implementation is not a guarantee against every
power loss, damaged filesystem, missing telemetry file or
broken SQLite installation. Safe recovery prefers preserving
uncertain evidence over deleting or inventing a result.

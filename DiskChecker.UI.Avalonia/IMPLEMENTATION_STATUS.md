# Avalonia UI implementation status

## 2026-11-22 — packaged Linux desktop startup investigation

- Maintainer reports Windows app working and
  manually compiled/started Linux Avalonia UI
  working. A DEB-installed GUI instead prompts
  for administrator authentication and then
  shows no window; menu icon is blank.
- Current application startup still checks
  effective UID 0 on Linux and starts Avalonia
  using `UsePlatformDetect()` with Inter fonts.
  **No UI startup code was changed** because
  the packaging layer already demonstrated
  two concrete defects: direct `pkexec`
  without GUI session coordinates, and missing
  system theme icon.
- Installer now uses a desktop-aware launcher
  and targeted polkit helper, forwarding only
  X11/Wayland session coordinates after
  authentication. App is still privileged and
  should load from `/opt/diskchecker`.
  Installer ships the SVG under hicolor so
  `Icon=diskchecker` resolves.
- Existing Windows execution manifest and
  runtime packages remain unchanged.
- **Packaged Linux runtime verification pending:**
  rebuild and install latest test DEB, launch
  from menu as ordinary user, inspect
  `sudo journalctl -b -t diskchecker
  --no-pager -n 150` on failure.
  Do not mark it green until a real GUI test
  succeeds. Future architectural improvement:
  unprivileged GUI plus privileged disk service.
- See `../installer/IMPLEMENTATION_STATUS.md`
  and `../docs/Linux-Desktop-Launcher/en.md`.

## 2026-11-22 — PR17 Copilot follow-up corrections

- Current master already contains the SMTP functionality
  from merged PR #16; PR #17 was a 67-commits-behind
  conflicting implementation, not a safe merge target.
  All seven PR17 review threads had been marked
  resolved, but the current master was checked
  independently for the actual issues.
- Corrected a remaining destructive-test status
  issue: `StartTestAsync` no longer overwrites
  completion/SMTP warnings with `PowerRestored`.
  A power restore failure now appends a separate
  warning instead of replacing the disk/mail result.
- Safe destructive certificate delivery no longer
  shares the cancellation token that navigation
  disposes after the disk restore is complete.
- Certificate builders now attach bounded actual
  seek and sanitization speed series and generic
  speed values for PDF graphs, not just summary
  statistics. The PDF renderer identifies a
  comprehensive test from measured sanitizer +
  seek data independently of localized TestType.
- Added `LocalizedCertificateCategoryTests`
  including localized strings and bounded chronological
  chart sample checks; physical disk tests were
  **not** executed.
- English rationale:
  `docs/Pull-Request-Reconciliation/en.md`.
  **.NET build/full test results pending**.

## 2026-11-23 — sanitization SQLite error 13, partial persistence visibility

A real 465.76 GB Windows disk sanitization finished its full
write+read verify but displayed
`SQLite Error 13: database or disk is full`
while saving the resulting disk card. The tested drive's
remaining space is **not evidence of a full SQLite volume**:
`SettingsService` can select a DB in the working folder,
beside the executable or under AppData, and transaction
journals/TEMP or SQLite limits can fail separately.
The **actual cause is still undetermined** without
inspectable running database path and free-space/quota data.

Code audit found that `SurfaceTestViewModel.RunSanitizationAsync`
had one broad `try/catch` for *both* the authoritative
`SaveSanitizationAsync` SQLite commit *and*
later certificate DB/PDF operations. This made a
post-commit certificate failure falsely appear as a
missing disk card. The UI now tracks whether the session
has been committed and uses a distinct warning when
only certificate/PDF creation fails. For a real
SQLITE_FULL (13), the error dialog also includes
the active startup DB path, available space on its
volume, SQLite extended error code and TEMP location;
it never prints connection passwords.

The physically finished test must **not** be repeated
merely to recover the card. Its old-process telemetry
may still exist as
`%LOCALAPPDATA%\\DiskChecker\\Telemetry\\pending_*.dat`;
the user was advised to copy (not delete) these
files while the process remains open. No SQLite
VACUUM, WAL cleanup, drive reformatting, or
destructive fallback is attempted by the fix.
This commit does not automatically reconstruct
a previously failed save, and it cannot alter
the already-running application instance.

## 2026-11-23 — SQLite sanitization save error classification verified

Change `c35eca9a95fac6a835cd7a517246879049ebf785`
compiles and passes the normal project/desktop
test suite:
- [Surface/SMART handoff](https://github.com/afrowaveltd/DiskChecker/actions/runs/45192209701)
  — success.
- [Windows/Linux safety tests](https://github.com/afrowaveltd/DiskChecker/actions/runs/45192209686)
  — both platforms success.

Documented read-only investigation, user-safe preservation
of closed `pending_*.dat` telemetry logs, and why
`SQLITE_FULL` is not evidence about the sanitized HDD:
`docs/Sqlite-Sanitization-Recovery/en.md`.
A DB/SQLite error 13 root cause remains unverified:
no original live database connection path, quota,
WAL state, filesystem details or SQLite pragmas
have been obtained from the affected machine.
No physical drive access, filesystem creation,
automatic SQLite cleanup or auto-retry was performed.

## 2026-11-23 — preventive storage preflight for destructive sanitization

The maintainer reports **230,741,725,184 B free on C:**
and **331,763,261,440 B free on D:**. The prior manual
PowerShell inventory found no `pending_*.dat` files in the
current user's LocalAppData telemetry folder; their absence
is not evidence that telemetry caused the SQLite error.
A 1-TB sanitization succeeded on this machine earlier
the same day. Root cause of the later `SQLITE_FULL` remains
**unconfirmed**; no disk-wide capacity hypothesis is assumed.

The long-running destructive sanitization flow now
performs **two blocking preflight checks before raw I/O**:

1. Real configured SQLite persistence: verify
   volume headroom (128 MiB minimum), `max_page_count`
   capacity (32 MiB minimum) and an **actual MAIN
   database CREATE TABLE + INSERT inside an explicit
   rollback-only transaction**. SQLite failures
   (permissions, no growth quota, no space, locked DB)
   block the destructive test early rather than
   allowing hundreds of GiB to be processed first.
   For PostgreSQL/SQL Server the current check
   is connectivity-only; this limitation is explicit.
2. The actual `DiskTelemetryArchive.ArchiveDirectory`:
   inventory `pending_*.dat` files, distinguish
   those older than one day, probe a 4-KiB physical
   write+flush, check 128-MiB volume headroom.
   No cleanup or modification of pending/committed
   archives; the probe is removed. Failure blocks
   the new destructive run. Existing pending files
   result in a localized warning, not deletion.

These are **point-in-time checks** and cannot
guarantee against later disk or database failures.
No destructive disk operations were performed for
the tests. The old in-progress application version
is unchanged until rebuilt/relaunched.

## 2026-11-23 — durable recovery metadata wired into sanitization

The Avalonia sanitation workflow now creates a unique
`LongTestRecoveryJournal` before opening the raw device,
periodically checkpoints phase/progress, and records the
actual terminal result + SMART + bounded write/read
graphs **before** attempting SQLite persistence.

`SaveSanitizationAsync` accepts a backward-compatible
optional `recoveryRunId` so the DB `TestSession.SessionId`
is deterministic and a retry can recognize an already
committed run. The original data and public overload
continue to work. The UI records successful database
commit separately from certificate/PDF, and deletes the
JSON checkpoint only after independent SQLite session
and certificate verification plus an existing PDF.
The high-resolution binary telemetry file is not
deleted (it remains required for detailed graph zoom).
Failed/cancelled tests are recorded with explicit
terminal states; incomplete or failed saves preserve
the checkpoint on disk.

**This is checkpoint creation + safe retention, not
yet automatic database replay or a raw-disk resume.**
Such automation will require conservative identity,
transaction and cross-process ownership checks.
Physical disk tests were not repeated. CI pending.

## 2026-11-23 — startup reconciliation connected to Avalonia

After the main window opens, a scoped
`LongTestRecoveryReconciler` examines at
most 100 on-disk recovery records from
the configured account's local telemetry
location. Valid old terminal sanitization
outcomes may be backfilled idempotently
into the existing database, including
stable run GUID, SMART and bounded samples,
while exact binary telemetry is
reattached and the PDF/certificate
is rebuilt only when none exists.

Unfinished, ambiguous, invalid, too-recent,
or serial-less records are **never**
automatically marked successful or
replayed onto a physical device.
A warning describes unresolved records,
without deleting any. Startup errors
are caught so the desktop still opens;
the underlying recovery remains on disk.

This is conservative automatic **data
reconciliation**, not physical I/O resume.
CI and database integration verification
still required before broad rollout.

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

## 2026-11-23 — absolute destructive phase checkpoint integration

The absolute destructive GUI now creates a
typed `AbsoluteDestructive` JSON recovery header
before raw-device access, then records each
phase boundary and actual write/read verification
progress for both sanitization passes.
Cancellation and errors are preserved as
distinct terminal outcomes. An explicitly
returned sequence of destructive/seek phases
is recorded before SQL persistence.

The saved `TestSession.SessionId` uses the
same recovery GUID. The extra JSON checkpoint
is removed **only after** independent
repository lookups confirm the exact session
and certificate and the PDF is present.
An unsuccessful SQL/certificate write
retains the recovery file.

**Not yet automatic:** reconstructing a
missing absolute-destructive certificate
from all stage results, or resuming a
specific failed write/verify phase.
The startup reconciler conservatively
leaves this test kind unresolved; no
physical raw-device resume is attempted.
Cross-platform CI and physical-drive
acceptance still required.

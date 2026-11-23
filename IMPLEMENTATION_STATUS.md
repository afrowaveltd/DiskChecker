# DiskChecker implementation status

## 2026-11-22 — Linux package launcher and missing menu icon

**Source of truth:** `afrowaveltd/DiskChecker`,
default branch `master`. Commit every change
alongside installer tests and documentation.

### Evidence and scope

- Maintainer confirms Windows works and a
  manually compiled Linux application works.
- Test DEB installs, prompts for administrator
  credentials, but no window appears after
  authentication; the desktop icon is blank.
- Packaging review found direct
  `pkexec /opt/diskchecker/DiskChecker.UI.Avalonia`
  in the installed desktop entry and a missing
  hicolor icon installation despite
  `Icon=diskchecker`.
  These defects have been corrected in the
  packaging scripts for all Linux formats.
  The exact cause of this machine's GUI
  exit remains to be confirmed from logs.

### Committed implementation

- Unified `/usr/bin/diskchecker` launch path
  in DEB/RPM/portable and desktop entry.
- New polkit-authenticated root-owned helper
  passing only explicit X11/Wayland session
  coordinates; strict `PKEXEC_UID` checks,
  controlled root HOME/PATH and journald
  startup-error logging when available.
- New narrowly scoped
  `org.afrowave.diskchecker.policy`
  (administrator authentication required).
- Install existing SVG as
  `/usr/share/icons/hicolor/scalable/apps/diskchecker.svg`.
- Correct Debian `polkitd` / `pkexec`
  runtime dependencies; RPM requires polkit.
- New `installer/tests/verify-linux-packages.sh`
  checks package contents and configuration;
  updated non-release test installer workflow.
- Updated `docs/LINUX_INSTALL.md`,
  new `docs/Linux-Desktop-Launcher/en.md`,
  root `README.md`, `README-EN.md`,
  plus scoped `IMPLEMENTATION_STATUS.md`
  files for installer and Avalonia UI.
- No changes to disk operations, Windows
  setup, Avalonia startup code or PDF logic.

### Verification state

**Pending:** new Linux DEB/RPM/portable package
build + tests, actual graphical login test
on maintainer's Debian, and log confirmation
if application still exits after polkit.

Use:
`bash installer/tests/verify-linux-packages.sh`
after building packages; install fresh DEB,
launch `diskchecker` as a normal desktop
user (no `sudo`), and collect
`sudo journalctl -b -t diskchecker
--no-pager -n 150` if needed.

Do not create a new public release until
the maintainer confirms the installed Linux
GUI and icon both work. On some Wayland
compositors, privileged GUI clients may
remain unsupported; investigate logs and
prefer privilege separation long term.

## 2026-11-22 — SMTP PR review reconciliation and smartmontools installer

- Read PR #17 metadata, its top-level and inline
  comments, all **seven** Copilot review threads,
  PR #15/#16/#18 states, and the current master
  implementations. PR #16 was merged at
  `3d345e2d24d48810ad86708a2448a1191e461b91`,
  superseding most PR #15/#17 SMTP changes.
  PR #17 remained unmergeable and 67 commits
  behind master at inspection. Do not force-merge
  stale code over the more recent master.
- Forward-fixed remaining visible defects directly
  on master: final power-restoration message
  no longer hides SMTP/certificate warnings;
  completed safe-destructive SMTP delivery does
  not depend on the navigational cancellation token;
  absolute destructive adds bounded actual seek
  samples to generated PDF; safe destructive
  populates all sanitization and seek graph
  series plus generic speed figures; renderer
  recognizes localized comprehensive tests
  using measured data rather than one fixed
  English display string.
- Added `tests/DiskChecker.Tests/LocalizedCertificateCategoryTests.cs`
  for localized test categories, seek-only and
  read-only fallbacks and bounded ordered points.
- The DEB/RPM already declared native
  `smartmontools` dependencies; portable
  installer now checks `smartctl --version`
  before copying app files and installs missing
  `smartmontools` via an available trusted
  distribution package manager. Added safe mock
  tests and package metadata/archive validation.
- Documentation:
  `docs/Pull-Request-Reconciliation/en.md`,
  `docs/Installer-Dependencies/en.md`,
  updated `docs/LINUX_INSTALL.md`, root README
  and scoped implementation status files.
- **New focused/full .NET and installer CI checks
  still pending**, as are runtime SMTP, PDF,
  and DEB GUI smoke tests. No production release
  created from these changes.

## 2026-11-22 — Windows smartmontools dependency preflight

- Inno Setup now verifies that `smartctl.exe --version` works
  from the standard Program Files directory or PATH before
  installing DiskChecker.
- If smartmontools is missing, the elevated installer requests
  the exact `smartmontools.smartmontools` WinGet package
  from the `winget` source, with silent installation
  and source/package agreements explicitly accepted.
  WinGet's manifest controls checksums; no arbitrary
  unsigned remote EXE is executed directly by DiskChecker.
- A failed WinGet installation or unusable smartctl
  aborts DiskChecker setup before copying application
  files, with a clear manual-install instruction.
  Already installed usable smartctl is left unchanged.
- Normal Windows deployments need working WinGet and
  network access when the dependency is absent;
  offline machines must install smartmontools separately.
- The Linux DEB/RPM dependencies and portable helper
  remain unchanged. GitHub Actions installer workflow
  passed after the earlier CA1512 fix; a new Inno
  compilation/runtime confirmation is still pending.
- Details in `docs/Installer-Dependencies/en.md`.

## 2026-11-22 — PR cleanup and CI verification checkpoint

- Closed superseded PRs **#15** and **#17** without merging
  their diverged branches into modern master; written
  comments explain why merged **#16** and subsequent
  forward fixes are now the source of truth.
  All seven earlier Copilot review threads were resolved
  and substantive issues were checked against master.
- Windows smartmontools dependency check/install added
  to Inno Setup; Linux DEB/RPM use native package metadata;
  portable Linux now checks and installs smartmontools.
- GitHub Actions
  [installer run 45010055347](https://github.com/afrowaveltd/DiskChecker/actions/runs/45010055347)
  passed for **Windows Inno and Linux DEB/RPM/portable**
  packages; preceding
  [run 45009823681](https://github.com/afrowaveltd/DiskChecker/actions/runs/45009823681)
  also passed the Linux package dependency checks.
  [surface/SMART run 45009823705](https://github.com/afrowaveltd/DiskChecker/actions/runs/45009823705)
  passed after the CA1512 fix.
- Broader safety workflows and real installed-app
  validation remain separate pending checkpoints.
  No new public release was created.


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

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

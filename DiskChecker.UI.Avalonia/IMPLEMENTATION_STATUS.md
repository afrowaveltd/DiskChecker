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

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

# Linux / Windows installer implementation status

## 2026-11-22 — Linux desktop application launch and icon repair

### Observed maintainer problem
- Windows installer and application run as expected.
- Linux DEB installs successfully; its menu icon is blank.
- The menu launcher requests root authentication but no
  Avalonia window is shown after the password is accepted.
- Manual Linux compilation and application launch work.
- No full system log or compositor/version details were
  supplied yet, so the exact runtime exit reason is
  **not independently confirmed**.

### Confirmed packaging defects and changes
- Previous `installer/diskchecker.desktop` executed
  `pkexec /opt/diskchecker/DiskChecker.UI.Avalonia`
  directly. Default polkit sanitization drops the
  display session variables, a strong explanation
  for a post-authentication GUI startup failure.
- Desktop entry now uses
  `Exec=/usr/bin/diskchecker`; a new non-root
  launcher transfers only essential X11/Wayland
  session coordinates as positional arguments to
  `pkexec /usr/lib/diskchecker/diskchecker-elevated`.
- New root-owned helper validates `PKEXEC_UID`
  against the originating user, validates its
  Wayland runtime directory and session type,
  explicitly forwards needed display variables,
  keeps root HOME and PATH controlled, and runs
  the existing packaged Avalonia binary.
  When available, `systemd-cat` captures its
  stdout/stderr under journal identifier
  `diskchecker`, making post-authentication
  failures observable.
- New targeted
  `installer/org.afrowave.diskchecker.policy`
  requires administrator authentication, matches
  only the installed root helper and does **not**
  enable the discouraged `allow_gui` annotation
  or passwordless raw-disk access.
- The old desktop entry named `diskchecker` as
  its icon but none of DEB/RPM/portable installed
  a matching icon. All now install the existing
  project SVG under
  `/usr/share/icons/hicolor/scalable/apps/diskchecker.svg`.
- DEB now explicitly depends on `pkexec`,
  `polkitd` and `smartmontools`, recommending
  `hicolor-icon-theme`. RPM depends on
  `polkit` and `smartmontools`.
  Portable installer uses the same common
  `/usr/bin/diskchecker` path.
- Added `installer/tests/verify-linux-packages.sh`
  for script syntax, policy/XML, desktop/Icon and
  **actual DEB/RPM/portable archive contents**.
  GitHub test installer workflow runs this
  and `desktop-file-validate` before publishing
  non-release test artifacts.
- Existing Windows installer, core disk-test
  code, database, PDF certificate behavior and
  Linux self-contained published executable
  are unchanged.

### Documentation
- `docs/LINUX_INSTALL.md` updated with Debian,
  Fedora and portable instructions plus
  `journalctl -b -t diskchecker` diagnostics.
- `docs/Linux-Desktop-Launcher/en.md` records
  launch architecture, caveats and checks.
- Root `README.md` and `README-EN.md` updated.

### Verification and next step
1. **Not yet compiled or manually tested** in
   the maintainer's Linux desktop; no success
   claim for the installed DEB.
2. Trigger the existing `Build test installers
   (no release)` GitHub Action, or locally:
   `dotnet publish DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64`,
   `bash installer/package-linux.sh 1.0.0`,
   `bash installer/tests/verify-linux-packages.sh`.
3. Install rebuilt DEB, check desktop icon,
   launch from graphical menu as normal user,
   authenticate and observe the window.
4. If it still fails, capture
   `diskchecker` terminal output and
   `sudo journalctl -b -t diskchecker
   --no-pager -n 150`.
5. Investigate compositor-specific Wayland
   restrictions rather than weakening global
   privileges. A future unprivileged GUI +
   narrow privileged backend remains the
   preferred long-term security architecture.
6. Re-check RPM and portable artifacts via
   CI, then preserve test results and move
   toward a new release only after maintainer
   verification.

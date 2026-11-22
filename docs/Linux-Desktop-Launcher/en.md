# Linux desktop launcher, polkit authentication and packaged icons

## Observed failure

On the maintainer's Linux system, a self-contained Avalonia
build worked when compiled and launched manually, and a DEB
installed successfully. But opening its desktop entry prompted
for administrator authentication and then showed no window.
The installed desktop entry also had an empty application icon.
Windows launches were unaffected.

This is **not** proof that the installed application binary is
broken: both problems were identified in the Linux packaging
source independently of the application code.

## Root cause in previous packaging

The previous `installer/diskchecker.desktop` declared:

```ini
Exec=pkexec /opt/diskchecker/DiskChecker.UI.Avalonia
Icon=diskchecker
```

The standard `pkexec` environment is deliberately sanitized.
It normally drops X11 display authorization and does not forward
the Wayland runtime/socket and session bus variables needed
by a graphical client. Authentication success therefore does
**not** mean a root process can connect to the user's compositor.
This is a highly plausible explanation of the observed startup
failure; a runtime log is still needed to confirm the particular
display-session failure.

The DEB/RPM/portable installers did not install an icon named
`diskchecker` into a standard icon theme directory, leaving
the requested `Icon=diskchecker` unresolved.

## New architecture

The desktop entry now launches `/usr/bin/diskchecker` as
the signed-in desktop user. That non-root shell wrapper:

1. Executes the installed binary directly when already root.
2. Otherwise requires `pkexec` and an active graphical
   X11 or Wayland session.
3. Invokes the dedicated root-owned helper
   `/usr/lib/diskchecker/diskchecker-elevated` through polkit.
4. Passes only **explicit positional arguments** carrying
   the originating user ID and these display-session
   coordinates:
   `DISPLAY`, `XAUTHORITY`, `WAYLAND_DISPLAY`,
   `XDG_RUNTIME_DIR`, `DBUS_SESSION_BUS_ADDRESS`,
   `XDG_SESSION_TYPE`.

The helper requires effective UID 0, compares the original
user ID against the `PKEXEC_UID` supplied by polkit,
requires the Wayland runtime directory (when set) to equal
`/run/user/<original UID>`, and allows only known X11/Wayland
session type values. It sets a controlled PATH and
`HOME=/root`, not the user's arbitrary root environment.
The original self-contained binary still runs with the
administrative privileges it needs.

The targeted action is installed at:

`/usr/share/polkit-1/actions/org.afrowave.diskchecker.policy`

It explicitly requires administrator authentication and
matches the exact root helper path. It does **not** use
the discouraged `org.freedesktop.policykit.exec.allow_gui`
annotation, does not change global polkit rules, and does
not permit passwordless disk access.

On Linux systems with `systemd-cat`, the helper captures
standard output and standard error with journal identifier
`diskchecker`. This makes post-authentication failures
visible after a desktop-launch attempt. On systems without
`systemd-cat`, normal console stderr remains available.

## Packages and icon

All three Linux formats now install the same resources:

| Installed path | Purpose |
| --- | --- |
| `/opt/diskchecker/DiskChecker.UI.Avalonia` | Existing self-contained .NET GUI |
| `/usr/bin/diskchecker` | Desktop-aware non-root entry point |
| `/usr/lib/diskchecker/diskchecker-elevated` | Root-only polkit invocation target |
| `/usr/share/polkit-1/actions/org.afrowave.diskchecker.policy` | Dedicated administrative authentication |
| `/usr/share/applications/diskchecker.desktop` | Standard menu entry |
| `/usr/share/icons/hicolor/scalable/apps/diskchecker.svg` | System application icon |

The DEB now depends on Debian's `polkitd` and `pkexec`
packages as well as `smartmontools`; the RPM depends on
`polkit` and `smartmontools`. The portable archive ships
every referenced resource. None of these installers
formats disks or deletes user data.

The existing SVG is re-used, so no extra graphics,
icon conversion dependency or platform-specific
asset generator is required.

## Tests and limitations

`installer/tests/verify-linux-packages.sh` is a
non-privileged packaging regression test. It validates
shell syntax, parses the polkit XML, checks its
non-passwordless privileges, checks desktop Exec/Icon,
parses SVG, and inspects the **built DEB, RPM and
portable archive** for every required file and
executable permission. GitHub's test-installer workflow
runs it before publishing its *temporary* downloadable
test artifacts, along with `desktop-file-validate`.
No production release or tag is created.

These tests are not a replacement for a real login
session: some Wayland compositors prohibit privileged
GUI clients even with correct session coordinates.
The long-term security architecture should move
raw-disk operations to a narrow privileged backend
while keeping Avalonia itself unprivileged. This change
is a compatibility correction for the existing design,
not a proposal to weaken global compositor permissions.

## Maintainer verification on Debian

After rebuilding the Linux test DEB and installing it,
open DiskChecker from the application launcher. The
new icon should render and polkit should ask for
administrator authentication; then the window should
open in the logged-in user's display session.

To diagnose a remaining failure, run:

```bash
grep '^Exec=' /usr/share/applications/diskchecker.desktop
ls -l /usr/lib/diskchecker/diskchecker-elevated
ls -l /usr/share/icons/hicolor/scalable/apps/diskchecker.svg
diskchecker
sudo journalctl -b -t diskchecker --no-pager -n 150
```

Start `diskchecker` as the **normal desktop user**,
not with `sudo`, to test the new path. If it still
fails, collect terminal output and the journal errors
and distinguish missing shared libraries, process
exceptions and compositor permission failures.

**Verification state (2026-11-22):** code and packaging
changes committed; the assistant cannot build/run the
installed package or its GUI in the maintainer's Linux
session. No post-fix success is claimed yet.

See [Linux install and troubleshooting](../../LINUX_INSTALL.md).

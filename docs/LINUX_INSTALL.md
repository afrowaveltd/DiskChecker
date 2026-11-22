# DiskChecker – Linux test installation (x86_64)

The installed Avalonia GUI requires administrative rights for raw-disk
operations. Use the graphical application entry **DiskChecker**, or run
`diskchecker` as the **signed-in user** from a terminal in the active
desktop session. The launcher prompts for administrator authentication
through polkit. Do not run destructive tests on disks containing valuable data.

## Test installers (not a release)

The GitHub Actions workflow
[`Build test installers (no release)`](../.github/workflows/test-installers.yml)
produces DEB, RPM and portable Linux archives. All use the same
self-contained .NET 10 `linux-x64` build and the **same launcher,
polkit helper, policy and icon**.

### Debian / Ubuntu

```bash
sudo apt install ./diskchecker-1.0.0-linux-x64.deb
diskchecker
```

The package depends on `smartmontools`, `polkitd` and `pkexec`;
`hicolor-icon-theme` is recommended for desktop icon theming.
The old package contained a desktop entry with direct
`Exec=pkexec /opt/diskchecker/DiskChecker.UI.Avalonia`. That
command can authenticate successfully, but `pkexec` removes
graphical display variables, leaving Avalonia unable to
open a window. The old DEB also **did not install** the
system-wide icon named by `Icon=diskchecker`.

### Fedora / RHEL

```bash
sudo dnf install ./diskchecker-1.0.0-1.x86_64.rpm
diskchecker
```

The RPM requires `polkit` and `smartmontools` and installs
the same desktop, root helper, targeted policy and SVG icon.

### Portable archive

```bash
mkdir -p diskchecker-package
tar -xzf diskchecker-1.0.0-linux-x64-installer.tar.gz -C diskchecker-package
cd diskchecker-package
sudo bash ./install-linux.sh
diskchecker
```

The portable installer now puts the command in
`/usr/bin/diskchecker` (the same location as DEB/RPM).
If an older portable installation left
`/usr/local/bin/diskchecker`, check
`type -a diskchecker` to detect a stale wrapper that
might take precedence in your terminal's PATH.

## How graphical startup works

The desktop entry now contains:

```ini
Exec=/usr/bin/diskchecker
TryExec=/usr/bin/diskchecker
Icon=diskchecker
Path=/opt/diskchecker
```

The non-root `/usr/bin/diskchecker` launcher gathers only
the session's `DISPLAY`, `XAUTHORITY`,
`WAYLAND_DISPLAY`, `XDG_RUNTIME_DIR`,
`DBUS_SESSION_BUS_ADDRESS` and `XDG_SESSION_TYPE`.
It invokes polkit for the **specific root-owned helper**
`/usr/lib/diskchecker/diskchecker-elevated`.
The helper verifies the original user against
`PKEXEC_UID`, validates the Wayland runtime directory,
sets its own root HOME/PATH and forwards just those
explicit graphical session coordinates to the existing
`/opt/diskchecker/DiskChecker.UI.Avalonia` executable.

The policy `/usr/share/polkit-1/actions/org.afrowave.diskchecker.policy`
requests administrator authentication for this specific
helper. It does not grant passwordless disk access,
use `allow_gui`, or preserve arbitrary caller environment.
The icon is installed at
`/usr/share/icons/hicolor/scalable/apps/diskchecker.svg`.

On systems that provide `systemd-cat`, application
standard output/errors are captured in the system
journal with the identifier `diskchecker`. This makes
a startup failure diagnosable even when the GUI closes
immediately after authentication.

**Security note:** this preserves the project's current
privileged-GUI architecture. The longer-term recommended
design is an unprivileged Avalonia UI with a narrowly
privileged disk backend; connecting a root GUI to a
user-controlled Wayland/X11 session is a compatibility
measure, not a substitute for privilege separation.

## Troubleshooting: password accepted, no window

First check that you installed a package built with
the **updated launcher** rather than the earlier
artifact:

```bash
grep '^Exec=' /usr/share/applications/diskchecker.desktop
ls -l /usr/lib/diskchecker/diskchecker-elevated
ls -l /usr/share/icons/hicolor/scalable/apps/diskchecker.svg
type -a diskchecker
```

Next, from a terminal in the same graphical session,
start without `sudo`:

```bash
diskchecker
```

If authentication succeeds but the window does not
appear, inspect the captured errors:

```bash
sudo journalctl -b -t diskchecker --no-pager -n 150
```

You can also check the display environment (no secrets
or auth cookies are printed here):

```bash
printf 'Session: %s\nX11: %s\nWayland: %s\n' \
  "${XDG_SESSION_TYPE:-unknown}" "${DISPLAY:-unset}" \
  "${WAYLAND_DISPLAY:-unset}"
```

Important limitations: some hardened Wayland compositors
do not permit graphical processes running as root to
connect to a normal user's session. In that case the
startup log should be used to diagnose the compositor
restriction; the correct long-term fix is separating
UI privileges, **not** disabling the compositor's access
controls or granting all users raw-disk access.

Do not add ordinary users to the `disk` group or grant
broad raw-disk capabilities as a workaround.
Do not blindly copy X11 authentication cookies or set
unsafe blanket `xhost` access.

## Mandatory smartmontools dependency

DiskChecker requires a functioning `smartctl` from
`smartmontools`. Installing a DEB with `sudo apt install
./diskchecker-1.0.0-linux-x64.deb` automatically installs
the package when absent, because the DEB declares
`Depends: smartmontools`. The RPM declares
`Requires: smartmontools`; use `dnf install ./...`
to resolve it.

For the **portable archive**, `install-linux.sh`
now checks whether `smartctl --version` runs.
If it does not, the installer invokes the available
system package manager (`apt-get`, `dnf`, `yum`,
`zypper`, or `pacman`) to install `smartmontools`
and rechecks it before copying application files.
Unsupported or failed dependency installation stops
with a clear error. Already-installed working
smartmontools are left unchanged.

```bash
command -v smartctl
smartctl --version
bash installer/tests/test-smartmontools-dependency.sh
```

The last command tests the helper with **mock commands**,
not real system package installation. See
[dependency design](Installer-Dependencies/en.md).

## PDF certificate folder under alternate administrators

In **Settings**, choose and save the PDF certificate
folder. Certificates are written below its
`Certificates` subdirectory. Choose a destination
accessible to the test operator, not accidentally
the elevated account's private home. The setting
takes effect for subsequent PDF exports; existing
PDFs remain at their original locations.

## Building and verifying packages

```bash
dotnet publish DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj \
  -c Release -r linux-x64 --self-contained true -o publish/linux-x64
# Debian packaging requires dpkg-deb; RPM packaging requires rpmbuild.
bash installer/package-linux.sh 1.0.0
bash installer/tests/verify-linux-packages.sh
```

The verification script checks shell syntax, targeted
polkit action metadata, desktop entry and source SVG,
then inspects the **actual built DEB, RPM and portable
archive** for the helper, policy and installed icon.
GitHub Actions runs these checks before uploading
test installers.

No GitHub release is created by these test installers.

For issues:
https://github.com/afrowaveltd/DiskChecker/issues

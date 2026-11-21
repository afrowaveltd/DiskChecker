# DiskChecker – Linux test installation (x86_64)

DiskChecker's raw-disk operations require root. The Linux application checks its
effective UID at startup; the packaged desktop shortcut uses `pkexec`. Depending
on the display server and polkit configuration, graphical root access may not
be available. The CLI fallback is `sudo diskchecker` in a suitable X11 session.
Do not run destructive tests on a disk with valuable data.

## Test packages (before public release)

The GitHub Actions workflow
[`Build test installers (no release)`](../.github/workflows/test-installers.yml)
produces downloadable test artifacts on pull requests or manual runs:
- `diskchecker-1.0.0-linux-x64.deb`
- `diskchecker-1.0.0-1.x86_64.rpm` (name may include distro suffix)
- `diskchecker-1.0.0-linux-x64-installer.tar.gz`

All contain the same self-contained .NET 10 `linux-x64` application, so the
.NET runtime does not need to be installed separately. Install dependencies
such as `smartmontools` and the OS graphical/font libraries using your package
manager. Linux ARM64 and other architectures need separately built packages.

### Debian / Ubuntu

```bash
sudo apt install ./diskchecker-1.0.0-linux-x64.deb
sudo diskchecker
```

### Fedora / RHEL

```bash
sudo dnf install ./diskchecker-1.0.0-1.x86_64.rpm
sudo diskchecker
```

### Portable installer archive

```bash
tar -xzf diskchecker-1.0.0-linux-x64-installer.tar.gz -C ./diskchecker-package
cd ./diskchecker-package
sudo bash ./install-linux.sh
sudo diskchecker
```

Create the extraction folder first (`mkdir -p diskchecker-package`) if needed.
The installer copies the application to `/opt/diskchecker`, the privileged
CLI launcher to `/usr/local/bin/diskchecker`, and the desktop entry to
`/usr/share/applications/diskchecker.desktop`.

### Building packages yourself

```bash
git clone https://github.com/afrowaveltd/DiskChecker.git
cd DiskChecker
dotnet publish DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj \
  -c Release -r linux-x64 --self-contained true -o publish/linux-x64
# Requires dpkg-deb and rpmbuild:
bash installer/package-linux.sh 1.0.0
```

## PDF certificate output and elevated users

In **Settings**, choose the `PDF certificate folder` and save it.
PDF files are written into its `Certificates` subdirectory. Choose a
writable folder accessible to the *test operator*, not an elevated service
account's home. The choice is persisted by the settings service, changes
take effect for subsequent PDFs without restarting the program, and
existing PDFs remain in their original locations. If the chosen path
is missing, export reports an error rather than silently redirecting
to root's folder.

## Known limits before release

- Test in a real graphical X11/Wayland environment: `pkexec` can fail to
  pass GUI display authorization on some distributions and compositors.
- Verify PDF opening, directory ownership and SMTP attachment delivery
  with both ordinary-user and sudo/polkit launches.
- Keep Linux disk access restricted: adding ordinary users to the `disk`
  group or granting `cap_sys_rawio` can expose all raw-disk contents and
  is **not** a recommended installation shortcut.
- The previous RPM spec removed `/var/lib/diskchecker` on uninstall.
  This has been removed; user data must not be erased as a packaging side effect.

For problems: https://github.com/afrowaveltd/DiskChecker/issues

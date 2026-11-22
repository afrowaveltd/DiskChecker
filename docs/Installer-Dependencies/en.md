# Installer dependency handling: smartmontools on Windows and Linux

## Purpose

DiskChecker uses `smartctl` from the upstream `smartmontools`
package to query device SMART status. A successful installation of
the graphical application is not sufficient if `smartctl` cannot
be executed by its privileged disk provider.

The package dependency already existed for DEB and RPM. This
milestone adds the missing **preflight and installation** to the
portable Linux installer, while retaining package-manager-native
dependency resolution for DEB/RPM.

## Windows Inno Setup (offline, version aware)

**Pinned upstream package:** [smartmontools 8.0-623-gd788ad8c38ee](https://github.com/smartmontools/smartmontools-builds/releases/tag/smartmontools-8.0-623-gd788ad8c38ee),
official 2026-11-21 **prerelease** (not the latest stable 7.5).
The build script `installer/prepare-windows-smartmontools.ps1` fetches two
artifacts and compares SHA-256 hashes with the upstream release metadata:

- `smartmontools-win32-setup-8.0-623-gd788ad8c38ee.exe`:
  `b2c77ffb58c223e8e4494442e4cd751006c00af1cfd77e834fc9f97d2fcc9f3e`
- Complete corresponding source `smartmontools-8.0-623-gd788ad8c38ee.tar.gz`:
  `9da427312bb8aee04b867b487f4cdebfbe510d9e6b16d5726daec5a71260662e`

The official installer is embedded in DiskChecker Setup with `dontcopy`.
**No WinGet, download, or internet access is required on the target PC.**
The GPLv2 license and corresponding source tarball are included in
`{app}\licenses\smartmontools`.

### Decision table (visible wizard checkbox)

| Detected state | Selected | Enabled | Operation |
| --- | --- | --- | --- |
| No working `smartctl.exe` | Yes | No | Mandatory installation |
| Exactly `8.0-623-gd788ad8c38ee` | No | No | Keep installed version |
| Older version | Yes | Yes | Offer upgrade; can decline |
| Newer version | No | No | Never downgrade |
| Existing, version unidentifiable | No | Yes | Optional replacement; never overwrite silently |

The installer checks that the actual `smartctl.exe --version` runs,
rather than trusting registry entries alone. It discovers an official
machine-wide installation using the upstream HKLM uninstall key or known
protected Program Files paths. For security, the elevated installer never
executes arbitrary smartctl.exe files from the current user's PATH.
Exact snapshot identity
comes from the upstream NSIS `DisplayVersion`; the CLI version
alone may report `8.0` without a Git commit suffix.

If selected, setup extracts and runs the embedded **official upstream
NSIS** installer silently with system-wide PATH enabled:
`/S /SO x64,smartctl,smartd,smartctlnc,drivedb,doc,uninst,menu,syspath`.
It then verifies the executable is runnable and that the registered
version matches the bundle. An error **aborts DiskChecker setup**.
The older version can be kept, but `smartctl` must be usable.

These checks happen during installation. They do not grant disk access
to normal users; DiskChecker still requires UAC administrative rights.
Existing administrator profile differences are addressed separately
by the selectable PDF output directory.

### Building the installer from source

```powershell
dotnet publish DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
pwsh -File installer/prepare-windows-smartmontools.ps1
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" "installer\DiskChecker.iss"
```

The GitHub Actions `Build test installers (no release)` workflow
runs the same dependency preparation and publishes the combined setup
as a test artifact. The pinned revision should only change after
updating both download hashes and testing the new snapshot.

## DEB and RPM

The Debian package declares:

```text
Depends: smartmontools, polkitd, pkexec
```

Using `sudo apt install ./diskchecker-1.0.0-linux-x64.deb`
automatically resolves and installs `smartmontools` from the
configured trusted distribution repositories when absent.
`dpkg -i` alone does not resolve absent dependencies; prefer
`apt install`.

The RPM spec declares both `Requires: smartmontools`
and `Requires: polkit`. Install using `dnf install ./...`
(or the distribution's equivalent resolver) so missing
`smartmontools` is installed through standard dependencies.

The installed application never downloads random binaries,
modifies the system's package sources or changes smartctl's
permissions itself. A package-manager dependency is the safe,
auditable way to handle mandatory OS tools.

## Portable archive

A portable `tar.gz` has **no native dependency metadata**.
The root-only `installer/install-linux.sh` now runs
`installer/ensure-smartmontools.sh` before touching any
existing DiskChecker installation.

The helper first resolves `smartctl` in the root command
search path and verifies `smartctl --version` succeeds.
If that command works, it does nothing.

When missing or unusable, the helper selects an available
system package manager in this order:

1. `apt-get install -y smartmontools`
2. `dnf install -y smartmontools`
3. `yum install -y smartmontools`
4. `zypper --non-interactive install smartmontools`
5. `pacman --noconfirm --needed -S smartmontools`

These commands use the package sources configured by the
administrator. The helper intentionally does **not** run
`apt-get update` or modify repositories without a separate
administrator decision. If installation fails, no supported
manager is found, or `smartctl --version` still does not
work after installation, the portable installer fails
*before installing/replacing DiskChecker application files*.

This dependency check is an installer operation, not a
background action on every GUI startup, and does not bypass
the application's existing mandatory administrator privileges.

## Regression checks

`installer/tests/test-smartmontools-dependency.sh` runs with
a private command path and **fake** `smartctl`/`apt-get`
commands. It tests the three important cases without
installing anything on the CI machine:

- A working smartctl is reused and no package-manager
  command is invoked.
- A missing smartctl invokes precisely
  `apt-get install -y smartmontools`, then verifies the
  newly available executable.
- A simulated package-manager failure stops the helper.

The existing `installer/tests/verify-linux-packages.sh`
executes those unit tests, verifies DEB dependency metadata,
checks RPM requirements and asserts the *actual portable
archive* includes `ensure-smartmontools.sh`. The
`Build test installers (no release)` GitHub Actions workflow
executes these checks on a freshly built test package.

### Manual verification

```bash
# After installing the updated DEB:
command -v smartctl
smartctl --version

# Locally when preparing Linux test packages:
bash installer/tests/test-smartmontools-dependency.sh
bash installer/tests/verify-linux-packages.sh
```

The second command requires built DEB/RPM/portable artifacts.
The mocked dependency tests do not require administrative
privileges or modify machine packages.

## Status

Windows packaging now embeds and SHA-256-verifies the upstream
prerelease during the CI build instead of depending on WinGet.
Linux native packages keep their distribution-managed dependencies.
Final CI and an actual UAC/manual hardware smoke test are required
before a new public release.

See [Linux installation](../LINUX_INSTALL.md) and
[installer implementation status](../../installer/IMPLEMENTATION_STATUS.md).

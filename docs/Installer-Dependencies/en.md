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

## Windows Inno Setup

The Windows installer now uses an Inno Setup
`PrepareToInstall` hook to check whether
`smartctl.exe --version` runs from the standard
Program Files installation directory or the system
command search path.

When a working executable is present, the installer
does not change the existing smartmontools installation.
Otherwise, before installing DiskChecker, it requests
the exact publisher package identifier from Windows
Package Manager:

```powershell
winget install --exact --id smartmontools.smartmontools --source winget --scope machine --silent --accept-package-agreements --accept-source-agreements
```

WinGet resolves the package through the configured
trusted source and validates the installation
manifest's checksums. The DiskChecker installer does
**not** download and execute an unverified third-party
EXE on its own and does not modify WinGet sources.

After installation, `smartctl --version` must succeed.
If WinGet is unavailable, its installation fails, or
smartctl remains unusable, Inno Setup reports the
problem and stops before copying DiskChecker files,
with instructions to install official smartmontools
manually and retry.

This requires a working Windows Package Manager
and network access when smartmontools is not already
installed. Offline installation requires preparing
smartmontools separately. The installer does not
silently bypass the dependency check.

## DEB and RPM

The Debian package declares:

```text
Depends: smartmontools, polkitd, pkexec, util-linux, e2fsprogs, fdisk | parted, udev
```

Using `sudo apt install ./diskchecker-1.0.0-linux-x64.deb`
automatically resolves and installs `smartmontools` from the
configured trusted distribution repositories when absent.
`dpkg -i` alone does not resolve absent dependencies; prefer
`apt install`.

The RPM spec declares `Requires: smartmontools`, `polkit`,
`util-linux` and `e2fsprogs`. Install using `dnf install ./...`
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

## GPT preparation and optional file systems

The application checks *actual installed formatter commands* when it asks for
a filesystem after a successful destructive test and persisted certificate.
The default full-disk GPT/EXT4 preparation depends on `wipefs`,
`sfdisk` (or `parted`), `udevadm` and `mkfs.ext4`.
DEB and RPM packages install core utilities through native dependencies.
The portable installer uses `ensure-partition-tools.sh` before copying app files.

On Linux, additional formats are offered only when the corresponding
`mkfs.*` tool is present. NTFS is offered only when **both** `ntfs-3g`
and `mkfs.ntfs` are found. Optional formatter packages are recommended,
not silently installed just to add menu entries.
Windows offers GPT/NTFS with the fixed SCCM volume label.

All preparation is optional and occurs after test persistence.
A 'No' response does not change the verified test result.

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

The Windows Inno Setup dependency preflight and the new
Linux portable helper, package archive inclusion, tests and
documentation are committed. **A fresh package build and
runtime installation test are still required** before a
new public release; neither is claimed as passed by the
assistant environment.

See [Linux installation](../LINUX_INSTALL.md) and
[installer implementation status](../../installer/IMPLEMENTATION_STATUS.md).

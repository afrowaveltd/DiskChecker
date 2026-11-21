#!/usr/bin/env bash
# Builds test DEB, RPM, and a portable installer archive from a linux-x64 publish.
# No release or GitHub tag is created.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"
VERSION="${1:-1.0.0}"
PUBLISH="publish/linux-x64"
PACKAGES="packages"
[[ -f "${PUBLISH}/DiskChecker.UI.Avalonia" ]] || {
    echo "Missing ${PUBLISH}; run dotnet publish -c Release -r linux-x64 --self-contained true -o ${PUBLISH}" >&2
    exit 1
}
mkdir -p "${PACKAGES}"
STAGE="$(mktemp -d)"
trap 'rm -rf "${STAGE}"' EXIT

# Debian: no distribution-specific libicu SONAME pinned to an obsolete distro.
DEB="${STAGE}/deb"
install -d "${DEB}/DEBIAN" "${DEB}/opt/diskchecker" "${DEB}/usr/bin" "${DEB}/usr/share/applications"
cp -a "${PUBLISH}/." "${DEB}/opt/diskchecker/"
chmod 755 "${DEB}/opt/diskchecker/DiskChecker.UI.Avalonia"
install -m 755 installer/diskchecker-launcher "${DEB}/usr/bin/diskchecker"
install -m 644 installer/diskchecker.desktop "${DEB}/usr/share/applications/diskchecker.desktop"
cat > "${DEB}/DEBIAN/control" <<EOF
Package: diskchecker
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: amd64
Depends: smartmontools
Maintainer: Afrowave Community <noreply@afrowave.ltd>
Description: Disk health analysis, surface diagnostics and PDF certificates
 Cross-platform self-contained .NET disk diagnostics.
 Requires administrative privileges for raw-disk access.
EOF
dpkg-deb --root-owner-group --build "${DEB}" "${PACKAGES}/diskchecker-${VERSION}-linux-x64.deb"

# RPM: build a binary-only package from the same published files, not a source recompilation.
RPM_TOP="${STAGE}/rpmbuild"
mkdir -p "${RPM_TOP}"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS}
rpmbuild -bb installer/diskchecker.spec \
  --define "_topdir ${RPM_TOP}" \
  --define "_builddir ${ROOT}" \
  --define "_sourcedir ${ROOT}" \
  --define "debug_package %{nil}"
find "${RPM_TOP}/RPMS" -name '*.rpm' -type f -exec cp -f {} "${PACKAGES}/" \;

# Portable offline install: unpack and run sudo ./install-linux.sh.
BUNDLE="${STAGE}/portable"
mkdir -p "${BUNDLE}/publish/linux-x64"
cp -a "${PUBLISH}/." "${BUNDLE}/publish/linux-x64/"
cp installer/{install-linux.sh,diskchecker-launcher,diskchecker.desktop} "${BUNDLE}/"
chmod 755 "${BUNDLE}/install-linux.sh" "${BUNDLE}/diskchecker-launcher"
tar -C "${BUNDLE}" -czf "${PACKAGES}/diskchecker-${VERSION}-linux-x64-installer.tar.gz" .
echo "Produced packages in ${PACKAGES}"

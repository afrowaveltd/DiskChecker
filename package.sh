#!/bin/bash
set -e

echo "=== DiskChecker Packaging Script ==="
echo ""

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_DIR="${PROJECT_DIR}/publish"
PACKAGE_DIR="${PROJECT_DIR}/packages"
INSTALLER_DIR="${PROJECT_DIR}/installer"
VERSION="1.0.0"

if [ -f "${PROJECT_DIR}/version.properties" ]; then
    VERSION=$(grep "version=" "${PROJECT_DIR}/version.properties" | cut -d'=' -f2 || echo "1.0.0")
fi

echo "Package version: ${VERSION}"

package_linux() {
    local PLATFORM="$1"
    local OUTPUT_DIR="${PACKAGE_DIR}/${PLATFORM}"
    local APP_DIR="${OUTPUT_DIR}/opt/diskchecker"
    
    echo ""
    echo "=== Packaging for ${PLATFORM} ==="
    
    rm -rf "${OUTPUT_DIR}"
    mkdir -p "${APP_DIR}"
    mkdir -p "${OUTPUT_DIR}/usr/bin"
    mkdir -p "${OUTPUT_DIR}/usr/share/applications"
    mkdir -p "${OUTPUT_DIR}/var/lib/diskchecker"
    
    cp -r "${PUBLISH_DIR}/${PLATFORM}/"* "${APP_DIR}/"
    chmod +x "${APP_DIR}/DiskChecker.UI.Avalonia"
    
    ln -sf /opt/diskchecker/DiskChecker.UI.Avalonia "${OUTPUT_DIR}/usr/bin/diskchecker"
    
    if [ -f "${INSTALLER_DIR}/diskchecker.desktop" ]; then
        cp "${INSTALLER_DIR}/diskchecker.desktop" "${OUTPUT_DIR}/usr/share/applications/"
    fi
    
    chmod 755 "${OUTPUT_DIR}/var/lib/diskchecker"
    
    echo "Package directory: ${OUTPUT_DIR}"
}

package_deb() {
    local PLATFORM="$1"
    local DEB_DIR="${PACKAGE_DIR}/deb-${PLATFORM}"
    
    echo ""
    echo "=== Creating DEB package for ${PLATFORM} ==="
    
    package_linux "${PLATFORM}"
    
    mv "${PACKAGE_DIR}/${PLATFORM}" "${DEB_DIR}"
    
    mkdir -p "${DEB_DIR}/DEBIAN"
    cat > "${DEB_DIR}/DEBIAN/control" << EOF
Package: diskchecker
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: $(echo "${PLATFORM}" | sed 's/linux-//')
Depends: libc6 (>= 2.28), smartmontools
Maintainer: DiskChecker Team <support@diskchecker.cz>
Description: Professional Disk Diagnosis and Sanitization Tool
 A comprehensive tool for diagnosing hard drives and SSDs.
 Features include SMART monitoring, surface testing, disk sanitization,
 and detailed reporting.
Homepage: https://github.com/diskchecker/diskchecker
EOF

    dpkg-deb --build "${DEB_DIR}" "${PACKAGE_DIR}/diskchecker-${VERSION}-${PLATFORM}.deb" 2>/dev/null || {
        echo "Note: dpkg-deb not available. Skipping DEB package creation."
        echo "Use 'fakeroot dpkg-deb --build' on Debian/Ubuntu systems."
    }
    echo "DEB package: ${PACKAGE_DIR}/diskchecker-${VERSION}-${PLATFORM}.deb"
}

package_tarball() {
    local PLATFORM="$1"
    
    echo ""
    echo "=== Creating tarball for ${PLATFORM} ==="
    
    package_linux "${PLATFORM}"
    
    tar -czf "${PACKAGE_DIR}/diskchecker-${VERSION}-${PLATFORM}.tar.gz" \
        -C "${PACKAGE_DIR}/${PLATFORM}" \
        opt usr var 2>/dev/null
    
    echo "Tarball: ${PACKAGE_DIR}/diskchecker-${VERSION}-${PLATFORM}.tar.gz"
}

echo "Cleaning old packages..."
rm -rf "${PACKAGE_DIR}"/*.deb "${PACKAGE_DIR}"/*.rpm "${PACKAGE_DIR}"/*.tar.gz 2>/dev/null || true

if [ -d "${PUBLISH_DIR}/linux-x64" ]; then
    package_tarball "linux-x64"
    package_deb "linux-x64"
fi

if [ -d "${PUBLISH_DIR}/linux-arm64" ]; then
    package_tarball "linux-arm64"
    package_deb "linux-arm64"
fi

echo ""
echo "=== Packaging completed ==="
echo ""
echo "Packages are in: ${PACKAGE_DIR}/"
echo ""
echo "Installation:"
echo "  sudo tar -xzf diskchecker-${VERSION}-linux-x64.tar.gz -C /"
echo "  sudo diskchecker"
echo ""
echo "Note: Application requires root privileges for disk access."
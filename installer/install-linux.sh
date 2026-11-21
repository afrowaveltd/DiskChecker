#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="/opt/diskchecker"
if [[ "${EUID}" -ne 0 ]]; then
    echo "DiskChecker installation requires root: sudo $0" >&2
    exit 1
fi

if [[ -d "${SCRIPT_DIR}/publish/linux-x64" ]]; then
    PUBLISH_DIR="${SCRIPT_DIR}/publish/linux-x64"  # portable archive
elif [[ -d "${SCRIPT_DIR}/../publish/linux-x64" ]]; then
    PUBLISH_DIR="${SCRIPT_DIR}/../publish/linux-x64" # git checkout
else
    echo "Missing published linux-x64 application. Build with:" >&2
    echo "dotnet publish DiskChecker.UI.Avalonia -c Release -r linux-x64 --self-contained true -o publish/linux-x64" >&2
    exit 1
fi

if [[ ! -f "${PUBLISH_DIR}/DiskChecker.UI.Avalonia" ]]; then
    echo "Missing DiskChecker executable in ${PUBLISH_DIR}" >&2
    exit 1
fi

install -d -m 755 "${INSTALL_DIR}" /usr/local/bin /usr/share/applications
cp -a "${PUBLISH_DIR}/." "${INSTALL_DIR}/"
chmod 755 "${INSTALL_DIR}/DiskChecker.UI.Avalonia"
install -m 755 "${SCRIPT_DIR}/diskchecker-launcher" /usr/local/bin/diskchecker
install -m 644 "${SCRIPT_DIR}/diskchecker.desktop" /usr/share/applications/diskchecker.desktop
echo "Installed DiskChecker. Launch with: sudo diskchecker"

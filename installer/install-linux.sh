#!/bin/bash
set -e

INSTALL_DIR="/opt/diskchecker"
BIN_LINK="/usr/local/bin/diskchecker"
DESKTOP_FILE="/usr/share/applications/diskchecker.desktop"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RELEASE_DIR="${SCRIPT_DIR}/DiskChecker.UI.Avalonia/bin/Release/net10.0/linux-x64/publish"

echo "=== DiskChecker Linux Installer ==="
echo ""

if [ "$EUID" -ne 0 ]; then
    echo "Chyba: Instalace vyžaduje root práva."
    echo "Spusťte: sudo $0"
    exit 1
fi

if [ ! -d "${RELEASE_DIR}" ]; then
    echo "Chyba: Build neexistuje v ${RELEASE_DIR}"
    echo "Nejprve spusťte: dotnet publish -c Release -r linux-x64 --self-contained"
    exit 1
fi

echo "Instaluji do ${INSTALL_DIR}..."
mkdir -p "${INSTALL_DIR}"
cp -r "${RELEASE_DIR}"/* "${INSTALL_DIR}/"
chmod +x "${INSTALL_DIR}/DiskChecker.UI.Avalonia"

echo "Vytvářím symlink..."
ln -sf "${INSTALL_DIR}/DiskChecker.UI.Avalonia" "${BIN_LINK}"

echo "Instaluji desktop entry..."
if [ -f "${SCRIPT_DIR}/diskchecker.desktop" ]; then
    cp "${SCRIPT_DIR}/diskchecker.desktop" "${DESKTOP_FILE}"
    chmod 644 "${DESKTOP_FILE}"
fi

echo "Vytvářím datový adresář..."
mkdir -p /var/lib/diskchecker
chmod 755 /var/lib/diskchecker

echo ""
echo "=== Instalace dokončena ==="
echo ""
echo "Spuštění: sudo diskchecker"
echo "Aplikace vyžaduje root práva pro přístup k diskům."
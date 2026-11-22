#!/usr/bin/env bash
# Install smartmontools only when smartctl is unavailable or unusable.
# Called by the root-only portable installer BEFORE modifying app files.
# DEB/RPM use their native dependency solver instead.
set -euo pipefail

has_working_smartctl() {
    local executable
    executable="$(command -v smartctl || true)"
    if [[ -n "$executable" ]] && "$executable" --version >/dev/null 2>&1; then
        return 0
    fi
    return 1
}

if has_working_smartctl; then
    echo "DiskChecker: smartmontools (smartctl) is already installed."
    exit 0
fi

echo "DiskChecker: smartctl is missing or unusable; installing smartmontools..."

if command -v apt-get >/dev/null 2>&1; then
    apt-get install -y smartmontools
elif command -v dnf >/dev/null 2>&1; then
    dnf install -y smartmontools
elif command -v yum >/dev/null 2>&1; then
    yum install -y smartmontools
elif command -v zypper >/dev/null 2>&1; then
    zypper --non-interactive install smartmontools
elif command -v pacman >/dev/null 2>&1; then
    pacman --noconfirm --needed -S smartmontools
else
    echo "DiskChecker: unsupported package manager. Install smartmontools manually." >&2
    exit 1
fi

if ! has_working_smartctl; then
    echo "DiskChecker: installation did not provide a working smartctl; aborting." >&2
    exit 1
fi

echo "DiskChecker: smartmontools installed and verified."

#!/usr/bin/env bash
# Ensure the base Linux GPT/EXT4 tools exist for optional post-test formatting.
# Native DEB/RPM installations use package dependencies instead.
set -euo pipefail

have_tools() {
    command -v wipefs >/dev/null 2>&1 &&
    command -v mkfs.ext4 >/dev/null 2>&1 &&
    command -v udevadm >/dev/null 2>&1 &&
    { command -v sfdisk >/dev/null 2>&1 || command -v parted >/dev/null 2>&1; }
}

if have_tools; then
    echo "DiskChecker: GPT/EXT4 tools are ready."
    exit 0
fi

echo "DiskChecker: missing GPT/EXT4 tools; installing OS dependencies..."
if command -v apt-get >/dev/null 2>&1; then
    apt-get install -y util-linux fdisk e2fsprogs udev
elif command -v dnf >/dev/null 2>&1; then
    dnf install -y util-linux e2fsprogs systemd-udev
elif command -v yum >/dev/null 2>&1; then
    yum install -y util-linux e2fsprogs systemd-udev
elif command -v zypper >/dev/null 2>&1; then
    zypper --non-interactive install util-linux e2fsprogs systemd-udev
elif command -v pacman >/dev/null 2>&1; then
    pacman --noconfirm --needed -S util-linux e2fsprogs systemd
else
    echo "DiskChecker: unsupported package manager; install GPT/EXT4 utilities manually." >&2
    exit 1
fi
if ! have_tools; then
    echo "DiskChecker: GPT/EXT4 utilities are still missing; installation aborted." >&2
    exit 1
fi
echo "DiskChecker: GPT/EXT4 dependencies ready."

#!/usr/bin/env bash
# Safe local unit test for the portable installer dependency helper.
# Runs with a *private fake PATH*; no package manager or sudo is executed.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HELPER="$ROOT/installer/ensure-smartmontools.sh"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/bin"
export TEST_SMART_BIN="$STAGE/bin"
export TEST_INSTALL_LOG="$STAGE/install.log"

make_fake_smartctl() {
    cat > "$STAGE/bin/smartctl" <<'SMART'
#!/bin/sh
if [ "$1" = "--version" ]; then
    echo "smartctl 7.5 test stub"
    exit 0
fi
exit 3
SMART
    chmod 755 "$STAGE/bin/smartctl"
}

# When present, the installer must NOT attempt to reinstall the package.
make_fake_smartctl
cat > "$STAGE/bin/apt-get" <<'APT'
#!/bin/sh
echo "unexpected apt-get invocation" >&2
exit 71
APT
chmod 755 "$STAGE/bin/apt-get"
PATH="$STAGE/bin" /bin/bash "$HELPER" > "$STAGE/present.out"
grep -q "already installed" "$STAGE/present.out"
[[ ! -e "$TEST_INSTALL_LOG" ]]

# Missing smartctl: the helper should install exactly smartmontools, then
# verify the resulting executable instead of assuming package success.
rm "$STAGE/bin/smartctl"
cat > "$STAGE/bin/apt-get" <<'APT'
#!/bin/sh
printf '%s\n' "$*" >> "$TEST_INSTALL_LOG"
/bin/cat > "$TEST_SMART_BIN/smartctl" <<'SMART'
#!/bin/sh
[ "$1" = "--version" ]
SMART
/bin/chmod 755 "$TEST_SMART_BIN/smartctl"
APT
chmod 755 "$STAGE/bin/apt-get"
PATH="$STAGE/bin" /bin/bash "$HELPER" > "$STAGE/installed.out"
grep -qx 'install -y smartmontools' "$TEST_INSTALL_LOG"
grep -q 'installed and verified' "$STAGE/installed.out"

# Package-manager failure must stop the installer. Never report success.
rm "$STAGE/bin/smartctl"
cat > "$STAGE/bin/apt-get" <<'APT'
#!/bin/sh
exit 8
APT
chmod 755 "$STAGE/bin/apt-get"
if PATH="$STAGE/bin" /bin/bash "$HELPER" > "$STAGE/failure.out" 2>&1; then
    echo "Expected failed smartmontools installation to abort" >&2
    exit 1
fi
echo "PASS: installed/absent/failing smartmontools dependency handling"

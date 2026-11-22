#!/usr/bin/env bash
# Build-output-only contract checks; do not install packages or run GUI as root.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
command -v dpkg-deb >/dev/null || { echo 'dpkg-deb missing' >&2; exit 2; }
command -v rpm >/dev/null || { echo 'rpm missing' >&2; exit 2; }
command -v python3 >/dev/null || { echo 'python3 missing' >&2; exit 2; }

sh -n installer/diskchecker-launcher
sh -n installer/diskchecker-elevated
bash -n installer/install-linux.sh
bash -n installer/package-linux.sh
bash -n installer/ensure-smartmontools.sh
bash -n installer/tests/test-smartmontools-dependency.sh
bash installer/tests/test-smartmontools-dependency.sh

python3 - <<'PY'
from pathlib import Path
from xml.etree import ElementTree as ET
import configparser

root = Path("installer")
policy = ET.parse(root / "org.afrowave.diskchecker.policy").getroot()
actions = policy.findall("action")
assert len(actions) == 1
assert actions[0].get("id") == "org.afrowave.diskchecker.run"
annotations = {node.get("key"): node.text for node in actions[0].findall("annotate")}
assert annotations["org.freedesktop.policykit.exec.path"] == "/usr/lib/diskchecker/diskchecker-elevated"
assert "org.freedesktop.policykit.exec.allow_gui" not in annotations
defaults = actions[0].find("defaults")
assert defaults.findtext("allow_active") == "auth_admin"
assert defaults.findtext("allow_any") == "no"
assert defaults.findtext("allow_inactive") == "no"

entry = configparser.ConfigParser(interpolation=None)
entry.optionxform = str
entry.read(root / "diskchecker.desktop", encoding="utf-8")
desktop = entry["Desktop Entry"]
assert desktop["Exec"] == "/usr/bin/diskchecker"
assert desktop["TryExec"] == "/usr/bin/diskchecker"
assert desktop["Icon"] == "diskchecker"
assert desktop["Terminal"] == "false"
ET.parse("DiskChecker.UI.Avalonia/Assets/disk-checker-icon.svg")
print("PASS: desktop, polkit identity, privileges and SVG icon")
PY

shopt -s nullglob
debs=(packages/*.deb)
rpms=(packages/*.rpm)
bundles=(packages/*installer.tar.gz)
(( ${#debs[@]} > 0 )) || { echo "No DEB package" >&2; exit 1; }
(( ${#rpms[@]} > 0 )) || { echo "No RPM package" >&2; exit 1; }
(( ${#bundles[@]} > 0 )) || { echo "No portable archive" >&2; exit 1; }

stage="$(mktemp -d)"
trap 'rm -rf -- "$stage"' EXIT
dpkg-deb -x "${debs[0]}" "$stage/deb"
for relative in \
  opt/diskchecker/DiskChecker.UI.Avalonia \
  usr/bin/diskchecker \
  usr/lib/diskchecker/diskchecker-elevated \
  usr/share/applications/diskchecker.desktop \
  usr/share/polkit-1/actions/org.afrowave.diskchecker.policy \
  usr/share/icons/hicolor/scalable/apps/diskchecker.svg; do
    [[ -f "$stage/deb/$relative" ]] || {
        echo "DEB missing: $relative" >&2; exit 1;
    }
    rpm -qpl "${rpms[0]}" | grep -Fx "/$relative" >/dev/null || {
        echo "RPM missing: $relative" >&2; exit 1;
    }
done
[[ -x "$stage/deb/usr/bin/diskchecker" ]]
[[ -x "$stage/deb/usr/lib/diskchecker/diskchecker-elevated" ]]
for portable in install-linux.sh diskchecker-launcher diskchecker-elevated \
  diskchecker.desktop org.afrowave.diskchecker.policy diskchecker.svg; do
    tar -tzf "${bundles[0]}" | grep -Fx "./$portable" >/dev/null || {
        echo "Portable archive missing: $portable" >&2; exit 1;
    }
done
depends="$(dpkg-deb -f "${debs[0]}" Depends)"
[[ "$depends" == *pkexec* && "$depends" == *polkitd* ]] || {
    echo "DEB is missing pkexec/polkitd dependency" >&2; exit 1;
}
echo "PASS: DEB, RPM and portable archive contain privileged helper, policy and icon"

#!/bin/bash
# Verifies the terminal front end: solution build (warnings are errors) plus both test suites
# and a non interactive smoke run of the terminal UI.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${PROJECT_DIR}"

export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-/tmp/nuget-http}"

# Tests write telemetry and settings below the XDG directories; keep the developer's real
# profile untouched by pointing them at a temporary location for the test runs only.
TEST_XDG_DATA="${TMPDIR:-/tmp}/diskchecker-xdg-data"
TEST_XDG_CONFIG="${TMPDIR:-/tmp}/diskchecker-xdg-config"
mkdir -p "${TEST_XDG_DATA}" "${TEST_XDG_CONFIG}"

echo "=== Build: whole solution (desktop + terminal) ==="
dotnet build DiskChecker.slnx --configuration Release -p:NuGetAudit=false -v q

echo ""
echo "=== Tests: desktop application ==="
XDG_DATA_HOME="${TEST_XDG_DATA}" XDG_CONFIG_HOME="${TEST_XDG_CONFIG}" \
  dotnet test tests/DiskChecker.Tests/DiskChecker.Tests.csproj \
  --configuration Release --no-build --no-restore

echo ""
echo "=== Tests: terminal application ==="
XDG_DATA_HOME="${TEST_XDG_DATA}" XDG_CONFIG_HOME="${TEST_XDG_CONFIG}" \
  dotnet test tests/DiskChecker.Tui.Tests/DiskChecker.Tui.Tests.csproj \
  --configuration Release --no-build --no-restore

echo ""
echo "=== Smoke run: terminal UI with closed stdin ==="
SMOKE_DB="${TMPDIR:-/tmp}/diskchecker-smoke.db"
rm -f "${SMOKE_DB}"
printf '' | XDG_CONFIG_HOME="${TEST_XDG_CONFIG}" \
  dotnet run --project DiskChecker.TUI --configuration Release --no-build -- \
  --screen about --db "${SMOKE_DB}" > /dev/null
rm -f "${SMOKE_DB}" "${SMOKE_DB}-shm" "${SMOKE_DB}-wal"

echo "OK – solution builds, both test suites pass and the TUI starts and exits cleanly."

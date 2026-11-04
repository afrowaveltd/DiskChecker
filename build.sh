#!/bin/bash
set -e

echo "=== DiskChecker Build Script ==="
echo ""

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UI_PROJECT="${PROJECT_DIR}/DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj"
PUBLISH_DIR="${PROJECT_DIR}/publish"

echo "Restoring packages..."
dotnet restore -v q

echo "Building..."
dotnet build --configuration Release -v q

echo ""
echo "=== Publishing for Windows x64 ==="
dotnet publish "${UI_PROJECT}" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output "${PUBLISH_DIR}/win-x64" \
  -v q

echo ""
echo "=== Publishing for Linux x64 ==="
dotnet publish "${UI_PROJECT}" \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --output "${PUBLISH_DIR}/linux-x64" \
  -v q

echo ""
echo "=== Publishing for Linux ARM64 ==="
dotnet publish "${UI_PROJECT}" \
  --configuration Release \
  --runtime linux-arm64 \
  --self-contained true \
  --output "${PUBLISH_DIR}/linux-arm64" \
  -v q

echo ""
echo "=== Publishing terminal version (TUI) ==="
TUI_PROJECT="${PROJECT_DIR}/DiskChecker.TUI/DiskChecker.TUI.csproj"
for RID in win-x64 linux-x64 linux-arm64; do
  dotnet publish "${TUI_PROJECT}" \
    --configuration Release \
    --runtime "${RID}" \
    --self-contained true \
    --output "${PUBLISH_DIR}/tui-${RID}" \
    -v q
done

echo ""
echo "Making executables..."
chmod +x "${PUBLISH_DIR}/linux-x64/DiskChecker.UI.Avalonia" 2>/dev/null || true
chmod +x "${PUBLISH_DIR}/linux-arm64/DiskChecker.UI.Avalonia" 2>/dev/null || true
chmod +x "${PUBLISH_DIR}/tui-linux-x64/dcheck" 2>/dev/null || true
chmod +x "${PUBLISH_DIR}/tui-linux-arm64/dcheck" 2>/dev/null || true

echo ""
echo "=== Build completed ==="
echo ""
echo "Windows: ${PUBLISH_DIR}/win-x64/DiskChecker.UI.Avalonia.exe"
echo "Linux:   ${PUBLISH_DIR}/linux-x64/DiskChecker.UI.Avalonia"
echo "ARM64:   ${PUBLISH_DIR}/linux-arm64/DiskChecker.UI.Avalonia"
echo "TUI Win: ${PUBLISH_DIR}/tui-win-x64/dcheck.exe"
echo "TUI Linux: ${PUBLISH_DIR}/tui-linux-x64/dcheck"
echo ""
echo "Note: Windows executable requires Administrator privileges (configured in app.manifest)"
echo "Note: Linux executable requires root/sudo for disk access"
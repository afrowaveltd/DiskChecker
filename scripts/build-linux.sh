#!/bin/bash
# DiskChecker Linux Build Script
# Usage: ./build-linux.sh [x64|arm64]

set -e

ARCH=${1:-x64}
PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UI_PROJECT="$PROJECT_ROOT/DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj"
OUTPUT_DIR="$PROJECT_ROOT/publish/linux-$ARCH"

echo "🚀 Building DiskChecker for Linux $ARCH..."
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Clean previous build
if [ -d "$OUTPUT_DIR" ]; then
    echo "🧹 Cleaning previous build..."
    rm -rf "$OUTPUT_DIR"
fi

# Build self-contained
echo "📦 Publishing self-contained build..."
dotnet publish "$UI_PROJECT" \
    --configuration Release \
    --runtime "linux-$ARCH" \
    --self-contained true \
    --output "$OUTPUT_DIR" \
    -p:DebugType=None \
    -p:DebugSymbols=false

# Make executable
chmod +x "$OUTPUT_DIR/DiskChecker.UI.Avalonia" 2>/dev/null || true

echo ""
echo "✅ Build completed!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📁 Output: $OUTPUT_DIR"
echo "📊 Size: $(du -sh "$OUTPUT_DIR" | cut -f1)"
echo ""
echo "💡 To run on Linux:"
echo "   sudo $OUTPUT_DIR/DiskChecker.UI.Avalonia"
echo ""
echo "⚠️  Root privileges required for disk access!"
echo ""
echo "📦 Dependencies on target system:"
echo "   - smartmontools (for SMART data)"
echo "   - lsblk (usually pre-installed)"
echo ""
echo "   Install: sudo apt install smartmontools"
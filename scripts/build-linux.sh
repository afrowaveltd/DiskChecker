#!/bin/bash
# DiskChecker Linux Build Script
# Usage: ./build-linux.sh [x64|arm64]

set -e

ARCH=${1:-x64}
PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT_DIR="$PROJECT_ROOT/publish/linux-$ARCH"

echo "🚀 Building DiskChecker for Linux $ARCH..."
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Clean previous build
if [ -d "$OUTPUT_DIR" ]; then
    echo "🧹 Cleaning previous build..."
    rm -rf "$OUTPUT_DIR"
fi

# Build self-contained single-file
echo "📦 Publishing self-contained single-file..."
dotnet publish "$PROJECT_ROOT/DiskChecker.UI/DiskChecker.UI.csproj" \
    --configuration Release \
    --runtime "linux-$ARCH" \
    --self-contained true \
    --output "$OUTPUT_DIR" \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -p:DebugType=None \
    -p:DebugSymbols=false

# Make executable
chmod +x "$OUTPUT_DIR/DiskChecker.UI"

# Display size
echo ""
echo "✅ Build completed!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📁 Output: $OUTPUT_DIR"
echo "📊 Size: $(du -h "$OUTPUT_DIR/DiskChecker.UI" | cut -f1)"
echo ""
echo "💡 To run on Linux:"
echo "   sudo $OUTPUT_DIR/DiskChecker.UI"
echo ""
echo "⚠️  Note: Root privileges required for disk access!"
echo ""
echo "📦 Dependencies required on target system:"
echo "   - smartmontools (for SMART data)"
echo "   - lsblk (usually pre-installed)"
echo ""
echo "   Install: sudo apt install smartmontools"

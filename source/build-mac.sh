#!/bin/bash
set -euo pipefail
SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"
PACKAGE_DIR="$(cd "$SOURCE_DIR/.." && pwd)"
APP_DIR="$PACKAGE_DIR/Mac/EdgeLink.app"
VERSION="$(tr -d '\r\n' < "$PACKAGE_DIR/VERSION")"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
# Swift top-level app entry needs to be named main.swift.
SCRATCH_DIR="$(mktemp -d)"
trap 'rm -rf "$SCRATCH_DIR"' EXIT
cp "$SOURCE_DIR/mac/App.swift" "$SCRATCH_DIR/main.swift"
swiftc -swift-version 5 -O -target arm64-apple-macos15.0 \
  "$SOURCE_DIR/mac/Wire.swift" "$SOURCE_DIR/mac/Input.swift" "$SOURCE_DIR/mac/Link.swift" "$SOURCE_DIR/mac/PCM.swift" "$SOURCE_DIR/mac/OutputMute.swift" "$SOURCE_DIR/mac/Audio.swift" "$SOURCE_DIR/mac/Updates.swift" "$SOURCE_DIR/mac/Discovery.swift" "$SOURCE_DIR/mac/Dashboard.swift" "$SOURCE_DIR/mac/Permissions.swift" "$SCRATCH_DIR/main.swift" \
  -o "$APP_DIR/Contents/MacOS/EdgeLink"
cp "$SOURCE_DIR/mac/Info.plist" "$APP_DIR/Contents/Info.plist"
cp "$SOURCE_DIR/assets/EdgeLink.icns" "$APP_DIR/Contents/Resources/EdgeLink.icns"
cp "$SOURCE_DIR/assets/icon-128.png" "$APP_DIR/Contents/Resources/AppIcon.png"
cp "$SOURCE_DIR/assets/status-1x.png" "$APP_DIR/Contents/Resources/EdgeLinkStatus.png"
cp "$SOURCE_DIR/assets/status-2x.png" "$APP_DIR/Contents/Resources/EdgeLinkStatus@2x.png"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP_DIR/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" "$APP_DIR/Contents/Info.plist"
# Prefer a stable signing identity so TCC grants survive rebuilds; do not embed private keys.
SIGN_IDENTITY="${EDGELINK_SIGN_IDENTITY:-}"
if [ -z "$SIGN_IDENTITY" ]; then
  SIGN_IDENTITY="$(security find-identity -v -p codesigning | sed -nE '/"(Developer ID Application|Apple Development):/s/.*\) ([A-F0-9]{40}) .*/\1/p' | head -n 1)"
fi
codesign --force --sign "${SIGN_IDENTITY:--}" "$APP_DIR"
printf 'Built %s\n' "$APP_DIR"

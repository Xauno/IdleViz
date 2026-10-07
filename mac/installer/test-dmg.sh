#!/bin/zsh
# Checks mac/artifacts/IdleViz.dmg: mounts it and looks at what a person who downloads it would get.
# Usage: mac/installer/test-dmg.sh [version]     With a version, the app must say the same.
set -euo pipefail
cd "${0:A:h}/.."

DMG="artifacts/IdleViz.dmg"
WANTED_VERSION="${1:-}"
MOUNT="$(mktemp -d)"
FAILED=0

check() {
  if eval "$2" >/dev/null 2>&1; then
    echo "  ok    $1"
  else
    echo "  FAIL  $1"
    FAILED=1
  fi
}

[[ -f "$DMG" ]] || { echo "$DMG isn't there. Run mac/installer/build-dmg.sh first." >&2; exit 1; }
hdiutil attach "$DMG" -nobrowse -readonly -mountpoint "$MOUNT" >/dev/null
trap 'hdiutil detach "$MOUNT" -quiet || true; rmdir "$MOUNT" 2>/dev/null || true' EXIT

APP="$MOUNT/IdleViz.app"
PLIST="$APP/Contents/Info.plist"
value() { /usr/libexec/PlistBuddy -c "Print :$1" "$PLIST"; }

echo "Checking $DMG…"
check "the app is in the image" '[[ -d "$APP" ]]'
check "Applications points at /Applications" '[[ "$(readlink "$MOUNT/Applications")" == "/Applications" ]]'
check "the window has its background and layout" '[[ -f "$MOUNT/.background.png" && -f "$MOUNT/.DS_Store" ]]'
check "the image has its own icon" '[[ -f "$MOUNT/.VolumeIcon.icns" ]]'
check "the bundle is com.xauno.IdleViz" '[[ "$(value CFBundleIdentifier)" == "com.xauno.IdleViz" ]]'
check "the app has its icon" '[[ -f "$APP/Contents/Resources/$(value CFBundleIconFile).icns" ]]'
check "the page is in the app" '[[ -f "$APP/Contents/Resources/web/index.html" ]]'
check "it runs on Apple silicon and Intel" '[[ "$(lipo -archs "$APP/Contents/MacOS/IdleViz" | tr " " "\n" | sort | xargs)" == "arm64 x86_64" ]]'
check "the signature is whole" 'codesign --verify --deep --strict "$APP"'
check "the hardened runtime is on" '[[ "$(codesign -dv "$APP" 2>&1)" == *"flags="*"runtime"* ]]'
ENTITLEMENTS="$(codesign -d --entitlements - --xml "$APP" 2>/dev/null || true)"
check "it may control Spotify and use the microphone" '[[ "$ENTITLEMENTS" == *apple-events* && "$ENTITLEMENTS" == *audio-input* ]]'
if [[ -n "$WANTED_VERSION" ]]; then
  check "the version is $WANTED_VERSION" '[[ "$(value CFBundleShortVersionString)" == "$WANTED_VERSION" ]]'
fi

if (( FAILED )); then
  echo "✗ $DMG has problems."
  exit 1
fi
echo "✓ $DMG is fine (IdleViz $(value CFBundleShortVersionString))."

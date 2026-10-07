#!/bin/zsh
# Builds IdleViz.dmg: the disk image people download, with the app and a shortcut to Applications.
# Usage: installer/build-dmg.sh        The result is artifacts/IdleViz.dmg.
#
# The app in it runs on Apple silicon and Intel and is signed ad hoc, meaning with no certificate:
# the project has no Developer ID and the image isn't notarized. So on another Mac, macOS blocks
# the first open until it is allowed under System Settings → Privacy & Security. For your own Mac,
# IdleViz.command installs a copy signed with your own certificate instead.
set -euo pipefail
cd "${0:A:h}/.."

DERIVED=".build/dmg"
APP="$DERIVED/Build/Products/Release/IdleViz.app"
OUT="artifacts/IdleViz.dmg"
VENV=".build/dmg-venv"

command -v xcodebuild >/dev/null || { echo "xcodebuild not found. Install Xcode." >&2; exit 1; }

echo "Building IdleViz (Release, Apple silicon and Intel)…"
xcodebuild build -project IdleViz.xcodeproj -scheme IdleViz -configuration Release \
  -destination "generic/platform=macOS" -derivedDataPath "$DERIVED" -quiet \
  ARCHS="arm64 x86_64" ONLY_ACTIVE_ARCH=NO \
  CODE_SIGN_STYLE=Manual CODE_SIGN_IDENTITY=- DEVELOPMENT_TEAM=
[[ -d "$APP" ]] || { echo "The build finished but $APP isn't there." >&2; exit 1; }

# dmgbuild writes the window's layout itself, so no Finder has to be scripted and it works in CI.
if [[ ! -x "$VENV/bin/dmgbuild" ]]; then
  echo "Installing dmgbuild…"
  python3 -m venv "$VENV"
  "$VENV/bin/pip" install --quiet "dmgbuild==1.6.7"
fi

echo "Making $OUT…"
mkdir -p artifacts
rm -f "$OUT"
"$VENV/bin/dmgbuild" -s installer/dmg-settings.py -D app="$APP" -D background=installer/background.png "IdleViz" "$OUT"
echo "✓ $OUT ($(du -h "$OUT" | cut -f1 | tr -d ' '))"

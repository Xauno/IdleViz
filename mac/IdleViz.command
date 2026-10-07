#!/bin/zsh
# Builds IdleViz, installs it in /Applications and opens it.
# Double-click this file in Finder, or run it from Terminal.

cd "${0:A:h}" || exit 1

APP_NAME="IdleViz.app"
BUNDLE_ID="com.xauno.IdleViz"
BUILT=".build/xcode/Build/Products/Release/$APP_NAME"
INSTALLED="/Applications/$APP_NAME"

fail() {
  echo
  echo "✗ $1"
  exit 1
}

bundle_id() {
  /usr/libexec/PlistBuddy -c "Print :CFBundleIdentifier" "$1/Contents/Info.plist" 2>/dev/null
}

command -v xcodebuild >/dev/null || fail "xcodebuild not found. Install Xcode (the full app, not just the Command Line Tools)."
[[ -f Config/Local.xcconfig ]] || fail "mac/Config/Local.xcconfig is missing. Copy mac/Config/Local.example.xcconfig and set DEVELOPMENT_TEAM (see the README, Installation)."

echo "Building IdleViz (Release)…"
xcodebuild build -project IdleViz.xcodeproj -scheme IdleViz -configuration Release \
  -destination "platform=macOS,arch=$(uname -m)" -derivedDataPath .build/xcode -quiet || fail "The build failed. The errors are above."
[[ -d "$BUILT" ]] || fail "The build finished but $BUILT isn't there."

# Never replace something in /Applications that isn't IdleViz.
if [[ -e "$INSTALLED" && "$(bundle_id "$INSTALLED")" != "$BUNDLE_ID" ]]; then
  fail "$INSTALLED exists but isn't IdleViz ($BUNDLE_ID). Move it away and run this again."
fi

# A running copy would keep using the old files, so quit it first.
if pgrep -xq IdleViz; then
  echo "Quitting the running IdleViz…"
  pkill -x IdleViz
  for _ in {1..50}; do
    pgrep -xq IdleViz || break
    sleep 0.1
  done
  pgrep -xq IdleViz && fail "IdleViz didn't quit. Quit it and run this again."
fi

echo "Installing to $INSTALLED…"
# Copy next to the old one first, so a failed copy leaves the installed app untouched.
STAGED="/Applications/.$APP_NAME.new"
rm -rf "$STAGED"
ditto "$BUILT" "$STAGED" || fail "Couldn't copy the app into /Applications."
rm -rf "$INSTALLED"
mv "$STAGED" "$INSTALLED" || fail "Couldn't put the new app in place."

echo "Opening IdleViz…"
open "$INSTALLED" || fail "Couldn't open $INSTALLED."

echo
echo "✓ IdleViz is running. It has no Dock icon: look for the waveform in the menu bar."

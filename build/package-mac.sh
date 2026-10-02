#!/usr/bin/env bash
# Builds Trainer.app for macOS (self-contained: no .NET install needed on the Mac).
#   ./build/package-mac.sh            # Apple Silicon (M1/M2/M3/M4 Mac mini)
#   ./build/package-mac.sh osx-x64    # Intel Mac
# Output: publish/mac/Trainer.app and publish/Trainer-<rid>.zip
set -euo pipefail
RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/publish/mac"
STAGE="$OUT/stage-$RID"
APP="$OUT/Trainer.app"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/Trainer.Desktop/Trainer.Desktop.csproj")"

rm -rf "$STAGE" "$APP"
dotnet publish "$ROOT/src/Trainer.Desktop/Trainer.Desktop.csproj" -c Release -r "$RID" --self-contained true \
  -p:UseAppHost=true -p:DebugType=none -o "$STAGE"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$STAGE/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Trainer"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Training Planner</string>
  <key>CFBundleDisplayName</key><string>Training Planner</string>
  <key>CFBundleIdentifier</key><string>com.trainingplanner.desktop</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>Trainer</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST
rm -rf "$STAGE"

if [[ "$(uname)" == "Darwin" ]]; then
  # Ad-hoc signature: required for Apple Silicon to run it. Not notarised, so the first launch needs
  # right-click → Open (see README).
  codesign --force --deep --sign - "$APP"
  (cd "$OUT" && ditto -c -k --keepParent Trainer.app "$ROOT/publish/Trainer-$RID.zip")
else
  (cd "$OUT" && zip -qry "$ROOT/publish/Trainer-$RID.zip" Trainer.app)
  echo "Note: built off a Mac, so not signed. Run 'codesign --force --deep --sign - Trainer.app' on the Mac."
fi
echo "Done: $APP"

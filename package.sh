#!/usr/bin/env bash
# Packages a player release: dist/olcoop-<version>.zip with the mod DLL and the installer files.
# Run ./build.sh first. The zip contains no game or olmod files.
set -euo pipefail
cd "$(dirname "$0")"
read VER PHASE < VERSION
NAME="olcoop-$VER-$PHASE"
[ -f build/Mod-olcoop.dll ] || { echo "build/Mod-olcoop.dll missing - run ./build.sh first" >&2; exit 1; }
STAGE=$(mktemp -d)
mkdir -p "$STAGE/$NAME" dist
cp build/Mod-olcoop.dll "$STAGE/$NAME/"
cp installer/install.bat installer/uninstall.bat installer/olcoop.bat installer/olcoop-steamvr.bat installer/olcoop-oculus.bat installer/find-overload.ps1 \
   installer/olcoop.ico installer/README.txt "$STAGE/$NAME/"
rm -f "dist/$NAME.zip"
(cd "$STAGE" && zip -qr "$OLDPWD/dist/$NAME.zip" "$NAME")
rm -rf "$STAGE"
echo "Packaged dist/$NAME.zip"

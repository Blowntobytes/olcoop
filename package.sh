#!/usr/bin/env bash
# Packages a player release: dist/olcoop-<version>.zip = installer files + olcoop\ (Mod-olcoop.dll, launchers and the bundled,
# unmodified olmod 0.5.14 from third_party/, checked against its SHA1SUMS). Run ./build.sh first. The zip contains no game files.
set -euo pipefail
cd "$(dirname "$0")"
read VER PHASE < VERSION
NAME="olcoop-$VER-$PHASE"
[ -f build/Mod-olcoop.dll ] || { echo "build/Mod-olcoop.dll missing - run ./build.sh first" >&2; exit 1; }
STAGE=$(mktemp -d)
mkdir -p "$STAGE/$NAME" dist
(cd third_party/olmod-0.5.14 && sha1sum -c --quiet SHA1SUMS) || { echo "bundled olmod does not match SHA1SUMS" >&2; exit 1; }
mkdir -p "$STAGE/$NAME/olcoop"
cp installer/install.bat installer/uninstall.bat installer/find-overload.ps1 installer/README.txt "$STAGE/$NAME/"
cp build/Mod-olcoop.dll installer/olcoop.bat installer/olcoop-steamvr.bat installer/olcoop-oculus.bat installer/olcoop.ico "$STAGE/$NAME/olcoop/"
cp third_party/olmod-0.5.14/{olmod.exe,GameMod.dll,0Harmony.dll,olmod-LICENSE.txt,olmod-README.txt,olmodchanges.txt} "$STAGE/$NAME/olcoop/"
rm -f "dist/$NAME.zip"
(cd "$STAGE" && zip -qr "$OLDPWD/dist/$NAME.zip" "$NAME")
rm -rf "$STAGE"
echo "Packaged dist/$NAME.zip"

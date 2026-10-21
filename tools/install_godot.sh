#!/usr/bin/env bash
# Installs the Godot editor binary (checksum verified) into $GODOT_BIN_DIR (default ~/godot).
# Used by the cloud-session startup hook and by CI. Safe to run repeatedly.
#
#   tools/install_godot.sh                      # Godot only
#   tools/install_godot.sh --windows-templates  # also the Windows export templates (CI)
set -euo pipefail
VERSION="${GODOT_VERSION:-4.7.2}"
BIN_DIR="${GODOT_BIN_DIR:-$HOME/godot}"
TEMPLATES_DIR="$HOME/.local/share/godot/export_templates/${VERSION}.stable"
BASE="https://github.com/godotengine/godot/releases/download/${VERSION}-stable"
WANT_TEMPLATES=false
[[ "${1:-}" == "--windows-templates" ]] && WANT_TEMPLATES=true

have_godot() { [[ -x "$BIN_DIR/godot" ]] && "$BIN_DIR/godot" --version 2>/dev/null | grep -q "^${VERSION}.stable"; }
have_templates() { [[ -f "$TEMPLATES_DIR/windows_release_x86_64.exe" ]]; }

if have_godot && { ! $WANT_TEMPLATES || have_templates; }; then
  echo "Godot $VERSION already installed in $BIN_DIR"
  exit 0
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cd "$WORK"
curl -fsSL --retry 3 -o SHA512-SUMS.txt "$BASE/SHA512-SUMS.txt"

fetch() { # file name inside the release
  curl -fsSL --retry 3 -o "$1" "$BASE/$1"
  grep -E "  $1\$" SHA512-SUMS.txt | sha512sum -c - >/dev/null
  echo "Downloaded and verified $1"
}

if ! have_godot; then
  ZIP="Godot_v${VERSION}-stable_linux.x86_64.zip"
  fetch "$ZIP"
  mkdir -p "$BIN_DIR"
  unzip -q -o "$ZIP"
  mv -f "Godot_v${VERSION}-stable_linux.x86_64" "$BIN_DIR/godot"
  chmod +x "$BIN_DIR/godot"
fi

if $WANT_TEMPLATES && ! have_templates; then
  TPZ="Godot_v${VERSION}-stable_export_templates.tpz"
  fetch "$TPZ"
  mkdir -p "$TEMPLATES_DIR"
  # Only the Windows 64-bit templates are needed; the full set is over 1 GB.
  unzip -q -o -j "$TPZ" templates/version.txt templates/windows_release_x86_64.exe \
    templates/windows_release_x86_64_console.exe -d "$TEMPLATES_DIR"
fi

"$BIN_DIR/godot" --version

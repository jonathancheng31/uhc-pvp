#!/usr/bin/env bash
# Installs Godot (.NET edition, checksum verified) and the .NET SDK. Used by the
# cloud-session startup hook and by CI. Safe to run repeatedly.
#
#   tools/install_godot.sh                      # Godot .NET + .NET SDK
#   tools/install_godot.sh --windows-templates  # also the Windows export templates
#   tools/install_godot.sh --skip-dotnet        # don't install the .NET SDK (CI installs it separately)
#
# Godot ends up at $GODOT_DIR/godot (default ~/godot-mono). The .NET SDK goes to
# $DOTNET_INSTALL_DIR (default /usr/share/dotnet if writable, else ~/.dotnet).
set -euo pipefail
VERSION="${GODOT_VERSION:-4.7.2}"
DOTNET_CHANNEL="${DOTNET_CHANNEL:-10.0}"
GODOT_DIR="${GODOT_DIR:-$HOME/godot-mono}"
TEMPLATES_DIR="$HOME/.local/share/godot/export_templates/${VERSION}.stable.mono"
BASE="https://github.com/godotengine/godot/releases/download/${VERSION}-stable"
WANT_TEMPLATES=false
WANT_DOTNET=true
for arg in "$@"; do
  case "$arg" in
    --windows-templates) WANT_TEMPLATES=true ;;
    --skip-dotnet) WANT_DOTNET=false ;;
    *) echo "unknown option $arg" >&2; exit 2 ;;
  esac
done
if [[ -z "${DOTNET_INSTALL_DIR:-}" ]]; then
  if [[ -w /usr/share ]]; then DOTNET_INSTALL_DIR=/usr/share/dotnet; else DOTNET_INSTALL_DIR="$HOME/.dotnet"; fi
fi

have_godot() { [[ -x "$GODOT_DIR/godot" ]] && "$GODOT_DIR/godot" --version 2>/dev/null | grep -q "^${VERSION}.stable.mono"; }
have_templates() { [[ -f "$TEMPLATES_DIR/windows_release_x86_64.exe" ]]; }
have_dotnet() { [[ -x "$DOTNET_INSTALL_DIR/dotnet" ]] && "$DOTNET_INSTALL_DIR/dotnet" --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL%%.*}\."; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

fetch() { # file name inside the Godot release; verified against SHA512-SUMS.txt
  [[ -f "$WORK/SHA512-SUMS.txt" ]] || curl -fsSL --retry 3 -o "$WORK/SHA512-SUMS.txt" "$BASE/SHA512-SUMS.txt"
  curl -fsSL --retry 3 -o "$WORK/$1" "$BASE/$1"
  (cd "$WORK" && grep -E "  $1\$" SHA512-SUMS.txt | sha512sum -c - >/dev/null)
  echo "Downloaded and verified $1"
}

if $WANT_DOTNET && ! have_dotnet; then
  curl -fsSL --retry 3 -o "$WORK/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh
  bash "$WORK/dotnet-install.sh" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_INSTALL_DIR" >/dev/null
  echo "Installed .NET SDK $("$DOTNET_INSTALL_DIR/dotnet" --version) to $DOTNET_INSTALL_DIR"
fi

if ! have_godot; then
  ZIP="Godot_v${VERSION}-stable_mono_linux_x86_64.zip"
  fetch "$ZIP"
  (cd "$WORK" && unzip -q "$ZIP")
  rm -rf "$GODOT_DIR"
  mv "$WORK/Godot_v${VERSION}-stable_mono_linux_x86_64" "$GODOT_DIR"
  mv "$GODOT_DIR/Godot_v${VERSION}-stable_mono_linux.x86_64" "$GODOT_DIR/godot"
  chmod +x "$GODOT_DIR/godot"
fi

if $WANT_TEMPLATES && ! have_templates; then
  TPZ="Godot_v${VERSION}-stable_mono_export_templates.tpz"
  fetch "$TPZ"
  mkdir -p "$TEMPLATES_DIR"
  # Only the Windows 64-bit templates are needed; the full set is over 1 GB.
  unzip -q -o -j "$WORK/$TPZ" templates/version.txt templates/windows_release_x86_64.exe \
    templates/windows_release_x86_64_console.exe -d "$TEMPLATES_DIR"
  rm -f "$WORK/$TPZ"
fi

echo "Godot $("$GODOT_DIR/godot" --version) at $GODOT_DIR/godot"

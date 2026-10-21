#!/bin/bash
# Cloud-session setup: installs the .NET SDK and Godot (.NET edition) so the build, tests
# and smoke test can run, then builds the C# project and imports it into Godot.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "$CLAUDE_PROJECT_DIR"
export GODOT_DIR="$HOME/godot-mono"
export DOTNET_INSTALL_DIR="/usr/share/dotnet"
[ -w /usr/share ] || DOTNET_INSTALL_DIR="$HOME/.dotnet"
tools/install_godot.sh

# Put godot and dotnet on PATH for this session.
if [ -w /usr/local/bin ]; then
  ln -sf "$GODOT_DIR/godot" /usr/local/bin/godot
  ln -sf "$DOTNET_INSTALL_DIR/dotnet" /usr/local/bin/dotnet
fi
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_INSTALL_DIR\""
    echo "export PATH=\"$GODOT_DIR:$DOTNET_INSTALL_DIR:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
  } >> "$CLAUDE_ENV_FILE"
fi
export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
export PATH="$GODOT_DIR:$DOTNET_INSTALL_DIR:$PATH"

dotnet build UhcPvp.csproj --nologo -v quiet -clp:ErrorsOnly >/dev/null || { echo "C# build failed; run: dotnet build" >&2; exit 1; }
godot --headless --path . --import >/dev/null 2>&1 || { echo "Godot import failed; run: godot --headless --path . --import" >&2; exit 1; }
echo "Godot .NET and .NET SDK ready; project built and imported."

# Where the project stands, so every session starts with it in view.
section() { awk -v h="$2" '$0 == h { on = 1; next } on && /^## / { exit } on' "$1"; }
echo
echo "## Current status / next up (docs/design.md)"
section docs/design.md "## Current status / next up"
echo "## Open playtest feedback (docs/playtest-notes.md)"
section docs/playtest-notes.md "## Open"
echo "Commands: /next-milestone, /ship, /screenshot, /playtest (see CLAUDE.md, \"How we work\")."

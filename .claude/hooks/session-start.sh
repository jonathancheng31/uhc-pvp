#!/bin/bash
# Cloud-session setup: installs Godot so tests and the smoke test can run,
# then imports the project (builds Godot's class cache in .godot/).
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "$CLAUDE_PROJECT_DIR"
export GODOT_BIN_DIR="$HOME/godot"
tools/install_godot.sh

# Put godot on PATH for this session.
if [ -w /usr/local/bin ]; then
  ln -sf "$GODOT_BIN_DIR/godot" /usr/local/bin/godot
fi
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  echo "export PATH=\"$GODOT_BIN_DIR:\$PATH\"" >> "$CLAUDE_ENV_FILE"
fi

"$GODOT_BIN_DIR/godot" --headless --path . --import >/dev/null 2>&1 || {
  echo "Godot import failed; run: godot --headless --path . --import" >&2
  exit 1
}
echo "Godot ready; project imported."

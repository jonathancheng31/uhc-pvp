#!/usr/bin/env bash
# Runs the unit tests headless. Exit code is non-zero if any test fails.
set -euo pipefail
GODOT="${GODOT:-godot}"
cd "$(dirname "$0")/.."
"$GODOT" --headless --path . res://tests/test_runner.tscn

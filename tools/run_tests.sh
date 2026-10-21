#!/usr/bin/env bash
# Builds the C# project and runs the unit tests headless. Non-zero exit if anything fails.
# Pass a filter to run only matching tests: tools/run_tests.sh Movement
set -euo pipefail
GODOT="${GODOT:-godot}"
cd "$(dirname "$0")/.."
dotnet build UhcPvp.csproj --nologo -v quiet -clp:ErrorsOnly
"$GODOT" --headless --path . res://tests/test_runner.tscn ${1:+-- --filter="$1"}

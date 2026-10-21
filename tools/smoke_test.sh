#!/usr/bin/env bash
# Multiplayer smoke test: a host and two bot players run headless on this
# machine (one with simulated lag), and each must see the other two moving.
set -uo pipefail
GODOT="${GODOT:-godot}"
PORT="${PORT:-27777}"
cd "$(dirname "$0")/.."
dotnet build UhcPvp.csproj --nologo -v quiet -clp:ErrorsOnly
LOGS="$(mktemp -d)"

run() { # name, extra args...
  local name="$1"; shift
  "$GODOT" --headless --path . -- --name="$name" --bot --port="$PORT" "$@" > "$LOGS/$name.log" 2>&1
}

run Host --host --quit-after=9 &
run BotA --join=127.0.0.1 --quit-after=8 &
run BotB --join=127.0.0.1 --ping=120 --jitter=20 --loss=2 --quit-after=8 &
wait

status=0
for name in Host BotA BotB; do
  line="$(grep '^SMOKE' "$LOGS/$name.log" || true)"
  echo "$name: ${line:-<no report>}"
  if [[ "$line" != *"seen=2 moving=2"* ]]; then
    status=1
    echo "---- $name log ----"; cat "$LOGS/$name.log"
  fi
done
if grep -l "SCRIPT ERROR" "$LOGS"/*.log; then
  echo "Script errors found in the logs above"; status=1
fi
[[ $status -eq 0 ]] && echo "Smoke test passed" || echo "Smoke test FAILED"
exit $status

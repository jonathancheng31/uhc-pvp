#!/usr/bin/env bash
# Multiplayer smoke test. A host and two network players run headless on this machine, all
# played by the fighting bot AI; one has simulated lag. The host also adds a practice
# dummy that chases without hitting. Teams: host + BotA (Red) vs dummy + BotB (Blue).
# Every player must see the other three and land sword and rod hits, which exercises every
# combat path: host hitting its own bot, a client hitting a host-run bot, a client hitting the
# host, and a client hitting another client through the host. The host also turns on every
# twist (strafe speed 130%, grip 50%, wasted clicks 0.15 s, player pushing 100%, combo
# knockback 80%, a 200 ms head start, a 0.5 s rod cooldown), which every player must get from it.
set -uo pipefail
GODOT="${GODOT:-godot}"
PORT="${PORT:-27777}"
cd "$(dirname "$0")/.."
dotnet build UhcPvp.csproj --nologo -v quiet -clp:ErrorsOnly
LOGS="${LOGS:-$(mktemp -d)}"

run() { # name, extra args...
  local name="$1"; shift
  "$GODOT" --headless --path . -- --name="$name" --bot=fight --port="$PORT" "$@" > "$LOGS/$name.log" 2>&1
}

run Host --host --dummies=1 --dummy-mode=chase --strafe=130 --grip=50 --wasted-clicks=0.15 --push=100 --combo-kb=80 --head-start=200 --rod-cooldown=0.5 --quit-after=15 &
run BotA --join=127.0.0.1 --quit-after=14 &
run BotB --join=127.0.0.1 --ping=120 --jitter=20 --loss=2 --quit-after=14 &
wait

status=0
for name in Host BotA BotB; do
  line="$(grep '^SMOKE' "$LOGS/$name.log" || true)"
  echo "$name: ${line:-<no report>}"
  landed="$(sed -n 's/.* landed=\([0-9]*\).*/\1/p' <<< "$line")"
  rods="$(sed -n 's/.* rods=\([0-9]*\).*/\1/p' <<< "$line")"
  if [[ "$line" != *"seen=3"* || "$line" != *"strafe=130% grip=50% wasted=0.15 push=100% combo=80% head=200 ms rod_cooldown=0.50"* || "${landed:-0}" -lt 1 || "${rods:-0}" -lt 1 ]]; then
    status=1
    echo "---- $name log ----"; grep -v "ALSA\|audio" "$LOGS/$name.log" | tail -40
  fi
done
if grep -lE "SCRIPT ERROR|Unhandled exception|System\.[A-Za-z]*Exception" "$LOGS"/*.log; then
  echo "Errors found in the logs above"; status=1
fi
[[ $status -eq 0 ]] && echo "Smoke test passed" || echo "Smoke test FAILED"
exit $status

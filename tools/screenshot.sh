#!/usr/bin/env bash
# Screenshots of the running game on a virtual display, so a cloud session can see it.
#
#   tools/screenshot.sh [--out=DIR] [--at=SEC[,SEC...]] [--size=WxH] [--host-args="..."] [-- GAME OPTIONS]
#
# --out        Folder for the PNGs and the game log (default /tmp/uhc-shots).
# --at         When to shoot, in seconds after the match starts (default 3). Several times
#              (--at=2,2.1,2.2) give a sequence, handy for animations.
# --size       Window size (default 1280x720).
# --host-args  Also run a headless host with these options; the game you see joins it.
#              Use this to look at other players through the network (with --ping etc.).
# GAME OPTIONS are launch options (scripts/Autoload/LaunchArgs.cs). Default: --practice --bot=fight,
# which is you fighting a practice bot, seen through your eyes. Pass --name=Me alone for the main menu.
#
# Prints the saved PNG paths. Examples:
#   tools/screenshot.sh --at=3,3.1,3.2 -- --practice --bot=fight --dummy-mode=fight
#   tools/screenshot.sh --host-args="--dummies=1 --dummy-mode=fight" -- --join=127.0.0.1 --bot=fight --ping=150
set -euo pipefail
GODOT="${GODOT:-godot}"
cd "$(dirname "$0")/.."

out="/tmp/uhc-shots"; at="3"; size="1280x720"; host_args=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --out=*) out="${1#*=}" ;;
    --at=*) at="${1#*=}" ;;
    --size=*) size="${1#*=}" ;;
    --host-args=*) host_args="${1#*=}" ;;
    --) shift; break ;;
    *) echo "Unknown option: $1 (game options go after --)" >&2; exit 2 ;;
  esac
  shift
done
game_args=("$@")
[[ ${#game_args[@]} -eq 0 ]] && game_args=(--practice --bot=fight)

mkdir -p "$out"
stamp="shot_$(date +%H%M%S)_$$"
last="$(tr ',' '\n' <<< "$at" | sort -g | tail -1)"
quit="$(awk -v t="$last" 'BEGIN { print t + 1.5 }')"
port=$((30000 + RANDOM % 20000))

dotnet build UhcPvp.csproj --nologo -v quiet -clp:ErrorsOnly >/dev/null

host_pid=""
if [[ -n "$host_args" ]]; then
  # shellcheck disable=SC2086 # host_args is a list of options
  "$GODOT" --headless --path . -- --host --name=Host --port="$port" $host_args \
    --quit-after="$(awk -v t="$quit" 'BEGIN { print t + 3 }')" > "$out/${stamp}_host.log" 2>&1 &
  host_pid=$!
  sleep 1
fi

xvfb-run -a -s "-screen 0 ${size}x24" "$GODOT" --path . --rendering-driver opengl3 --resolution "$size" -- \
  --port="$port" "${game_args[@]}" --screenshot="$out/$stamp.png" --screenshot-at="$at" --quit-after="$quit" \
  > "$out/$stamp.log" 2>&1 || true
[[ -n "$host_pid" ]] && { kill "$host_pid" 2>/dev/null || true; wait "$host_pid" 2>/dev/null || true; }

shopt -s nullglob
shots=()
for f in "$out/$stamp.png" "$out/${stamp}"_*.png; do
  [[ -f "$f" ]] && shots+=("$f")
done
if [[ ${#shots[@]} -eq 0 ]]; then
  echo "No screenshots saved. End of the game log ($out/$stamp.log):" >&2
  grep -v "ALSA\|audio" "$out/$stamp.log" | tail -30 >&2
  exit 1
fi
if grep -qE "SCRIPT ERROR|Unhandled exception|System\.[A-Za-z]*Exception" "$out/$stamp.log"; then
  echo "Warning: errors in the game log $out/$stamp.log" >&2
fi
printf '%s\n' "${shots[@]}"

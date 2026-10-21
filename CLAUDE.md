# UHC PvP

First-person, block-world, team vs team PvP (1v1 up to 6v6) inspired by Minecraft 1.8.9 /
Hypixel UHC Duels combat. Built in Godot 4.7 with GDScript. Long-term goal: Steam release.

Read these first; they hold the plan and every decision made so far:
- `docs/design.md`: vision, decisions, milestones, open questions
- `docs/netcode.md`: how lag is handled and why

## Working with the owner

- **Quality over build cost.** The owner cares about the final result and its impact, not
  how long or expensive something is to build. When one option is better for the game but
  takes longer or costs more, always choose it. Don't cut corners to save effort.
- The owner doesn't read or edit code. They talk in chat, play the Windows builds and
  give feedback. Explain things in plain language, without jargon.
- Claude has full control of changes: **commit and push straight to `main`**. No pull
  requests, no review requests.
- Every push to `main` builds a Windows version, published at
  https://github.com/jonathancheng31/uhc-pvp/releases/download/dev-build/UHC-PvP-Windows.zip
  Check the Build workflow passed before telling the owner a new build is ready.
- When a decision changes, update `docs/design.md` (and `docs/netcode.md` if relevant) in the same commit.

## Commands

Cloud sessions run `.claude/hooks/session-start.sh` at startup, which installs Godot
(`tools/install_godot.sh`, checksum verified) and imports the project.

```bash
godot --headless --path . --import   # after adding assets or class_name scripts
tools/run_tests.sh                   # unit tests (tests/test_*.gd), incl. a check that every script compiles
tools/smoke_test.sh                  # host + 2 bots (one laggy) must all see each other moving
python3 tools/gen_textures.py        # rebuild the block texture atlas, then re-import
```

Run the game with launch options after `--` (full list in `scripts/autoload/launch_args.gd`):

```bash
godot --path . -- --host --name=Alice
godot --path . -- --join=127.0.0.1 --name=Bot --bot --ping=120 --jitter=20 --loss=2
```

To see the game in a cloud session, render to a virtual display and save a screenshot:

```bash
xvfb-run -a -s "-screen 0 1280x720x24" godot --path . --rendering-driver opengl3 \
  -- --host --screenshot=/tmp/shot.png --screenshot-at=3 --quit-after=4
```

Windows export (needs export templates installed): `godot --headless --path . --export-release "Windows Desktop" build/windows/UHC-PvP.exe`

## Layout

- `scripts/autoload/`: `LaunchArgs` (command-line options), `Controls` (input actions in code),
  `Net` (ENet host/join, messages, clock sync, lag simulator), `Session` (roster, teams, scene changes)
- `scripts/core/`: engine-independent logic with unit tests: `Tuning` (every gameplay number),
  `PlayerState`, `SnapshotBuffer`, `NetClock`, `LagSimulator`
- `scripts/world/`: `VoxelWorld` (block grid), `ChunkMesher`, `WorldRenderer`, `Arena`, `Blocks`
- `scripts/game/`: `game.gd` (match scene logic), `Player`, `PlayerModel`
- `scripts/ui/`: main menu and in-match HUD, both built in code
- `tests/`: `TestCase` base class and `test_*.gd` files, run by `tests/test_runner.tscn`
- `tools/`: test scripts, texture generator, Godot installer (used by CI and the startup hook)

## Conventions

- Every gameplay number goes in `scripts/core/tuning.gd`, starting from the 1.8.9 value.
  Minecraft runs 20 ticks/s and we run 60; constants named `*_PER_MC_TICK` keep Minecraft units.
- Units: blocks and seconds. Yaw/pitch in degrees; yaw 0 faces -Z, positive yaw turns left,
  positive pitch looks up.
- Block (x, y, z) fills the cube from (x, y, z) to (x+1, y+1, z+1). Players stand on `Tuning.ARENA_FLOOR_Y`.
- Network messages are Arrays starting with a `Net.Msg` value; validate sizes and types on receipt.
- Static typing everywhere in GDScript; tabs for indentation.
- Put logic that can be tested without a scene into `scripts/core/` and add tests for it.
  Multiplayer changes must keep `tools/smoke_test.sh` passing.
- Commit Godot's generated `*.uid` and `*.import` files. Never commit `.godot/` or `build/`.
- Art and sound must be our own; nothing copied from Minecraft.

# UHC PvP

First-person, block-world, team vs team PvP (1v1 up to 6v6) inspired by Minecraft 1.8.9 /
Hypixel UHC Duels combat. Built in Godot 4.7 (.NET edition) with C# on .NET 10. Long-term goal: Steam release.

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

Cloud sessions run `.claude/hooks/session-start.sh` at startup, which installs the .NET 10 SDK
and Godot 4.7.2 .NET (`tools/install_godot.sh`, checksum verified), builds and imports the project.

```bash
dotnet build UhcPvp.csproj            # compile; warnings are errors (nullable + .NET analyzers)
dotnet format UhcPvp.csproj           # fix formatting; CI runs it with --verify-no-changes
tools/run_tests.sh [Filter]          # build + unit tests (tests/*Tests.cs), optionally only matching names
tools/smoke_test.sh                  # host + 2 bots (one laggy) must all see each other moving
godot --headless --path . --import   # after adding assets (textures etc.)
python3 tools/gen_textures.py        # rebuild the block texture atlas, then re-import
```

Run the game with launch options after `--` (full list in `scripts/Autoload/LaunchArgs.cs`):

```bash
godot --path . -- --host --name=Alice
godot --path . -- --join=127.0.0.1 --name=Bot --bot --ping=120 --jitter=20 --loss=2
```

To see the game in a cloud session, render to a virtual display and save a screenshot:

```bash
xvfb-run -a -s "-screen 0 1280x720x24" godot --path . --rendering-driver opengl3 \
  -- --host --screenshot=/tmp/shot.png --screenshot-at=3 --quit-after=4
```

Windows export (needs the .NET export templates, see `tools/install_godot.sh --windows-templates`):
`godot --headless --path . --export-release "Windows Desktop" build/windows/UHC-PvP.exe`.
It produces `UHC-PvP.exe` plus a `data_UhcPvp_windows_x86_64` folder (the .NET runtime); ship both.

## Layout

- `scripts/Autoload/`: `LaunchArgs` (command-line options), `Controls` (input actions in code),
  `Network` (ENet host/join, messages, clock sync, lag simulator), `Session` (roster, teams, scene changes)
- `scripts/Core/`: engine-light logic with unit tests: `Tuning` (every gameplay number),
  `PlayerState`, `SnapshotBuffer`, `NetClock`, `LagSimulator`, `Team`
- `scripts/Movement/`: physics in doubles: `Vec3d`, `Box` (Minecraft's collision math),
  `MotionModel` (20-tick to 60-tick conversion), `PlayerMotor`
- `scripts/Net/Messages.cs`: every network message as a typed record with its binary format
- `scripts/World/`: `VoxelWorld` (block grid), `ChunkMesher`, `WorldRenderer`, `Arena`, `Blocks`
- `scripts/Gameplay/`: `Game` (match scene logic), `Player`, `PlayerModel`
- `scripts/Ui/`: main menu and in-match HUD, both built in code
- `tests/`: `TestCase` base class, `*Tests.cs` suites, `McReference` (exact Minecraft 1.8.9
  movement used as the yardstick), run by `tests/test_runner.tscn`. Excluded from release builds.
- `tools/`: test scripts, texture generator, Godot/.NET installer (used by CI and the startup hook)

## Conventions

- Every gameplay number goes in `scripts/Core/Tuning.cs`, starting from the 1.8.9 value.
  Minecraft runs 20 ticks/s and we run 60; Minecraft-unit values are converted in `MotionModel`.
  Movement must stay equal to `McReference` at Minecraft tick boundaries (tests enforce this).
- Units: blocks and seconds. Yaw/pitch in degrees; yaw 0 faces -Z, positive yaw turns left,
  positive pitch looks up.
- Block (x, y, z) fills the cube from (x, y, z) to (x+1, y+1, z+1). Players stand on `Tuning.ArenaFloorY`.
- C#: file-scoped namespaces (`UhcPvp.*`), nullable enabled, warnings are errors. Godot node
  scripts are `partial` classes whose name matches the file name. Autoloads expose `Instance`.
- Subscribe to C# events in `_Ready` and unsubscribe in `_ExitTree` (C# events don't auto-disconnect).
- Network messages: add a record to `Messages.cs`, append its id to `MsgType` (never reorder),
  cover it in `MessageTests`, and bump `Network.ProtocolVersion` whenever the wire format changes.
  Decoding must never throw; bad input becomes null.
- Put logic that can be tested without a scene into `scripts/Core/` or `scripts/Movement/` and add tests.
  Multiplayer changes must keep `tools/smoke_test.sh` passing.
- Commit Godot's generated `*.uid` and `*.import` files and `UhcPvp.sln` (export needs it).
  Never commit `.godot/`, `build/`, `bin/` or `obj/`.
- Art and sound must be our own; nothing copied from Minecraft.

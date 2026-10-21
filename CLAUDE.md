# UHC PvP

First-person, block-world, team vs team PvP (1v1 up to 6v6) inspired by Minecraft 1.8.9 /
Hypixel UHC Duels combat. Built in Godot 4.7 (.NET edition) with C# on .NET 10. Long-term goal: Steam release.

Read these first; they hold the plan and every decision made so far:
- `docs/design.md`: vision, decisions, milestones, open questions
- `docs/netcode.md`: how lag is handled and why
- `docs/movement.md`: how movement matches Minecraft 1.8.9 exactly at 60 ticks/s (read before touching movement)
- `docs/combat.md`: melee rules, w-tapping, practice bots
- `docs/rod.md`: the fishing rod (our own design) and its numbers
- `docs/playtest-notes.md`: the owner's feedback on each build, open and done, plus known gaps

`docs/design.md` has a "Current status / next up" section: read it first, and update it when a
milestone ships.

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

## How we work

- **One session per milestone.** Start a session with `/next-milestone`; it picks up from the
  status section in `docs/design.md` and the open items in `docs/playtest-notes.md`. When a
  session gets long, finish the current step, ship it, update the status section and suggest
  a fresh session rather than starting something big.
- **Project commands** (`.claude/skills/`):
  - `/next-milestone`: catch up, research, plan with the owner, build, ship
  - `/ship`: checks, commit, push, wait for the Windows build, tell the owner
  - `/screenshot`: screenshots of the game on a virtual display, to check visuals
  - `/playtest`: log the owner's playtest feedback and act on it
- **Every piece of feedback goes in `docs/playtest-notes.md`,** typed as `/playtest` or said in
  plain chat. Fixes record the version that fixed them.
- **Research happens in helpers.** Look up Minecraft 1.8.9 mechanics with the `mc-researcher`
  agent (`.claude/agents/`), one per topic, in parallel. Its brief is the starting point;
  numbers still get parity tests.
- **Look before asking the owner to look.** Check visual changes with `/screenshot` first.
- **Chat stays short:** outcome first, plain language, one or two specific questions at most.

## Commands

Cloud sessions run `.claude/hooks/session-start.sh` at startup, which installs the .NET 10 SDK
and Godot 4.7.2 .NET (`tools/install_godot.sh`, checksum verified), builds and imports the project.

```bash
dotnet build UhcPvp.csproj            # compile; warnings are errors (nullable + .NET analyzers)
dotnet format UhcPvp.csproj           # fix formatting; CI runs it with --verify-no-changes
tools/run_tests.sh [Filter]          # build + unit tests (tests/*Tests.cs), optionally only matching names
tools/smoke_test.sh                  # host + 2 players + dummy, all bots fighting over the network (one laggy)
tools/screenshot.sh [--at=3,3.1] [-- launch options]   # screenshots on a virtual display (see /screenshot)
godot --headless --path . --import   # after adding assets (textures etc.)
python3 tools/gen_textures.py        # rebuild block atlas and HUD icons, then re-import
python3 tools/gen_sounds.py          # rebuild sound effects, then re-import
```

Run the game with launch options after `--` (full list in `scripts/Autoload/LaunchArgs.cs`):

```bash
godot --path . -- --host --name=Alice
godot --path . -- --join=127.0.0.1 --name=Bot --bot --ping=120 --jitter=20 --loss=2
godot --path . -- --practice --bot=fight          # solo practice; the local player is a fighting bot
godot --path . -- --host --dummies=2 --dummy-mode=strafe
```

To see the game in a cloud session, use `tools/screenshot.sh`; it runs the game under
`xvfb-run` with `--screenshot` and `--screenshot-at` (a comma-separated list takes several shots).

Windows export (needs the .NET export templates, see `tools/install_godot.sh --windows-templates`):
`godot --headless --path . --export-release "Windows Desktop" build/windows/UHC-PvP.exe`.
It produces `UHC-PvP.exe` plus a `data_UhcPvp_windows_x86_64` folder (the .NET runtime); ship both.

## Layout

- `scripts/Autoload/`: `LaunchArgs` (command-line options), `Controls` (input actions in code),
  `Network` (ENet host/join, messages, clock sync, lag simulator), `Session` (roster, teams, scene changes)
- `scripts/Core/`: engine-light logic with unit tests: `Tuning` (every gameplay number),
  `PlayerState`, `SnapshotBuffer`, `NetClock`, `LagSimulator`, `Team`
- `scripts/Movement/`: physics in doubles: `Vec3d`, `Box` (Minecraft's collision math),
  `MotionModel` (20-tick to 60-tick conversion), `PlayerMotor` (also knockback, sprint hits,
  w-tap, and the emulated server copy of the player that knockback is built from)
- `scripts/Combat/`: `CombatRules` (damage, armor, knockback, plus `HitProtection` and
  `Vitality` for health), `MissPenalty` (1.8's miss delay), `HitScan` (Minecraft's aiming and reach),
  `Bobber` (the fishing rod's bobber: flight and whom it catches)
- `scripts/Net/Messages.cs`: every network message as a typed record with its binary format
- `scripts/World/`: `VoxelWorld` (block grid), `ChunkMesher`, `WorldRenderer`, `Arena`, `Blocks`
- `scripts/Gameplay/`: `Game` (players, sync, practice bots), `Game.Combat.cs` (attack flow) and
  `Game.Rod.cs` (rod flow), `Player` (local, bot or remote), `PlayerModel`, `ViewModel` (first-person
  sword and rod), `BobberView` (bobber and line), `BotBrain`, `Effects`
- `scripts/Ui/`: main menu and in-match HUD, both built in code
- `tests/`: `TestCase` base class, `*Tests.cs` suites, `McReference.cs` (exact Minecraft 1.8.9
  movement, `McServerCopy`, the server's copy of a player, and `McBobber`, used as yardsticks), run by
  `tests/test_runner.tscn`. Excluded from release builds.
- `tools/`: test and screenshot scripts, texture and sound generators, Godot/.NET installer
  (used by CI and the startup hook)
- `.claude/`: the startup hook, project commands (`skills/`), the `mc-researcher` agent
  (`agents/`) and pre-approved commands (`settings.json`)

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

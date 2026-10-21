# UHC PvP

A first-person, block-world PvP game where teams fight teams, from 1v1 up to 6v6.
Combat is inspired by Minecraft 1.8.9 and Hypixel UHC Duels: combos, knockback and the
fishing rod. It's a spin-off, not a replica.

**Status:** early development. Movement and sword combat match Minecraft 1.8.9 (knockback,
crits, hit protection, w-tapping). Practice solo against bots that stand still, strafe or fight
back. The fishing rod is next. See the [milestones](docs/design.md#milestones).

## Play the latest build (Windows)

1. Download **[UHC-PvP-Windows.zip](https://github.com/jonathancheng31/uhc-pvp/releases/download/dev-build/UHC-PvP-Windows.zip)**.
   It's rebuilt automatically after every change.
2. Unzip it and run `UHC-PvP.exe` (keep the `data_...` folder next to it).
3. Windows may say "Windows protected your PC" because the game isn't signed yet.
   Click **More info**, then **Run anyway**.

**Practicing solo:** click **Practice** on the title screen. A training dummy appears in front
of you. Press **Esc** to add bots, remove them, or make them strafe or fight back.

**Playing with friends**
- One person clicks **Host match**. If Windows Firewall asks, allow access.
- Everyone else types the host's address and clicks **Join match**.
  - Same Wi-Fi: use the host's local address (e.g. `192.168.1.20`; find it with `ipconfig`).
  - Over the internet: the host needs to forward **UDP port 7777** on their router, and others
    use the host's public IP. Steam invites will replace this later.

**Controls**

| Key | Action |
|---|---|
| Mouse | Look around |
| Left click | Attack (every click counts, no cooldown) |
| W A S D | Move (moving forward always sprints) |
| Space | Jump (hold to keep hopping) |
| Shift | Sneak (stops you at ledges) |
| Esc | Pause menu (free the mouse, practice bot options, leave the match) |
| F3 | Show or hide debug info (FPS, ping, position) |

## Docs

- [Game design](docs/design.md): the plan, decisions and milestones
- [Netcode](docs/netcode.md): how lag is handled
- [Movement](docs/movement.md) and [Combat](docs/combat.md): how they match 1.8.9

## Development

Built with [Godot 4.7.2 .NET](https://godotengine.org) and C# on .NET 10. See
[CLAUDE.md](CLAUDE.md) for commands, project layout and conventions.

```bash
dotnet build UhcPvp.csproj
godot --headless --path . --import
tools/run_tests.sh
tools/smoke_test.sh
```

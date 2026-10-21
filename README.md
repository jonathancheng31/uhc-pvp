# UHC PvP

A first-person, block-world PvP game where teams fight teams, from 1v1 up to 6v6.
Combat is inspired by Minecraft 1.8.9 and Hypixel UHC Duels: combos, knockback and the
fishing rod. It's a spin-off, not a replica.

**Status:** early development. You can host or join a match and see the other players,
but walking and combat aren't in yet. See the [milestones](docs/design.md#milestones).

## Play the latest build (Windows)

1. Download **[UHC-PvP-Windows.zip](https://github.com/jonathancheng31/uhc-pvp/releases/download/dev-build/UHC-PvP-Windows.zip)**.
   It's rebuilt automatically after every change.
2. Unzip it and run `UHC-PvP.exe`.
3. Windows may say "Windows protected your PC" because the game isn't signed yet.
   Click **More info**, then **Run anyway**.

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
| W A S D, Space, Ctrl, Shift | Move, jump, sprint, sneak (coming in the movement milestone) |
| Esc | Pause menu (free the mouse, leave the match) |
| F3 | Show or hide debug info (FPS, ping, position) |

## Docs

- [Game design](docs/design.md): the plan, decisions and milestones
- [Netcode](docs/netcode.md): how lag is handled

## Development

Built with [Godot 4.7.2](https://godotengine.org) (GDScript). See [CLAUDE.md](CLAUDE.md)
for commands, project layout and conventions.

```bash
godot --headless --path . --import
tools/run_tests.sh
tools/smoke_test.sh
```

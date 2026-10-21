# Game design

The plan for the game: what it is, what's decided, and what we're building next.
Update this file whenever a decision changes.

## The game

A first-person, block-world PvP game where **teams fight teams**, from 1v1 up to 6v6.
Combat is inspired by **Hypixel UHC Duels on Minecraft 1.8.9**. It's a spin-off, not a
replica: we keep what makes that combat great and are free to change the rest.

Long-term goal: a **Steam release**. Every decision should keep that possible.

## What matters most

1. **Combos and knockback.** The 1.8 feel: no attack cooldown (clicks per second
   matter), sprint resets / w-tapping, strafing, and knockback that lets a skilled
   player chain hits.
2. **The fishing rod.** The second core tool, used to start and extend combos.
3. **Fair under lag.** No ghost hits, consistent knockback at any ping (see
   [netcode.md](netcode.md)).
4. **Quick to get into a match.** Host or join and fight.

## Decisions

| Topic | Decision |
|---|---|
| Engine | Godot 4.7, GDScript |
| View | First person |
| World | Made of blocks. No building at first, but the world is stored as blocks so building can be added later |
| Teams | Team vs team, 1v1 up to 6v6 (12 players max). No big UHC survival mode |
| Platform | Windows first. Keyboard and mouse only |
| Combat numbers | Start from exact Minecraft 1.8.9 values (all in `scripts/core/tuning.gd`), then tune |
| Block-hitting | Not included |
| Armor | A fixed armor set so fights last about as long as UHC Duels |
| Mouse and FOV | Match Minecraft's sensitivity curve and FOV so aim muscle memory carries over |
| Simulation rate | 60 ticks per second (Minecraft uses 20); Minecraft's per-tick values are converted |
| Netcode | Each player's game owns its own movement and hits, with fairness fixes. See [netcode.md](netcode.md) |
| Hosting | Everyone connects to one host. A player hosts for now; the same host code can run on a rented server later |
| Anti-cheat | Not a goal for now. Moving to anti-cheat later would mean reworking the netcode |
| Art and sound | Our own. Nothing taken from Minecraft |

## Milestones

### 0. Setup (done)
Godot project, block arena, Host/Join menu, teams up to 6v6, players visible to each
other, clock sync, smoothing of other players, lag simulator, bot players, unit tests,
multiplayer smoke test, automatic Windows builds.

### 1. Movement
Minecraft 1.8.9 movement: walk, sprint, sneak, jump, sprint-jump, strafing, air control,
block collision. Tests that compare against Minecraft (jump height, sprint speed).
Sprint FOV change. Smooth camera between ticks.

### 2. Melee
Sword and clicking. 3-block reach checked against what the attacker saw, with the lag cap.
Damage, half-second hit immunity, crits, sprint-hit knockback (applied by the victim's
game), red hurt flash, hit sounds. Health and hearts on screen, armor, death and respawn.

### 3. Fishing rod
Cast, bobber flight, hooking players, the rod hit and its knockback, reeling in.
Hotbar with sword and rod.

### 4. Match flow
Rounds, scoreboard, spawns per team, end-of-match screen.

### 5. Feel pass
Tune combos with the lag simulator and bots at 0, 50, 100 and 150 ms. Arm swing and
hit animations, sounds.

### 6. Playing over the internet
Steam invites and Steam's relay network (gets through home routers, free during
development), so friends can join without router setup.

### Later
Bow, water and lava buckets, building, golden apples and golden heads, more arenas,
spin-off ideas (draft kits, new items), a real name, Steam store page, rented servers
for 6v6, anti-cheat if we add ranked play.

## Open questions

- Rounds (eliminated until next round) or respawns?
- Friendly fire on or off?
- Arena size and layout for bigger team fights.
- Which healing items are in the base kit?
- The game's real name.

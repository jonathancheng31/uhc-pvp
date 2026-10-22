# Game design

The plan for the game: what it is, what's decided, and what we're building next.
Update this file whenever a decision changes.

## The game

A first-person, block-world PvP game where **teams fight teams**, from 1v1 up to 6v6.
Combat is inspired by **Hypixel UHC Duels on Minecraft 1.8.9**. It's a spin-off, not a
replica: we keep what makes that combat great and are free to change the rest.

Long-term goal: a **Steam release**. Every decision should keep that possible.

Guiding rule: **build the better thing, even when it takes longer or costs more.** The
final result matters, not build effort.

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
| Engine | Godot 4.7 (.NET edition) |
| Language | C# on .NET 10 (switched from GDScript: faster code, more mistakes caught before the game runs, better tooling) |
| View | First person |
| World | Made of blocks. No building at first, but the world is stored as blocks so building can be added later |
| Teams | Team vs team, 1v1 up to 6v6 (12 players max). No big UHC survival mode |
| Platform | Windows first. Keyboard and mouse only |
| Direction | Inspired by Minecraft PvP, not a copy. Since 2026-10-21 the owner wants new features to be our own designs, starting from 1.8.9's numbers where they help. Since 2026-10-22, small twists on the 1.8.9 mechanics too, each one a match rule the host can switch between plain 1.8 and the twist, so it can be playtested ("Twists" below) |
| Twists | Our changes to 1.8.9's mechanics, set by the host in the pause menu's Twists screen for the whole match and sent to every player (`MatchRules`); all default to 1.8.9 until playtesting picks values. **Faster strafing** (0.4.5): sideways speed on the ground up to 150%, never faster than a straight sprint. **Snappier strafing** (0.4.6): starting, stopping and switching A/D on the ground up to instant (1.8.9 takes a quarter second to switch). Both leave the air alone and knockback whole ([movement.md](movement.md)). **Wasted clicks** (0.4.6): a click that does nothing blocks clicking for up to 0.3 s, so timing beats click speed ([combat.md](combat.md)). Ruled out: a combo breaker (the owner: full combos are hard to land and very satisfying) and saving early clicks to land the instant hit protection ends (spam would guarantee perfectly timed hits) |
| Combat numbers | Start from exact Minecraft 1.8.9 values (all in `scripts/Core/Tuning.cs`), then tune |
| Block-hitting | Not included |
| Kit | Diamond sword (8 damage). Fixed armor worth 18 points (diamond helmet and chestplate, iron leggings and boots), so a sword hit does 2.24 and a kill takes 9 hits (about 6 with crits). Tunable in `Tuning.cs` |
| Friendly fire | Off: hits on teammates do nothing (default; open to change) |
| Miss delay | Exactly 1.8's: clicking at nothing (no player in reach, no block within 4.5) blocks clicks for up to half a second, but only while the button stays held; letting go clears it within 1/20 s. The main PvP clients (Lunar, Badlion) keep it. Before 0.3.1 we blocked every click for the full half second, which ate hits |
| Knockback | Built exactly like a 1.8.9 server: from the server's copy of the victim, which barely moves sideways and keeps falling while the victim is airborne, so every hit pushes the same distance and combos lift less and less. Emulated on the victim's own game, so it's the same at any ping. Before 0.3.1 we started from the victim's real motion, which sent combos too far and too high |
| Hit protection timing | Half a second after a full hit, counted on Minecraft's 1/20 s ticks from the moment of each click (450 to 500 ms in real time, like a server) |
| Sprinting | Always on: moving forward sprints, like 1.8 with the sprint key always held (or Lunar/Badlion toggle sprint). No sprint key. Hits never stop your sprint (1.8.9 stopped double-tap sprinting on every hit). Changed in 0.3.2 at the owner's request. Faster strafing is a twist (below) |
| W-tapping | Works exactly like 1.8: after a sprint hit, the next hit only gets sprint knockback once you stop and restart sprinting (let go of W for a moment) |
| Fishing rod | Our own design: always in the other hand, right-click throws, right-click again replaces the bobber. A 0.75 s cooldown after each throw stops spamming (owner's request, 0.4.2; before that there was none). A catch knocks back (no damage) and counts for hit protection like 1.8: a sword hit right after does damage but no knockback, and a rod on a protected player flies through. Numbers start from 1.8.9's. See [rod.md](rod.md) |
| Respawning | Respawn at your team's spawn 3 seconds after dying (rounds come in the match-flow milestone) |
| Practice bots | Run by the host; stand still, strafe, chase (run at you without swinging), or fight back (sprinting, strafing, clicking about 9 times a second, w-tapping, rodding at 3.5 to 8 blocks). They heal after 5 seconds without a hit |
| Settings | A Settings screen on the main menu and in the pause menu (Esc): max FPS (30, 60, 75, 120, 144, 165, 240, 360 or Unlimited, the default; the game runs 60 ticks a second either way) and volume (0 to 100%, scaling loudness linearly like Minecraft's slider; letting go plays a hit sound). Changes apply at once and are saved on the player's computer. Added in 0.4.3 at the owner's request ("just basic settings for now"). 0.4.6 added camera shake when hit (0 to 100% of Minecraft's 14 degree tilt, default 25%: the owner found the full tilt too much). Mouse sensitivity, FOV and key binds could come later |
| HUD | Crosshair, hearts and armor (centered at the bottom), the rod's cooldown as a small bar under the crosshair, combo and reach readout, kill feed. No hotbar since 0.4.4 (the owner's request): the sword is always in hand and the rod in the other, so there's nothing to switch. It can come back when there are items to pick between |
| Mouse and FOV | Match Minecraft's sensitivity curve and FOV (70) so aim muscle memory carries over. The view never widens while sprinting (1.8.9's does by up to 15%; removed in 0.4.2 at the owner's request) |
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

### 1. Movement (done)
Minecraft 1.8.9 movement: walk, sprint (always on since 0.3.2), sneak (stops at ledges), jump,
sprint-jump, strafing, air control, block collision. Tests that compare against an exact copy
of 1.8.9's movement code. Sprint FOV change (removed in 0.4.2). Smooth camera between ticks. Walk and sneak
animations on other players. Details in [movement.md](movement.md).

### 2. Melee (done)
Sword and clicking. 3-block reach checked against what the attacker saw, with the lag cap.
Damage, half-second hit immunity, crits, sprint-hit knockback (applied by the victim's
game), w-tap rule, red hurt flash, hurt camera tilt, hit sound, crit sparks. Hearts, armor
and hotbar on screen (the hotbar was removed in 0.4.4), combo and reach readout, kill feed, death and respawn. Practice mode
with bots (still, strafing, chasing without hitting, fighting). Details in [combat.md](combat.md).

### 3. Fishing rod (done)
Our own rod, inspired by 1.8's (the owner's design, 2026-10-21): right-click throws it at any
time, with no switching slots and no cooldown (0.4.2 added a 0.75 s one). Right-clicking again makes the old bobber vanish
and throws a new one. A bobber that hits a player knocks them back (no damage) and counts as a
hit for hit protection, like 1.8. Flight and knockback numbers start from 1.8.9's. Fighting
practice bots use it too. Details in [rod.md](rod.md).

### 4. Match flow
Rounds, scoreboard, spawns per team, end-of-match screen.

### 5. Feel pass
Tune combos with the lag simulator and bots at 0, 50, 100 and 150 ms. Arm swing and
hit animations, sounds.

### 6. Playing over the internet
Steam invites and Steam's relay network (gets through home routers, free during
development), so friends can join without router setup.

### Later
Better hit feel (a hit-landed sound, a small screen kick, a stronger crit effect, a low-health
heartbeat; the owner, 2026-10-22: "we can add to more later"), bow, water and lava buckets, building, golden apples and golden heads, more arenas,
spin-off ideas (draft kits, new items), a real name, Steam store page, rented servers
for 6v6, anti-cheat if we add ranked play.

## Current status / next up

*Updated at the end of each milestone, so a new session knows where things stand.*

- **Done:** setup, movement (milestone 1), melee and practice bots (milestone 2), the fishing rod
  (milestone 3, version 0.4.0, the owner's own design). 0.3.2 made sprint always on (hits never
  stop it) and added chase bots. 0.4.1 fixed two small sprint timing differences from 1.8.9:
  sprint changes in the air take effect a tick late, and a wall stops sprint a tick late
  (`movement.md`). 0.4.2, from the owner's feedback: sprinting no longer changes the FOV, and
  the rod has a 0.75 s cooldown. 0.4.3 added a Settings screen (max FPS, volume). 0.4.4
  removed the hotbar. 0.4.5 started our own twists: faster strafing, with a slider in the
  pause menu for playtesting. 0.4.6 added two more (snappier strafing, wasted clicks) on a
  Twists screen, and toned down the camera shake when hit (a setting, 25% by default).
- **Next up:** polishing gameplay. The owner, 2026-10-22: "lets ignore match flow and other
  aspects for a while, we wanna polish gameplay." Match flow (milestone 4) and internet play
  (milestone 6) wait; the feel pass (milestone 5) and the twists come first. Rounds vs respawns
  stays open (below) until match flow comes back.
- **Waiting on the owner:** which twist values feel best (strafe speed, snappiness, wasted-click
  delay; playtest the Twists screen), how the rod feels (range, knockback, the 0.75 s cooldown) and how always-on sprint feels. Open feedback and known gaps are tracked in
  [playtest-notes.md](playtest-notes.md).

## Open questions

- Rounds (eliminated until next round) or respawns?
- Friendly fire on or off? (Off for now.)
- Arena size and layout for bigger team fights.
- Which healing items are in the base kit?
- The game's real name.
